using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Decorator = SourceGit.Models.Decorator;

internal static partial class Program
{
    private static void TestBranchVisibility()
    {
        var field = typeof(CommitRefsPresenter).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current.RequestedThemeVariant = theme;
            var dark = theme == ThemeVariant.Dark;
            var background = dark ? Brushes.Black : Brushes.White;
            var foreground = dark ? Brushes.White : Brushes.Black;
            var panel = new StackPanel { Background = background, Margin = new Thickness(16), Spacing = 12 };
            foreach (var pair in new[] { false, true })
            {
                foreach (var graphColor in new[] { false, true })
                {
                    var current = new Decorator { Type = DecoratorType.CurrentBranchHead, Name = "main",
                        Color = 0xFF2E8B57, IsRebaseBaseBranch = true, IsBranchFoldable = true };
                    var commit = new Commit { Color = 0, Decorators = [current,
                        new Decorator { Type = DecoratorType.LocalBranchHead, Name = "feature", Color = 0xFF2574C9 }] };
                    if (pair)
                        commit.Decorators.Add(new Decorator { Type = DecoratorType.RemoteBranchHead,
                            Name = "origin/main", Color = current.Color });
                    var presenter = new CommitRefsPresenter { DataContext = commit,
                        FontFamily = FontFamily.Default, Foreground = foreground, Background = background,
                        UseGraphColor = graphColor, CompactTrackingBranches = true };
                    foreach (var size in new[] { 10.5, 14, 21, 35, 14 })
                    {
                        presenter.FontSize = size;
                        presenter.Measure(Size.Infinity);
                        presenter.Arrange(new Rect(presenter.DesiredSize));
                        var items = (List<CommitRefsPresenter.RenderItem>)field.GetValue(presenter);
                        var item = items.Single(i => i.Decorator == current);
                        Check(item.HeadLabel != null && items.Count(i => i.HeadLabel != null) == 1,
                            $"Only the checked-out branch has HEAD: {theme}, pair {pair}, color {graphColor}, font {size}");
                        Check(item.HeadLabel.WidthIncludingTrailingWhitespace + 8 <= item.HeadBadgeWidth &&
                            item.HeadLabel.Height <= item.Height - 4, "HEAD text fits its badge at graph zoom");
                        var badgeRight = 1.5 + item.LeadingWidth + item.Label.Width + 4 + item.HeadBadgeWidth;
                        Check(badgeRight <= 1.5 + item.Width - 19, "HEAD stays clear of the fold button");
                        Check(((ISolidColorBrush)item.Brush).Color.ToUInt32() == current.Color && item.IsTrackingPair == pair,
                            "Checkout emphasis preserves the assigned branch color and L+R identity");
                        Check(presenter.TryGetFoldableDecoratorAt(new Point(1.5 + item.Width - 10, item.Height / 2), out var folded) &&
                            folded == current, "Fold button still targets the checked-out branch after badge resizing");
                        Check(presenter.DecoratorAt(new Point(badgeRight - 2, item.Height / 2)) == current,
                            "HEAD badge belongs to the branch hit target");
                    }
                    presenter.FontSize = pair ? 35 : 21;
                    panel.Children.Add(presenter);
                }
            }
            var window = new Window { Width = 1000, Height = 320, Content = panel };
            window.Show();
            PumpFor(100);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Check(frame != null, "Current branch preview renders in " + theme);
            frame.Save(Path.Combine(_root, $"current-branch-{theme}.png"));
            window.Close();
        }
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        TestDeleteBranchVisibility();
    }

    private static void TestDeleteBranchVisibility()
    {
        var path = NewRepository("delete-branch-visibility");
        Git(path, "commit", "--allow-empty", "-m", "initial");
        Git(path, "branch", "feature");
        Git(path, "remote", "add", "origin", path);
        Git(path, "update-ref", "refs/remotes/origin/feature", "HEAD");
        var repo = Open(path);
        var tree = new BranchTree { DataContext = repo };
        var treeWindow = new Window { Content = tree, Width = 400, Height = 240 };
        treeWindow.Show();
        PumpFor(100);
        try
        {
            var local = repo.Branches.Single(b => b.IsLocal && b.Name == "feature");
            var remote = repo.Branches.Single(b => !b.IsLocal);
            var treeLocal = (ContextMenu)Invoke(tree, "CreateContextMenuForLocalBranch", repo, local);
            var treeRemote = tree.CreateContextMenuForRemoteBranch(repo, remote);
            var treeCurrent = (ContextMenu)Invoke(tree, "CreateContextMenuForLocalBranch", repo, repo.CurrentBranch);
            var commit = Wait(new SourceGit.Commands.QuerySingleCommit(path, "HEAD").GetResultAsync());
            commit.Decorators = [new Decorator { Type = DecoratorType.LocalBranchHead, Name = "feature" },
                new Decorator { Type = DecoratorType.RemoteBranchHead, Name = "origin/feature" }];
            var histories = new Histories { DataContext = repo.Histories };
            var graph = (ContextMenu)Invoke(histories, "CreateContextMenuForSingleCommit", repo, commit, false, null);
            var graphDeletes = FindDeleteItems(graph).ToList();
            Check(graphDeletes.Count == 2, "Both graph local and remote delete actions use the danger style");
            var localDelete = FindDeleteItems(treeLocal).Single();
            var remoteDelete = FindDeleteItems(treeRemote).Single();
            var disabled = FindDeleteItems(treeCurrent).Single();
            Check(localDelete.IsEnabled && remoteDelete.IsEnabled && !disabled.IsEnabled,
                "Sidebar delete styling preserves current-branch deletion protection");

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
                var menuItems = new[] { localDelete, remoteDelete }.Concat(graphDeletes).Append(disabled).ToList();
                foreach (var item in menuItems)
                {
                    if (item.Parent is ItemsControl parent)
                        parent.Items.Remove(item);
                    panel.Children.Add(item);
                }
                var window = new Window { Width = 640, Height = 230, Content = panel };
                window.Show();
                PumpFor(100);
                foreach (var item in menuItems)
                {
                    var border = item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot");
                    if (!item.IsEnabled)
                    {
                        Check(border.Background is ISolidColorBrush { Color.A: 0 }, "Disabled delete does not look actionable");
                        continue;
                    }
                    Check(IsRed(border.Background) && border.Bounds.Width >= item.Bounds.Width - 1,
                        "Delete action has a full-width red background in " + theme);
                    var icon = (Avalonia.Controls.Shapes.Path)item.Icon;
                    Check(icon.Fill is ISolidColorBrush { Color: var iconColor } && iconColor == Colors.White,
                        "Delete icon remains white on red");
                    var labels = item.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.Parent is not Border && !string.IsNullOrEmpty(t.Text));
                    Check(labels.All(t => t.Foreground is ISolidColorBrush { Color: var color } && color == Colors.White),
                        "Delete label and shortcut remain readable on red");
                    item.IsSelected = true;
                    PumpFor(20);
                    Check(IsRed(border.Background) && ((ISolidColorBrush)border.Background).Color.R < 0xC6,
                        "Selected delete stays red and darkens");
                    item.IsSelected = false;
                }
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                frame.Save(Path.Combine(_root, $"delete-branch-{theme}.png"));
                window.Close();
                panel.Children.Clear();
            }
        }
        finally
        {
            treeWindow.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            repo.Close();
        }
    }

    private static IEnumerable<MenuItem> FindDeleteItems(ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            if (item.Classes.Contains("delete_branch"))
                yield return item;
            foreach (var child in FindDeleteItems(item))
                yield return child;
        }
    }

    private static bool IsRed(IBrush brush) => brush is ISolidColorBrush { Color: var color } &&
        color.A == 255 && color.R > 100 && color.R > color.G * 2 && color.R > color.B * 2;
}
