using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Decorator = SourceGit.Models.Decorator;

internal static partial class Program
{
    private static void TestBranchMenus()
    {
        var path = NewRepository("branch menu actions");
        Git(path, "commit", "--allow-empty", "-m", "Initial");
        const string name = "benbrandt/keep-agent-versions-in-use";
        Git(path, "branch", name);
        Git(path, "branch", "origin/keep-this-local-prefix");
        foreach (var remote in new[] { "origin", "company" })
        {
            Git(path, "remote", "add", remote, path);
            Git(path, "update-ref", "refs/remotes/" + remote + "/" + name, "HEAD");
        }
        var repo = Open(path);
        var histories = new Histories { DataContext = repo.Histories };
        var tree = new BranchTree { DataContext = repo };
        var hosts = new Grid { RowDefinitions = new RowDefinitions("1,1,*") };
        Grid.SetRow(tree, 1);
        hosts.Children.Add(histories);
        hosts.Children.Add(tree);
        var window = new Window { Content = hosts, Width = 1000, Height = 640 };
        var field = typeof(SourceGit.App).GetField("_launcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var previousLauncher = field.GetValue(Application.Current);
        var workspace = SourceGit.ViewModels.Preferences.Instance.GetActiveWorkspace();
        var previousRepositories = workspace.Repositories.ToArray();
        var previousIndex = workspace.ActiveIdx;
        workspace.Repositories.Clear();
        var launcher = new SourceGit.ViewModels.Launcher(null);
        var page = new SourceGit.ViewModels.LauncherPage(new SourceGit.ViewModels.RepositoryNode { Id = path }, repo);
        launcher.Pages.Add(page);
        launcher.ActivePage = page;
        field.SetValue(Application.Current, launcher);
        try
        {
            window.Show();
            var commit = Wait(new SourceGit.Commands.QuerySingleCommit(path, "HEAD").GetResultAsync());
            foreach (var branch in repo.Branches)
            {
                var decorator = new Decorator
                {
                    Name = branch.FriendlyName,
                    Type = branch.IsCurrent ? DecoratorType.CurrentBranchHead : branch.IsLocal ? DecoratorType.LocalBranchHead : DecoratorType.RemoteBranchHead,
                    Color = 0xFF744DA9,
                };
                commit.Decorators = [decorator];
                var menu = (ContextMenu)Invoke(histories, "CreateContextMenuForSingleCommit", repo, commit, false, decorator);
                var copy = menu.Items.OfType<MenuItem>().Single(item => item.Classes.Contains("copy_branch_name"));
                var preview = ((Control)copy.Header).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("branch_name_preview"));
                Check(preview.Text == branch.Name && Equals(ToolTip.GetTip(copy), branch.Name),
                    "Copy preview and tooltip show the exact clipboard value for " + branch.FriendlyName);
                copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpFor(30);
                Check(Wait(window.Clipboard.TryGetTextAsync()) == branch.Name,
                    "Graph copy keeps nested paths and removes only the actual remote prefix for " + branch.FriendlyName);

                if (branch.IsCurrent)
                {
                    Check(!FindDeleteItems(menu).Any(), "Graph never offers deletion of the checked-out branch");
                }
                else
                {
                    var delete = menu.Items.OfType<MenuItem>().Single(item => item.Classes.Contains("delete_branch"));
                    CheckBranchDeleteScope(delete, branch);
                    Check(delete.Classes.Contains("branch_action") && ReferenceEquals(delete.Background, copy.Background) && delete.Margin == copy.Margin,
                        "Delete shares the branch action group's live background, hover style, and indentation");
                    Check(delete.IsEnabled && menu.Items.IndexOf(delete) > menu.Items.IndexOf(copy) &&
                        !menu.Items.OfType<MenuItem>().SelectMany(FindDeleteItems).Any(),
                        "Delete is top-level after Copy, with no duplicate nested delete");
                    delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(page.Popup is SourceGit.ViewModels.DeleteBranch popup && popup.Target.FullName == branch.FullName &&
                        !popup.Force && !popup.DeleteUpstream,
                        "Promoted Delete opens the existing confirmation for the original full branch target");
                    Check(Git(path, "rev-parse", branch.FullName) == branch.Head,
                        "Opening Delete confirmation does not execute a deletion");
                    page.CancelPopup();
                }

                var sidebar = branch.IsLocal ? (ContextMenu)Invoke(tree, "CreateContextMenuForLocalBranch", repo, branch) : tree.CreateContextMenuForRemoteBranch(repo, branch);
                var sidebarCopy = sidebar.Items.OfType<MenuItem>().Single(item => item.Classes.Contains("copy_branch_name"));
                CheckBranchDeleteScope(FindDeleteItems(sidebar).Single(), branch);
                sidebarCopy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpFor(30);
                Check(Wait(window.Clipboard.TryGetTextAsync()) == branch.Name,
                    "Sidebar uses the same unprefixed copy value as the graph");
                if (branch.IsCurrent)
                    Check(!FindDeleteItems(sidebar).Single().IsEnabled, "Sidebar retains current-branch deletion protection");

                if (branch.Name == name && (branch.IsLocal || branch.Remote == "origin"))
                {
                    var panel = new StackPanel { Margin = new Thickness(16) };
                    Grid.SetRow(panel, 2);
                    hosts.Children.Add(panel);
                    foreach (var item in menu.Items.OfType<MenuItem>().ToList())
                    {
                        menu.Items.Remove(item);
                        panel.Children.Add(item);
                        if (item.Classes.Contains("delete_branch"))
                            break;
                    }
                    foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    {
                        Application.Current.RequestedThemeVariant = theme;
                        PumpFor(100);
                        var delete = panel.Children.OfType<MenuItem>().Single(item => item.Classes.Contains("delete_branch"));
                        var background = delete.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot");
                        Check(ReferenceEquals(background.Background, copy.Background), "Top-level Delete renders the same background as the other branch actions in " + theme);
                        var scope = delete.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("branch_delete_scope_label"));
                        Check(scope.Foreground is Avalonia.Media.ISolidColorBrush { Color: var scopeColor } &&
                            scopeColor == (branch.IsLocal ? Avalonia.Media.Colors.White : Avalonia.Media.Color.Parse("#332500")) &&
                            scope.TextLayout.Width <= scope.Bounds.Width + 1,
                            "Deletion scope stays readable and fits in its badge in " + theme);
                        Check(preview.Bounds.Width <= 360 && preview.TextLayout.Width <= preview.Bounds.Width + 1,
                            "Long copy previews stay inside their chip in " + theme);
                        CaptureUxFrame(window, (branch.IsLocal ? "branch-menu-local-" : "branch-menu-promoted-") + theme);
                    }
                    hosts.Children.Remove(panel);
                    Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                }
            }
        }
        finally
        {
            window.Close();
            field.SetValue(Application.Current, previousLauncher);
            workspace.Repositories.Clear();
            workspace.Repositories.AddRange(previousRepositories);
            workspace.ActiveIdx = previousIndex;
            repo.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static void CheckBranchDeleteScope(MenuItem item, Branch branch)
    {
        var header = (StackPanel)item.Header;
        var scope = header.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("branch_delete_scope"));
        var label = (TextBlock)scope.Child;
        Check(label.Text == (branch.IsLocal ? "LOCAL" : "REMOTE: " + branch.Remote),
            "Delete badge uses the actual local/remote target, not the branch-name prefix: " + branch.FriendlyName);
        Check(header.Children.IndexOf(scope) == 1 && Equals(ToolTip.GetTip(scope), branch.FullName),
            "Delete scope precedes the branch name and exposes the full target in its tooltip");
    }
}
