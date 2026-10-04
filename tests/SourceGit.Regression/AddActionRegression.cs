using System.Reflection;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SourceGit.Models;

internal static partial class Program
{
    private static void TestAddActionPicker()
    {
        var path = NewRepository("add action picker");
        var repo = Open(path);
        var toolbar = new SourceGit.Views.RepositoryToolbar { DataContext = repo };
        var owner = new Window { Content = toolbar, Width = 1000, Height = 200 };
        _lifetime.MainWindow = owner;
        owner.Show();
        try
        {
            foreach (var cancel in new[] { false, true })
            {
                var configure = (Task)Invoke(toolbar, "OpenCustomActionsConfigureAsync", repo, true);
                var dialog = owner.OwnedWindows.OfType<SourceGit.Views.RepositoryConfigure>().Single();
                var vm = (SourceGit.ViewModels.RepositoryConfigure)dialog.DataContext;
                var action = vm.SelectedCustomAction;
                var provider = DispatchProxy.Create<IStorageProvider, ActionPickerProbe>();
                var picker = (ActionPickerProbe)provider;
                picker.Owner = dialog;
                SetPicker(dialog, provider);
                PumpFor(100);
                Check(picker.Calls == 1 && picker.OwnerWasVisible, "Add action automatically opens the picker after its window is visible");
                Check(picker.FolderLookup != null && Path.GetFullPath(picker.FolderLookup.LocalPath).TrimEnd('\\', '/') ==
                    Path.GetFullPath(path).TrimEnd('\\', '/'), "Picker starts at the repository: " + picker.FolderLookup);
                Check(!picker.Options.AllowMultiple && picker.Options.FileTypeFilter[0].Patterns.Contains("*"),
                    "Picker accepts executables and scripts as a single selection");
                Check(dialog.FindControl<TabControl>("Tabs").SelectedIndex == 4 && vm.CustomActions.Contains(action),
                    "New action is selected in the custom actions tab before picking");

                var file = DispatchProxy.Create<IStorageFile, ActionPickedFile>();
                ((ActionPickedFile)file).FileUri = new Uri(Path.Combine(path, "fetch script.bat"));
                picker.Result.SetResult(cancel ? [] : [file]);
                PumpFor(100);
                Check(cancel ? string.IsNullOrEmpty(action.Executable) && action.Name == "Unnamed Action" :
                    action.Executable == file.Path.LocalPath && action.Name == "fetch script.bat",
                    "Picking fills path and name; cancel leaves the new action editable");
                PumpFor(100);
                Check(picker.Calls == 1, "Picker does not reopen after selection or cancel");

                action.Name = "Keep my action name";
                picker.Result = new();
                var manual = dialog.SelectExecutableForCustomActionAsync(action);
                Check(picker.Calls == 2, "Manual executable selection remains available");
                picker.Result.SetResult([file]);
                Wait(manual);
                Check(action.Name == "Keep my action name", "Executable selection preserves a user-provided action name");
                // Picker tests must not race the window's async save against repository disposal.
                dialog.DataContext = null;
                dialog.Close();
                Wait(configure);
                Check(repo.Settings.CustomActions.Contains(action), "Configured new action is retained when the window closes");
            }

            foreach (var executable in new[] { "", "   ", "cmd.exe" })
            {
                var previous = repo.Settings.CustomActions.ToArray();
                var configure = (Task)Invoke(toolbar, "OpenCustomActionsConfigureAsync", repo, true);
                var dialog = owner.OwnedWindows.OfType<SourceGit.Views.RepositoryConfigure>().Single();
                var vm = (SourceGit.ViewModels.RepositoryConfigure)dialog.DataContext;
                var action = vm.SelectedCustomAction;
                var provider = DispatchProxy.Create<IStorageProvider, ActionPickerProbe>();
                var picker = (ActionPickerProbe)provider;
                picker.Owner = dialog;
                SetPicker(dialog, provider);
                PumpFor(100);
                picker.Result.SetResult([]);
                PumpFor(100);
                Check(vm.CustomActions.Contains(action), "Canceling the picker keeps the draft editable until the window closes");
                action.Executable = executable;
                if (executable.Length > 0)
                    action.Name = "Named draft";
                var removedBeforeSave = false;
                dialog.Closing += (_, _) =>
                {
                    removedBeforeSave = !vm.CustomActions.Contains(action);
                    repo.Settings.Save();
                };
                dialog.DataContext = null;
                dialog.Close();
                Wait(configure);
                var discarded = string.IsNullOrWhiteSpace(executable);
                Check(removedBeforeSave == discarded && repo.Settings.CustomActions.Contains(action) != discarded,
                    "Empty drafts are discarded before settings save; manually typed commands are kept");
                Check(previous.All(repo.Settings.CustomActions.Contains), "Discarding a draft preserves every existing action");
                using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(path, ".git", "sourcegit.settings")));
                Check(saved.RootElement.GetProperty("CustomActions").GetArrayLength() == repo.Settings.CustomActions.Count &&
                    saved.RootElement.GetProperty("CustomActions").EnumerateArray().All(item =>
                        !string.IsNullOrWhiteSpace(item.GetProperty("Executable").GetString())),
                    "Saved settings contain no incomplete actions that could reappear after restart");
                if (discarded)
                {
                    Check(repo.Settings.CustomActions.SequenceEqual(previous), "Discarding a draft restores the original action list");
                    Check(toolbar.FindControl<Button>("CustomActionSlot2Button").Tag == null,
                        "Unused toolbar slot returns to Add action instead of an Unnamed Action button");
                }
            }

