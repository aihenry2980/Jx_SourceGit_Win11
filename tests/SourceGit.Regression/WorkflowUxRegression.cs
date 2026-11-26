using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.ViewModels;
using Popup = Avalonia.Controls.Primitives.Popup;

internal static partial class Program
{
    private static void TestWorkflowUx()
    {
        TestCommitScopePresentation();
        TestBackgroundTaskPresentation();
        TestRecursiveFetchFlyout();
        TestRecursiveFetchProgressAndRetry();
    }

    private static void TestCommitScopePresentation()
    {
        var path = NewRepository("commit scope");
        File.WriteAllText(Path.Combine(path, "one.txt"), "before");
        File.WriteAllText(Path.Combine(path, "two.txt"), "before");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "initial");
        File.WriteAllText(Path.Combine(path, "one.txt"), "after");
        File.WriteAllText(Path.Combine(path, "two.txt"), "after");
        var repo = Open(path);
        try
        {
            var flow = repo.SubmoduleCommitFlow;
            Wait(flow.RefreshAsync());
            for (var i = 0; i < 100 && (flow.IsLoading || flow.Changes.Count != 2); i++)
                PumpFor(50);
            Check(flow.Changes.Count == 2 && flow.IncludedChangeCount == 2, "Commit Flow loads changed files");
            flow.Changes[0].IsCommitFlowIncluded = false;
            flow.NotifyCommitIncludeChanged();
            Check(flow.CommitButtonText == "Commit Selected (1)" && flow.CommitAndPushButtonText == "Commit & Push (1)",
                "Both commit buttons count included files, not highlighted or skipped files");
            Check(flow.CommitTargetSummary.Contains("Branch: main") && flow.CommitTargetSummary.Contains("No upstream target"),
                "Always-visible commit target includes repository, branch and missing push target");
            Check(flow.CommitAvailabilityText.Contains("no push-capable remote"), "Push disabled reason is exposed inline");
            flow.CommitMessage = "";
            Check(flow.CommitAvailabilityText.Contains("Enter a commit message"), "Commit disabled reason takes precedence over push configuration");
            flow.CommitMessage = "Test selected commit";
            flow.SelectedNode.Branch = "feature/AW2020TE-21356/long-submodule-branch-name-for-layout-verification";
            flow.NotifyCommitIncludeChanged();
            flow.IsCommitPlanExpanded = true;

            var view = new SourceGit.Views.SubmoduleCommitFlow { DataContext = flow };
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            foreach (var width in new[] { 1280, 920 })
            {
                window.Width = width;
                PumpFor(150);
                var buttons = view.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Content is TextBlock t && (t.Text == flow.CommitButtonText || t.Text == flow.CommitAndPushButtonText)).ToList();
                Check(buttons.Count == 2, "Commit scope buttons render at width " + width);
                foreach (var button in buttons)
                {
                    var pos = button.TranslatePoint(default, view).Value;
                    Check(pos.X >= 0 && pos.X + button.Bounds.Width <= view.Bounds.Width + 1 &&
                        pos.Y + button.Bounds.Height <= view.Bounds.Height + 1, "Commit button stays inside available panel");
                }
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                frame.Save(Path.Combine(_root, $"commit-scope-{width}.png"));
            }
            window.Close();

