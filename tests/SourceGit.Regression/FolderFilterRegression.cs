using SourceGit.Models;

internal static partial class Program
{
    private static void TestFolderFilterAfterBranchChanges()
    {
        var path = NewRepository("folder filter checkout");
        Git(path, "commit", "--allow-empty", "-m", "initial");
        Git(path, "branch", "feature/existing");
        var repo = Open(path);
        try
        {
            var states = repo.UIStates;
            const string folder = "refs/heads/feature";
            const uint color = 0xFF744DA9;
            states.UpdateHistoryFilters(folder, FilterType.LocalBranchFolder, FilterMode.Included, color);
            Invoke(repo, "RefreshHistoryFilters", false);

            void CheckFolder(string action)
            {
                Check(states.HistoryFilters.Count == 1 && states.HistoryFilters[0].Pattern == folder &&
                    states.HistoryFilters[0].Type == FilterType.LocalBranchFolder && states.HistoryFilters[0].Color == color,
                    action + " preserves the folder filter and its color without adding a redundant branch filter");
            }

            Check(states.IsCoveredByFolderFilter("refs/heads/feature/existing", FilterType.LocalBranchFolder) &&
                !states.IsCoveredByFolderFilter("refs/heads/feature-other/test", FilterType.LocalBranchFolder) &&
                !states.IsCoveredByFolderFilter("refs/heads/feature/existing", FilterType.RemoteBranchFolder),
                "Folder coverage respects path boundaries and branch type");
            Git(path, "checkout", "feature/existing");
            repo.RefreshAfterCheckoutBranch(repo.Branches.Single(b => b.Name == "feature/existing"));
            CheckFolder("Checkout inside a filtered folder");
            Check(repo.CurrentBranch.Name == "feature/existing", "Folder-covered checkout still updates the current branch");

            Git(path, "checkout", "-b", "feature/new");
            repo.RefreshAfterCreateBranch(new Branch { Name = "feature/new", FullName = "refs/heads/feature/new",
                Head = Git(path, "rev-parse", "HEAD"), IsLocal = true }, true);
            CheckFolder("Create-and-checkout inside a filtered folder");
            Check(repo.CurrentBranch.Name == "feature/new", "Folder-covered creation still updates the current branch");

            Git(path, "checkout", "main");
            repo.RefreshAfterCheckoutBranch(repo.Branches.Single(b => b.Name == "main"));
            Check(states.HistoryFilters.Any(f => f.Pattern == "refs/heads/main" && f.Type == FilterType.LocalBranch &&
                f.Mode == FilterMode.Included) && states.HistoryFilters.Any(f => f.Pattern == folder),
                "Checkout outside the folder automatically includes the branch while retaining the folder filter");
        }
        finally
        {
            repo.Close();
        }
    }
}
