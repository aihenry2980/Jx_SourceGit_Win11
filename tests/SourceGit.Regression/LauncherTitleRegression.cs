using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestLauncherTitleIntegration()
    {
        var workspace = Preferences.Instance.GetActiveWorkspace();
        workspace.Repositories.Clear();
        workspace.Name = "Workspace One";
        var launcher = new Launcher(null);
        PumpFor(100);
        launcher.Pages.Clear();
        var first = new LauncherPage(new RepositoryNode { Id = "title-first", Name = "Repository One", Bookmark = 1, IsRepository = true }, null);
        var second = new LauncherPage(new RepositoryNode { Id = "title-second", Name = "Repository Two", Bookmark = 2, IsRepository = true }, null);
        launcher.Pages.AddRange([first, second]);
        launcher.ActivePage = first;
        var window = new SourceGit.Views.Launcher { DataContext = launcher };
        window.Show();
        try
        {
            PumpFor(100);
            Check(window.Title == "Repository One - Workspace One", "Launcher title binds the active repository and workspace names");
            first.Node.Name = "Renamed Repository";
            PumpFor(30);
            Check(window.Title == "Renamed Repository - Workspace One", "Repository rename immediately updates the real window title");
            workspace.Name = "Renamed Workspace";
            PumpFor(30);
            Check(window.Title == "Renamed Repository - Renamed Workspace", "Workspace rename immediately updates the real window title");
            first.Node = new RepositoryNode { Id = "title-replacement", Name = "Replacement Repository", Bookmark = 1, IsRepository = true };
            PumpFor(30);
            Check(window.Title == "Replacement Repository - Renamed Workspace", "Replacing the active tab node updates the window title");
            var titleBar = window.FindControl<Border>("TitleBarBG");
            var before = ((ISolidColorBrush)titleBar.Background).Color;
            first.Node.Bookmark = 3;
            PumpFor(30);
            Check(((ISolidColorBrush)titleBar.Background).Color == before,
                "Changing a tab highlight does not tint the title bar");
            launcher.ActivePage = second;
            PumpFor(30);
            Check(window.Title == "Repository Two - Renamed Workspace", "Switching tabs updates the window title");
            first.Node.Name = "Inactive Rename";
            PumpFor(30);
            Check(window.Title == "Repository Two - Renamed Workspace", "Renaming an inactive tab does not change the window title");
            Check(DragDrop.GetAllowDrop((Grid)window.Content), "Launcher root retains drag/drop support after conflict resolution");
            CaptureUxFrame(window, "launcher-title-merge");
            var path = NewRepository("neutral launcher chrome");
            Git(path, "commit", "--allow-empty", "-m", "Initial commit");
            var repo = Open(path);
            try
            {
                second.Data = repo;
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    foreach (var page in new[] { first, second })
                    {
                        launcher.ActivePage = page;
                        PumpFor(100);
                        var toolbar = window.GetVisualDescendants().OfType<Border>()
                            .Single(border => border.Name == "ToolBarBorder" && border.IsEffectivelyVisible);
                        var titleColor = (Color)window.FindResource(theme, "Color.TitleBar");
                        var toolbarColor = (Color)window.FindResource(theme, "Color.ToolBar");
                        Check(((ISolidColorBrush)titleBar.Background).Color == titleColor &&
                            ((ISolidColorBrush)toolbar.Background).Color == toolbarColor,
                            "Title bar and toolbar use neutral theme colors after switching tabs in " + theme);
                        page.Node.Bookmark = page.Node.Bookmark == 1 ? 6 : 1;
                        repo.NotifyAccentColorChanged();
                        PumpFor(50);
                        Check(((ISolidColorBrush)titleBar.Background).Color == titleColor &&
                            ((ISolidColorBrush)toolbar.Background).Color == toolbarColor,
                            "Changing repository highlight leaves both chrome backgrounds neutral in " + theme);
                    }
                    CaptureUxFrame(window, "launcher-neutral-chrome-" + theme);
                }
            }
            finally
            {
                second.Data = null;
                repo.Close();
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
}
