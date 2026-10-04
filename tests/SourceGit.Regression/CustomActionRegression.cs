using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void RunCustomActionChild()
    {
        File.WriteAllText("child.pid", Environment.ProcessId.ToString());
        Directory.CreateDirectory(".git/refs/remotes/origin");
        File.WriteAllText(".git/refs/remotes/origin/action-test", new string('a', 40));
        for (var i = 0; i < 4000; i++)
        {
            Console.Out.WriteLine("stdout " + i);
            Console.Error.WriteLine("stderr " + i);
        }
        File.WriteAllText("ready", string.Empty);
        var timeout = Stopwatch.StartNew();
        while (!File.Exists("release") && timeout.Elapsed < TimeSpan.FromSeconds(15))
            Thread.Sleep(20);
    }

    private static void TestCustomActions()
    {
        foreach (var cancel in new[] { false, true })
        {
            var path = Path.Combine(_root, cancel ? "custom action cancel" : "custom action success");
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            var childCommand = $"\"{Environment.ProcessPath}\"";
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                childCommand += $" \"{typeof(Program).Assembly.Location}\"";
            var bat = Path.Combine(path, "test action.bat");
            File.WriteAllText(bat, $"@echo off\r\n{childCommand} --custom-action-child\r\nexit /b %errorlevel%\r\n");

            var refreshes = new CustomActionRefreshProbe();
            using var watcher = new Watcher(refreshes, path, Path.Combine(path, ".git"));
            var repo = new Repository(false, path, Path.Combine(path, ".git"));
            typeof(Repository).GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(repo, watcher);
            var action = new CustomAction { Name = "Regression action", Executable = "cmd.exe", Arguments = $"/d /s /c \"\"{bat}\"\"" };
            var vm = new ExecuteCustomAction(repo, action, null);
            CommandLog log = null;
            try
            {
                PumpFor(100); // Allow the filesystem watcher to start.
                Check(Wait(vm.Sure()), "Custom action returns control while the process runs");
                log = repo.Logs[0];
                WaitUntil(() => File.Exists(Path.Combine(path, "ready")), "Custom action child did not start.");
                var heartbeat = 0;
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) => heartbeat++);
                timer.Start();
                try { PumpFor(1500); }
                finally { timer.Stop(); }
                Check(!log.IsComplete && heartbeat > 5, "UI dispatcher stays responsive while BAT waits");
                Check(refreshes.Count == 0, "BAT reference changes do not refresh the repository before the action finishes");
                Check(log.Content.Contains("stdout 3999") && log.Content.Contains("stderr 3999"),
                    "Custom action drains stdout and stderr concurrently under load");
                Check(log.CanCancel, "Running custom actions expose Cancel task");

                var childPid = int.Parse(File.ReadAllText(Path.Combine(path, "child.pid")));
                using var child = Process.GetProcessById(childPid);
                if (cancel)
                {
                    var elapsed = Stopwatch.StartNew();
                    log.Cancel();
                    Check(elapsed.Elapsed < TimeSpan.FromMilliseconds(500), "Cancel does not terminate the process tree on the UI thread");
                }
                else
                {
                    File.WriteAllText(Path.Combine(path, "release"), string.Empty);
                }

                WaitUntil(() => log.IsComplete, "Custom action did not complete.");
                Check(log.IsSuccessful == !cancel && !log.CanCancel, "Success and cancellation leave the log in its final state");
                Check(!cancel || log.StatusText == "Canceled", "Canceled custom actions are identified in the log");
                WaitUntil(() => child.HasExited, "Cancel left the BAT child process running.");
                Check(child.HasExited, "Custom action child has exited before returning to idle");
                WaitUntil(() => refreshes.Count > 0, "Watcher did not resume after the action.");
                Check(refreshes.Count > 0, "Pending reference changes refresh after success or cancellation");
            }
            finally
            {
                File.WriteAllText(Path.Combine(path, "release"), string.Empty);
                if (log != null && !log.IsComplete)
                {
                    log.Cancel();
                    WaitUntil(() => log.IsComplete, "Test custom action did not clean up.");
                }
                foreach (var window in _lifetime.Windows.OfType<SourceGit.Views.ViewLogs>().ToArray())
                    window.Close();
                vm.Cleanup();
            }
        }

        TestCustomActionStartupFailure();
    }

    private static void TestCustomActionStartupFailure()
    {
        var path = Path.Combine(_root, "custom action startup failure");
        Directory.CreateDirectory(Path.Combine(path, ".git", "refs", "remotes", "origin"));
        var refreshes = new CustomActionRefreshProbe();
        using var watcher = new Watcher(refreshes, path, Path.Combine(path, ".git"));
        var repo = new Repository(false, path, Path.Combine(path, ".git"));
        typeof(Repository).GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(repo, watcher);
        var action = new CustomAction { Name = "Missing executable", Executable = Path.Combine(path, "missing.exe") };
        try
        {
            Wait(new ExecuteCustomAction(repo, action, null).Sure());
            var failed = repo.Logs[0];
            WaitUntil(() => failed.IsComplete, "Startup failure left the action running.");
            Check(!failed.IsSuccessful && !failed.CanCancel, "Startup failure completes the custom action log");
            File.WriteAllText(Path.Combine(path, ".git", "refs", "remotes", "origin", "after-failure"), new string('b', 40));
            WaitUntil(() => refreshes.Count > 0, "Startup failure left the watcher locked.");
            Check(refreshes.Count > 0, "Startup failure releases the watcher");

            action = new CustomAction { Name = "Retry after failure", Executable = "cmd.exe", Arguments = "/d /c echo recovered" };
            Wait(new ExecuteCustomAction(repo, action, null).Sure());
            var retry = repo.Logs[0];
            WaitUntil(() => retry.IsComplete, "Retry after startup failure did not finish.");
            Check(retry.IsSuccessful && retry.Content.Contains("recovered"), "Another action works after startup failure without restarting the app");
        }
        finally
        {
            foreach (var window in _lifetime.Windows.OfType<SourceGit.Views.ViewLogs>().ToArray())
                window.Close();
        }
    }

    private static void WaitUntil(Func<bool> ready, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException(message);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private sealed class CustomActionRefreshProbe : IRepository
    {
        public int Count => Volatile.Read(ref _count);
        public bool MayHaveSubmodules() => false;
        public void RefreshBranches() => Interlocked.Increment(ref _count);
        public void RefreshWorktrees() { }
        public void RefreshTags() { }
        public void RefreshCommits() { }
        public void RefreshSubmodules(bool force = false) { }
        public void RefreshWorkingCopyChanges() { }
        public void RefreshStashes() { }
        private int _count;
    }
}
