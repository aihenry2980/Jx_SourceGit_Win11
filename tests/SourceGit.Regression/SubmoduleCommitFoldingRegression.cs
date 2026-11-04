using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;

internal static partial class Program
{
    private static void TestSubmoduleCommitFolding()
    {
        static Commit Update(string sha, string parent, params string[] paths) => new()
        {
            SHA = sha, Parents = [parent], Subject = "Update pointers",
            HasSubmodulePointerChange = true, ChangedFileCount = paths.Length,
            SubmodulePointerChangeCount = paths.Length, ChangedSubmodulePaths = paths,
        };
        static List<Commit> Chain() => [Update("newest", "middle", "alpha", "beta"),
            Update("middle", "oldest", "beta", "alpha"), Update("oldest", "base", "alpha", "beta"),
            new() { SHA = "base" }];

        var commits = Chain();
        SubmoduleCommitFolding.Apply(commits, new HashSet<string>());
        Check(commits.Count == 2 && commits[0].SubmoduleCommitGroupCount == 3,
            "Consecutive pointer-only updates to the same path set fold into one row");
        Check(commits[0].Parents[0] == "middle" && commits[0].GraphParents[0] == "base",
            "Folding preserves real Git parents and bridges only the displayed graph");
        Check(commits[0].MatchesHistoryQuickFind("middle"),
            "Quick find can identify a collapsed group by a hidden commit SHA");
        var graph = CommitGraph.Generate(commits, false, CommitGraphHighlighting.All, []);
        Check(graph.Dots.Count == 2 && graph.Dots[0].HasFoldedSubmoduleCommits &&
            graph.Dots[0].Center.X == graph.Dots[1].Center.X,
            "Collapsed groups keep a connected lane and a stacked graph marker");
        commits = Chain();
        SubmoduleCommitFolding.Apply(commits, new HashSet<string> { "newest" });
        Check(commits.Count == 4 && commits[0].IsSubmoduleCommitGroupExpanded && commits[0].GraphParents[0] == "middle",
            "Expanded groups show every original commit and retain their collapse control");

        void KeepsMiddle(Action<List<Commit>> modify, string label)
        {
            var chain = Chain();
            modify(chain);
            SubmoduleCommitFolding.Apply(chain, new HashSet<string>());
            Check(chain.Any(c => c.SHA == "middle"), label);
        }
        KeepsMiddle(c => c[1].RegularFileChangeCount = 1, "Mixed regular-file commits never disappear into a pointer group");
        KeepsMiddle(c => c[1].ChangedSubmodulePaths = ["alpha", "gamma"], "Different submodule path sets start a different group");
        KeepsMiddle(c => c[1].Decorators.Add(new() { Type = DecoratorType.Tag, Name = "release" }), "Tagged commits remain visible");
        KeepsMiddle(c => c[1].Decorators.Add(new() { Type = DecoratorType.LocalBranchHead, Name = "feature" }), "Branch tips remain visible");
        KeepsMiddle(c => c.Add(new() { SHA = "fork", Parents = ["middle"] }), "Commits at branch junctions remain visible");
        KeepsMiddle(c => c[1].Parents.Add("other"), "Merge commits remain visible");
        KeepsMiddle(c => c[0].Parents = ["unloaded"], "Adjacent rows not linked by real parentage do not fold together");
        KeepsMiddle(c => c[1].ChangedSubmodulePaths = [], "Missing path metadata never guesses a matching submodule set");
        KeepsMiddle(c => c[1].HasTypeChange = true, "Mode or type changes remain visible");
        KeepsMiddle(c => c[1].HasRenameOrCopyChange = true, "Renames and copies remain visible");
        KeepsMiddle(c => c[1].FoldedCommitsBelow = 5, "Existing branch fold markers are not hidden by pointer folding");

        var path = NewRepository("submodule commit folding");
        Git(path, "commit", "--allow-empty", "-m", "Seed A");
        var seedA = Git(path, "rev-parse", "HEAD");
        Git(path, "commit", "--allow-empty", "-m", "Seed B");
        var seedB = Git(path, "rev-parse", "HEAD");
        Git(path, "config", "-f", ".gitmodules", "submodule.alpha.path", "modules/alpha");
        Git(path, "config", "-f", ".gitmodules", "submodule.alpha.url", ".");
        Git(path, "update-index", "--add", "--cacheinfo", $"160000,{seedA},modules/alpha");
        Git(path, "add", ".gitmodules");
        Git(path, "commit", "-m", "Initialize submodule");
        var basis = Git(path, "rev-parse", "HEAD");
        var updates = new List<string>();
        foreach (var target in new[] { seedB, seedA, seedB })
        {
            Git(path, "update-index", "--cacheinfo", $"160000,{target},modules/alpha");
            Git(path, "commit", "-m", "Update alpha pointer");
            updates.Add(Git(path, "rev-parse", "HEAD"));
        }
        var repo = Open(path);
        Window window = null;
        void Refresh() => Wait((Task)Invoke(repo, "RefreshCommitsAsync", false));
        try
        {
            repo.SetPresetBranchFilterMode(true);
            Refresh();
            var fullCount = repo.Histories.Commits.Count;
            Check(!repo.FoldSubmoduleUpdatesInHistory && updates.All(sha => repo.Histories.Commits.Any(c => c.SHA == sha)),
                "New repositories show all updates until folding is enabled");
            var view = new SourceGit.Views.Repository { DataContext = repo };
            window = new Window { Content = view, Width = 1700, Height = 850 };
            window.Show();
            PumpFor(150);
            repo.Histories.SelectedCommits = [repo.Histories.Commits.Single(c => c.SHA == updates[0])];
            var toggle = view.FindControl<CheckBox>("FoldSubmoduleUpdatesToggle");
            toggle.IsChecked = true;
            Refresh();
            PumpFor(150);
            var group = repo.Histories.Commits.Single(c => c.HasSubmoduleCommitGroup);
            Check(repo.FoldSubmoduleUpdatesInHistory && repo.Histories.Commits.Count == fullCount - 2 && group.SHA == updates[^1],
                "The visible folding toggle collapses actual gitlink-only commits");
            Check(repo.Histories.SelectedCommits.Any(c => c.SHA == group.SHA),
                "Collapsing a selected hidden commit keeps its group selected");
            Check(group.SubmoduleCommitGroupCount == 3 && group.Parents[0] == updates[1] && group.GraphParents[0] == basis,
                "Real metadata groups three pointer updates without rewriting the newest SHA or parent");
            Button FoldButton() => view.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("submodule_commit_fold") && button.IsEffectivelyVisible);
            Check(FoldButton().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "3"),
                "The stacked indicator displays the total number of commits in the group");
            CaptureUxFrame(window, "submodule-fold-collapsed-Light");
            repo.FoldVisibleBranchesInGraph();
            Refresh();
            Check(repo.Histories.Commits.Any(c => c.FoldedCommitsBelow > 0) &&
                repo.Histories.Commits.All(c => !c.HasSubmoduleCommitGroup),
                "Manual branch folding remains independent and does not nest invisible pointer groups");
            repo.UnfoldAllBranchesInGraph();
            Refresh();
            PumpFor(100);
            Check(repo.Histories.Commits.Any(c => c.HasFoldedSubmoduleCommits),
                "Unfolding branches restores the automatic pointer groups");
            FoldButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Refresh();
            PumpFor(100);
            Check(repo.Histories.Commits.Count == fullCount && repo.Histories.Commits.Any(c => c.IsSubmoduleCommitGroupExpanded),
                "Clicking the stacked indicator expands the original commits");
            CaptureUxFrame(window, "submodule-fold-expanded");
            FoldButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Refresh();
            Check(repo.Histories.Commits.Count == fullCount - 2, "Clicking again collapses the same group");
            repo.NavigateToCommit(updates[0]);
            Refresh();
            PumpFor(50);
            Check(repo.Histories.Commits.Count == fullCount && repo.Histories.SelectedCommits.Any(c => c.SHA == updates[0]),
                "Navigating to a hidden SHA expands its group and selects the exact commit");
            repo.ToggleSubmoduleCommitGroup(repo.Histories.Commits.Single(c => c.HasSubmoduleCommitGroup));
            Refresh();
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            PumpFor(100);
            CaptureUxFrame(window, "submodule-fold-collapsed-Dark");
            window.Width = 1100;
            PumpFor(100);
            var position = toggle.TranslatePoint(default, window).Value;
            Check(position.X >= 0 && position.X + toggle.Bounds.Width <= window.ClientSize.Width,
                "The folding toggle remains inside a narrow desktop window");
            var unfold = view.FindControl<Button>("UnfoldAllBranchesButton");
            Check(unfold.TranslatePoint(default, window).Value.X + unfold.Bounds.Width <= window.ClientSize.Width,
                "Narrow toolbar keeps existing unfold controls accessible alongside the new toggle");
            CaptureUxFrame(window, "submodule-fold-1100");
            window.Width = 1024;
            PumpFor(200);
            var topo = view.FindControl<RadioButton>("HistoryTopoOrderButton");
            Check(topo.TranslatePoint(default, window).Value.X + topo.Bounds.Width <= window.ClientSize.Width,
                "History ordering and folding controls fit the minimum desktop window width");
            CaptureUxFrame(window, "submodule-fold-1024");
            var originalZoom = SourceGit.ViewModels.Preferences.Instance.HistoriesZoom;
            try
            {
                SourceGit.ViewModels.Preferences.Instance.HistoriesZoom = 30 / SourceGit.ViewModels.Preferences.Instance.DefaultFontSize;
                PumpFor(100);
                var indicator = FoldButton();
                var label = indicator.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "3");
                Check(label.Bounds.Height <= indicator.Bounds.Height && label.Bounds.Width <= indicator.Bounds.Width,
                    "Fold count remains inside its indicator when history text is zoomed");
                CaptureUxFrame(window, "submodule-fold-zoomed");
            }
            finally { SourceGit.ViewModels.Preferences.Instance.HistoriesZoom = originalZoom; }
            toggle.IsChecked = false;
            Refresh();
            Check(repo.Histories.Commits.Count == fullCount && repo.Histories.Commits.All(c => !c.HasSubmoduleCommitGroup),
                "Disabling folding removes every group and restores the original history");
            repo.FoldSubmoduleUpdatesInHistory = true;
            Refresh();
            repo.UIStates.Save();
            window.Close();
            window = null;
        }
        finally
        {
            window?.Close();
            repo.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
        repo = Open(path);
        try
        {
            Refresh();
            Check(repo.FoldSubmoduleUpdatesInHistory && repo.Histories.Commits.Any(c => c.HasFoldedSubmoduleCommits),
                "The folding preference survives closing and reopening the repository");
        }
        finally { repo.Close(); }

        File.WriteAllText(Path.Combine(path, "regular.txt"), "mixed change");
        Git(path, "add", "regular.txt");
        Git(path, "update-index", "--cacheinfo", $"160000,{seedA},modules/alpha");
        Git(path, "commit", "-m", "Mixed pointer and source change");
        var mixed = Git(path, "rev-parse", "HEAD");
        var stats = Wait(new SourceGit.Commands.QueryCommitSubmodulePointerFlags(path, "-10 HEAD -- modules/alpha").GetResultAsync());
        Check(stats[mixed].RegularFileChangeCount == 1 && stats[mixed].SubmodulePointerChangeCount == 1,
            "Path-filtered metadata still reports regular changes outside the submodule path");
    }
}
