using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestWorkflowUx()
    {
        TestCommitScopePresentation();
        TestBackgroundTaskPresentation();
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
            foreach (var (name, label) in new[] { ("FetchWithSubmodulesButton", "Fetch Subs"),
                ("PullWithSubmodulesButton", "Pull + Subs"), ("UpdateSubmodulesRecursivelyButton", "Update Subs") })
            {
                var button = toolbar.FindControl<Button>(name);
                var text = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label);
                Check(text.Bounds.Width + 8 <= button.Bounds.Width + 1, label + " has room for its complete label");
            }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame())
                frame.Save(Path.Combine(_root, "toolbar-scopes.png"));
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
        try
        {
            var operation = repo.FetchAndPruneAllRepositoriesInBackgroundAsync();
            var progress = repo.BackgroundTasks.Single();
            Check(progress.IsRunning && repo.IsBackgroundRecursiveFetchRunning, "Recursive fetch starts task strip and toolbar spinner synchronously");
            Wait(operation);
            Check(progress.TotalCount == 3 && progress.CompletedCount == 3 && progress.IsFailed && progress.CanRetry,
                "Recursive fetch counts top repository plus both submodules, retaining failed targets");
            Check(progress.Detail.Contains("2 succeeded") && progress.Detail.Contains("1 failed"), "Task summary distinguishes partial success");
            Check(progress.Log.Content.Split("=== Refresh history once ===").Length == 2, "Recursive fetch refreshes history once");
            PumpFor(5200);
            Check(repo.BackgroundTasks.Contains(progress), "Failure stays visible beyond success auto-dismiss timeout");

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
        }
        finally
        {
            repo.Close();
        }
    }
}
