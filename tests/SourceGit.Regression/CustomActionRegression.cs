using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void RunCustomActionChild(string scenario = "normal")
    {
        File.WriteAllText("child.pid", Environment.ProcessId.ToString());
        Directory.CreateDirectory(".git/refs/remotes/origin");
        File.WriteAllText(".git/refs/remotes/origin/action-test", new string('a', 40));
        if (scenario == "continuous")
        {
            File.WriteAllText("ready", string.Empty);
            var duration = Stopwatch.StartNew();
            var progress = new string('p', 4096);
            while (!File.Exists("release") && duration.Elapsed < TimeSpan.FromSeconds(15))
            {
                Console.Out.Write(progress);
                Console.Error.Write(progress);
            }
            return;
        }
        if (scenario == "flood")
        {
            Console.Out.Write(new string('x', 4 * 1024 * 1024));
            Console.Error.Write(new string('e', 4 * 1024 * 1024));
            Console.Out.WriteLine();
            Console.Error.WriteLine();
        }
        var lines = scenario == "flood" ? 100000 : scenario is "inherited-pipe" or "quiet" ? 0 : 4000;
        for (var i = 0; i < lines; i++)
        {
            Console.Out.WriteLine("stdout " + i);
            Console.Error.WriteLine("stderr " + i);
        }
        File.WriteAllText("ready", string.Empty);
        var timeout = Stopwatch.StartNew();
        while (!File.Exists("release") && timeout.Elapsed < TimeSpan.FromSeconds(scenario == "quiet" ? 60 : 15))
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
                // These tests pump jobs without running a native dispatcher timer loop.
                var timer = new System.Threading.Timer(_ => Dispatcher.UIThread.Post(() => heartbeat++), null, 0, 20);
                try { PumpFor(1500); }
                finally { timer.Dispose(); }
                Check(!log.IsComplete && heartbeat > 5, $"UI dispatcher stays responsive while BAT waits (ticks={heartbeat}, completed={log.IsComplete})");
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
        TestBoundedCommandLog();
        TestCustomActionStress("flood");
        TestCustomActionStress("continuous");
        TestCustomActionStress("inherited-pipe");
        TestCustomActionStress("quiet");
    }

    private static void TestBoundedCommandLog()
    {
        var log = new CommandLog("Flooded log");
        var receiver = new LogBatchProbe();
        log.Subscribe(receiver);
        log.AppendLine("$ git fetch --prune --recurse-submodules");
        var longLine = new string('x', 1024 * 1024);
        for (var i = 0; i < 100; i++)
            log.AppendLine(longLine);
        Check((int)typeof(CommandLog).GetField("_pendingLength", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(log) <= 64 * 1024,
            "Unrendered log output is bounded even while the UI cannot pump");
        log.AppendLine("final output");
        log.Complete(true);
        log.Complete();
        PumpFor(150);
        Check(log.IsSuccessful && log.Content.Contains("final output") && log.Content.Contains("truncated"),
            "Completing twice is harmless and preserves the tail and truncation notice");
        Check(receiver.Batches == 1 && receiver.LargestBatch < 66 * 1024,
            "A flooded log never sends an unbounded batch to the editor");
        Check(log.LatestCommand == "git fetch --prune --recurse-submodules",
            "Copy current command survives truncation of pending log output");

        var editor = new SourceGit.Views.CommandLogContentPresenter { Width = 700, Height = 200 };
        var window = new Window { Width = 720, Height = 240, Content = editor };
        try
        {
            window.Show();
            editor.Text = "prefix with indentation    ! [rejected] branch\n! [remote rejected]";
            PumpFor(100);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Check(frame != null, "Indented and exact-length rejected messages render without invalid highlight ranges");
        }
        finally { window.Close(); }
    }

    private static void TestCustomActionStress(string scenario)
    {
        var path = Path.Combine(_root, "custom action " + scenario);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        var childCommand = $"\"{Environment.ProcessPath}\"";
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            childCommand += $" \"{typeof(Program).Assembly.Location}\"";
        var bat = Path.Combine(path, "stress.bat");
        var launch = scenario == "inherited-pipe" ? "start \"\" /b " : string.Empty;
        File.WriteAllText(bat, $"@echo off\r\n{launch}{childCommand} --custom-action-child {scenario}\r\nexit /b 0\r\n");
        var refreshes = new CustomActionRefreshProbe();
        using var watcher = new Watcher(refreshes, path, Path.Combine(path, ".git"));
        var repo = new Repository(false, path, Path.Combine(path, ".git"));
        typeof(Repository).GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(repo, watcher);
        var action = new CustomAction { Name = scenario, Executable = "cmd.exe", Arguments = $"/d /s /c \"\"{bat}\"\"" };
        CommandLog log = null;
        Process child = null;
        try
        {
            PumpFor(100);
            Wait(new ExecuteCustomAction(repo, action, null).Sure());
            log = repo.Logs[0];
            var heartbeat = 0;
            var timer = new System.Threading.Timer(_ => Dispatcher.UIThread.Post(() => heartbeat++), null, 0, 20);
            var elapsed = Stopwatch.StartNew();
            try
            {
                WaitUntil(() => File.Exists(Path.Combine(path, "ready")), "Stress child did not become ready.");
                if (scenario == "continuous")
                    PumpFor(600);
            }
            finally { timer.Dispose(); }
            child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(path, "child.pid"))));

            if (scenario is "flood" or "continuous")
            {
                PumpFor(200);
                Check(heartbeat > 5 && elapsed.Elapsed < TimeSpan.FromSeconds(10),
                    "UI keeps ticking while stdout and stderr emit millions of characters without newlines");
                Check(log.Content.Length < 300 * 1024 && log.Content.Contains("truncated"),
                    "Flooded action log stays bounded and reports discarded output");
                foreach (var window in _lifetime.Windows.OfType<SourceGit.Views.ViewLogs>().ToArray())
                    window.Close();
                log.Cancel();
                WaitUntil(() => log.IsComplete, "Flooded action did not cancel after closing its log window.");
                Check(!log.IsSuccessful && log.StatusText == "Canceled", "Flooded action can be canceled with its log window closed");
            }
            else if (scenario == "quiet")
            {
                WaitUntil(() => log.Content.Contains("No output for"), "Silent action did not report its wait.", 35);
                Check(!log.IsComplete && log.CanCancel && !child.HasExited,
                    "Silence reports a possible network or input wait without killing legitimate long fetches");
                var canceledAt = Stopwatch.StartNew();
                log.Cancel();
                Check(canceledAt.Elapsed < TimeSpan.FromMilliseconds(500), "Cancel remains responsive after a prolonged silent wait");
                WaitUntil(() => log.IsComplete && child.HasExited, "Silent action did not cancel.");
            }
            else
            {
                WaitUntil(() => log.IsComplete, "Inherited child pipe kept the action running.");
                Check(log.IsSuccessful && !child.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(8),
                    "Parent exit is detected without waiting for an inherited child pipe to close");
                Check(log.Content.Contains("kept its output pipe open"), "Inherited pipe timeout is explained in the log");
            }
            WaitUntil(() => refreshes.Count > 0, "Stress action left the watcher locked.");
            Check(refreshes.Count > 0, "Watcher resumes after flood cancellation or inherited pipe timeout");
        }
        finally
        {
            File.WriteAllText(Path.Combine(path, "release"), string.Empty);
            if (log is { IsComplete: false })
            {
                log.Cancel();
                WaitUntil(() => log.IsComplete, "Stress action cleanup did not finish.");
            }
            if (child != null)
            {
                WaitUntil(() => child.HasExited, "Stress child did not exit.");
                child.Dispose();
            }
            foreach (var window in _lifetime.Windows.OfType<SourceGit.Views.ViewLogs>().ToArray())
                window.Close();
        }
    }

    private sealed class LogBatchProbe : ICommandLogReceiver
    {
        public int Batches;
        public int LargestBatch;
        public void OnReceiveCommandLog(string content) { Batches++; LargestBatch = Math.Max(LargestBatch, content.Length); }
        public void OnResetCommandLog(string content) => OnReceiveCommandLog(content);
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

    private static void WaitUntil(Func<bool> ready, string message, int timeoutSeconds = 10)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
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
