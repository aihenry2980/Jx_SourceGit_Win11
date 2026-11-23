using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;

internal static partial class Program
{
    private static void TestUpstreamBranchPinningMerge()
    {
        TestSafeGraphPenRendering();
        var path = NewRepository("upstream pinned branches");
        Git(path, "commit", "--allow-empty", "-m", "Initial");
        Git(path, "branch", "feature/alpha");
        Git(path, "branch", "feature/beta");
        var repo = Open(path);
        Window window = null;
        var theme = Application.Current.RequestedThemeVariant;
        try
        {
            var alpha = repo.Branches.Single(b => b.Name == "feature/alpha");
            repo.TogglePinBranch(alpha);
            Check(repo.HasPinnedBranches && repo.IsPinnedBranchGroupExpanded && repo.PinnedBranchTrees.Single().Backend == alpha,
                "Pinning a local branch opens a flat pinned group without changing its branch identity");
            repo.Filter = "beta";
            Check(!repo.HasPinnedBranches && repo.IsBranchPinned(alpha), "Text filtering hides unmatched pins without forgetting them");
            repo.Filter = string.Empty;
            repo.IsPinnedBranchGroupExpanded = false;
            repo.Close();
            repo = Open(path);
            alpha = repo.Branches.Single(b => b.Name == "feature/alpha");
            Check(repo.HasPinnedBranches && repo.IsBranchPinned(alpha) && !repo.IsPinnedBranchGroupExpanded,
                "Opening a repository retains pinned branches and collapse state through the initial filter refresh");
            repo.IsPinnedBranchGroupExpanded = true;
            repo.PresetBranchExactNames = "main";
            repo.ApplyPresetBranchFilter();
            Check(repo.PinnedBranchTrees.Single().Path == alpha.FullName, "Preset graph filtering keeps explicitly pinned shortcuts available");

            var view = new SourceGit.Views.Repository { DataContext = repo };
            window = new Window { Content = view, Width = 1200, Height = 760 };
            window.Show();
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = variant;
                repo.IsLeftSidebarCompact = false;
                PumpFor(100);
                var pinned = view.FindControl<BranchTree>("PinnedBranchTree");
                var locals = view.FindControl<BranchTree>("LocalBranchTree");
                Check(pinned.IsEffectivelyVisible && pinned.Bounds.Height == 24 &&
                    pinned.TranslatePoint(default, view).Value.Y < locals.TranslatePoint(default, view).Value.Y,
                    "The pinned tree is above local branches with a correctly budgeted row: " + variant);
                var toggle = view.GetVisualDescendants().OfType<ToggleButton>()
                    .Single(b => Equals(ToolTip.GetTip(b), "Collapse the left sidebar into icon-only mode for smaller displays"));
                var x = toggle.TranslatePoint(default, view).Value.X;
                repo.IsLeftSidebarCompact = true;
                PumpFor(80);
                Check(Math.Abs(toggle.TranslatePoint(default, view).Value.X - x) < 1, "Sidebar toggle stays anchored when entering compact mode");
                repo.IsLeftSidebarCompact = false;
                repo.IsPinnedBranchGroupExpanded = false;
                PumpFor(60);
                Check(pinned.Height == 0 && !pinned.IsEffectivelyVisible, "Collapsing pins returns their vertical space to other sidebar groups");
                repo.IsPinnedBranchGroupExpanded = true;
                PumpFor(60);
                CaptureUxFrame(window, "upstream-pinned-sidebar-" + variant);
            }

            var histories = view.GetVisualDescendants().OfType<Histories>().Single();
            var commit = Wait(new SourceGit.Commands.QuerySingleCommit(path, "HEAD").GetResultAsync());
            IEnumerable<MenuItem> Items(ItemsControl menu) => menu.Items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(Items(i)));
            static string Label(MenuItem item) => item.Header is Control c
                ? string.Join(" ", c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)) : item.Header?.ToString() ?? string.Empty;
            foreach (var branch in new[] { repo.CurrentBranch, alpha })
            {
                var decorator = new SourceGit.Models.Decorator
                {
                    Name = branch.Name, Type = branch.IsCurrent ? DecoratorType.CurrentBranchHead : DecoratorType.LocalBranchHead,
                    Color = 0xFF0078D4,
                };
                commit.Decorators = [decorator];
                var menu = (ContextMenu)Invoke(histories, "CreateContextMenuForSingleCommit", repo, commit, false, decorator);
                var pins = Items(menu).Where(i => Label(i).StartsWith("Pin ") || Label(i).StartsWith("Unpin ")).ToArray();
                Check(pins.Length == 1 && pins[0].Header is Control,
                    "Graph menus expose one pin action with the existing colored branch header for " + branch.Name);
                var wasPinned = repo.IsBranchPinned(branch);
                pins[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(repo.IsBranchPinned(branch) != wasPinned, "Graph pin action changes the original local branch, not another same-SHA ref");
            }
            if (!repo.IsBranchPinned(alpha))
                repo.TogglePinBranch(alpha);
            var sidebar = (ContextMenu)Invoke(view.FindControl<BranchTree>("PinnedBranchTree"), "CreateContextMenuForLocalBranch", repo, alpha);
            Check(sidebar.Items.OfType<MenuItem>().Count(i => Label(i).StartsWith("Unpin ")) == 1,
                "Pinned sidebar context menus expose unpin without duplicating rename or delete actions");
            Git(path, "branch", "-m", alpha.Name, "feature/renamed");
            repo.RefreshAfterRenameBranch(alpha, "feature/renamed");
            Check(repo.UIStates.PinnedBranches.Contains("refs/heads/feature/renamed") &&
                !repo.UIStates.PinnedBranches.Contains(alpha.FullName), "Renaming a pinned branch updates its persisted full ref");
            Git(path, "branch", "-D", "feature/renamed");
            Wait(InvokeTask(repo, "RefreshBranchesAsync"));
            Check(!repo.UIStates.PinnedBranches.Contains("refs/heads/feature/renamed"), "Branch refresh removes deleted pins without retaining stale shortcuts");
        }
        finally { window?.Close(); repo.Close(); Application.Current.RequestedThemeVariant = theme; }
    }

    private static void TestSafeGraphPenRendering()
    {
        var commit = new Commit { Color = int.MaxValue, Decorators = [new() { Type = DecoratorType.LocalBranchHead, Name = "feature/safe-render" }] };
        var refs = new CommitRefsPresenter { DataContext = commit, FontFamily = FontFamily.Default, FontSize = 14, UseGraphColor = true };
        var graph = new SourceGit.Models.CommitGraph();
        graph.Dots.Add(new() { Center = new Point(12, 0.5), Color = int.MinValue, IsHighlighted = true, HasFoldedSubmoduleCommits = true });
        var graphView = new SourceGit.Views.CommitGraph { Graph = graph, Layout = new CommitGraphLayout(0, 80, 24), Height = 60 };
        var panel = new StackPanel();
        panel.Children.Add(refs);
        panel.Children.Add(graphView);
        var window = new Window { Content = panel, Width = 500, Height = 120 };
        try
        {
            window.Show();
            foreach (var colors in new[] { new List<Color>(), new List<Color> { Colors.Red }, new List<Color> { Colors.Blue, Colors.Green } })
            {
                SourceGit.Models.CommitGraph.SetPens(colors, 2);
                Check(SourceGit.Models.CommitGraph.GetPen(int.MinValue) != null && SourceGit.Models.CommitGraph.GetPen(int.MaxValue) != null,
                    "Graph pen snapshots safely handle empty, shortened and replaced palettes");
                refs.InvalidateMeasure();
                graphView.InvalidateVisual();
                PumpFor(40);
                CaptureUxFrame(window, "upstream-safe-pens-" + colors.Count);
                Check(refs.Bounds.Width > 0, "Ref chips and folded graph dots render with an out-of-range color index");
            }
            commit.Decorators.Clear();
            refs.InvalidateMeasure();
            PumpFor(40);
            Check(refs.Bounds.Width >= 0, "Ref measurement tolerates decoration removal after an earlier layout");
        }
        finally { window.Close(); SourceGit.Models.CommitGraph.SetDefaultPens(); }
    }
}
