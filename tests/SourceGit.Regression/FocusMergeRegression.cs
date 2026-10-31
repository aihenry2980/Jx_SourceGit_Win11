using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using SourceGit.Models;
using Repo = SourceGit.ViewModels.Repository;

internal static partial class Program
{
    private static void TestFocusBranchMergeIntegration()
    {
        var path = NewRepository("focus branch integration");
        File.WriteAllText(Path.Combine(path, "current.txt"), "base\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "base");
        var basis = Git(path, "rev-parse", "HEAD");
        Git(path, "checkout", "-b", "other");
        File.WriteAllText(Path.Combine(path, "other.txt"), "other\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "unrelated branch");
        var unrelated = Git(path, "rev-parse", "HEAD");
        Git(path, "tag", "unrelated-tag");
        Git(path, "checkout", "-b", "upstream-fixture", basis);
        File.WriteAllText(Path.Combine(path, "upstream.txt"), "upstream\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "remote ahead");
        var upstream = Git(path, "rev-parse", "HEAD");
        Git(path, "checkout", "main");
        Git(path, "branch", "-D", "upstream-fixture");
        File.WriteAllText(Path.Combine(path, "current.txt"), "current\n");
        Git(path, "commit", "-am", "local ahead");
        var current = Git(path, "rev-parse", "HEAD");
        Git(path, "remote", "add", "origin", path);
        Git(path, "update-ref", "refs/remotes/origin/main", upstream);
        Git(path, "config", "branch.main.remote", "origin");
        Git(path, "config", "branch.main.merge", "refs/heads/main");

        var repo = Open(path);
        Window window = null;
        void Refresh() => Wait((Task)Invoke(repo, "RefreshCommitsAsync", false));
        bool HasExactly(params string[] revisions) => repo.Histories.Commits.Select(c => c.SHA).ToHashSet().SetEquals(revisions);
        try
        {
            repo.SetPresetBranchFilterMode(true);
            Refresh();
            Check(HasExactly(basis, current, upstream, unrelated), "Normal history includes unrelated branches and tags before focusing");
            Check(Wait(new SourceGit.Commands.QueryUpstreamOfCurrentBranch(path).GetResultAsync()) == "refs/remotes/origin/main",
                "Focus resolves the actual tracking ref of the current branch");
            repo.UIStates.HistoryShowFlags |= HistoryShowFlags.Reflog;
            var snapshots = new List<HashSet<string>>();
            PropertyChangedEventHandler observe = (_, e) =>
            {
                if (e.PropertyName == nameof(repo.Histories.Commits) && repo.Histories.Commits is { Count: > 0 } commits)
                    snapshots.Add(commits.Select(c => c.SHA).ToHashSet());
            };
            repo.Histories.PropertyChanged += observe;
            repo.FocusOnCurrentBranch = true;
            Refresh();
            repo.Histories.PropertyChanged -= observe;
            Check(HasExactly(basis, current, upstream), "Focus includes diverged local and upstream histories but excludes unrelated refs");
            Check(snapshots.Count > 0 && snapshots.All(s => s.SetEquals(new[] { basis, current, upstream })),
                "Progressive history snapshots consistently obey focus mode");
            Check(!repo.Histories.CanToggleReflog && !repo.UIStates.BuildHistoryParams_FocusOnCurrentBranchMode("refs/remotes/origin/main").Contains("--reflog"),
                "Focus suppresses lost-commit history even when the stored reflog flag is enabled");

            var toolbar = new SourceGit.Views.RepositoryToolbar { DataContext = repo };
            window = new Window { Content = toolbar, Width = 1100, Height = 140 };
            window.Show();
            PumpFor(100);
            var toggle = toolbar.FindControl<ToggleButton>("FocusCurrentBranchButton");
            Check(toggle?.IsChecked == true, "Customized toolbar exposes the upstream Focus toggle");
            toggle.IsChecked = false;
            PumpFor(30);
            Refresh();
            Check(!repo.FocusOnCurrentBranch && repo.Histories.CanToggleReflog && HasExactly(basis, current, upstream, unrelated),
                "Toolbar toggle restores normal history and the lost-commit option");
            repo.UIStates.HistoryShowFlags &= ~HistoryShowFlags.Reflog;

            repo.FocusOnCurrentBranch = true;
            repo.PresetBranchExactNames = "other";
            repo.ApplyPresetBranchFilter();
            Refresh();
            Check(!repo.FocusOnCurrentBranch && repo.Histories.Commits.Any(c => c.SHA == unrelated),
                "Applying custom preset rules leaves focus mode and displays the requested branch");
            repo.FocusOnCurrentBranch = true;
            repo.SetHistoryPathFilter("current.txt");
            Refresh();
            Check(!repo.FocusOnCurrentBranch && HasExactly(basis, current), "Custom path history remains effective after leaving focus mode");
            repo.FocusOnCurrentBranch = true;
            repo.SetPresetBranchFilterMode(true);
            Refresh();
            Check(!repo.FocusOnCurrentBranch && HasExactly(basis, current, upstream, unrelated), "All-branches mode explicitly leaves focus mode");

            repo.FocusOnCurrentBranch = true;
            Git(path, "update-ref", "-d", "refs/remotes/origin/main");
            Wait(InvokeTask(repo, "RefreshBranchesAsync"));
            Refresh();
            Check(HasExactly(basis, current), "A gone upstream falls back to current branch history without a Git failure");
            Git(path, "update-ref", "refs/remotes/origin/main", upstream);
            Git(path, "checkout", "--detach", current);
            Wait(InvokeTask(repo, "RefreshBranchesAsync"));
            Refresh();
            Check(repo.FocusOnCurrentBranch && HasExactly(basis, current), "Detached HEAD focus works without a tracking branch");
            Git(path, "checkout", "main");
            Wait(InvokeTask(repo, "RefreshBranchesAsync"));
            Refresh();
            Check(HasExactly(basis, current, upstream), "Returning to a tracking branch restores its upstream in focus mode");

            Wait(repo.Settings.SaveAsync());
            repo.Close();
            repo = null;
            repo = Open(path);
            Refresh();
            Check(repo.FocusOnCurrentBranch && HasExactly(basis, current, upstream), "Reopening preserves focus without automatically applying saved preset rules");
        }
        finally
        {
            window?.Close();
            PumpFor(100);
            repo?.Close();
        }
    }

    private static void TestMergedRepositoryStatus()
    {
        var path = NewRepository("merged repository status");
        File.WriteAllText(Path.Combine(path, "tracked.txt"), "base\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "base");
        Git(path, "remote", "add", "origin", path);
        Git(path, "update-ref", "refs/remotes/origin/main", "HEAD");
        Git(path, "config", "branch.main.remote", "origin");
        Git(path, "config", "branch.main.merge", "refs/heads/main");
        var status = Wait(new SourceGit.Commands.QueryRepositoryStatus(path).GetResultAsync());
        Check(status.CurrentBranch == "main" && status.LocalChanges == 0, "Status does not count branch tracking headers as local changes");
        File.WriteAllText(Path.Combine(path, "untracked file.txt"), "new\n");
        Git(path, "mv", "tracked.txt", "renamed file.txt");
        status = Wait(new SourceGit.Commands.QueryRepositoryStatus(path).GetResultAsync());
        Check(status.LocalChanges == 2, "NUL-delimited status counts a rename once alongside an untracked file");
        Git(path, "checkout", "--detach", "HEAD");
        status = Wait(new SourceGit.Commands.QueryRepositoryStatus(path).GetResultAsync());
        Check(status.CurrentBranch == "(" + Git(path, "rev-parse", "HEAD")[..10] + ")", "Status preserves the short SHA label for detached HEAD");
    }
}