            var edit = (Task)Invoke(toolbar, "OpenCustomActionsConfigureAsync", repo, false);
            var editDialog = owner.OwnedWindows.OfType<SourceGit.Views.RepositoryConfigure>().Single();
            var editProvider = DispatchProxy.Create<IStorageProvider, ActionPickerProbe>();
            var editPicker = (ActionPickerProbe)editProvider;
            editPicker.Owner = editDialog;
            SetPicker(editDialog, editProvider);
            PumpFor(100);
            Check(editPicker.Calls == 0, "Opening existing custom action configuration does not auto-pick a file");
            editDialog.DataContext = null;
            editDialog.Close();
            Wait(edit);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray())
            {
                dialog.DataContext = null;
                dialog.Close();
            }
            _lifetime.MainWindow = null;
            owner.Close();
            repo.Close();
        }
    }

    private static void SetPicker(TopLevel window, IStorageProvider picker)
    {
        var field = typeof(TopLevel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(IStorageProvider));
        field.SetValue(window, picker);
    }

    public class ActionPickerProbe : DispatchProxy
    {
        public Window Owner;
        public int Calls;
        public bool OwnerWasVisible;
        public Uri FolderLookup;
        public FilePickerOpenOptions Options;
        public TaskCompletionSource<IReadOnlyList<IStorageFile>> Result = new();
        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_CanOpen": return true;
                case "OpenFilePickerAsync":
                    Calls++;
                    OwnerWasVisible = Owner.IsVisible;
                    Options = (FilePickerOpenOptions)args[0];
                    return Result.Task;
                case "TryGetFolderFromPathAsync":
                    FolderLookup = (Uri)args[0];
                    return Task.FromResult<IStorageFolder>(null);
                default: throw new NotSupportedException(method.Name);
            }
        }
    }

    public class ActionPickedFile : DispatchProxy
    {
        public Uri FileUri;
        protected override object Invoke(MethodInfo method, object[] args) => method.Name switch
        {
            "get_Path" => FileUri,
            "get_Name" => Path.GetFileName(FileUri.LocalPath),
            "Dispose" => null,
            _ => throw new NotSupportedException(method.Name),
        };
    }
}
