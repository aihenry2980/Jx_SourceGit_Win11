using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void TestWelcomeUpstreamMerge()
    {
        var cleanPath = NewRepository("welcome merge clean");
        var oldName = "# branch.head not-the-branch";
        File.WriteAllText(Path.Combine(cleanPath, oldName), "tracked");
        Git(cleanPath, "add", ".");
        Git(cleanPath, "commit", "-m", "Initial");
        var dirtyPath = NewRepository("welcome merge dirty");
        Git(dirtyPath, "commit", "--allow-empty", "-m", "Initial");
        Directory.CreateDirectory(Path.Combine(dirtyPath, "new folder"));
        File.WriteAllText(Path.Combine(dirtyPath, "new folder", "first.txt"), "new");
        File.WriteAllText(Path.Combine(dirtyPath, "new folder", "second.txt"), "new");
        var statusQuery = new SourceGit.Commands.QueryRepositoryStatus(dirtyPath);
        Check(Wait(statusQuery.GetResultAsync()).HasLocalChanges && statusQuery.Args.Contains("-unormal") && statusQuery.Args.Contains("--no-optional-locks"),
            "Welcome dirty queries avoid expanding every untracked file and do not take optional index locks");
        Git(cleanPath, "mv", oldName, "renamed file.txt");
        var status = Wait(new SourceGit.Commands.QueryRepositoryStatus(cleanPath).GetResultAsync());
        Check(status.HasLocalChanges && status.CurrentBranch == "main", "Rename source paths cannot masquerade as porcelain branch headers");
        Git(cleanPath, "commit", "-m", "Rename file");
        Check(!Wait(new SourceGit.Commands.QueryRepositoryStatus(cleanPath).GetResultAsync()).HasLocalChanges, "Clean repositories have no dirty marker");
        var unborn = NewRepository("welcome merge unborn");
        status = Wait(new SourceGit.Commands.QueryRepositoryStatus(unborn).GetResultAsync());
        Check(status is { CurrentBranch: "main", HasLocalChanges: false }, "Welcome status supports unborn branches without fixed-offset header assumptions");

        var rebasePath = NewRepository("welcome merge rebase");
        File.WriteAllText(Path.Combine(rebasePath, "conflict.txt"), "base");
        Git(rebasePath, "add", ".");
        Git(rebasePath, "commit", "-m", "Base");
        Git(rebasePath, "checkout", "-b", "topic");
        File.WriteAllText(Path.Combine(rebasePath, "conflict.txt"), "topic");
        Git(rebasePath, "commit", "-am", "Topic");
        Git(rebasePath, "checkout", "main");
        File.WriteAllText(Path.Combine(rebasePath, "conflict.txt"), "main");
        Git(rebasePath, "commit", "-am", "Main");
        Git(rebasePath, "checkout", "topic");
        try { Git(rebasePath, "rebase", "main"); }
        catch (Exception ex) when (ex.Message.Contains("git rebase main failed")) { }
        try
        {
            var current = Wait(new SourceGit.Commands.QueryBranches(rebasePath).GetResultAsync()).Single(b => b.IsCurrent);
            Check(current.IsDetachedHead && current.Head == Git(rebasePath, "rev-parse", "HEAD"),
                "The active rebase HEAD survives upstream branch parsing and remains available to amend workflows");
        }
        finally { Git(rebasePath, "rebase", "--abort"); }

        var prefs = Preferences.Instance;
        var previousNodes = prefs.RepositoryNodes.ToArray();
        var previousStatusCheck = prefs.CheckRepositoryStatusOnWelcome;
        var previousTheme = Application.Current.RequestedThemeVariant;
        var welcome = Welcome.Instance;
        var updatingField = typeof(Welcome).GetField("_isUpdatingStatus", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastStatusField = typeof(RepositoryNode).GetField("_lastUpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance);
        var clean = new RepositoryNode { Id = cleanPath, Name = "Clean repository", IsRepository = true };
        var dirty = new RepositoryNode { Id = dirtyPath, Name = "Repository with untracked folder", IsRepository = true };
        var toolbar = new SourceGit.Views.WelcomeToolbar { DataContext = welcome };
        var view = new SourceGit.Views.Welcome { DataContext = welcome };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(toolbar);
        Grid.SetRow(view, 1);
        layout.Children.Add(view);
        var window = new Window { Content = layout, Width = 1200, Height = 700 };
        try
        {
            prefs.CheckRepositoryStatusOnWelcome = false;
            WaitUntil(() => !(bool)updatingField.GetValue(welcome), "Existing Welcome status scan did not stop");
            prefs.RepositoryNodes.Clear();
            prefs.RepositoryNodes.AddRange([clean, dirty]);
            welcome.Refresh();
            window.Show();
            PumpFor(150);
            Check(clean.Status == null && dirty.Status == null, "Welcome does not scan repository status on load when the new option is disabled");
            Wait(dirty.UpdateStatusAsync(true, null));
            Check(dirty.Status is { HasLocalChanges: true }, "Manual Refresh Status still works while automatic status checking is disabled");
            dirty.Status = null;
            lastStatusField.SetValue(dirty, DateTime.UnixEpoch);
            var toggle = toolbar.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), SourceGit.App.Text("Welcome.ToggleStatusCheck")));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => clean.Status != null && dirty.Status != null && !(bool)updatingField.GetValue(welcome), "Enabling Welcome status checks did not update rows");
            Check(prefs.CheckRepositoryStatusOnWelcome && !clean.Status.HasLocalChanges && dirty.Status.HasLocalChanges,
                "The Welcome toolbar toggle enables automatic checks and distinguishes clean from dirty rows");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                PumpFor(60);
                CaptureUxFrame(window, "welcome-upstream-merge-" + theme);
            }
            var previousToken = welcome.StatusCheckCancellationToken;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!prefs.CheckRepositoryStatusOnWelcome && previousToken.IsCancellationRequested && !welcome.StatusCheckCancellationToken.IsCancellationRequested,
                "Disabling status checks cancels the old generation and leaves a reusable token for future checks");
            var context = (JsonSerializerContext)typeof(SourceGit.App).Assembly.GetType("SourceGit.JsonCodeGen").GetProperty("Default").GetValue(null);
            var type = context.GetTypeInfo(typeof(Preferences));
            var saved = (Preferences)JsonSerializer.Deserialize(JsonSerializer.Serialize(prefs, type), type);
            Check(!saved.CheckRepositoryStatusOnWelcome && saved.FileTypeColors.Length == 10 && saved.UserAvatars.Length == 5,
                "Upstream status preferences serialize without dropping custom file colors and avatars");
            Check(((Preferences)JsonSerializer.Deserialize("{}", type)).CheckRepositoryStatusOnWelcome,
                "Old settings retain upstream's enabled-by-default status checking");

            var repo = Open(cleanPath);
            try
            {
                File.WriteAllText(Path.Combine(cleanPath, "awaited refresh.txt"), "new");
                var refresh = (Task)typeof(Repository).GetMethod("RefreshWorkingCopyChangesAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(repo, new object[] { false });
                Wait(refresh);
                Check(repo.LocalChangesCount == 1, "Awaited refresh completes after applying working-copy data without blocking a worker on synchronous UI Invoke");
            }
            finally { repo.Close(); }
        }
        finally
        {
            window.Close();
            prefs.CheckRepositoryStatusOnWelcome = false;
            WaitUntil(() => !(bool)updatingField.GetValue(welcome), "Welcome scan did not finish during cleanup");
            prefs.RepositoryNodes.Clear();
            prefs.RepositoryNodes.AddRange(previousNodes);
            welcome.Refresh();
            prefs.CheckRepositoryStatusOnWelcome = previousStatusCheck;
            WaitUntil(() => !(bool)updatingField.GetValue(welcome), "Restored Welcome scan did not settle");
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }
}
