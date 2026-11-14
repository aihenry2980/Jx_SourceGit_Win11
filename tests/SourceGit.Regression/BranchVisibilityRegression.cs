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
            Check(graph.Items.OfType<MenuItem>().Count(item => item.Classes.Contains("delete_branch")) == 2 &&
                !graph.Items.OfType<MenuItem>().SelectMany(FindDeleteItems).Any(),
                "Graph branch deletion is available at the top level, not duplicated in submenus");
            var localDelete = FindDeleteItems(treeLocal).Single();
            var remoteDelete = FindDeleteItems(treeRemote).Single();
            var disabled = FindDeleteItems(treeCurrent).Single();
            Check(localDelete.IsEnabled && remoteDelete.IsEnabled && !disabled.IsEnabled,
                "Sidebar delete styling preserves current-branch deletion protection");
            var bulkDelete = new MenuItem
            {
                Classes = { "delete_branch" },
                Header = SourceGit.Views.ControlExtensions.CreateBranchDeleteActionBadge(SourceGit.App.Text("BranchCM.DeleteMultiBranches", 2)),
                Icon = tree.CreateMenuIcon("Icons.Clear"),
            };

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
                var menuItems = new[] { localDelete, remoteDelete }.Concat(graphDeletes).Append(disabled).Append(bulkDelete).ToList();
                foreach (var item in menuItems)
                {
                    if (item.Parent is ItemsControl parent)
                        parent.Items.Remove(item);
                    panel.Children.Add(item);
                }
                var window = new Window { Width = 640, Height = 270, Content = panel };
                window.Show();
                PumpFor(100);
                foreach (var item in menuItems)
                {
                    var border = item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot");
                    var action = item.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("branch_delete_action"));
                    var scope = item.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Classes.Contains("branch_delete_scope"));
                    if (!item.IsEnabled)
                    {
                        Check(!IsRed(border.Background) && action.Opacity <= 0.4 && scope?.Opacity <= 0.4,
                            "Disabled delete badges are muted and the row is not red");
                        continue;
                    }
                    Check(!IsRed(border.Background) && IsRed(action.Background) && action.Bounds.Width < border.Bounds.Width - 20,
                        "Only the Delete action badge is red, not the menu row in " + theme);
                    var icon = (Avalonia.Controls.Shapes.Path)item.Icon;
                    Check(IsRed(icon.Fill), "Delete icon retains its danger color");
                    Check(((TextBlock)action.Child).Foreground is ISolidColorBrush { Color: var actionColor } && actionColor == Colors.White,
                        "Delete action uses white text on its red badge");
                    if (scope != null)
                    {
                        var localScope = scope.Classes.Contains("local");
                        Check(scope.Background is ISolidColorBrush { Color: var scopeBackground } &&
                            scopeBackground == Color.Parse(localScope ? "#21834A" : "#F4CE61"),
                            "Local scope is green and remote scope is yellow in " + theme);
                        Check(((TextBlock)scope.Child).Foreground is ISolidColorBrush { Color: var scopeText } &&
                            scopeText == (localScope ? Colors.White : Color.Parse("#332500")),
                            "Scope text contrasts with the green/yellow badge");
                    }
                    item.IsSelected = true;
                    PumpFor(20);
                    Check(!IsRed(border.Background) && IsRed(action.Background),
                        "Hover uses the normal menu highlight and keeps the red Delete badge");
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
