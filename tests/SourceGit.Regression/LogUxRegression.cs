using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestLogUx()
    {
        var repo = new Repository(false, _root, Path.Combine(_root, ".git"));
        var running = repo.CreateLog("Fetch submodules");
        var cancelCount = 0;
        running.SetCancelAction(() => cancelCount++);
        var succeeded = repo.CreateLog("Successful fetch");
        succeeded.Complete(true);
        var failed = repo.CreateLog("Rejected push");
        failed.Complete(false);
        var unknown = repo.CreateLog("Legacy operation");
        unknown.Complete();
        Check(running.StatusText == "Running" && succeeded.StatusText == "Succeeded" && failed.StatusText == "Failed" && unknown.StatusText == "Result unknown",
            "Logs distinguish running, success, explicit failure and unknown results");
        Check(failed.IsFailed && !unknown.IsFailed && !succeeded.IsFailed, "Unknown results are never reported as failures");

        var vm = new ViewLogs(repo) { SelectedLog = failed };
        var window = new SourceGit.Views.ViewLogs { DataContext = vm };
        window.Show();
        try
        {
            vm.RemoveLog(running);
            Check(repo.Logs.Contains(running) && running.CanCancel, "Deleting a running log leaves its cancellation entry intact");
            vm.ClearAll();
            Check(repo.Logs.Count == 1 && vm.SelectedLog == running && running.CanCancel,
                "Clear completed keeps running logs and selects a running task");
            Invoke(window, "OnLogKeyDown", null, new KeyEventArgs { Key = Key.Delete });
            Check(repo.Logs.Contains(running), "Delete key cannot remove a running task");
            Invoke(window, "OnLogKeyDown", null, new KeyEventArgs { Key = Key.Back });
            Check(repo.Logs.Contains(running), "Backspace cannot remove a running task");
            PumpFor(100);
            var row = window.GetVisualDescendants().OfType<Grid>().First(grid => grid.DataContext == running && grid.Background != null);
            Invoke(window, "OnLogContextRequested", row, new ContextRequestedEventArgs());
            var menu = (ContextMenu)typeof(SourceGit.Views.ViewLogs).GetField("_logContextMenu", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            var delete = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, SourceGit.App.Text("ViewLogs.Delete")));
            var cancel = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, SourceGit.App.Text("ViewLogs.CancelTask")));
            Check(menu.IsOpen && !delete.IsEnabled && cancel.IsEnabled, "Running log context menu disables Delete and offers Cancel task");
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(repo.Logs.Contains(running), "The deletion handler also guards against a stale menu click");
            menu.Close();
            var cancelButton = window.FindControl<Button>("CancelTaskButton");
            Check(cancelButton.IsEnabled && cancelButton.Classes.Contains("primary") && !window.FindControl<Button>("ClearCompletedButton").Classes.Contains("primary"),
                "Cancel is prominent while log cleanup is secondary");
            cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(30);
            Check(cancelCount == 1 && !running.CanCancel && !cancelButton.IsEnabled && running.StatusText == "Canceling...",
                "Cancellation is requested once and immediately shows Canceling");
            vm.ClearAll();
            Check(repo.Logs.Contains(running), "Canceling tasks are retained until termination completes");
            running.Complete(false);
            Check(running.StatusText == "Canceled" && !running.IsFailed, "Cancellation is not mislabeled as failure");
            vm.ClearAll();
            Check(repo.Logs.Count == 0 && vm.SelectedLog == null, "Completed cancellation can be cleared without a stale selection");
            PumpFor(30);
            Check(window.FindControl<SourceGit.Views.CommandLogContentPresenter>("LogContent").IsFollowingOutput,
                "An empty log pane does not offer a meaningless Back to bottom action");

            var active = repo.CreateLog("Long-running action");
            active.SetCancelAction(() => { });
            var maxLogs = (int)typeof(Repository).GetField("MAX_LOGS", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            for (var i = 0; i < maxLogs + 2; i++)
                repo.CreateLog("Finished " + i).Complete(true);
            Check(repo.Logs.Contains(active) && active.CanCancel && repo.Logs.Count <= maxLogs,
                "Automatic log retention evicts completed logs, never a running task");
            vm.SelectedLog = active;
            TestLogFollowing(window, active);

            var fail = repo.CreateLog("Failed fetch");
            fail.AppendLine("fatal: unable to access remote");
            fail.Complete(false);
            vm.SelectedLog = fail;
            foreach (var width in new[] { 800, 620 })
            {
                window.Width = width;
                PumpFor(100);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Check(frame != null, "Log controls render at width " + width);
                frame.Save(Path.Combine(_root, "logs-ux-" + width + ".png"));
            }
            SourceGit.App.SetLocale("zh_CN");
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            var localized = repo.CreateLog("Localized failure");
            localized.Complete(false);
            vm.SelectedLog = localized;
            PumpFor(100);
            Check(localized.StatusText == "失败", "Log outcomes use the selected language");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame())
                frame.Save(Path.Combine(_root, "logs-ux-dark-zh.png"));
            active.Complete(true);
        }
        finally
        {
            window.Close();
            SourceGit.App.SetLocale("en_US");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static void TestLogFollowing(SourceGit.Views.ViewLogs window, CommandLog log)
    {
        var editor = window.FindControl<SourceGit.Views.CommandLogContentPresenter>("LogContent");
        for (var i = 0; i < 300; i++)
            log.AppendLine($"output {i:D4} " + new string('x', 50));
        PumpFor(250);
        var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        Check(editor.IsFollowingOutput && editor.VerticalOffset >= editor.ExtentHeight - editor.ViewportHeight - 2,
            "Initial streaming output follows the bottom");
        scroll.Offset = new Vector(0, 120);
        PumpFor(100);
        var offset = editor.VerticalOffset;
        editor.Select(editor.Document.GetLineByNumber(10).Offset, 8);
        var selected = editor.SelectedText;
        log.AppendLine("new output while reading");
        PumpFor(250);
        Check(!editor.IsFollowingOutput && Math.Abs(editor.VerticalOffset - offset) < 2 && editor.SelectedText == selected,
            "Scrolling up pauses following and new output preserves position and selected text");
        var back = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsVisible && button.Content is StackPanel panel &&
            panel.Children.OfType<TextBlock>().Any(text => text.Text == "Back to bottom"));
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpFor(100);
        log.AppendLine("follow again");
        PumpFor(250);
        Check(editor.IsFollowingOutput && editor.VerticalOffset >= editor.ExtentHeight - editor.ViewportHeight - 2,
            "Back to bottom restores live following");
        scroll.Offset = new Vector(0, 120);
        PumpFor(80);
        scroll.ScrollToEnd();
        PumpFor(80);
        Check(editor.IsFollowingOutput, "Manually scrolling to bottom also resumes following");

        var oldText = editor.Text;
        var retainedOffset = editor.Document.GetLineByNumber(30).Offset;
        scroll.Offset = new Vector(0, 60 * editor.TextArea.TextView.DefaultLineHeight);
        PumpFor(80);
        var beforeTrim = editor.VerticalOffset;
        editor.Select(editor.Document.GetLineByNumber(65).Offset, 10);
        selected = editor.SelectedText;
        editor.OnResetCommandLog("[... older log output truncated ...]\n" + oldText[retainedOffset..] + "more output\n");
        PumpFor(100);
        Check(!editor.IsFollowingOutput && editor.SelectedText == selected &&
            Math.Abs(editor.VerticalOffset - (beforeTrim - 28 * editor.TextArea.TextView.DefaultLineHeight)) < 2,
            "Trimming older output preserves the retained reading anchor and selection");

        editor.Log = null;
        editor.Log = log;
        var sequence = 0;
        while (log.EstimatedContentLength < 230 * 1024)
        {
            for (var i = 0; i < 200; i++)
                log.AppendLine($"retained output {sequence++:D6} " + new string('z', 100));
            PumpFor(150);
        }
        scroll.Offset = new Vector(0, 1200 * editor.TextArea.TextView.DefaultLineHeight);
        PumpFor(80);
        var visibleLine = (int)(editor.VerticalOffset / editor.TextArea.TextView.DefaultLineHeight) + 1;
        var anchor = editor.Document.GetText(editor.Document.GetLineByNumber(visibleLine));
        editor.Select(editor.Document.GetLineByNumber(visibleLine + 2).Offset, 24);
        selected = editor.SelectedText;
        for (var i = 0; i < 300; i++)
            log.AppendLine($"retained output {sequence++:D6} " + new string('z', 100));
        PumpFor(250);
        visibleLine = (int)(editor.VerticalOffset / editor.TextArea.TextView.DefaultLineHeight) + 1;
        Check(log.Content.Contains("older log output truncated") && !editor.IsFollowingOutput && editor.SelectedText == selected &&
            editor.Document.GetText(editor.Document.GetLineByNumber(visibleLine)) == anchor,
            "Real bounded-log truncation across output batches retains the visible line and copied selection");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var frame = window.CaptureRenderedFrame())
            frame.Save(Path.Combine(_root, "logs-ux-paused.png"));
        editor.Log = new CommandLog("Another task");
        PumpFor(100);
        Check(editor.IsFollowingOutput && editor.Text.Length == 0, "Switching tasks resets following without stale content");
        editor.Log = string.Join(Environment.NewLine, Enumerable.Range(0, 100).Select(i => "static error " + i));
        PumpFor(100);
        Check(editor.VerticalOffset == 0, "Static error notifications still start at the first line");
    }
}
