using Avalonia.Controls;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestVisibilityIncludes()
    {
        var path = NewRepository("visibility inclusion sync");
        Git(path, "commit", "--allow-empty", "-m", "Initial");
        Git(path, "branch", "feature/same");
        foreach (var name in new[] { "origin", "upstream" })
        {
            Git(path, "remote", "add", name, path);
            Git(path, "update-ref", "refs/remotes/" + name + "/feature/same", "HEAD");
        }
        Git(path, "branch", "--set-upstream-to=origin/feature/same", "feature/same");
        Git(path, "tag", "v1");
        var repo = Open(path);
        try
        {
            repo.PresetBranchExactNames = "main";
            repo.ApplyPresetBranchFilter();
            var remote = repo.Branches.Single(b => b.FullName == "refs/remotes/origin/feature/same");
            var local = repo.Branches.Single(b => b.IsLocal && b.Name == "feature/same");
            var draft = repo.PresetBranchFilterDraft;
            draft.ExactInput = "unfinished-exact";
            draft.ContainsInput = "unfinished-contains";
            draft.ExcludeInput = "unfinished-exclude";
            repo.IsPresetBranchFilterEditorExpanded = false;
            repo.UIStates.UpdateHistoryFilters("src", FilterType.Path, FilterMode.Included);
            var visibility = new FilterModeInGraph(repo, remote, 0xFFF7630C) { IsFiltered = true };
            Check(repo.Settings.GetPresetBranchExactNameList().SequenceEqual(new[] { "main", remote.FriendlyName }) &&
                draft.ExactNames == repo.PresetBranchExactNames && repo.IsPresetBranchFilterEditorExpanded,
                "Visibility Include immediately adds an Exact names chip even for a branch absent from the sidebar");
            Check(draft.ExactInput == "unfinished-exact" && draft.ContainsInput == "unfinished-contains" && draft.ExcludeInput == "unfinished-exclude",
                "Including a branch preserves all unfinished rule input");
            Check(repo.UIStates.HistoryFilters.Any(f => f.Type == FilterType.Path && f.Pattern == "src") &&
                repo.UIStates.GetHistoryFilterMode(remote.FullName) == FilterMode.Included,
                "Include updates graph visibility without clearing unrelated path filters");
            visibility.IsFiltered = true;
            new FilterModeInGraph(repo, remote).IsFiltered = true;
            Check(repo.Settings.GetPresetBranchExactNameList().Count(n => n == remote.FriendlyName) == 1,
                "Repeated Include never duplicates an Exact names chip");

            var view = new SourceGit.Views.Repository { DataContext = repo };
            var window = new Window { Content = view, Width = 1200, Height = 850 };
            try
            {
                window.Show();
                PumpFor(150);
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == remote.FriendlyName &&
                    t.GetVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("preset_rule_chip"))),
                    "The included remote name renders as a rule chip in the sidebar");
                CaptureUxFrame(window, "visibility-include-chip");
            }
            finally { window.Close(); }

            repo.ApplyPresetBranchFilter();
            var visible = (List<Branch>)Invoke(repo, "GetVisibleBranchesByCurrentFilter");
            Check(visible.Any(b => b.FullName == remote.FullName) && !visible.Any(b => b.FullName == local.FullName) &&
                !visible.Any(b => b.FullName == "refs/remotes/upstream/feature/same"),
                "Qualified Exact names survive Apply without including same-named branches from other scopes");
            Check(repo.UIStates.HistoryFilters.Single(f => f.Pattern == remote.FullName).Color == 0xFFF7630C,
                "Reapplying an included remote preserves its selected color");
            repo.SetBranchDisplayColor(remote, 0xFF744DA9);
            repo.ApplyPresetBranchFilter();
            Check(repo.UIStates.HistoryFilters.Single(f => f.Pattern == remote.FullName).Color == 0xFF744DA9,
                "Changing the branch color also updates the qualified Exact names rule");
            var reopened = new FilterModeInGraph(repo, remote);
            Check(reopened.IsFiltered, "A reopened visibility menu reflects the applied Exact names rule");
            reopened.IsFiltered = false;
            Check(repo.PresetBranchExactNames == "main" && draft.ExactNames == "main" && string.IsNullOrEmpty(repo.PresetBranchExcludeNames),
                "Canceling Include removes just its Exact names rule without creating an exclusion");
            visibility = new FilterModeInGraph(repo, remote) { IsFiltered = true };
            visibility.IsExcluded = true;
            Check(!repo.Settings.GetPresetBranchExactNameList().Contains(remote.FriendlyName) &&
                repo.Settings.GetPresetBranchExcludeNameList().Contains(remote.FriendlyName),
                "Switching Include to Hide moves the branch from Exact names to Exclude names");
            visibility.IsFiltered = true;
            Check(repo.Settings.GetPresetBranchExactNameList().Contains(remote.FriendlyName) &&
                !repo.Settings.GetPresetBranchExcludeNameList().Contains(remote.FriendlyName),
                "Switching Hide to Include moves the branch back without conflicting chips");
            repo.SetBranchFilterMode(local, FilterMode.Included, false, true);
            Check(repo.Settings.GetPresetBranchExactNameList().Contains(local.Name) &&
                repo.Settings.GetPresetBranchExactNameList().Contains(remote.FriendlyName),
                "Sidebar Include synchronizes both targets of a tracked local branch");
            repo.SetBranchFilterMode(local, FilterMode.None, false, true);
            Check(repo.PresetBranchExactNames == "main", "Resetting a tracked branch removes both exact targets and keeps other rules");
            new FilterModeInGraph(repo, remote) { IsFiltered = true };
            var beforeTag = repo.PresetBranchExactNames;
            new FilterModeInGraph(repo, new Tag { Name = "v1" }) { IsFiltered = true };
            Check(repo.PresetBranchExactNames == beforeTag, "Tag inclusion never adds branch Exact names");
            Wait(repo.Settings.SaveAsync());
        }
        finally { repo.Close(); }
        repo = Open(path);
        try
        {
            Check(repo.Settings.GetPresetBranchExactNameList().SequenceEqual(new[] { "main", "origin/feature/same" }) &&
                repo.PresetBranchFilterDraft.ExactNames == repo.PresetBranchExactNames,
                "Included branch rules and Exact names chips survive reopening the repository");
        }
        finally { repo.Close(); }
    }

    private static void TestVisibilityExclusions()
    {
        var path = NewRepository("visibility exclusion sync");
        Git(path, "commit", "--allow-empty", "-m", "Initial");
        Git(path, "branch", "feature/same");
        Git(path, "branch", "feature/other");
        foreach (var remote in new[] { "origin", "upstream" })
        {
            Git(path, "remote", "add", remote, path);
            Git(path, "update-ref", "refs/remotes/" + remote + "/feature/same", "HEAD");
        }
        Git(path, "branch", "--set-upstream-to=origin/feature/same", "feature/same");
        Git(path, "tag", "v1");
        var repo = Open(path);
        try
        {
            repo.PresetBranchExactNames = "main\nfeature/same\nfeature/other";
            repo.PresetBranchContainsPatterns = "feature/";
            repo.ApplyPresetBranchFilter();
            var remote = repo.Branches.Single(b => b.FullName == "refs/remotes/origin/feature/same");
            var local = repo.Branches.Single(b => b.IsLocal && b.Name == "feature/same");
            var draft = repo.PresetBranchFilterDraft;
            draft.ExactInput = "unfinished-exact";
            draft.ContainsInput = "unfinished-contains";
            draft.ExcludeInput = "unfinished-exclude";
            repo.IsPresetBranchFilterEditorExpanded = false;
            repo.UIStates.UpdateHistoryFilters("src", FilterType.Path, FilterMode.Included);
            var color = repo.UIStates.HistoryFilters.Single(f => f.Pattern == "refs/heads/main").Color;
            var visibility = new FilterModeInGraph(repo, remote) { IsExcluded = true };
            Check(repo.Settings.GetPresetBranchExcludeNameList().SequenceEqual(new[] { "origin/feature/same" }) &&
                draft.ExcludeNames == "origin/feature/same" && repo.IsPresetBranchFilterEditorExpanded,
                "Visibility Hide immediately appears as a qualified Exclude names chip and expands the editor");
            Check(draft.ExactInput == "unfinished-exact" && draft.ContainsInput == "unfinished-contains" && draft.ExcludeInput == "unfinished-exclude",
                "Hiding a branch preserves unfinished input in every filter field");
            Check(repo.UIStates.HistoryFilters.Any(f => f.Type == FilterType.Path && f.Pattern == "src") &&
                repo.UIStates.HistoryFilters.Single(f => f.Pattern == "refs/heads/main").Color == color,
                "Hide preserves unrelated path filters and assigned branch colors");
            visibility.IsExcluded = true;
            repo.SetBranchFilterMode(remote, FilterMode.Excluded, false, false);
            Check(repo.Settings.GetPresetBranchExcludeNameList().Count == 1, "Repeated Hide never duplicates exclusion chips");

            var view = new SourceGit.Views.Repository { DataContext = repo };
            var window = new Window { Content = view, Width = 1200, Height = 850 };
            try
            {
                window.Show();
                PumpFor(150);
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "origin/feature/same" &&
                    t.GetVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("preset_rule_chip"))),
                    "The hidden remote branch renders in the sidebar's rule chips");
                Check(view.FindControl<Button>("ApplyPresetBranchFilterButton").IsEffectivelyEnabled,
                    "Apply stays available after a visibility change");
                CaptureUxFrame(window, "visibility-exclude-chip");
            }
            finally { window.Close(); }

            repo.ApplyPresetBranchFilter();
            var visible = (List<Branch>)Invoke(repo, "GetVisibleBranchesByCurrentFilter");
            Check(!visible.Any(b => b.FullName == remote.FullName) && visible.Any(b => b.FullName == local.FullName) &&
                visible.Any(b => b.FullName == "refs/remotes/upstream/feature/same"),
                "Reapplying a remote exclusion does not hide same-named local or other-remote branches");
            var reopened = new FilterModeInGraph(repo, remote);
            Check(reopened.IsExcluded, "Reopened visibility menu reflects an exclusion applied through preset rules");
            reopened.IsExcluded = false;
            Check(string.IsNullOrEmpty(draft.ExcludeNames) && string.IsNullOrEmpty(repo.PresetBranchExcludeNames),
                "Unhide removes the rule even when the branch is absent from the filtered sidebar");

            repo.SetBranchFilterMode(remote, FilterMode.Excluded, false, true);
            var included = new FilterModeInGraph(repo, remote) { IsFiltered = true };
            Check(included.IsFiltered && !included.IsExcluded && !repo.Settings.GetPresetBranchExcludeNameList().Contains(remote.FriendlyName),
                "Switching Hide to Include removes the conflicting exclusion");

            repo.SetBranchFilterMode(local, FilterMode.Excluded, false, true);
            Check(repo.Settings.GetPresetBranchExcludeNameList().Contains(local.Name) &&
                repo.Settings.GetPresetBranchExcludeNameList().Contains(remote.FriendlyName),
                "Hiding a tracked local branch synchronizes both existing visibility targets");
            repo.SetBranchFilterMode(local, FilterMode.None, false, true);
            Check(string.IsNullOrEmpty(repo.PresetBranchExcludeNames), "Resetting a tracked branch clears both exclusions");
            repo.PresetBranchExcludeNames = "feature/same";
            repo.ApplyPresetBranchFilter();
            visible = (List<Branch>)Invoke(repo, "GetVisibleBranchesByCurrentFilter");
            Check(visible.All(b => b.Name != "feature/same"), "Legacy unqualified exclusion names retain their matching behavior");
            repo.PresetBranchExcludeNames = "origin/feature/same";
            repo.ApplyPresetBranchFilter();
            Check(repo.AddBranchToPresetFilter(remote) && string.IsNullOrEmpty(repo.PresetBranchExcludeNames),
                "Add branch to filter also resolves a qualified Hide rule");
            var beforeTag = repo.PresetBranchExcludeNames;
            var tag = new FilterModeInGraph(repo, new Tag { Name = "v1" }) { IsExcluded = true };
            Check(tag.IsExcluded && repo.PresetBranchExcludeNames == beforeTag, "Hiding tags never adds branch exclusion names");

            repo.PresetBranchExactNames = "";
            repo.PresetBranchContainsPatterns = "";
            repo.PresetBranchExcludeNames = "";
            repo.ApplyPresetBranchFilter();
            repo.SetBranchFilterMode(remote, FilterMode.Excluded, false, true);
            repo.ApplyPresetBranchFilter();
            Check(repo.UIStates.GetHistoryFilterMode(remote.FullName) == FilterMode.Excluded &&
                repo.UIStates.GetHistoryFilterMode(local.FullName) != FilterMode.Excluded,
                "Exclude-only presets honor qualified remote exclusions");
            Wait(repo.Settings.SaveAsync());
        }
        finally { repo.Close(); }

        repo = Open(path);
        try
        {
            Check(repo.PresetBranchExcludeNames == "origin/feature/same" && repo.PresetBranchFilterDraft.ExcludeNames == repo.PresetBranchExcludeNames,
                "Hidden branch rules and their sidebar chips survive reopening the repository");
        }
        finally { repo.Close(); }
    }
}
