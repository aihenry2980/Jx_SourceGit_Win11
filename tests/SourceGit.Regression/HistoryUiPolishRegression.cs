using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Prefs = SourceGit.ViewModels.Preferences;

internal static partial class Program
{
    private static void TestHistoryUiPolish()
    {
        TestSubmoduleExtensionTags();
        TestCommitTimeRecency();
        TestHistorySelectionAndHover();
        TestCheckoutWithoutSubmodules();
    }

    private static void TestCommitTimeRecency()
    {
        static ulong Timestamp(DateTime value) => (ulong)new DateTimeOffset(value).ToUnixTimeSeconds();
        var today = DateTime.Today;
        var update = typeof(CommitTimeTextBlock).GetMethod("UpdateRecency", BindingFlags.NonPublic | BindingFlags.Instance);
        var control = new CommitTimeTextBlock();
        foreach (var offset in new[] { -2, -1, 0, 1 })
        foreach (var hour in new[] { 0, 23 })
        {
            var timestamp = Timestamp(today.AddDays(offset).AddHours(hour).AddMinutes(1));
            control.DataContext = new Commit { CommitterTime = timestamp };
            update.Invoke(control, [today.AddHours(12)]);
            Check(control.Classes.Contains("time_today") == (offset == 0) &&
                control.Classes.Contains("time_yesterday") == (offset == -1),
                $"Time colors follow local calendar days, not elapsed hours: day={offset}, hour={hour}");
        }
        control.DataContext = new Commit
        {
            CommitterTime = Timestamp(today.AddHours(12)),
            AuthorTime = Timestamp(today.AddDays(-5).AddHours(12)),
        };
        control.ShowAsDateTime = false;
        var committerText = control.Text;
        control.UseAuthorTime = true;
        Check(!control.Classes.Contains("time_today") && !control.Classes.Contains("time_yesterday") && control.Text != committerText,
            "Switching to author time refreshes both color and relative text immediately");
        control.UseAuthorTime = false;
        update.Invoke(control, [today.AddDays(1)]);
        Check(control.Classes.Contains("time_yesterday") && !control.Classes.Contains("time_today"), "Midnight changes today's time color to yesterday's");
        update.Invoke(control, [today.AddDays(2)]);
        Check(!control.Classes.Contains("time_yesterday") && !control.Classes.Contains("time_today"), "The following midnight clears yesterday's blue");
        control.DataContext = new Commit();
        control.Timestamp = Timestamp(today.AddDays(-1).AddHours(12));
        Check(control.Classes.Contains("time_yesterday"), "Timestamp fallback uses the same calendar color rules");
        control.Timestamp = 0;
        Check(!control.Classes.Contains("time_today") && !control.Classes.Contains("time_yesterday"), "Missing timestamps stay neutral");
        control.Timestamp = ulong.MaxValue;
        Check(control.Text == string.Empty && !control.Classes.Contains("time_today") && !control.Classes.Contains("time_yesterday"), "Invalid timestamps do not crash the time control");
        control.DataContext = null;
        Check(control.Text == string.Empty, "Recycled empty rows clear date text and recency");
        control.ShowAsDateTime = true;
        var window = new Window { Content = control, Width = 300, Height = 100 };
        window.Show();
        PumpFor(30);
        var timer = (Avalonia.Threading.DispatcherTimer)typeof(CommitTimeTextBlock).GetField("_refreshTimer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(control);
        Check(timer.IsEnabled, "Absolute date mode also checks for midnight color rollover");
        window.Close();
        PumpFor(30);
        Check(!timer.IsEnabled && timer.Tag == null, "Unloaded time controls stop their timer and release its target");
    }

    private static void TestSubmoduleExtensionTags()
    {
        var prefs = Prefs.Instance;
        var previousRules = prefs.FileTypeColors;
        var previousTheme = Application.Current.RequestedThemeVariant;
        var diff = new DiffView();
        var content = ((Grid)((Border)diff.Content).Child).Children.OfType<ContentControl>().Single(c => Grid.GetRow(c) == 1);
        var changes = new List<Change>
        {
            new() { Path = "main.c", AddedLines = "2", DeletedLines = "0" },
            new() { Path = "include/detail.hpp", AddedLines = "12", DeletedLines = "3" },
            new() { Path = "src/Program.cs", AddedLines = "0", DeletedLines = "1" },
            new() { Path = "nested/submodule", IsSubmodulePointerChange = true, AddedLines = "1", DeletedLines = "1" },
        };
        content.Content = new SubmoduleDiff { Changes = changes };
        var window = new Window { Content = diff, Width = 900, Height = 550 };
        try
        {
            prefs.ResetFileTypeColors();
            prefs.FileTypeColors[9].Extension = ".cs";
            prefs.FileTypeColors[9].Color = 0xFFF5D966;
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var width in new[] { 900, 480 })
            {
                Application.Current.RequestedThemeVariant = theme;
                window.Width = width;
                PumpFor(80);
                var tags = diff.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("file_type_tag") && b.IsEffectivelyVisible).ToArray();
                Check(tags.Length == 3, "Submodule file details use extension tags for files, not nested gitlinks");
                var cTag = tags.Single(b => ((Change)b.DataContext).Path == "main.c");
                var hTag = tags.Single(b => ((Change)b.DataContext).Path.EndsWith(".hpp"));
                Check(cTag.Bounds.Width < hTag.Bounds.Width && cTag.Bounds.Width < 24,
                    "Extension tag width follows short and long text instead of a fixed forty-pixel box");
                var csTag = tags.Single(b => ((Change)b.DataContext).Path.EndsWith(".cs"));
                Check(((ISolidColorBrush)csTag.Background).Color == Color.FromUInt32(0xFFF5D966) &&
                    ((ISolidColorBrush)csTag.GetVisualDescendants().OfType<TextBlock>().Single().Foreground).Color == Colors.Black,
                    "Submodule details honor custom extension colors and automatic foreground contrast");
                var button = csTag.FindAncestorOfType<Button>();
                var count = button.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("submodule_line_count"));
                var pathText = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "src/Program.cs");
                Check(pathText.TranslatePoint(default, button).Value.X + pathText.Bounds.Width <= count.TranslatePoint(default, button).Value.X + 1,
                    "Adding an extension tag does not overlap submodule line counts at narrow widths");
                CaptureUxFrame(window, $"submodule-file-type-tags-{width}-{theme}");
            }
            prefs.FileTypeColors[9].Color = 0xFF153649;
            PumpFor(50);
            var changed = diff.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("file_type_tag") && b.DataContext is Change c && c.Path.EndsWith(".cs"));
            Check(((ISolidColorBrush)changed.Background).Color == Color.FromUInt32(0xFF153649), "Submodule extension tags update in place when settings change");
        }
        finally { window.Close(); prefs.FileTypeColors = previousRules; Application.Current.RequestedThemeVariant = previousTheme; }
    }

    private static void TestHistorySelectionAndHover()
    {
        var path = NewRepository("history selection hover polish");
        for (var i = 0; i < 8; i++)
            Git(path, "commit", "--allow-empty", "-m", $"Commit {i}: display readable text without changing layout");
        var repo = Open(path);
        WaitUntil(() => repo.Histories.Commits.Count == 8 && !repo.Histories.IsLoading, "Polish history did not load");
        var head = repo.Histories.Commits[0];
        var today = DateTime.Today;
        for (var i = 0; i < repo.Histories.Commits.Count; i++)
        {
            var commit = repo.Histories.Commits[i];
            commit.CommitterTime = (ulong)new DateTimeOffset(today.AddDays(-(i % 3)).AddHours(12)).ToUnixTimeSeconds();
            commit.AuthorTime = (ulong)new DateTimeOffset(today.AddDays(-5).AddHours(12)).ToUnixTimeSeconds();
        }
        head.IsMerged = false;
        head.HasSubmodulePointerChange = true;
        head.SubmoduleUpdateBadges = [new("auth", 0xFF0F766E), new("core", 0xFF6D28D9), new("network", 0xFF1D4ED8)];
        var prefs = Prefs.Instance;
        var previousAccent = prefs.MainAccentColor;
        var previousZoom = prefs.HistoriesZoom;
        var previousTheme = Application.Current.RequestedThemeVariant;
        var previousAuthorTime = prefs.ShowAuthorTimeInGraph;
        var previousRelativeTime = prefs.DisplayTimeAsPeriodInHistories;
        var view = new Histories { DataContext = repo.Histories };
        var window = new Window { Content = view, Width = 1500, Height = 900 };
        static Color ColorOf(IBrush brush) => ((ISolidColorBrush)brush).Color;
        DataGridRow Row(int index) => view.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, repo.Histories.Commits[index]));
        static CommitSubjectPresenter Message(DataGridRow row) => row.GetVisualDescendants().OfType<CommitSubjectPresenter>().Single(s => s.Subject.Length > 5);
        static Color BackgroundPixel(DataGridRow row)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(row.Bounds.Width), (int)Math.Ceiling(row.Bounds.Height)));
            bitmap.Render(row);
            var bytes = new byte[4];
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect((int)row.Bounds.Width - 4, 3, 1, 1), pin.AddrOfPinnedObject(), 4, 4);
                var red = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2;
                return Color.FromArgb(bytes[3], bytes[red], bytes[1], bytes[2 - red]);
            }
            finally { pin.Free(); }
        }
        try
        {
            window.Show();
            prefs.ShowAuthorTimeInGraph = false;
            repo.Histories.SelectedCommits = [repo.Histories.Commits[1]];
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var zoom in new[] { 1.0, 2.0 })
            {
                Application.Current.RequestedThemeVariant = theme;
                prefs.HistoriesZoom = zoom;
                foreach (var accent in new uint[] { 0xFF102030, 0xFFF5D966, 0xFFAD2438, 0xFF0078D4, 0xFF000000 })
                {
                    prefs.MainAccentColor = accent;
                    window.MouseMove(new Point(4, 4));
                    PumpFor(80);
                    var headRow = Row(0);
                    var selected = Row(1);
                    CaptureUxFrame(window, $"history-polish-accent-{accent:X8}-{theme}-{zoom}");
                    var accentColor = Color.FromUInt32(accent);
                    static byte Tint(byte channel) => (byte)Math.Round(255 * 0.85 + channel * 0.15);
                    var expectedBackground = Color.FromRgb(Tint(accentColor.R), Tint(accentColor.G), Tint(accentColor.B));
                    var headBackground = ColorOf(headRow.Background);
                    Check(headBackground == expectedBackground && BackgroundPixel(headRow) == expectedBackground,
                        $"HEAD paints a pale live accent tint in {theme}, zoom={zoom}");
                    var badges = headRow.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.DataContext is SubmoduleUpdateBadge && t.IsEffectivelyVisible).ToArray();
                    Check(badges.Length == 3 && badges.All(t => ColorOf(t.Foreground) == Color.FromUInt32(((SubmoduleUpdateBadge)t.DataContext).AccentColor) &&
                        WorkspaceContrast(ColorOf(t.Foreground), headBackground) >= 3.0) &&
                        SubmoduleUpdateBadge.ColorPalette.All(c => WorkspaceContrast(Color.FromUInt32(c), headBackground) >= 3.0),
                        "SPP labels retain their identity colors and the full badge palette remains distinguishable on the pale HEAD background");
                    var selection = ColorOf(selected.Background);
                    Check(selection.R == selection.G || Math.Abs(selection.R - selection.G) < 6, "Selected history rows use neutral gray independent of the accent hue");
                    Check(BackgroundPixel(selected) == selection, "Selected gray is actually painted, not just assigned to an unused brush");
                    var shaFrame = headRow.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("commit_sha_cell"));
                    var sha = shaFrame.GetVisualDescendants().OfType<CommitSubjectPresenter>().Single();
                    Check(sha.Opacity == 1 && ColorOf(shaFrame.Background) == accentColor && WorkspaceContrast(ColorOf(sha.Foreground), ColorOf(shaFrame.Background)) >= 4.5,
                        "Current SHA remains fully opaque with contrasting text even when the HEAD is not marked merged");
                    Check(WorkspaceContrast(ColorOf(Message(headRow).Foreground), headBackground) >= 4.5,
                        "HEAD message text contrasts with the pale tint, not the original accent");
                }

                var hoverRow = Row(2);
                var message = Message(hoverRow);
                CaptureUxFrame(window, $"history-polish-before-{theme}-{zoom}");
                var field = typeof(CommitSubjectPresenter).GetField("_inlines", BindingFlags.NonPublic | BindingFlags.Instance);
                object[] Inlines() => ((IEnumerable)field.GetValue(message)).Cast<object>().ToArray();
                var inlines = Inlines();
                var idleColor = ColorOf(message.Foreground);
                var expectedIdle = theme == ThemeVariant.Light ? Color.Parse("#808080") : Color.Parse("#B4B4B4");
                Check(idleColor == expectedIdle && inlines.Any(i => ColorOf((IBrush)i.GetType().GetProperty("RenderBrush").GetValue(i)) == idleColor),
                    "Ordinary commit messages paint neutral gray before hover, independently of general text colors");
                var texts = inlines.Select(i => i.GetType().GetProperty("Text").GetValue(i)).ToArray();
                var beforeSize = message.Bounds.Size;
                var weight = message.FontWeight;
                var hoverPoint = message.TranslatePoint(new Point(Math.Min(80, message.Bounds.Width / 2), message.Bounds.Height / 2), window).Value;
                window.MouseMove(hoverPoint);
                PumpFor(80);
                Check(message.IsRowHovered && ColorOf(hoverRow.Background).A == 0 && BackgroundPixel(hoverRow).A == 0,
                    "Hovering an ordinary row changes text emphasis without coloring its background");
                Check(message.FontWeight == weight && message.Bounds.Size == beforeSize && inlines.SequenceEqual(Inlines()) &&
                    texts.SequenceEqual(Inlines().Select(i => i.GetType().GetProperty("Text").GetValue(i))),
                    "Hover preserves font weight, measured bounds, inline objects and formatted-text objects");
                var hoverColor = ColorOf((IBrush)view.FindResource("Brush.History.HoverForeground"));
                Check(Inlines().All(i => ColorOf((IBrush)i.GetType().GetProperty("RenderBrush").GetValue(i)) == hoverColor),
                    "Hover paints darker text in Light and brighter text in Dark");
                CaptureUxFrame(window, $"history-polish-hover-{theme}-{zoom}");
                var selectedBackground = BackgroundPixel(Row(1));
                window.MouseMove(Message(Row(1)).TranslatePoint(new Point(40, Message(Row(1)).Bounds.Height / 2), window).Value);
                PumpFor(40);
                Check(BackgroundPixel(Row(1)) == selectedBackground, "Hover leaves a selected row's gray background unchanged");
                window.MouseMove(new Point(4, 4));
                PumpFor(40);
                Check(!message.IsRowHovered && Inlines().All(i => ReferenceEquals(i.GetType().GetProperty("RenderBrush").GetValue(i), i.GetType().GetProperty("Brush").GetValue(i))),
                    "Leaving a row restores its original text colors without replacing formatted text");
                var headMessage = Message(Row(0));
                Check(ColorOf(headMessage.Foreground) == Color.Parse("#5C5C5C"), "HEAD messages also start gray while retaining contrast on the pale accent tint");
                var headBeforeHover = BackgroundPixel(Row(0));
                window.MouseMove(headMessage.TranslatePoint(new Point(40, headMessage.Bounds.Height / 2), window).Value);
                PumpFor(40);
                var headInlines = ((IEnumerable)field.GetValue(headMessage)).Cast<object>().ToArray();
                Check(headMessage.IsRowHovered && headInlines.All(i => ColorOf((IBrush)i.GetType().GetProperty("RenderBrush").GetValue(i)) == Colors.Black) &&
                    BackgroundPixel(Row(0)) == headBeforeHover, "HEAD hover paints black message text in both themes without changing its pale background");
                foreach (var relative in new[] { false, true })
                {
                    prefs.DisplayTimeAsPeriodInHistories = relative;
                    PumpFor(30);
                    for (var i = 0; i < 4; i++)
                    {
                        var time = Row(i).GetVisualDescendants().OfType<CommitTimeTextBlock>().Single();
                        var key = i % 3 == 0 ? "Brush.History.TimeToday" : i % 3 == 1 ? "Brush.History.TimeYesterday" : "Brush.History.TimeOlder";
                        var expected = i == 0 ? Color.Parse("#10234D") : ColorOf((IBrush)view.FindResource(key));
                        Check(ColorOf(time.Foreground) == expected,
                            $"Graph date column paints today blue, yesterday lighter blue and older gray: row={i}, theme={theme}, relative={relative}, actual={ColorOf(time.Foreground)}, expected={expected}");
                        Check(time.FontWeight == (i % 3 == 0 ? FontWeight.Bold : FontWeight.Normal),
                            "Today is additionally bold, while yesterday and older dates remain regular weight");
                    }
                    var todayColor = ColorOf((IBrush)view.FindResource("Brush.History.TimeToday"));
                    var yesterdayColor = ColorOf((IBrush)view.FindResource("Brush.History.TimeYesterday"));
                    Check(WorkspaceContrast(todayColor, yesterdayColor) >= 2.0,
                        "Today and yesterday retain clearly separated luminance in both themes");
                    prefs.ShowAuthorTimeInGraph = true;
                    PumpFor(30);
                    var authorTime = Row(1).GetVisualDescendants().OfType<CommitTimeTextBlock>().Single();
                    Check(ColorOf(authorTime.Foreground) == ColorOf((IBrush)view.FindResource("Brush.History.TimeOlder")), "Author-time selection also updates actual graph date colors");
                    prefs.ShowAuthorTimeInGraph = false;
                    PumpFor(30);
                    Check(Row(3).GetVisualDescendants().OfType<CommitTimeTextBlock>().Single().FontWeight == FontWeight.Bold,
                        "Switching back from old author dates restores today's bold emphasis");
                }
                window.MouseMove(new Point(4, 4));
                PumpFor(30);
                CaptureUxFrame(window, $"history-time-recency-{theme}-{zoom}");
            }
        }
        finally
        {
            window.Close();
            repo.Close();
            prefs.MainAccentColor = previousAccent;
            prefs.HistoriesZoom = previousZoom;
            prefs.ShowAuthorTimeInGraph = previousAuthorTime;
            prefs.DisplayTimeAsPeriodInHistories = previousRelativeTime;
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }

    private static void TestCheckoutWithoutSubmodules()
    {
        var path = NewRepository("checkout no submodule option");
        Git(path, "commit", "--allow-empty", "-m", "Initial");
        Git(path, "branch", "next");
        var repo = Open(path);
        var target = repo.Branches.Single(b => b.Name == "next");
        var page = new SourceGit.ViewModels.LauncherPage(new SourceGit.ViewModels.RepositoryNode { Id = path }, repo);
        var view = new SourceGit.Views.LauncherPage { DataContext = page };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        try
        {
            page.Popup = new SourceGit.ViewModels.Checkout(repo, target);
            window.Show();
            PumpFor(80);
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CheckoutWithSubmodulesButton");
            Check(!page.Popup.ShowCheckoutSubmoduleAction && !button.IsEffectivelyVisible &&
                view.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CheckoutOnlyButton" && b.IsEffectivelyVisible),
                "Repositories without submodules expose only plain checkout and cancel");
            Wait(page.ProcessPopupAsync(true));
            Check(page.Popup == null && repo.Logs[0].IsSuccessful && !repo.Logs[0].Content.Contains("submodule update"),
                "No submodule update command runs when a repository has no submodules");
            Git(path, "config", "-f", ".gitmodules", "submodule.uninitialized.path", "child");
            Git(path, "config", "-f", ".gitmodules", "submodule.uninitialized.url", path);
            page.Popup = new SourceGit.ViewModels.Checkout(repo, target);
            PumpFor(60);
            Check(page.Popup.ShowCheckoutSubmoduleAction && view.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CheckoutWithSubmodulesButton" && b.IsEffectivelyVisible),
                "Uninitialized submodules still expose the init/update choice before the sidebar scan finishes");
            CaptureUxFrame(window, "checkout-submodule-option-present");
        }
        finally { window.Close(); repo.Close(); }
    }
}
