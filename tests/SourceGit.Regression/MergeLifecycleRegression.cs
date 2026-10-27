using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using SourceGit.Models;

internal static partial class Program
{
    private static void TestMergeLifecycle()
    {
        TestDiffCancellationLifecycle();
        var log = new SourceGit.ViewModels.CommandLog("Background completion");
        Wait(Task.Run(() =>
        {
            for (var i = 0; i < 10000; i++)
                log.AppendLine("output " + i);
            log.Complete(true);
        }));
        Check(log.IsComplete && log.IsSuccessful && log.Content.Contains("output 9999") && log.Content.Length < 300000,
            "Background log completion preserves the explicit result and bounded final output");

        var key = new SourceGit.AI.ApiKey { Service = "https://fixture.invalid", Storage = SourceGit.AI.ApiKeyStorage.PlainText,
            KeyToAccess = "fixture-only-key" };
        var edit = key.CreateEditObject();
        edit.KeyToAccess = "changed-fixture-key";
        Check(key.KeyToAccess == "fixture-only-key" && edit.Service == key.Service && edit.Storage == key.Storage,
            "API key editing clones plaintext configuration without mutating the original or accessing the keychain");
        key.Storage = SourceGit.AI.ApiKeyStorage.EnvironmentVariable;
        key.KeyToAccess = "SOURCEGIT_TEST_ONLY_API_KEY";
        Check(key.CreateEditObject().KeyToAccess == key.KeyToAccess, "Environment-backed key editing preserves the variable name");

        var path = NewRepository("delete attached worktree");
        Git(path, "commit", "--allow-empty", "-m", "initial");
        var attached = Path.Combine(_root, "attached worktree");
        Git(path, "worktree", "add", "-b", "attached-branch", attached);
        var repo = Open(path);
        try
        {
            repo.RefreshWorktrees();
            WaitUntil(() => repo.Worktrees.Count == 2, "Worktree list did not load.");
            var branch = repo.Branches.Single(b => b.Name == "attached-branch");
            var deletion = new SourceGit.ViewModels.DeleteBranch(repo, branch);
            Check(deletion.AttachedWorktree?.FullPath.Replace('\\', '/') == attached.Replace('\\', '/') && !deletion.DeleteWorktree,
                "Branch deletion detects its attached worktree and defaults to keeping it");
            Check(!Wait(deletion.Sure()) && Directory.Exists(attached) && Git(path, "branch", "--list", "attached-branch").Contains("attached-branch"),
                "Deleting without opting into worktree removal preserves the worktree and branch");
            repo.UIStates.UpdateHistoryFilters(branch.FullName, FilterType.LocalBranch, FilterMode.Included);
            File.WriteAllText(Path.Combine(attached, "keep.txt"), "uncommitted fixture");
            deletion = new SourceGit.ViewModels.DeleteBranch(repo, branch) { DeleteWorktree = true };
            Check(!Wait(deletion.Sure()) && File.Exists(Path.Combine(attached, "keep.txt")) &&
                Git(path, "branch", "--list", "attached-branch").Contains("attached-branch") &&
                repo.UIStates.HistoryFilters.Any(f => f.Pattern == branch.FullName),
                "Failed dirty-worktree removal stops before deleting its branch or filter");
            deletion = new SourceGit.ViewModels.DeleteBranch(repo, branch) { DeleteWorktree = true, Force = true };
            Check(Wait(deletion.Sure()) && !Directory.Exists(attached) && string.IsNullOrWhiteSpace(Git(path, "branch", "--list", "attached-branch")) &&
                !repo.UIStates.HistoryFilters.Any(f => f.Pattern == branch.FullName),
                "Explicit forced worktree removal deletes the temporary worktree, branch and its filter together");

            var tree = new SourceGit.Views.BranchTree { DataContext = repo, Nodes = repo.LocalBranchTrees };
            var window = new Window { Content = tree, Width = 360, Height = 240 };
            window.Show();
            try
            {
                PumpFor(100);
                var list = tree.FindControl<ListBox>("BranchesPresenter");
                list.SelectedItem = tree.Rows.Single(n => n.Backend is Branch { Name: "main" });
                var copy = new KeyEventArgs { Key = Key.C, KeyModifiers = KeyModifiers.Control };
                Invoke(tree, "OnTreeKeyDown", list, copy);
                PumpFor(30);
                Check(copy.Handled && Wait(window.Clipboard.TryGetTextAsync()) == "main", "Branch copy hotkey retains the exact selected branch name");
            }
            finally { window.Close(); }
        }
        finally
        {
            PumpFor(100);
            repo.Close();
        }
    }

    private static void TestDiffCancellationLifecycle()
    {
        var path = NewRepository("diff cancellation lifetime");
        File.WriteAllText(Path.Combine(path, "diff.txt"), "before\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "initial");
        File.WriteAllText(Path.Combine(path, "diff.txt"), "after\n");
        var option = new DiffOption(new Change { Path = "diff.txt", WorkTree = ChangeState.Modified }, true);
        for (var i = 0; i < 40; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var read = new SourceGit.Commands.Diff(path, option, 3, false, false) { CancellationToken = cancellation.Token }.ReadAsync();
            var delay = i % 8;
            var cancel = Task.Run(async () =>
            {
                await Task.Delay(delay);
                await cancellation.CancelAsync();
            });
            Wait(Task.WhenAll(read, cancel));
        }
        Check(true, "Repeated diff cancellation racing process completion does not throw from token callbacks");
        var result = Wait(new SourceGit.Commands.Diff(path, option, 3, false, false).ReadAsync());
        Check(result.TextDiff?.Lines.Count > 0, "Normal diff still loads after cancellation races");
    }
}
