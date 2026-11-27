using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestNovemberUpstreamMerge()
    {
        TestAnnotatedMergeHead();
        TestRebaseBreakHead();
        TestFailedIndexSelection();
        TestSearchBackNavigation();
    }

    private static void TestAnnotatedMergeHead()
    {
        var path = NewRepository("annotated merge head");
        File.WriteAllText(Path.Combine(path, "conflict.txt"), "base\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "base");
        Git(path, "checkout", "-b", "incoming");
        File.WriteAllText(Path.Combine(path, "conflict.txt"), "incoming\n");
        Git(path, "commit", "-am", "incoming tagged commit");
        Git(path, "tag", "-a", "incoming-tag", "-m", "annotated tag metadata");
        var tag = Git(path, "rev-parse", "incoming-tag");
        var commit = Git(path, "rev-parse", "HEAD");
        var queried = Wait(new SourceGit.Commands.QuerySingleCommit(path, tag).GetResultAsync());
        Check(tag != commit && queried?.SHA == commit && queried.Subject == "incoming tagged commit" && queried.AuthorTime > 0,
            "Asynchronous single-commit queries peel annotated tags instead of parsing tag metadata as commit fields");
        Check(new SourceGit.Commands.QuerySingleCommit(path, "incoming-tag").GetResult()?.SHA == commit,
            "Synchronous single-commit queries also resolve annotated tag names");
        Git(path, "checkout", "main");
        File.WriteAllText(Path.Combine(path, "conflict.txt"), "ours\n");
        Git(path, "commit", "-am", "ours");
        Check(!Wait(new SourceGit.Commands.Command { WorkingDirectory = path, Args = "merge --no-commit incoming-tag", RaiseError = false }.ExecAsync()),
            "Annotated-tag merge fixture stops at a real conflict");
        File.WriteAllText(Path.Combine(path, ".git", "MERGE_HEAD"), tag + "\n");
        var repo = Open(path);
        try
        {
            WaitUntil(() => repo.WorkingCopy.InProgressContext is MergeInProgress, "Annotated MERGE_HEAD did not load");
            Check(((MergeInProgress)repo.WorkingCopy.InProgressContext).Source.SHA == commit,
                "Opening a repository whose MERGE_HEAD contains a tag object loads the actual commit without crashing");
            Wait(repo.WorkingCopy.InProgressContext.AbortAsync(new CommandLog("Abort annotated merge")));
        }
        finally { repo.Close(); }
    }

    private static void TestRebaseBreakHead()
    {
        var path = NewRepository("rebase break head");
        Git(path, "commit", "--allow-empty", "-m", "base");
        var basis = Git(path, "rev-parse", "HEAD");
        Git(path, "checkout", "-b", "onto");
        File.WriteAllText(Path.Combine(path, "onto.txt"), "new base\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "onto commit");
        Git(path, "checkout", "main");
        foreach (var name in new[] { "first", "second" })
        {
            File.WriteAllText(Path.Combine(path, name + ".txt"), name + "\n");
            Git(path, "add", ".");
            Git(path, "commit", "-m", name + " replayed commit");
        }
        var originalTip = Git(path, "rev-parse", "HEAD");
        var editor = Path.Combine(_root, "break-editor.sh");
        File.WriteAllText(editor, "#!/bin/sh\nsed -i '2i break' \"$1\"\n");
        Git(path, "-c", $"sequence.editor=sh \"{editor.Replace('\\', '/')}\"", "rebase", "-i", "--onto", "onto", basis, "main");
        var stopped = Git(path, "rev-parse", "HEAD");
        var metadata = Path.Combine(path, ".git", "rebase-merge");
        Check(Directory.Exists(metadata) && !File.Exists(Path.Combine(metadata, "stopped-sha")) && stopped != originalTip,
            "Interactive rebase stops at break without stopped-sha and before the original branch tip");
        File.Delete(Path.Combine(metadata, "message"));
        var repo = Open(path);
        try
        {
            WaitUntil(() => repo.WorkingCopy.InProgressContext is RebaseInProgress, "Break rebase metadata did not load");
            var context = (RebaseInProgress)repo.WorkingCopy.InProgressContext;
            Check(context.HeadName == "main" && context.StoppedAt?.SHA == stopped && context.StoppedAt.Subject == "first replayed commit",
                "Rebase break uses current HEAD, not the original branch tip, while preserving asynchronous metadata queries");
            WaitUntil(() => repo.WorkingCopy.CommitMessage.Contains("first replayed commit"), "Break commit message did not load asynchronously");
            Check(!repo.WorkingCopy.CommitMessage.Contains("second replayed commit"),
                "Rebase message fallback describes the currently replayed commit instead of a later original commit");
            Wait(context.AbortAsync(new CommandLog("Abort break rebase")));
            Check(Git(path, "rev-parse", "HEAD") == originalTip, "Aborting a break rebase restores its original branch tip");
        }
        finally { repo.Close(); }
    }

    private static void TestFailedIndexSelection()
    {
        var path = NewRepository("failed index selection");
        foreach (var name in new[] { "a.txt", "b.txt" })
            File.WriteAllText(Path.Combine(path, name), "before\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "base");
        foreach (var name in new[] { "a.txt", "b.txt" })
            File.WriteAllText(Path.Combine(path, name), "after\n");
        var repo = Open(path);
        var indexLock = Path.Combine(path, ".git", "index.lock");
        try
        {
            WaitUntil(() => repo.WorkingCopy.Unstaged.Count == 2, "Unstaged fixture files did not load");
            var copy = repo.WorkingCopy;
            var first = copy.Unstaged.Single(c => c.Path == "a.txt");
            var next = copy.Unstaged.Single(c => c.Path == "b.txt");
            copy.SelectedUnstaged = new(new[] { first });
            var selected = copy.SelectedUnstaged;
            File.WriteAllText(indexLock, "fixture lock");
            Wait(copy.StageChangesAsync([first], next));
            Check(ReferenceEquals(copy.SelectedUnstaged, selected) && !copy.IsStaging && !repo.Logs[0].IsSuccessful,
                "Failed staging retains the selected file, clears busy state and records failure");
            File.Delete(indexLock);
            Wait(copy.StageChangesAsync(copy.Unstaged.ToList(), null));
            WaitUntil(() => copy.Staged.Count == 2, "Staging retry did not succeed after releasing the index lock");
            foreach (var amend in new[] { false, true })
            {
                copy.UseAmend = amend;
                if (amend)
                    WaitUntil(() => copy.Staged.Count == 2 && copy.Staged.All(c => c.DataForAmend != null), "Amend index metadata did not finish loading");
                PumpFor(100);
                first = copy.Staged.Single(c => c.Path == "a.txt");
                next = copy.Staged.Single(c => c.Path == "b.txt");
                copy.SelectedStaged = new(new[] { first });
                var stagedSelection = copy.SelectedStaged;
                File.WriteAllText(indexLock, "fixture lock");
                Wait(copy.UnstageChangesAsync([first], next));
                Check(ReferenceEquals(copy.SelectedStaged, stagedSelection) && !copy.IsUnstaging && !repo.Logs[0].IsSuccessful,
                    "Failed unstaging preserves selection and restores controls: amend=" + amend);
                File.Delete(indexLock);
            }
            copy.UseAmend = false;
            Wait(copy.UnstageChangesAsync([copy.Staged.Single(c => c.Path == "a.txt")], copy.Staged.Single(c => c.Path == "b.txt")));
            WaitUntil(() => copy.Staged.Count == 1 && copy.Unstaged.Count == 1, "Unstaging retry did not update the index");
            Check(copy.SelectedStaged?.Changes.Single().Path == "b.txt" && repo.Logs[0].IsSuccessful,
                "Successful unstaging still advances to the next file and records success");
        }
        finally { File.Delete(indexLock); repo.Close(); }
    }

    private static void TestSearchBackNavigation()
    {
        var path = NewRepository("search back navigation");
        Git(path, "commit", "--allow-empty", "-m", "searchable commit");
        var repo = Open(path);
        var context = repo.SearchCommitContext;
        var view = new SourceGit.Views.CommitSearchView { DataContext = context };
        var window = new Window { Content = view, Width = 360, Height = 500 };
        var previousTheme = Avalonia.Application.Current.RequestedThemeVariant;
        try
        {
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current.RequestedThemeVariant = theme;
                repo.IsSearchingCommits = true;
                context.Filter = "searchable";
                context.StartSearch();
                WaitUntil(() => !context.IsQuerying && context.Results?.Count == 1, "Search results did not load");
                PumpFor(50);
                var back = ((Grid)view.Content).Children.OfType<Grid>().Single(g => Grid.GetRow(g) == 0).Children.OfType<Button>().Single();
                Check(back.Command != null && back.Command.CanExecute(back.CommandParameter) && back.Bounds.Width == 24,
                    "Search back button binds to the customized repository search context: " + theme);
                CaptureUxFrame(window, "upstream-search-back-" + theme);
                back.Command.Execute(back.CommandParameter);
                Check(!repo.IsSearchingCommits && context.Results == null && !context.IsQuerying,
                    "Back button exits search and cleans results through the shared repository workflow");
                repo.IsSearchingCommits = true;
                typeof(SearchCommitContext).GetProperty(nameof(SearchCommitContext.Suggestions)).SetValue(context, new List<object> { "suggestion" });
                var escape = new KeyEventArgs { Key = Key.Escape };
                Invoke(view, "OnSearchBoxKeyDown", null, escape);
                Check(escape.Handled && repo.IsSearchingCommits && context.Suggestions == null,
                    "First Escape dismisses suggestions without leaving search");
                Invoke(view, "OnSearchBoxKeyDown", null, new KeyEventArgs { Key = Key.Escape });
                Check(!repo.IsSearchingCommits, "Escape exits search after suggestions have been dismissed");
            }
        }
        finally { window.Close(); repo.Close(); Avalonia.Application.Current.RequestedThemeVariant = previousTheme; }
    }
}