            var toolbar = new SourceGit.Views.RepositoryToolbar { DataContext = repo };
            toolbar.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            window = new Window { Content = toolbar, Width = 1700, Height = 100 };
            window.Show();
            PumpFor(100);
            var fetchButtons = toolbar.FindControl<StackPanel>("FetchToolbarGroup").Children
                .OfType<Button>().Where(b => b.IsVisible).Select(b => b.Name).ToArray();
            Check(fetchButtons.SequenceEqual(new[] { "FetchButton", "FetchWithSubmodulesButton" }),
                "Fetch group contains only standard Fetch and Fetch Subs, without a redundant All button");
            foreach (var (name, label) in new[] { ("FetchWithSubmodulesButton", "Fetch + Subs"),
                ("FetchButton", "Fetch…"), ("PullButton", "Pull…"), ("PullTopRepositoryButton", "Pull Top"),
                ("PullWithSubmodulesButton", "Pull + Subs"), ("UpdateSubmodulesRecursivelyButton", "Update Subs") })
            {
                var button = toolbar.FindControl<Button>(name);
                var text = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label);
                Check(text.Bounds.Width + 8 <= button.Bounds.Width + 1, label + " has room for its complete label");
            }
            var options = toolbar.FindControl<Button>("PullSubmoduleOptionsButton");
            var launcherField = typeof(SourceGit.App).GetField("_launcher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var previousLauncher = launcherField.GetValue(Application.Current);
            var workspace = SourceGit.ViewModels.Preferences.Instance.GetActiveWorkspace();
            var previousRepositories = workspace.Repositories.ToArray();
            var previousIndex = workspace.ActiveIdx;
            workspace.Repositories.Clear();
            var launcher = new SourceGit.ViewModels.Launcher(null);
            launcher.Pages.Add(new SourceGit.ViewModels.LauncherPage(new SourceGit.ViewModels.RepositoryNode { Id = path }, repo));
            launcherField.SetValue(Application.Current, launcher);
            try
            {
                options.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var menu = (ContextMenu)typeof(SourceGit.Views.RepositoryToolbar)
                    .GetField("_activeToolbarGitCommandMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(toolbar);
                Check(menu != null && menu.IsOpen && menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, SourceGit.App.Text("Toolbar.ChooseSubmodules"))),
                    "Visible dropdown exposes submodule selection without right-clicking");
                menu.Close();
            }
            finally
            {
                launcherField.SetValue(Application.Current, previousLauncher);
                workspace.Repositories.Clear();
                workspace.Repositories.AddRange(previousRepositories);
                workspace.ActiveIdx = previousIndex;
            }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame())
                frame.Save(Path.Combine(_root, "toolbar-scopes.png"));
            window.Width = 920;
            window.Height = 160;
            PumpFor(150);
            foreach (var name in new[] { "FetchButton", "FetchWithSubmodulesButton", "PullButton", "PullTopRepositoryButton",
                "PullWithSubmodulesButton", "PullSubmoduleOptionsButton", "UpdateSubmodulesRecursivelyButton" })
            {
                var button = toolbar.FindControl<Button>(name);
                var position = button.TranslatePoint(default, toolbar).Value;
                Check(position.X >= 0 && position.X + button.Bounds.Width <= toolbar.Bounds.Width + 1 &&
                    position.Y + button.Bounds.Height <= window.ClientSize.Height + 1, name + " stays visible when the toolbar wraps at 920");
            }
            CaptureUxFrame(window, "toolbar-scopes-920");
            Check(toolbar.FindControl<StackPanel>("LeftToolbarGroup").Bounds.Height < 60,
                "Wrapped toolbar keeps the current branch chip and outer controls aligned to the first row");
            window.Close();

            flow.ExcludeAllSelectedNodeChanges();
            Check(flow.CommitButtonText == "Commit Selected (0)" && !flow.CanCommitSelectedNode &&
                flow.CommitAvailabilityText.Contains("All changes are skipped"), "Skipping everything updates count and inline reason");
        }
        finally
        {
            repo.Close();
        }
    }

    private static void TestBackgroundTaskPresentation()
    {
        var log = new CommandLog("Fetch Subs");
        var task = new RepositoryBackgroundTask(log);
        var canceled = false;
        log.SetCancelAction(() => canceled = true);
        Check(task.IsRunning && task.CanCancel && task.IsIndeterminate, "New background task is immediately visible and cancelable");
        task.Update("network/protocol | origin", 12, 30);
        Check(task.Summary.Contains("12/30") && !task.IsIndeterminate && task.Detail.Contains("network/protocol"),
            "Structured task progress shows current repository and completed count");
        var view = new SourceGit.Views.RepositoryBackgroundTask { DataContext = task };
        var window = new Window { Width = 560, Height = 100, Content = view };
        window.Show();
        PumpFor(100);
        Check(view.GetVisualDescendants().OfType<ProgressBar>().Single().Value == 12, "Progress bar binds repository count");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var frame = window.CaptureRenderedFrame())
            frame.Save(Path.Combine(_root, "background-progress.png"));
        log.Cancel();
        Check(canceled && !task.CanCancel && task.Summary.Contains("Canceling"), "Cancel feedback is immediate and prevents duplicate clicks");
        log.Complete(false);
        Check(!task.IsRunning && !task.IsFailed && task.Summary.Contains("Canceled"), "Cancellation is not presented as failure");
        window.Close();

        var failedLog = new CommandLog("Fetch Subs");
        var failed = new RepositoryBackgroundTask(failedLog);
        var attempts = 0;
        var gate = new TaskCompletionSource();
        failed.SetRetryAction(async () => { attempts++; await gate.Task; });
        failedLog.Complete(false);
        Check(failed.IsFailed && failed.CanRetry, "Failed task retains a retry action");
        var retry = failed.RetryAsync();
        Wait(failed.RetryAsync());
        Check(attempts == 1 && !failed.CanRetry, "Retry cannot be double-started");
        gate.SetResult();
        Wait(retry);

        var path = NewRepository("selected task log");
        var repo = new Repository(false, path, Path.Combine(path, ".git"));
        var older = repo.CreateLog("Older failed task");
        older.Complete(false);
        repo.CreateLog("Newer successful task").Complete(true);
        var logs = new ViewLogs(repo) { SelectedLog = older };
        var logWindow = new SourceGit.Views.ViewLogs { DataContext = logs };
        logWindow.Show();
        PumpFor(100);
        Check(ReferenceEquals(logs.SelectedLog, older), "Opening a task log preserves the requested task instead of selecting the newest log");
        logWindow.Close();
    }

    private static void TestRecursiveFetchFlyout()
    {
        var path = NewRepository("fetch flyout");
        Git(path, "commit", "--allow-empty", "-m", "initial");
        var repo = Open(path);
        var log = repo.CreateLog("Fetch and Prune All Repositories");
        var task = (RepositoryBackgroundTask)Invoke(repo, "TrackBackgroundTask", log);
        typeof(Repository).GetProperty(nameof(Repository.RecursiveFetchTask)).SetValue(repo, task);
        var canceled = false;
        log.SetCancelAction(() => canceled = true);
        task.UpdateRepository("modules/network/protocol", path + "/modules/network/protocol", "origin");
        task.Update("modules/network/protocol | origin", 12, 30);
        task.UpdateOutput("Receiving objects: 42% (420/1000), 1.24 MiB | 650.00 KiB/s");
        var toolbar = new SourceGit.Views.RepositoryToolbar { DataContext = repo };
        var content = new SourceGit.Views.Repository { DataContext = repo };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(toolbar);
        Grid.SetRow(content, 1);
        grid.Children.Add(content);
        var window = new Window { Content = grid, Width = 1280, Height = 760 };
        var previousTheme = Application.Current.RequestedThemeVariant;
        try
        {
            window.Show();
            repo.IsRecursiveFetchProgressVisible = true;
            PumpFor(100);
            var popup = toolbar.FindControl<Popup>("RecursiveFetchProgressPopup");
            var panel = toolbar.FindControl<SourceGit.Views.RecursiveFetchProgress>("FetchProgressPanel");
            var surface = panel.FindControl<Border>("ProgressSurface");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var width in new[] { 1280, 720 })
            {
                Application.Current.RequestedThemeVariant = theme;
                window.Width = width;
                PumpFor(80);
                Check(popup.IsOpen && ReferenceEquals(panel.FindControl<StackPanel>("ProgressDetails").DataContext, task), "Fetch progress opens below the toolbar without a modal window");
                var background = (ISolidColorBrush)surface.Background;
                Check(background.Color.A == 0xD9 && surface.Opacity == 1,
                    "Fetch surface uses a more transparent 85 percent background without fading text or controls");
                var name = panel.FindControl<TextBlock>("FetchProgressRepositoryName");
                var highlight = panel.FindControl<Border>("FetchProgressNameHighlight");
                Check(name.FontSize == 20 && name.FontWeight == FontWeight.Bold && name.Opacity == 1 &&
                    highlight.BorderThickness.Left == 3 && highlight.Background != null,
                    "Current repository name is large, bold and highlighted without losing text contrast");
                Check(!panel.FindControl<TextBlock>("FetchProgressCountdown").IsEffectivelyVisible,
                    "Auto-close countdown stays hidden while fetch is running");
                Check(panel.FindControl<TextBlock>("FetchProgressRepositoryName").Text == task.RepositoryName &&
                    panel.FindControl<SelectableTextBlock>("FetchProgressRepositoryPath").Text == task.RepositoryPath &&
                    panel.FindControl<TextBlock>("FetchProgressOutput").Text.Contains("42%"),
                    "Floating progress shows current submodule name, selectable full path and actual Git output");
                var bar = panel.FindControl<ProgressBar>("FetchProgressBar");
                Check(!bar.IsIndeterminate && bar.Value == 12 && bar.Maximum == 30,
                    "Floating progress uses actual completed repository counts");
                var topLeft = panel.TranslatePoint(default, window).Value;
                Check(topLeft.X >= 0 && topLeft.X + panel.Bounds.Width <= window.Bounds.Width + 1 &&
                    topLeft.Y >= toolbar.Bounds.Bottom,
                    "Fetch float stays inside the window and below all wrapped toolbar rows at narrow widths");
                CaptureUxFrame(window, $"fetch-float-{theme}-{width}");
            }
            window.MouseDown(new Point(10, window.Height - 10), MouseButton.Left);
            window.MouseUp(new Point(10, window.Height - 10), MouseButton.Left);
            Check(popup.IsOpen && task.IsRunning, "Using the repository does not dismiss or cancel the nonmodal fetch float");
            task.UpdateRepository("modules/auth", path + "/modules/auth", "upstream");
            task.Update("modules/auth | upstream", 13, 30);
            var previousSize = panel.Bounds.Size;
            PumpFor(30);
            Check(panel.FindControl<TextBlock>("FetchProgressRepositoryName").Text == "modules/auth" &&
                string.IsNullOrEmpty(panel.FindControl<TextBlock>("FetchProgressOutput").Text),
                "Changing repository clears the previous transfer's output instead of showing stale progress");
            Check(panel.Bounds.Size == previousSize, "Clearing Git output between repositories does not resize the float");
            panel.FindControl<Button>("FetchProgressCloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(30);
            Check(!popup.IsOpen && task.IsRunning && !canceled, "Hiding the float leaves the background fetch running");
            repo.IsRecursiveFetchProgressVisible = true;
            PumpFor(30);
            toolbar.DataContext = null;
            PumpFor(30);
            Check(!popup.IsOpen && task.IsRunning, "Switching repository contexts hides old fetch progress without stopping it");
            toolbar.DataContext = repo;
            PumpFor(30);
            Check(popup.IsOpen, "Returning to a running repository restores its floating progress");
            task.Update("Discovering initialized nested submodules...", 0, 0);
            Check(panel.FindControl<ProgressBar>("FetchProgressBar").IsIndeterminate,
                "Discovery shows activity rather than a fabricated percentage before the repository total is known");
            panel.FindControl<Button>("FetchProgressCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(canceled && !task.CanCancel, "The float has a distinct cancel action with immediate feedback");
            log.Complete(false);
            PumpFor(30);
            Check(!panel.FindControl<Button>("FetchProgressCancelButton").IsEffectivelyVisible, "Completed fetches do not offer a stale cancel action");
            Check(!panel.FindControl<ProgressBar>("FetchProgressBar").IsIndeterminate,
                "Canceling before discovery finishes also stops the indeterminate animation");
            var autoClose = (Task)Invoke(repo, "HideRecursiveFetchProgressLaterAsync", task);
            PumpFor(30);
            var countdown = panel.FindControl<TextBlock>("FetchProgressCountdown");
            Check(repo.RecursiveFetchCloseSeconds == 5 && countdown.IsEffectivelyVisible && countdown.Text == "Closing in 5s",
                "Finished progress immediately shows a visible five-second auto-close countdown");
            CaptureUxFrame(window, "fetch-float-countdown-dark");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            PumpFor(30);
            CaptureUxFrame(window, "fetch-float-countdown-light");
            PumpFor(1100);
            Check(repo.RecursiveFetchCloseSeconds is > 0 and < 5 && countdown.Text == $"Closing in {repo.RecursiveFetchCloseSeconds}s",
                "Countdown text updates each second without resizing the floating panel");
            var nextLog = repo.CreateLog("Next fetch");
            var nextTask = (RepositoryBackgroundTask)Invoke(repo, "TrackBackgroundTask", nextLog);
            typeof(Repository).GetProperty(nameof(Repository.RecursiveFetchTask)).SetValue(repo, nextTask);
            nextTask.UpdateRepository("next/repository", path + "/next/repository");
            Wait(autoClose);
            PumpFor(30);
            Check(repo.RecursiveFetchCloseSeconds == 0 && !countdown.IsEffectivelyVisible && popup.IsOpen && nextTask.IsRunning,
                "Starting a new fetch clears the countdown and prevents the previous timer from closing it");
        }
        finally
        {
            window.Close();
            repo.Close();
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }

    private static void TestRecursiveFetchProgressAndRetry()
    {
        var seed = NewRepository("fetch seed");
        Git(seed, "commit", "--allow-empty", "-m", "initial");
        var path = NewRepository("fetch progress");
        Git(path, "-c", "protocol.file.allow=always", "submodule", "add", seed, "modules/good");
        Git(path, "-c", "protocol.file.allow=always", "submodule", "add", seed, "modules/bad");
        Git(path, "commit", "-am", "add modules");
        Git(path, "remote", "add", "origin", path);
        var bad = Path.Combine(path, "modules", "bad");
        Git(bad, "remote", "set-url", "origin", Path.Combine(_root, "missing-remote"));
        var repo = Open(path);
        var closed = false;
        try
        {
            var closeSeconds = new List<int>();
            repo.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Repository.RecursiveFetchCloseSeconds))
                    closeSeconds.Add(repo.RecursiveFetchCloseSeconds);
            };
            var operation = repo.FetchAndPruneAllRepositoriesInBackgroundAsync();
            var progress = repo.BackgroundTasks.Single();
            Check(progress.IsRunning && repo.IsBackgroundRecursiveFetchRunning, "Recursive fetch starts task strip and toolbar spinner synchronously");
            Check(ReferenceEquals(repo.RecursiveFetchTask, progress) && repo.IsRecursiveFetchProgressVisible && progress.RepositoryPath == repo.FullPath,
                "Recursive fetch publishes its root location and opens floating progress immediately");
            var locations = new List<string> { progress.RepositoryPath };
            progress.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RepositoryBackgroundTask.RepositoryPath))
                    locations.Add(progress.RepositoryPath);
            };
            repo.IsRecursiveFetchProgressVisible = false;
            Wait(repo.FetchAndPruneAllRepositoriesInBackgroundAsync());
            Check(repo.IsRecursiveFetchProgressVisible && ReferenceEquals(repo.RecursiveFetchTask, progress) && repo.BackgroundTasks.Count == 1,
                "Clicking fetch while busy reopens progress without starting another operation");
            Wait(operation);
            Check(locations.Contains((path + "/modules/good").Replace('\\', '/')) && locations.Contains(bad.Replace('\\', '/')),
                "Real fetch progress follows both successful and failing nested repositories");
            Check(progress.TotalCount == 3 && progress.CompletedCount == 3 && progress.IsFailed && progress.CanRetry,
                "Recursive fetch counts top repository plus both submodules, retaining failed targets");
            Check(progress.Detail.Contains("2 succeeded") && progress.Detail.Contains("1 failed"), "Task summary distinguishes partial success");
            Check(progress.Log.Content.Split("=== Refresh history once ===").Length == 2, "Recursive fetch refreshes history once");
            Check(repo.RecursiveFetchCloseSeconds == 5, "Real fetch completion starts the auto-close countdown");
            PumpFor(5200);
            Check(repo.BackgroundTasks.Contains(progress), "Failure stays visible beyond success auto-dismiss timeout");
            Check(!repo.IsRecursiveFetchProgressVisible, "Completed floating progress closes after five seconds while failures remain in the task strip");
            Check(closeSeconds.SequenceEqual(new[] { 5, 4, 3, 2, 1, 0 }) && repo.RecursiveFetchCloseSeconds == 0,
                "Auto-close counts down every second and clears itself on dismissal");

            Git(seed, "commit", "--allow-empty", "-m", "new remote commit");
            Git(bad, "remote", "set-url", "origin", seed);
            Git(path, "remote", "set-url", "origin", Path.Combine(_root, "do-not-fetch-root"));
            Wait(repo.RetryBackgroundTaskAsync(progress));
            var retried = repo.BackgroundTasks.Single();
            Check(retried.Log.IsSuccessful && retried.TotalCount == 1 && retried.CompletedCount == 1,
                "Retry fetches only failed repositories and can succeed while root remote is unavailable");
            Check(!retried.Log.Content.Contains("modules/good") && !retried.Log.Content.Contains("`root`"),
                "Retry does not re-fetch successful root or siblings");
            Check(Git(bad, "rev-parse", "refs/remotes/origin/main") == Git(seed, "rev-parse", "HEAD"),
                "Retried remote ref is actually updated");
            PumpFor(5200);
            Check(repo.BackgroundTasks.Count == 0, "Successful result automatically disappears after five seconds");

            var failureLog = repo.CreateLog("Failure to dismiss");
            var failureTask = (RepositoryBackgroundTask)Invoke(repo, "TrackBackgroundTask", failureLog);
            failureLog.Complete(false);
            repo.DismissBackgroundTask(failureTask);
            Check(repo.BackgroundTasks.Count == 0, "Failed result can be explicitly dismissed");

            var cancelOperation = repo.FetchAndPruneAllRepositoriesInBackgroundAsync();
            var cancelTask = repo.BackgroundTasks.Single();
            repo.DismissBackgroundTask(cancelTask);
            Check(repo.BackgroundTasks.Contains(cancelTask), "Running task cannot be dismissed accidentally");
            cancelTask.Log.Cancel();
            Wait(cancelOperation);
            Check(cancelTask.Log.IsCancellationRequested && !cancelTask.IsFailed && !cancelTask.CanCancel &&
                !repo.IsBackgroundRecursiveFetchRunning, "Canceling real recursive fetch releases busy state and retains canceled result");
            WaitUntil(() => repo.Submodules.Count == 2 && !repo.IsSubmodulesLoading, "Submodule tree did not settle before close test");
            repo.RefreshSubmodules(true);
            // Let Git finish while its UI callbacks stay queued, then close before they can run.
            Thread.Sleep(500);
            repo.Close();
            closed = true;
            PumpFor(1000);
            Check(repo.Submodules.Count == 0 && repo.VisibleSubmodules == null && !repo.IsSubmodulesLoading,
                "Queued submodule refresh cannot access disposed repository state or restore a closed sidebar");
            repo.RefreshSubmodules(true);
            PumpFor(100);
            Check(!repo.IsSubmodulesLoading && repo.Submodules.Count == 0,
                "Refreshing a closed repository does not start another submodule query");
            repo.Open();
            closed = false;
            WaitUntil(() => repo.Submodules.Count == 2 && !repo.IsSubmodulesLoading, "Reopened submodule tree did not load");
            Check(repo.VisibleSubmodules != null, "Reopening the same repository accepts fresh submodule results");
        }
        finally
        {
            if (!closed)
                repo.Close();
        }
    }
}
