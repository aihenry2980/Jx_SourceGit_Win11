using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
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
            var list = bar.FindControl<ListBox>("LauncherTabsList");
            LauncherTabSizeBox Frame(int index) => list.ContainerFromIndex(index).GetVisualDescendants().OfType<LauncherTabSizeBox>().Single();
            Border Indicator(int index) => Frame(index).GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("active_tab_indicator"));
            Avalonia.Controls.Shapes.Path Star(int index) => Frame(index).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Classes.Contains("active_tab_star"));
            Ellipse CloseRing(int index) => Frame(index).GetVisualDescendants().OfType<Ellipse>().Single(e => e.Classes.Contains("active_tab_close_ring"));
            IEnumerable<TextBlock> Titles(int index) => Frame(index).GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("tab_title"));
            Color Pixel(Bitmap bitmap, Control control, Point point)
            {
                var position = control.TranslatePoint(point, window).Value;
                var pixel = new byte[4];
                var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
                try
                {
                    bitmap.CopyPixels(new PixelRect((int)position.X, (int)position.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
                    var red = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2;
                    return Color.FromRgb(pixel[red], pixel[1], pixel[2 - red]);
                }
                finally { handle.Free(); }
            }

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                launcher.ActivePage = pages[0];
                PumpFor(100);
                var active = Frame(0);
                var inactive = Frame(1);
                var paleColor = Color.FromRgb(191, 239, 255);
                var selectedColor = Color.FromRgb(0, 85, 114);
                Check(((ISolidColorBrush)inactive.Background).Color == paleColor,
                    "Inactive tab keeps its highlight hue as a pale background in " + theme);
                Check(((ISolidColorBrush)inactive.Foreground).Color == Colors.Black,
                    "Inactive tab text stays readable on its pale highlight in " + theme);
                Check(((ISolidColorBrush)active.Background).Color == selectedColor &&
                    ((ISolidColorBrush)active.Foreground).Color == Colors.White &&
                    Titles(0).All(t => ((ISolidColorBrush)t.Foreground).Color == Colors.White),
                    "Selected tab uses its dark highlight background and white text in " + theme);
                Check(active.BorderThickness == new Thickness(1) && active.Effect == null &&
                    !bar.GetVisualDescendants().OfType<Control>().Any(c => c.Classes.Contains("active_tab_outline")),
                    "Tabs have only a normal thin border, with no heavy selection frame");
                Check(Titles(0).All(t => t.Effect == null && t.FontWeight == FontWeight.SemiBold) &&
                    Titles(1).All(t => t.Effect == null && t.FontWeight == FontWeight.Normal),
                    "Selected titles use crisp SemiBold text; no tab title has a halo");
                var indicator = Indicator(0);
                var accent = ((ISolidColorBrush)Bookmarks.Get(6)).Color;
                Check(indicator.IsEffectivelyVisible && !indicator.IsHitTestVisible && indicator.Bounds.Height == 3 &&
                    indicator.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Top &&
                    ((ISolidColorBrush)indicator.Background).Color == accent && !Indicator(1).IsEffectivelyVisible,
                    "Only the selected tab has a three-pixel repository-color top indicator in " + theme);
                Check(!bar.GetVisualDescendants().OfType<SourceGit.Views.Bookmark>().Any(),
                    "Tabs do not reserve space for bookmark icons");
                Check(Star(0).IsEffectivelyVisible && !Star(0).IsHitTestVisible && !Star(1).IsEffectivelyVisible &&
                    ((ISolidColorBrush)Star(0).Fill).Color == Color.FromRgb(255, 193, 7),
                    "Only the active repository tab has a gold selection star in " + theme);
                var expectedRing = Color.FromRgb(76, 210, 255);
                Check(CloseRing(0).IsEffectivelyVisible && !CloseRing(0).IsHitTestVisible && !CloseRing(1).IsEffectivelyVisible &&
                    CloseRing(0).StrokeThickness == 1.5 && ((ISolidColorBrush)CloseRing(0).Stroke).Color == expectedRing,
                    "Only the selected close button has a thin, theme-readable highlight circle in " + theme);
                var close = Frame(0).GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab_close"));
                Check(close.Bounds.Width >= 22 && close.Bounds.Height >= 22,
                    "The decorative circle does not shrink the close button hit target");
                pages[0].ChangeDirtyState(DirtyState.HasLocalChanges, false);
                PumpFor(50);
                Check(Star(0).IsEffectivelyVisible && Frame(0).GetVisualDescendants().OfType<Ellipse>()
                    .Any(e => e.Classes.Contains("tab_dirty_state") && e.IsEffectivelyVisible),
                    "The selected star does not replace the existing dirty-state dot");
                CaptureUxFrame(window, "tab-selected-dirty-" + theme);
                pages[0].ChangeDirtyState(DirtyState.HasLocalChanges, true);
                PumpFor(50);
                CaptureUxFrame(window, "tab-highlights-" + theme);
                using (var bitmap = window.CaptureRenderedFrame())
                {
                    Check(Pixel(bitmap, inactive, new Point(10, 15)) == paleColor,
                        "Rendered inactive tab retains its pastel background in " + theme);
                    Check(Pixel(bitmap, active, new Point(3, 15)) == selectedColor,
                        "Rendered active tab has a dark same-hue background in " + theme);
                    Check(Pixel(bitmap, Star(0), new Point(8, 8)) == Color.FromRgb(255, 193, 7),
                        "The selected star actually renders gold in " + theme);
                    Check(Pixel(bitmap, CloseRing(0), new Point(10, 0)) != selectedColor,
                        "The selected close circle is visible in rendered pixels in " + theme);
                    Check(Pixel(bitmap, indicator, new Point(indicator.Bounds.Width / 2, 1)) == accent,
                        "Repository-color top indicator renders as a sharp solid line in " + theme);
                    Check(Pixel(bitmap, active, new Point(active.Bounds.Width / 2, 5)) == selectedColor,
                        "The top indicator does not spread into a thick frame or shadow in " + theme);
                }

                launcher.ActivePage = pages[1];
                PumpFor(50);
                Check(!Frame(0).IsActive && Frame(1).IsActive &&
                    ((ISolidColorBrush)Frame(0).Background).Color == paleColor &&
                    ((ISolidColorBrush)Frame(1).Background).Color == selectedColor,
                    "Switching tabs moves the dark selected surface and restores the old pastel background");
                Check(!Indicator(0).IsEffectivelyVisible && Indicator(1).IsEffectivelyVisible &&
                    Titles(0).All(t => t.FontWeight == FontWeight.Normal) && Titles(1).All(t => t.FontWeight == FontWeight.SemiBold),
                    "Switching tabs moves the top indicator and SemiBold title without stale selection styling");
                Check(!Star(0).IsEffectivelyVisible && Star(1).IsEffectivelyVisible &&
                    !CloseRing(0).IsEffectivelyVisible && CloseRing(1).IsEffectivelyVisible,
                    "Switching tabs moves the gold star and close circle to the new selection");
                pages[1].Node.Bookmark = 4;
                PumpFor(50);
                Check(((ISolidColorBrush)Frame(1).Background).Color == Color.FromRgb(15, 62, 15) &&
                    ((ISolidColorBrush)Indicator(1).Background).Color == Colors.ForestGreen,
                    "Changing the active highlight updates its dark background and top line");
                Check(((ISolidColorBrush)CloseRing(1).Stroke).Color ==
                    Color.FromRgb(100, 173, 100),
                    "Changing the repository highlight also updates the thin close circle");
                pages[1].Node.Bookmark = 6;
                pages[0].Node.Bookmark = 1;
                PumpFor(50);
                Check(((ISolidColorBrush)Frame(0).Background).Color == Color.FromRgb(255, 191, 191),
                    "Changing an inactive highlight immediately updates its pale background");
                pages[0].Node.Bookmark = 6;

                foreach (var index in new[] { 2, 3, 4, 5 })
                {
                    launcher.ActivePage = pages[index];
                    PumpFor(50);
                    var expected = Bookmarks.Get(colors[index]) is ISolidColorBrush hue
                        ? Color.FromRgb((byte)(hue.Color.R * 0.45), (byte)(hue.Color.G * 0.45), (byte)(hue.Color.B * 0.45))
                        : Color.FromRgb(48, 48, 48);
                    Check(((ISolidColorBrush)Frame(index).Background).Color == expected &&
                        ((ISolidColorBrush)Frame(index).Foreground).Color == Colors.White &&
                        Indicator(index).IsEffectivelyVisible && Indicator(index).Background != null &&
                        bar.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("active_tab_indicator") && b.IsEffectivelyVisible) == 1,
                        $"Exactly one dark selected tab uses white text for {names[index]} in {theme}");
                    CaptureUxFrame(window, $"tab-current-{index}-{theme}");
                }
                launcher.ActivePage = pages[0];
                foreach (var highlight in Enumerable.Range(0, Bookmarks.Brushes.Length).Append(-1).Append(Bookmarks.Brushes.Length))
                {
                    pages[0].Node.Bookmark = highlight;
                    PumpFor(10);
                    var background = ((ISolidColorBrush)Frame(0).Background).Color;
                    static double Linearize(byte channel)
                    {
                        var value = channel / 255.0;
                        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
                    }
                    var luminance = 0.2126 * Linearize(background.R) + 0.7152 * Linearize(background.G) + 0.0722 * Linearize(background.B);
                    Check(1.05 / (luminance + 0.05) >= 4.5 && ((ISolidColorBrush)Frame(0).Foreground).Color == Colors.White,
                        $"White selected text remains readable for bookmark {highlight} in {theme}");
                }
                pages[0].Node.Bookmark = 6;
            }
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            launcher.ActivePage = pages[0];
            window.Width = 420;
            PumpFor(100);
            launcher.ActivePage = pages[^1];
            PumpFor(100);
            Check(bar.IsScrollButtonVisible, "Tabs retain overflow scrolling in narrow windows");
            var scroller = bar.FindControl<ScrollViewer>("LauncherTabsScroller");
            var selectedPosition = Frame(5).TranslatePoint(default, bar).Value;
            Check(selectedPosition.X >= scroller.Bounds.X && selectedPosition.X + Frame(5).Bounds.Width <= scroller.Bounds.Right,
                "Selecting an overflowed tab scrolls its dark surface and top indicator into view");
            Check(Indicator(5).IsEffectivelyVisible && Titles(5).All(t => t.Effect == null),
                "An unhighlighted selected tab retains a visible top indicator and sharp text when scrolled");
            CaptureUxFrame(window, "tab-highlights-420");
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
}
