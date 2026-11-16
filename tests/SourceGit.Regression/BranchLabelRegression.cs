using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Decorator = SourceGit.Models.Decorator;

internal static partial class Program
{
    private static void TestBranchLabels()
    {
        var preferences = SourceGit.ViewModels.Preferences.Instance;
        var previous = preferences.BranchNameWrapLength;
        try
        {
            preferences.BranchNameWrapLength = 25;
            TestBranchLabelsAtThreshold25();
        }
        finally { preferences.BranchNameWrapLength = previous; }
    }

    private static void TestBranchLabelsAtThreshold25()
    {
        TestShortTrackingBranchRow();
        Check(BranchLabelLayout.GetLineBreak(new string('a', 25), 25) == -1, "25-character branch stays on one line");
        Check(BranchLabelLayout.GetLineBreak(new string('a', 26), 25) == 13, "26-character branch splits into balanced lines");
        var unicode = string.Concat(Enumerable.Repeat("e\u0301", 26));
        Check(BranchLabelLayout.GetLineBreak(unicode, 25) == 26 && BranchLabelLayout.GetLineBreak(unicode[..50], 25) == -1,
            "Threshold and split respect combining-character boundaries");
        var emoji = string.Concat(Enumerable.Repeat("\U0001F680", 26));
        Check(BranchLabelLayout.GetLineBreak(emoji, 25) == 26, "Line split never separates a surrogate pair");
        var longName = "feature/AW2020TE-13568/hapsim-home-network-fixes";
        var remoteName = "origin/" + longName;
        var split = BranchLabelLayout.GetLineBreak(longName, 25);
        Check(longName[split - 1] is '/' or '-' or '_' or '.', "Long branch prefers a natural separator");
        var metricCommits = new List<Commit>
        {
            new() { Decorators = [new Decorator { Type = DecoratorType.LocalBranchHead, Name = "main" }] },
            new() { Decorators = [new Decorator { Type = DecoratorType.RemoteBranchHead, Name = remoteName }] },
            new() { Decorators = [new Decorator { Type = DecoratorType.Tag, Name = longName }] },
            new() { Decorators = [new Decorator { Type = DecoratorType.RemoteBranchHead, Name = remoteName, Color = 0x18808080 }] },
        };
        var metrics = new CommitGraphRowMetrics(metricCommits, 23, 42, branchNameWrapLength: 25);
        Check(Enumerable.Range(0, 4).Select(metrics.GetHeight).SequenceEqual(new double[] { 23, 42, 23, 23 }),
            "Short names, tags and abbreviated muted branches never expand other rows");
        Check(metrics.GetTop(2) == 65 && metrics.GetY(1.5) == 44 && metrics.GetY(-0.5) == -11.5 && metrics.GetY(4.5) == 122.5,
            "Cumulative row coordinates cover centers, boundaries and offscreen paths");
        var measured = new CommitGraphLayout(0, 500, 23, Rows: metrics,
            VisibleRowCenters: [new Point(1.5, 50), new Point(2.5, 83)]);
        Check(measured.GetY(1.5) == 50 && measured.GetY(2) == 66.5 && measured.GetY(2.5) == 83 && measured.GetY(0.5) == 17.5,
            "Viewport interpolation aligns measured centers and continues smoothly offscreen");
        var compactPair = new List<Decorator>
        {
            new() { Name = new string('a', 25), Type = DecoratorType.LocalBranchHead },
            new() { Name = "origin/" + new string('a', 25), Type = DecoratorType.RemoteBranchHead },
        };
        Check(BranchLabelLayout.HasTwoLineNames(compactPair, false, 25) && !BranchLabelLayout.HasTwoLineNames(compactPair, true, 25),
            "Hidden remote prefixes in L+R chips do not unnecessarily expand the commit row");
        Check(BranchLabelLayout.HasTwoLineNames(compactPair.AsEnumerable().Reverse().ToList(), true, 25),
            "Row sizing also matches presenters whose decorators arrive in remote-first order");
        var field = typeof(CommitRefsPresenter).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
        List<CommitRefsPresenter.RenderItem> Items(CommitRefsPresenter presenter) => (List<CommitRefsPresenter.RenderItem>)field.GetValue(presenter);
        var window = new Window { Width = 1250, Height = 700 };
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16), Background = (IBrush)Application.Current.FindResource("Brush.Contents") };
                window.Content = panel;
                window.Show();
                foreach (var type in new[] { DecoratorType.LocalBranchHead, DecoratorType.RemoteBranchHead, DecoratorType.CurrentBranchHead, DecoratorType.Tag })
                {
                    var decorator = new Decorator { Name = type == DecoratorType.RemoteBranchHead ? remoteName : longName,
                        Type = type, Color = 0xFF2574C9, IsBranchFoldable = type != DecoratorType.Tag, IsRebaseBaseBranch = true };
                    var commit = new Commit { Decorators = [decorator] };
                    if (type == DecoratorType.CurrentBranchHead)
                        commit.Decorators.Add(new Decorator { Type = DecoratorType.RemoteBranchHead, Name = remoteName, Color = decorator.Color });
                    var presenter = new CommitRefsPresenter { DataContext = commit, FontFamily = FontFamily.Default,
                        Foreground = (IBrush)Application.Current.FindResource("Brush.FG1"), Background = panel.Background,
                        CompactTrackingBranches = true, BranchNameWrapLength = 25, HighlightText = "13568/hapsim" };
                    panel.Children.Add(presenter);
                    foreach (var size in new[] { 10.5, 14, 21, 35, 14 })
                    {
                        presenter.FontSize = size;
                        presenter.Measure(Size.Infinity);
                        presenter.Arrange(new Rect(presenter.DesiredSize));
                        var item = Items(presenter).Single();
                        if (type == DecoratorType.Tag)
                        {
                            Check(item.LineBreak == -1, "Long tags keep their existing single-line appearance");
                            continue;
                        }
                        Check(item.LineBreak > 0 && item.LineBreak < decorator.Name.Length && item.FirstLine != null && item.SecondLine != null && item.RawLabel == decorator.Name,
                            $"Full {type} name survives two-line presentation at {size} in {theme}");
                        Check(item.FirstLine.Height + item.SecondLine.Height + 3 <= item.Height && item.Height <= presenter.DesiredSize.Height,
                            "Both lines fit inside the branch background without shrinking text");
                        Check(presenter.DecoratorAt(new Point(item.LeadingWidth + 6, item.Height - 3)) == decorator,
                            "Second line targets the original branch");
                        Check(presenter.TryGetFoldableDecoratorAt(new Point(item.Width - 8, item.Height / 2), out var target) && target == decorator,
                            "Fold control keeps its hit area on a taller chip");
                        if (type == DecoratorType.CurrentBranchHead)
                            Check(item.IsTrackingPair && item.HeadLabel != null && item.TrackingPairLabel.Height < item.Height - 4 &&
                                item.LeadingWidth + item.Label.Width + 4 + item.HeadBadgeWidth < item.Width - 17,
                                "L+R and HEAD fit alongside a two-line checked-out branch");
                    }
                    presenter.FontSize = 21;
                }

                // A short chip follows a tall chip; the next line must start below both.
                var tall = new Decorator { Name = longName, Type = DecoratorType.LocalBranchHead, Color = 0xFF2574C9 };
                var shortBranch = new Decorator { Name = "main", Type = DecoratorType.LocalBranchHead, Color = 0xFF2E8B57 };
                var next = new Decorator { Name = "release/next", Type = DecoratorType.LocalBranchHead, Color = 0xFFF7630C };
                var wrapped = new CommitRefsPresenter { DataContext = new Commit { Decorators = [tall, shortBranch, next] },
                    FontFamily = FontFamily.Default, FontSize = 18, AllowWrap = true, BranchNameWrapLength = 25,
                    Foreground = (IBrush)Application.Current.FindResource("Brush.FG1"), Background = panel.Background };
                wrapped.Measure(Size.Infinity);
                var items = Items(wrapped);
                wrapped.Width = items[0].Width + items[1].Width + 10;
                panel.Children.Add(wrapped);
                PumpFor(100);
                items = Items(wrapped);
                Check(wrapped.DecoratorAt(new Point(8, items[0].Height + 8)) == next,
                    "Wrapped rows use their tallest chip consistently for rendering and hit testing");
                CaptureUxFrame(window, "two-line-branches-" + theme);
            }
        }
        finally { window.Close(); Application.Current.RequestedThemeVariant = ThemeVariant.Light; }

        var path = NewRepository("two line graph");
        for (var i = 0; i < 64; i++)
            Git(path, "commit", "--allow-empty", "-m", "Commit " + i);
        Git(path, "checkout", "-b", longName);
        Git(path, "update-ref", "refs/remotes/origin/" + longName, "HEAD");
        Git(path, "branch", "release/a-second-long-branch-for-row-sizing", "HEAD~10");
        Git(path, "branch", "feature/a-third-long-branch-for-recycling", "HEAD~35");
        var prefixOnly = new string('a', 25);
        Git(path, "branch", prefixOnly, "HEAD~20");
        Git(path, "update-ref", "refs/remotes/origin/" + prefixOnly, "HEAD~20");
        var repo = Open(path);
        var prefs = SourceGit.ViewModels.Preferences.Instance;
        var zoom = prefs.HistoriesZoom;
        var compactMode = prefs.CompactTrackingBranches;
        var view = new Histories { DataContext = repo.Histories };
        window = new Window { Content = view, Width = 1450, Height = 850 };
        try
        {
            window.Show();
            WaitUntil(() => repo.Histories.Commits.Count >= 64, "Two-line graph did not load");
            var grid = view.FindControl<HistoriesCommitList>("CommitListContainer");
            var graph = view.FindControl<SourceGit.Views.CommitGraph>("CommitGraph");
            void CheckVisibleRows()
            {
                var rows = grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>()
                    .Where(r => r.IsVisible && r.Bounds.Bottom > 0).ToList();
                Check(rows.Count > 2, "Virtualized graph has enough visible rows to verify mixed heights");
                foreach (var row in rows)
                {
                    var expanded = BranchLabelLayout.HasTwoLineNames(repo.Histories.Commits[row.Index].Decorators, grid.CompactTrackingBranches, grid.BranchNameWrapLength);
                    var refsHeight = row.GetVisualDescendants().OfType<CommitRefsPresenter>().Select(p => p.DesiredSize.Height).DefaultIfEmpty(0).Max();
                    var expected = Math.Max(grid.RowMetrics.CompactHeight, refsHeight);
                    Check((expanded ? row.Bounds.Height >= expected - 1 : Math.Abs(row.Bounds.Height - expected) < 1),
                        "Only commits with a visible two-line branch use expanded height: row " + row.Index);
                    var actualY = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), graph).Value.Y;
                    var graphY = graph.Layout.GetY(row.Index + 0.5) - graph.Layout.StartY + graph.Layout.OffsetY;
                    Check(Math.Abs(actualY - graphY) < 1, $"Graph anchor aligns with row {row.Index}: actual={actualY}, graph={graphY}");
                }
            }
            foreach (var value in new[] { 1.0, 1.5, 2.5, 1.0 })
            {
                prefs.HistoriesZoom = value;
                PumpFor(150);
                var presenters = grid.GetVisualDescendants().OfType<CommitRefsPresenter>().Where(p => Items(p).Count > 0).ToList();
                Check(grid.ExpandedRowHeight > prefs.HistoriesRowHeight && grid.CompactRowHeight == prefs.HistoriesRowHeight &&
                    presenters.All(p => Items(p).All(item => item.Height + 1 <= p.FindAncestorOfType<DataGridRow>().Bounds.Height + 1)),
                    "Graph row height accommodates full-size two-line branches at zoom " + value);
                Check(graph.Layout != null && ReferenceEquals(graph.Layout.Rows, grid.RowMetrics),
                    "Graph connections and grid rows share the same height at zoom " + value);
                CheckVisibleRows();
                CaptureUxFrame(window, "two-line-graph-" + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach (var index in new[] { 35, 50, 10, 0 })
            {
                grid.ScrollIntoView(repo.Histories.Commits[index], null);
                PumpFor(150);
                CaptureUxFrame(window, "two-line-graph-scroll-" + index);
                CheckVisibleRows();
            }
            var middle = repo.Histories.Commits[35];
            repo.Histories.NavigateTo(middle.SHA);
            PumpFor(150);
            CheckVisibleRows();
            var navigated = grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>()
                .Single(r => ReferenceEquals(r.DataContext, middle));
            Check(navigated.Bounds.Bottom > 0 && navigated.Bounds.Top < grid.FindDescendantOfType<DataGridRowsPresenter>().Bounds.Height &&
                repo.Histories.SelectedCommits.Single().SHA == middle.SHA,
                "Navigating to a mixed-height commit selects it and keeps it in the viewport");
            foreach (var compact in new[] { false, true, false })
            {
                prefs.CompactTrackingBranches = compact;
                grid.ScrollIntoView(repo.Histories.Commits[20], null);
                PumpFor(150);
                var prefixRow = grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>()
                    .First(r => ReferenceEquals(r.DataContext, repo.Histories.Commits[20]));
                Check(compact ? Math.Abs(prefixRow.Bounds.Height - grid.RowMetrics.CompactHeight) < 1 : prefixRow.Bounds.Height > grid.RowMetrics.CompactHeight,
                    "Changing L+R mode sizes rows by the names actually displayed: " + compact);
                CheckVisibleRows();
            }
            repo.Histories.Commits = repo.Histories.Commits.Select(c => new Commit { SHA = c.SHA, Parents = c.Parents, Subject = c.Subject,
                Decorators = c.Decorators.Where(d => !BranchLabelLayout.IsBranch(d)).ToList() }).ToList();
            PumpFor(100);
            Check(Enumerable.Range(0, grid.RowMetrics.Count).All(i => grid.RowMetrics.GetHeight(i) == prefs.HistoriesRowHeight),
                "Histories without long branch labels restore their compact row height");
        }
        finally { window.Close(); prefs.HistoriesZoom = zoom; prefs.CompactTrackingBranches = compactMode; repo.Close(); }
    }

    private static void TestShortTrackingBranchRow()
    {
        var path = NewRepository("short tracking branch with tag");
        for (var i = 0; i < 12; i++)
            Git(path, "commit", "--allow-empty", "-m", "Commit " + i);
        Git(path, "branch", "initial-git-staged-files", "HEAD~7");
        Git(path, "update-ref", "refs/remotes/origin/initial-git-staged-files", "HEAD~7");
        Git(path, "tag", "v1.0.6", "HEAD~7");
        var prefs = SourceGit.ViewModels.Preferences.Instance;
        var oldCompact = prefs.CompactTrackingBranches;
        var oldZoom = prefs.HistoriesZoom;
        prefs.CompactTrackingBranches = true;
        var repo = Open(path);
        var view = new Histories { DataContext = repo.Histories };
        var window = new Window { Content = view, Width = 1450, Height = 900 };
        try
        {
            window.Show();
            WaitUntil(() => repo.Histories.Commits.Count == 12, "Short branch history did not load");
            var grid = view.FindControl<HistoriesCommitList>("CommitListContainer");
            foreach (var zoom in new[] { 1.0, 1.5, 2.0, 1.0 })
            {
                prefs.HistoriesZoom = zoom;
                PumpFor(150);
                var row = grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>()
                    .Single(r => r.Index == 7 && r.IsVisible);
                CaptureUxFrame(window, "short-tracking-row-" + zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Check(Math.Abs(row.Bounds.Height - grid.RowMetrics.CompactHeight) < 1,
                    $"Screenshot branch initial-git-staged-files plus v1.0.6 uses one row at zoom {zoom}: actual={row.Bounds.Height}, compact={grid.RowMetrics.CompactHeight}");
            }

            DataGridRow TargetRow() => grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>()
                .Single(r => r.Index == 7 && r.IsVisible);
            // Estimates can lag display preferences during refresh. Measured chips must win.
            grid.SetCurrentValue(HistoriesCommitList.CompactTrackingBranchesProperty, false);
            PumpFor(150);
            Check(grid.RowMetrics.GetHeight(7) > grid.RowMetrics.CompactHeight && TargetRow().Bounds.Height == grid.RowMetrics.CompactHeight,
                "An uncollapsed remote-name estimate cannot enlarge a single-line L+R chip");
            TargetRow().Height = grid.ExpandedRowHeight;
            PumpFor(150);
            Check(TargetRow().Bounds.Height == grid.RowMetrics.CompactHeight,
                "A reused or stale tall row shrinks to match its actual short branch chip");
            prefs.CompactTrackingBranches = false;
            PumpFor(100);
            grid.SetCurrentValue(HistoriesCommitList.CompactTrackingBranchesProperty, true);
            PumpFor(150);
            Check(grid.RowMetrics.GetHeight(7) == grid.RowMetrics.CompactHeight && TargetRow().Bounds.Height > grid.RowMetrics.CompactHeight,
                "A visible two-line remote chip grows even if the offscreen estimate is compact");
        }
        finally
        {
            window.Close();
            repo.Close();
            prefs.CompactTrackingBranches = oldCompact;
            prefs.HistoriesZoom = oldZoom;
        }
    }
}
