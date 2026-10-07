using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestRuntimeCrashes()
    {
        foreach (var text in new[] { "hello", "one\ntwo\nthree", "one\r\ntwo\r\nthree" })
        {
            var document = new TextDocument(text);
            using var model = new TextEditorModel(new TextView(), document, ex => throw ex);
            var reads = 0;
            document.Changing += (_, _) =>
            {
                var count = 0;
                model.ForEach(_ => count++);
                if (model.DocumentSnapshot.LineCount != count || count < 1)
                    throw new Exception("Snapshot lost its surviving start line during an edit.");
                Task.Run(() => model.GetLineLength(0)).GetAwaiter().GetResult();
                reads++;
            };
            document.Text = "replacement";
            document.Remove(0, 1);
            document.Text = "";
            document.Text = "before\r\nafter";
            document.Remove(6, 2);
            Check(reads >= 4, "Background tokenization can read the first line throughout text removal");
            Check(model.DocumentSnapshot.LineCount == 1 && model.DocumentSnapshot.GetLineText(0) == "beforeafter",
                "Snapshot preserves the correct surviving line after newline removal");
        }

        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var editor = new AvaloniaEdit.TextEditor();
        using (var highlighting = editor.InstallTextMate(
            new TextMateSharp.Grammars.RegistryOptions(TextMateSharp.Grammars.ThemeName.LightPlus),
            exceptionHandler: errors.Enqueue))
        {
            highlighting.SetGrammar("source.cs");
            var editorWindow = new Window { Content = editor, Width = 640, Height = 320 };
            editorWindow.Show();
            try
            {
                var code = string.Join("\r\n", Enumerable.Repeat("public class Example { string Text = \"hello\"; }", 120));
                for (var i = 0; i < 100; i++)
                {
                    editor.Text = code;
                    PumpFor(5);
                    editor.Text = "";
                    editor.Text = "\n\r\n";
                    editor.Document.Insert(2, "split CRLF");
                    editor.Document.Remove(1, 2);
                    PumpFor(5);
                }
                Check(errors.IsEmpty, "Live TextMate highlighting survives repeated clear, replace and CRLF edits: " +
                    string.Join("; ", errors.Select(e => e.Message)));
            }
            finally
            {
                editorWindow.Close();
            }
        }

        var repo = new Repository(false, _root, Path.Combine(_root, ".git"));
        var vm = new ViewLogs(repo);
        var window = new SourceGit.Views.ViewLogs { DataContext = vm };
        for (var i = 0; i < 40; i++)
        {
            var log = repo.CreateLog("A long custom action name " + i);
            log.AppendLine("$ a-command-with-a-long-argument " + new string('x', 100));
            log.AppendLine("stderr: expected regression test failure");
            log.Complete(i % 2 == 0);
        }
        window.Show();
        try
        {
            foreach (var width in new[] { 800, 620, 1100, 800 })
            {
                window.Width = width;
                for (var i = 0; i < 10; i++)
                {
                    var log = repo.CreateLog("Retry " + i);
                    vm.SelectedLog = log;
                    log.AppendLine(new string('x', 2000));
                    PumpFor(20);
                    log.Complete(i % 2 == 0);
                    PumpFor(20);
                }
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                list.ScrollIntoView(repo.Logs[^1]);
                PumpFor(60);
                list.ScrollIntoView(repo.Logs[0]);
                PumpFor(60);
                Check(true, "Virtualized log columns settle after retries, scrolling and resize to " + width);
            }
            var completed = repo.CreateLog("Completed custom action");
            completed.AutoCloseOnSuccess = true;
            vm.SelectedLog = completed;
            completed.Complete(true);
            PumpFor(1500);
            Check(window.FindControl<TextBlock>("AutoCloseCountdown").IsVisible,
                "Success flashing and countdown do not cause a layout loop");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Check(frame != null, "Log window renders after repeated action state transitions");
            frame.Save(Path.Combine(_root, "custom-action-layout.png"));
        }
        finally
        {
            window.Close();
        }
    }
}
