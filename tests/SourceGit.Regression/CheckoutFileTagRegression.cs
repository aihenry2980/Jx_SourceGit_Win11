using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void TestCheckoutChoicesAndFileTags()
    {
        TestCheckoutChoices();
        TestChangeFileTypeTags();
    }

    private static void TestCheckoutChoices()
    {
        var leafSource = NewRepository("checkout leaf source");
        File.WriteAllText(Path.Combine(leafSource, "leaf.txt"), "old");
        Git(leafSource, "add", ".");
        Git(leafSource, "commit", "-m", "Old leaf");
        var leafOld = Git(leafSource, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(leafSource, "leaf.txt"), "new");
        Git(leafSource, "commit", "-am", "New leaf");
        var leafNew = Git(leafSource, "rev-parse", "HEAD");
        var moduleSource = NewRepository("checkout module source");
        Git(moduleSource, "-c", "protocol.file.allow=always", "submodule", "add", leafSource, "nested/leaf");
        Git(Path.Combine(moduleSource, "nested/leaf"), "checkout", "--detach", leafOld);
        File.WriteAllText(Path.Combine(moduleSource, "engine.cpp"), "old");
        Git(moduleSource, "add", ".");
        Git(moduleSource, "commit", "-m", "Old module");
        var moduleOld = Git(moduleSource, "rev-parse", "HEAD");
        Git(Path.Combine(moduleSource, "nested/leaf"), "checkout", "--detach", leafNew);
        File.WriteAllText(Path.Combine(moduleSource, "engine.cpp"), "new");
        Git(moduleSource, "commit", "-am", "New module");
        var moduleNew = Git(moduleSource, "rev-parse", "HEAD");
        var path = NewRepository("checkout choices parent");
        Git(path, "-c", "protocol.file.allow=always", "submodule", "add", moduleSource, "modules/engine");
        var modulePath = Path.Combine(path, "modules/engine");
        Git(modulePath, "checkout", "--detach", moduleOld);
        File.WriteAllText(Path.Combine(path, "parent.txt"), "old");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "Old parent");
        var parentOld = Git(path, "rev-parse", "HEAD");
        Git(path, "-c", "protocol.file.allow=always", "submodule", "update", "--init", "--recursive");
        var leafPath = Path.Combine(modulePath, "nested/leaf");
        Git(path, "checkout", "-b", "next");
        Git(modulePath, "checkout", "--detach", moduleNew);
        File.WriteAllText(Path.Combine(path, "parent.txt"), "new");
        Git(path, "commit", "-am", "New parent");
        var parentNew = Git(path, "rev-parse", "HEAD");
        Git(path, "-c", "submodule.recurse=false", "checkout", "main");
        Git(modulePath, "checkout", "--detach", moduleOld);
        Git(path, "config", "submodule.recurse", "true");
        Git(path, "remote", "add", "origin", path);
        var repo = Open(path);
        repo.Settings.AskBeforeAutoUpdatingSubmodules = true;
        var launcherField = typeof(SourceGit.App).GetField("_launcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var previousLauncher = launcherField.GetValue(Application.Current);
        var prefs = Preferences.Instance;
        var locale = prefs.Locale;
        var fontSize = prefs.DefaultFontSize;
        var showTags = prefs.ShowTagsInGraph;
        var theme = Application.Current.RequestedThemeVariant;
        var workspace = prefs.GetActiveWorkspace();
        var previousRepositories = workspace.Repositories.ToArray();
        var previousIndex = workspace.ActiveIdx;
        workspace.Repositories.Clear();
        var launcher = new Launcher(null);
        var page = new LauncherPage(new RepositoryNode { Id = path, Name = "Checkout choices" }, repo);
        launcher.Pages.Add(page);
        launcher.ActivePage = page;
        launcherField.SetValue(Application.Current, launcher);
        var view = new SourceGit.Views.LauncherPage { DataContext = page };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        try
        {
            window.Show();
            WaitUntil(() => !repo.Histories.IsLoading, "Checkout test history did not load");
            Branch Branch(string name) => repo.Branches.Single(b => b.IsLocal && b.Name == name);
            Button FindPopupButton(string name) => view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name && b.IsEffectivelyVisible);
            Wait(repo.CheckoutBranchAsync(Branch("next")));
            Check(page.Popup is Checkout && !page.Popup.InProgress && Git(path, "branch", "--show-current") == "main",
                "Clean branch checkout waits for an explicit choice instead of auto-starting");
            foreach (var language in new[] { "en_US", "zh_CN" })
            {
                prefs.Locale = language;
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Application.Current.RequestedThemeVariant = variant;
                    prefs.DefaultFontSize = 16;
                    PumpFor(100);
                    foreach (var name in new[] { "CheckoutOnlyButton", "CheckoutWithSubmodulesButton" })
                    {
                        var button = FindPopupButton(name);
                        var label = button.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
                        Check(label.TextLayout.Width <= label.Bounds.Width + 1 && !label.TextLayout.TextLines.Any(l => l.HasCollapsed),
                            "Checkout option fits at larger UI text: " + name + "/" + language + "/" + variant);
                    }
                    CaptureUxFrame(window, $"checkout-choices-{language}-{variant}");
                }
            }
            prefs.DefaultFontSize = fontSize;
            FindPopupButton("CheckoutOnlyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => page.Popup == null, "Checkout Only did not finish");
            Check(Git(path, "branch", "--show-current") == "next" && Git(modulePath, "rev-parse", "HEAD") == moduleOld &&
                Git(leafPath, "rev-parse", "HEAD") == leafOld, "Checkout Only leaves both submodule levels unchanged even when submodule.recurse=true");
            Check(repo.Logs[0].IsSuccessful && !repo.Logs[0].Content.Contains("submodule update"), "Plain checkout logs success without starting submodule work");

            void ResetToOld()
            {
                page.CancelPopup();
                Git(path, "-c", "submodule.recurse=false", "checkout", "main");
                Git(modulePath, "-c", "submodule.recurse=false", "checkout", "--detach", moduleOld);
                Git(leafPath, "checkout", "--detach", leafOld);
                Wait(InvokeTask(repo, "RefreshBranchesAsync"));
            }
            ResetToOld();
            Wait(repo.CheckoutBranchAsync(Branch("next")));
            FindPopupButton("CheckoutWithSubmodulesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => page.Popup == null, "Checkout + Update Submodules did not finish");
            Check(Git(modulePath, "rev-parse", "HEAD") == moduleNew && Git(leafPath, "rev-parse", "HEAD") == leafNew,
                "Combined checkout updates both levels to the pinned commits without another confirmation");
            Check(repo.Logs[0].IsSuccessful && repo.Logs[0].Content.Contains("--init --recursive --jobs=1") && !repo.Logs[0].Content.Contains("--remote"),
                "Combined checkout initializes recursively and does not advance to remote tips");

            foreach (var kind in new[] { "remote", "fast-forward", "commit" })
            foreach (var update in new[] { false, true })
            {
                ResetToOld();
                var name = kind + (update ? "-update" : "-only");
                CheckoutPopup popup;
                if (kind == "remote")
                {
                    Git(path, "update-ref", "refs/remotes/origin/" + name, parentNew);
                    var create = new CreateBranch(repo, new Branch { Name = name, FullName = "refs/remotes/origin/" + name, Head = parentNew, Remote = "origin" })
                    { Name = name, CheckoutAfterCreated = true, DealWithLocalChanges = DealWithLocalChanges.DoNothing };
                    Check(create.ShowCheckoutActions, "Remote branch creation exposes both checkout choices");
                    create.CheckoutAfterCreated = false;
                    Check(!create.ShowCheckoutActions, "Create without checkout retains the normal confirmation");
                    create.CheckoutAfterCreated = true;
                    popup = create;
                }
                else if (kind == "fast-forward")
                {
                    Git(path, "branch", name, parentOld);
                    Wait(InvokeTask(repo, "RefreshBranchesAsync"));
                    popup = new CheckoutAndFastForward(repo, Branch(name), new Branch { Head = parentNew });
                }
                else
                    popup = new CheckoutDetached(repo, Wait(new SourceGit.Commands.QuerySingleCommit(path, parentNew).GetResultAsync()));
                page.Popup = popup;
                Wait(page.ProcessPopupAsync(update));
                Check(page.Popup == null && Git(modulePath, "rev-parse", "HEAD") == (update ? moduleNew : moduleOld) &&
                    Git(leafPath, "rev-parse", "HEAD") == (update ? leafNew : leafOld), "Checkout choice also applies to " + name);
            }

            ResetToOld();
            File.WriteAllText(Path.Combine(path, "parent.txt"), "unsaved parent work");
            page.Popup = new Checkout(repo, Branch("next")) { DealWithLocalChanges = DealWithLocalChanges.DoNothing };
            Wait(page.ProcessPopupAsync(true));
            Check(page.Popup != null && Git(path, "branch", "--show-current") == "main" && Git(modulePath, "rev-parse", "HEAD") == moduleOld &&
                !repo.Logs[0].Content.Contains("submodule update"), "Failed checkout never starts a submodule update");
            Git(path, "restore", "parent.txt");
            ResetToOld();
            File.WriteAllText(Path.Combine(modulePath, "engine.cpp"), "unsaved module work");
            page.Popup = new Checkout(repo, Branch("next")) { DealWithLocalChanges = DealWithLocalChanges.DoNothing };
            Wait(page.ProcessPopupAsync(true));
            Check(page.Popup == null && Git(path, "branch", "--show-current") == "next" && repo.Logs[0].IsFailed &&
                File.ReadAllText(Path.Combine(modulePath, "engine.cpp")) == "unsaved module work",
                "Submodule update failure preserves edits and does not invite a duplicate checkout");
            Check(page.Notifications.Any(n => n.Message == SourceGit.App.Text("Checkout.SubmoduleUpdateFailed")),
                "Partial failure explicitly says that checkout completed but submodule update failed");
            Git(modulePath, "restore", "engine.cpp");
            ResetToOld();
            File.WriteAllText(Path.Combine(path, "parent.txt"), "unsaved parent work");
            page.Popup = new Checkout(repo, Branch("next")) { DealWithLocalChanges = DealWithLocalChanges.StashAndReapply };
            Wait(page.ProcessPopupAsync());
            Check(page.Popup == null && Git(path, "branch", "--show-current") == "next" && repo.Logs[0].IsFailed &&
                Git(path, "diff", "--name-only", "--diff-filter=U").Contains("parent.txt"),
                "A stash reapply conflict is not mislabeled as a successful checkout workflow");
            foreach (var tagsVisible in new[] { true, false })
            {
                prefs.ShowTagsInGraph = tagsVisible;
                var secondView = new SourceGit.Views.Repository { DataContext = repo };
                var secondWindow = new Window { Width = 1200, Height = 700, Content = secondView };
                try
                {
                    secondWindow.Show();
                    PumpFor(70);
                    Check(prefs.ShowTagsInGraph == tagsVisible, "Opening another repository view does not rewrite the Tags preference");
                    secondView.FindControl<RadioButton>("HistoryTagsShownToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(prefs.ShowTagsInGraph, "Tags click still updates the preference with one-way presentation bindings");
                    secondView.FindControl<RadioButton>("HistoryTagsHiddenToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!prefs.ShowTagsInGraph, "No Tags click still updates the preference with one-way presentation bindings");
                }
                finally { secondWindow.Close(); }
            }
        }
        finally
        {
            window.Close();
            repo.Close();
            launcherField.SetValue(Application.Current, previousLauncher);
            workspace.Repositories.Clear();
            workspace.Repositories.AddRange(previousRepositories);
            workspace.ActiveIdx = previousIndex;
            prefs.Locale = locale;
            prefs.DefaultFontSize = fontSize;
            prefs.ShowTagsInGraph = showTags;
            Application.Current.RequestedThemeVariant = theme;
        }
    }

    private static void TestChangeFileTypeTags()
    {
        var paths = new[] { "src/main.c", "src/ENGINE.CPP", "include/engine.h", "include/detail.hpp", "include/custom.hl", "include/inlines.inl",
            "readme.md", "Program.cs", "README", ".gitignore", "config/settings.json", "assets/picture.png", "file.verylongextension" };
        var changes = paths.Select(p => new Change { Path = p, WorkTree = ChangeState.Modified }).ToList();
        changes.Add(new Change { Path = "modules/engine", IsSubmodulePointerChange = true });
        var view = new SourceGit.Views.ChangeCollectionView { Changes = changes, ShowCommitIncludeCheckBoxes = true };
        var window = new Window { Content = view, Width = 720, Height = 600 };
        var oldTheme = Application.Current.RequestedThemeVariant;
        try
        {
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var mode in new[] { ChangeViewMode.Tree, ChangeViewMode.Grid, ChangeViewMode.List })
            {
                Application.Current.RequestedThemeVariant = theme;
                view.ViewMode = mode;
                PumpFor(100);
                var tags = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("file_type_tag") && b.IsEffectivelyVisible).ToArray();
                Check(tags.Length == paths.Length, "Only files, not folders or submodule pointers, have type tags: " + mode + "/" + theme);
                foreach (var tag in tags)
                {
                    var label = tag.GetVisualDescendants().OfType<TextBlock>().Single();
                    var change = tag.DataContext is Change c ? c : ((ChangeTreeNode)tag.DataContext).Change;
                    var expected = (IBrush)SourceGit.Converters.PathConverters.FileTypeBackground.Convert(new object[] { change.Path }, typeof(IBrush), null, null);
                    Check(((ISolidColorBrush)tag.Background).Color == ((ISolidColorBrush)expected).Color && tag.Bounds.Size == new Size(40, 18) &&
                        WorkspaceContrast(((ISolidColorBrush)tag.Background).Color, Colors.White) > 4.5,
                        "File type tags retain readable colors and compact fixed dimensions: " + change.Path);
                    var row = tag.FindAncestorOfType<ListBoxItem>();
                    Check(row.Bounds.Height == 24 && label.Bounds.Height <= tag.Bounds.Height,
                        "Type tags do not enlarge file rows or overlap labels");
                }
                foreach (var width in new[] { 720, 320 })
                {
                    window.Width = width;
                    PumpFor(50);
                    CaptureUxFrame(window, $"file-type-tags-{mode}-{width}-{theme}");
                }
                window.Width = 720;
            }
            foreach (var (path, expected) in new[] { ("file.c", "#B42332"), ("file.CPP", "#B42332"), ("file.h", "#245EB7"),
                ("file.hpp", "#245EB7"), ("file.hl", "#245EB7"), ("file.txt", "#656D78"), ("README", "#656D78") })
                Check(((ISolidColorBrush)SourceGit.Converters.PathConverters.FileTypeBackground.Convert(new object[] { path }, typeof(IBrush), null, null)).Color == Color.Parse(expected),
                    "File extensions use the requested category: " + path);
            Check((string)SourceGit.Converters.PathConverters.FileTypeLabel.Convert(".gitignore", typeof(string), null, null) == "FILE" &&
                (string)SourceGit.Converters.PathConverters.FileTypeLabel.Convert("config/.settings.json", typeof(string), null, null) == "JSON",
                "Dotfiles and extensionless files use sensible labels");
        }
        finally { window.Close(); Application.Current.RequestedThemeVariant = oldTheme; }
    }
}
