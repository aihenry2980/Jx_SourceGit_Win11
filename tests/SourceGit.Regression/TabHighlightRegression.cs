using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;
using LauncherTabBar = SourceGit.Views.LauncherTabBar;
using LauncherTabSizeBox = SourceGit.Views.LauncherTabSizeBox;

internal static partial class Program
{
    private static void TestTabHighlightPresentation()
    {
        Preferences.Instance.GetActiveWorkspace().Repositories.Clear();
        var launcher = new SourceGit.ViewModels.Launcher(null);
        PumpFor(50);
        launcher.Pages.Clear();
        var colors = new[] { 6, 6, 3, 7, 1, 0 };
        var names = new[] { "Active Network", "Network", "Build", "Tools", "Main", "No highlight" };
        var pages = colors.Select((color, index) => new LauncherPage(new RepositoryNode
        {
            Id = "tab-color-" + index,
            Name = names[index],
            Bookmark = color,
            IsRepository = true,
        }, null)).ToArray();
        launcher.Pages.AddRange(pages);
        launcher.ActivePage = pages[0];
        var bar = new LauncherTabBar { DataContext = launcher, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = bar, Width = 1000, Height = 110 };
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                launcher.ActivePage = pages[0];
                PumpFor(100);
                var list = bar.FindControl<ListBox>("LauncherTabsList");
                LauncherTabSizeBox Frame(int index) => list.ContainerFromIndex(index).GetVisualDescendants().OfType<LauncherTabSizeBox>().Single();
                var active = Frame(0);
                var inactive = Frame(1);
                var baseColor = ((ISolidColorBrush)Bookmarks.Get(6)).Color;
                var paleColor = Color.FromRgb(191, 239, 255);
                Check(((ISolidColorBrush)inactive.Background).Color == paleColor,
                    "Inactive tab keeps its highlight hue as a pale background in " + theme);
                Check(((ISolidColorBrush)inactive.Foreground).Color == Colors.Black,
                    "Inactive tab text stays readable on the pale highlight in " + theme);
                Check(((ISolidColorBrush)active.Background).Color.R <= baseColor.R &&
                    ((ISolidColorBrush)active.Background).Color.G < baseColor.G &&
                    ((ISolidColorBrush)active.Background).Color.B < baseColor.B,
                    "Current tab keeps a darker version of the same highlight in " + theme);
                foreach (var marker in new[] { "active_tab_arrow", "active_tab_outline", "active_tab_underline" })
                {
                    var visibleMarkers = bar.GetVisualDescendants().OfType<Control>()
                        .Where(control => control.Classes.Contains(marker) && control.IsEffectivelyVisible).ToList();
                    Check(visibleMarkers.Count == 1 && visibleMarkers[0].FindAncestorOfType<LauncherTabSizeBox>() == active,
                        "Only the current tab displays " + marker + " in " + theme);
                }
                Check(!bar.GetVisualDescendants().OfType<SourceGit.Views.Bookmark>().Any(), "Tabs no longer reserve space for bookmark icons");
                var neutral = ((ISolidColorBrush)Frame(5).Background).Color;
                Check(theme == ThemeVariant.Dark ? neutral.R < 128 : neutral.R > 128,
                    "Tabs without a highlight retain a neutral background matching " + theme);
                CaptureUxFrame(window, "tab-highlights-" + theme);
                using (var bitmap = window.CaptureRenderedFrame())
                {
                    var point = inactive.TranslatePoint(new Point(inactive.Bounds.Width / 2, 6), window).Value;
                    var pixel = new byte[4];
                    var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
                    try
                    {
                        bitmap.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
                        var red = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2;
                        var blue = red == 0 ? 2 : 0;
                        Check(pixel[blue] == paleColor.B && pixel[1] == paleColor.G && pixel[red] == paleColor.R,
                            $"Rendered inactive tab background is pale in {theme}: {pixel[0]}, {pixel[1]}, {pixel[2]} ({bitmap.Format})");
                    }
                    finally { handle.Free(); }
                }
                launcher.ActivePage = pages[1];
                PumpFor(50);
                Check(!Frame(0).IsActive && Frame(1).IsActive && ((ISolidColorBrush)Frame(0).Background).Color == paleColor,
                    "Switching tabs restores the old tab to its pale background");
                Check(Frame(0).GetVisualDescendants().OfType<Control>().Where(control => control.Classes.Contains("active_tab_underline")).All(control => !control.IsEffectivelyVisible) &&
                    Frame(1).GetVisualDescendants().OfType<Control>().Any(control => control.Classes.Contains("active_tab_underline") && control.IsEffectivelyVisible),
                    "Switching tabs moves the high-contrast selection marker to the new tab");
                pages[1].Node.Bookmark = 4;
                PumpFor(50);
                Check(((ISolidColorBrush)Frame(1).Background).Color.G > ((ISolidColorBrush)Frame(1).Background).Color.B,
                    "Changing the active tab highlight immediately updates its full background");
                pages[1].Node.Bookmark = 6;
                pages[0].Node.Bookmark = 1;
                PumpFor(50);
                Check(((ISolidColorBrush)Frame(0).Background).Color == Color.FromRgb(255, 191, 191),
                    "Changing an inactive highlight immediately updates its pale background");
                pages[0].Node.Bookmark = 6;
            }
            launcher.ActivePage = pages[4];
            PumpFor(50);
            CaptureUxFrame(window, "tab-current-red-Dark");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            PumpFor(50);
            CaptureUxFrame(window, "tab-current-red-Light");
            window.Width = 420;
            launcher.ActivePage = pages[^1];
            PumpFor(100);
            Check(bar.IsScrollButtonVisible, "Colored tabs retain overflow scrolling in narrow windows");
            CaptureUxFrame(window, "tab-highlights-420");
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
}
