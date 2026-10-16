using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
        var first = new LauncherPage(new RepositoryNode { Id = "title-first", Name = "Repository One", Bookmark = 1 }, null);
        var second = new LauncherPage(new RepositoryNode { Id = "title-second", Name = "Repository Two", Bookmark = 2 }, null);
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
            first.Node = new RepositoryNode { Id = "title-replacement", Name = "Replacement Repository", Bookmark = 1 };
            PumpFor(30);
            Check(window.Title == "Replacement Repository - Renamed Workspace", "Replacing the active tab node updates the window title");
            var before = ((ISolidColorBrush)launcher.ActivePageTitleBarBackground).Color;
            first.Node.Bookmark = 3;
            PumpFor(30);
            var titleBar = window.FindControl<Border>("TitleBarBG");
            Check(((ISolidColorBrush)titleBar.Background).Color == ((ISolidColorBrush)launcher.ActivePageTitleBarBackground).Color &&
                ((ISolidColorBrush)titleBar.Background).Color != before, "Upstream title binding preserves bookmark-based title bar colors");
            launcher.ActivePage = second;
            PumpFor(30);
            Check(window.Title == "Repository Two - Renamed Workspace", "Switching tabs updates the window title");
            first.Node.Name = "Inactive Rename";
            PumpFor(30);
            Check(window.Title == "Repository Two - Renamed Workspace", "Renaming an inactive tab does not change the window title");
            Check(DragDrop.GetAllowDrop((Grid)window.Content), "Launcher root retains drag/drop support after conflict resolution");
            CaptureUxFrame(window, "launcher-title-merge");
        }
        finally
        {
            window.Close();
        }
    }
}
