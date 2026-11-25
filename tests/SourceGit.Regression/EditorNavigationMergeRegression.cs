using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Prefs = SourceGit.ViewModels.Preferences;

internal static partial class Program
{
    private static void TestEditorNavigationMerge()
    {
        var path = NewRepository("editor navigation merge");
        var lines = Enumerable.Range(1, 120).Select(i => "Line " + i).ToArray();
        File.WriteAllLines(Path.Combine(path, "file.txt"), lines);
        Git(path, "add", ".");
        Git(path, "commit", "-m", "Initial editor fixture");
        lines[39] = "Changed line 40";
        lines[79] = "Changed line 80";
        File.WriteAllLines(Path.Combine(path, "file.txt"), lines);
        var prefs = Prefs.Instance;
        var oldSideBySide = prefs.UseSideBySideDiff;
        var theme = Application.Current.RequestedThemeVariant;
        var repo = Open(path);
        try
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var split in new[] { false, true })
            {
                Application.Current.RequestedThemeVariant = variant;
                prefs.UseSideBySideDiff = split;
                var context = new SourceGit.ViewModels.DiffContext(path, new DiffOption(new Change { Path = "file.txt", WorkTree = ChangeState.Modified }, true));
                WaitUntil(() => context.Content is SourceGit.ViewModels.TextDiffContext, "Navigation fixture diff did not load");
                var view = new DiffView { DataContext = context };
                var window = new Window { Content = view, Width = 900, Height = 520 };
                try
                {
                    window.Show();
                    PumpFor(80);
                    var diff = view.GetVisualDescendants().OfType<TextDiffView>().Single();
                    var presenter = diff.GetVisualDescendants().OfType<ThemedTextDiffPresenter>().Single(p => !p.IsOld);
                    presenter.TextArea.Focus();
                    window.KeyPress(Key.G, RawInputModifiers.Control);
                    PumpFor(50);
                    var bar = diff.FindControl<GotoLineBar>("GotoLineBar");
                    Check(bar.IsVisible, "Ctrl+G opens line navigation in a real unified/split diff: " + split + "/" + variant);
                    var input = bar.FindControl<TextBox>("TxtLine");
                    input.Text = "80";
                    PumpFor(40);
                    var displayLines = split ? ((SourceGit.ViewModels.TwoSideTextDiff)context.Content).New : ((SourceGit.ViewModels.TextDiffContext)context.Content).Data.Lines;
                    Check(displayLines[presenter.TextArea.Caret.Line - 1].NewLineNumber == 80,
                        "Diff navigation maps new-file line numbers to the correct displayed document row");
                    var size = presenter.Bounds.Size;
                    window.KeyPress(Key.Enter, RawInputModifiers.None);
                    PumpFor(40);
                    Check(!bar.IsVisible && presenter.TextArea.IsKeyboardFocusWithin && presenter.Bounds.Size == size,
                        "Enter closes navigation and restores editor focus without resizing diff panes");
                    view.FindControl<Button>("BtnGotoLine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpFor(40);
                    Check(bar.IsVisible, "The Ln toolbar button and keyboard shortcut share one navigation bar");
                    CaptureUxFrame(window, $"merge-goto-diff-{variant}-{split}");
                    window.KeyPress(Key.Escape, RawInputModifiers.None);
                    PumpFor(40);
                    Check(!bar.IsVisible, "Escape closes the shared diff navigation bar");
                }
                finally { window.Close(); context.CancelLoading(); }
            }

            var revision = new RevisionFileContentViewer { Content = new RevisionTextFile { FileName = "file.txt", Content = string.Join('\n', lines) } };
            var revisionWindow = new Window { Content = revision, Width = 760, Height = 420 };
            try
            {
                revisionWindow.Show();
                PumpFor(60);
                var editor = revision.GetVisualDescendants().OfType<RevisionTextFileView>().Single();
                editor.TextArea.Focus();
                revisionWindow.KeyPress(Key.G, RawInputModifiers.Control);
                PumpFor(40);
                var bar = revision.GetVisualDescendants().OfType<GotoLineBar>().Single();
                Check(bar.IsVisible, "Ctrl+G also opens navigation in historical file content");
                bar.FindControl<TextBox>("TxtLine").Text = "999999";
                PumpFor(40);
                Check(editor.TextArea.Caret.Line == editor.Document.LineCount, "Out-of-range input is clamped to the last document line");
                revision.Content = new RevisionTextFile { FileName = "short.txt", Content = "short" };
                PumpFor(50);
                Check(!revision.GetVisualDescendants().OfType<GotoLineBar>().Single().IsVisible,
                    "Changing revision content closes navigation even when the host DataContext is unchanged");
                bar.Open(0);
                PumpFor(20);
                Check(!bar.IsVisible, "Zero-line content never exposes a jump to invalid line zero");
            }
            finally { revisionWindow.Close(); }

            repo.IssueTrackers.Clear();
            repo.IssueTrackers.Add(new IssueTracker { Name = "Issues", RegexString = @"#(\d+)", URLTemplate = "https://example.invalid/issues/$1" });
            repo.IssueTrackers.Add(new IssueTracker { Name = "Duplicate rule", RegexString = @"#(\d+)", URLTemplate = "https://example.invalid/issues/$1" });
            repo.WorkingCopy.CommitMessage = "Fix #42 and #42; follow up #7";
            var toolbox = new CommitMessageToolBox { DataContext = repo.WorkingCopy, CommitMessage = repo.WorkingCopy.CommitMessage, ShowAdvancedOptions = true };
            var toolboxWindow = new Window { Content = toolbox, Width = 700, Height = 210 };
            ContextMenu menu = null;
            using var subscription = ContextMenu.OpenedEvent.AddClassHandler<ContextMenu>((sender, _) => menu = sender);
            try
            {
                toolboxWindow.Show();
                PumpFor(50);
                var button = toolbox.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), SourceGit.App.Text("WorkingCopy.IssueLinks")));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => menu?.IsOpen == true, "Issue link menu did not open");
                Check(menu.Items.Count == 2 && !button.IsEnabled, "Issue link menu deduplicates repeated matches across trackers and guards repeated opening");
                var item = menu.Items.OfType<MenuItem>().First();
                item.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpFor(40);
                Check(Wait(toolboxWindow.Clipboard.TryGetTextAsync()) == "https://example.invalid/issues/42", "Copy issue link uses the resolved URL without opening a browser");
                menu.Close();
                PumpFor(30);
                Check(button.IsEnabled, "Closing the issue menu restores its toolbar button");
                repo.WorkingCopy.CommitMessage = "No issue reference";
                menu = null;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => menu?.IsOpen == true, "Empty issue menu did not open");
                Check(menu.Items.OfType<MenuItem>().Single() is { IsEnabled: false }, "Messages without links display a disabled empty state");
                menu.Close();
                toolbox.ShowAdvancedOptions = false;
                PumpFor(30);
                Check(!button.IsEffectivelyVisible, "Compact commit message editors do not gain advanced issue controls");
            }
            finally { menu?.Close(); toolboxWindow.Close(); }
        }
        finally { repo.Close(); prefs.UseSideBySideDiff = oldSideBySide; Application.Current.RequestedThemeVariant = theme; }
    }
}
