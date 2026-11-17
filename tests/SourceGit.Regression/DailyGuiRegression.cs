using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Prefs = SourceGit.ViewModels.Preferences;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void TestDailyGui()
    {
        TestUserAvatarSettings();
        TestToolbarAndHistoryAffordances();
    }

    private static void TestUserAvatarSettings()
    {
        var prefs = Prefs.Instance;
        var previousAvatars = prefs.UserAvatars;
        var previousMode = prefs.UseGitHubStyleAvatar;
        var imageField = typeof(Avatar).GetField("_img", BindingFlags.Instance | BindingFlags.NonPublic);
        var context = (JsonSerializerContext)typeof(SourceGit.App).Assembly.GetType("SourceGit.JsonCodeGen").GetProperty("Default").GetValue(null);
        var type = context.GetTypeInfo(typeof(Prefs));
        var greenFile = Path.Combine(_root, "personal-avatar-green.png");
        var redFile = Path.Combine(_root, "personal-avatar-red.png");
        void MakeImage(string file, IBrush brush)
        {
            using var image = new RenderTargetBitmap(new PixelSize(96, 64));
            using (var draw = image.CreateDrawingContext())
                draw.FillRectangle(brush, new Rect(0, 0, 96, 64));
            image.Save(file);
        }
        MakeImage(greenFile, Brushes.Green);
        MakeImage(redFile, Brushes.Red);
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Margin = new Thickness(16) };
        var window = new Window { Content = panel, Width = 600, Height = 120 };
        var settings = new ReadOnlyPreferencesPreview();
        try
        {
            prefs.UserAvatars = [];
            Check(prefs.UserAvatars.Length == 5 && prefs.UserAvatars.All(a => !a.HasEmail), "Avatar settings always expose exactly five empty identity slots");
            var emails = Enumerable.Range(0, 6).Select(i => $"avatar-{i}-{Guid.NewGuid():N}@example.invalid").ToArray();
            for (var i = 0; i < 5; i++)
                Check(prefs.SetUserAvatar(emails[i], greenFile) && prefs.UserAvatars[i].Preview.PixelSize == new PixelSize(64, 64),
                    "Custom avatar is stored as a cropped 64x64 image: slot " + i);
            Check(!prefs.SetUserAvatar(emails[5], greenFile), "A sixth custom identity cannot be added");
            Check(prefs.SetUserAvatar(emails[0].ToUpperInvariant(), redFile) && prefs.UserAvatars.Count(a => a.HasEmail) == 5,
                "Replacing a configured identity is case-insensitive and does not consume another slot");
            var manager = AvatarManager.Instance;
            var expected = prefs.UserAvatars[0].Preview;
            Check(ReferenceEquals(manager.Request(emails[0], false), expected) && ReferenceEquals(manager.Request(emails[0], true), expected),
                "Custom avatars take priority over cached/network images and forced refetch");
            var avatar = new Avatar { Width = 64, Height = 64, User = new User { Name = "Same display name", Email = emails[0] } };
            panel.Children.Add(avatar);
            window.Show();
            PumpFor(80);
            prefs.SetUserAvatar(emails[0], greenFile);
            Check(ReferenceEquals(imageField.GetValue(avatar), prefs.UserAvatars[0].Preview) && !ReferenceEquals(expected, prefs.UserAvatars[0].Preview),
                "Changing a custom image updates already-visible avatar controls immediately");
            panel.Children.Remove(avatar);
            PumpFor(20);
            prefs.SetUserAvatar(emails[0], redFile);
            panel.Children.Add(avatar);
            PumpFor(80);
            Check(ReferenceEquals(imageField.GetValue(avatar), prefs.UserAvatars[0].Preview), "Virtualized avatars reload current images after being reattached");
            var json = JsonSerializer.Serialize(prefs, type);
            File.Delete(greenFile);
            File.Delete(redFile);
            var saved = (Prefs)JsonSerializer.Deserialize(json, type);
            Check(saved.UserAvatars.Length == 5 && saved.UserAvatars.All(a => a.Preview?.PixelSize == new PixelSize(64, 64)),
                "Saved avatars reload without the originally selected image files");
            var oversized = new Prefs { UserAvatars = Enumerable.Range(0, 8).Select(_ => new UserAvatar()).ToArray() };
            Check(oversized.UserAvatars.Length == 5, "Imported preferences also enforce the five-slot maximum");
            var data = prefs.UserAvatars[0].ImageData;
            try { prefs.UserAvatars[0].SetFromFile(Path.Combine(_root, "missing-image.png")); }
            catch (FileNotFoundException) { }
            Check(prefs.UserAvatars[0].ImageData == data, "Invalid image selection preserves the previous custom image");
            var slots = settings.FindControl<TabItem>("UserAvatarsTab");
            slots.IsSelected = true;
            settings.Show();
            PumpFor(100);
            Check(settings.FindControl<ItemsControl>("UserAvatarSlots").Items.Count == 5, "Settings renders all five avatar editors");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                PumpFor(50);
                CaptureUxFrame(settings, "custom-avatars-settings-" + theme);
            }
            var clear = settings.FindControl<ItemsControl>("UserAvatarSlots").GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.DataContext, prefs.UserAvatars[0]) && Equals(ToolTip.GetTip(b), SourceGit.App.Text("Preferences.Avatars.Clear")));
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!prefs.UserAvatars[0].HasEmail && string.IsNullOrEmpty(prefs.UserAvatars[0].ImageData) && imageField.GetValue(avatar) == null &&
                prefs.UserAvatars.Skip(1).All(a => a.Preview != null), "Clearing an avatar restores fallback rendering without changing other users");
            prefs.UserAvatars[0].Email = emails[0];
            prefs.UserAvatars[0].ImageData = "invalid-base64";
            Check(manager.Request(emails[0], false) == null, "Corrupt custom image data falls back instead of crashing");
            for (var i = 0; i < 5; i++)
            {
                var fallbackEmail = $"fallback-{i}-{Guid.NewGuid():N}@example.invalid";
                Invoke(manager, "SetResource", fallbackEmail, null, false);
                var fallback = new Avatar { Width = 64, Height = 64, User = new User { Name = "User " + i, Email = fallbackEmail } };
                panel.Children.Add(fallback);
                foreach (var identicon in new[] { false, true })
                {
                    prefs.UseGitHubStyleAvatar = identicon;
                    PumpFor(30);
                    using var rendered = new RenderTargetBitmap(new PixelSize(64, 64));
                    rendered.Render(fallback);
                    rendered.Save(Path.Combine(_root, $"muted-avatar-{i}-{identicon}.png"));
                    var pixels = new byte[64 * 64 * 4];
                    var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    try { rendered.CopyPixels(new PixelRect(0, 0, 64, 64), pinned.AddrOfPinnedObject(), pixels.Length, 256); }
                    finally { pinned.Free(); }
                    // Sample backgrounds/pattern tiles, not subpixel-antialiased letter edges.
                    Check(new[] { 4 * 64 + 4, 8 * 64 + 12, 8 * 64 + 52, 56 * 64 + 8 }.All(p =>
                        Math.Max(pixels[p * 4], Math.Max(pixels[p * 4 + 1], pixels[p * 4 + 2])) -
                        Math.Min(pixels[p * 4], Math.Min(pixels[p * 4 + 1], pixels[p * 4 + 2])) <= 24),
                        "Default avatar pixels use a low-saturation palette: " + identicon);
                }
            }
            CaptureUxFrame(window, "muted-default-avatars");
        }
        finally
        {
            settings.Close();
            window.Close();
            prefs.UserAvatars = previousAvatars;
            prefs.UseGitHubStyleAvatar = previousMode;
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static void TestToolbarAndHistoryAffordances()
    {
        var path = NewRepository("daily toolbar and history");
        for (var i = 0; i < 12; i++)
            Git(path, "commit", "--allow-empty", "-m", "Daily workflow commit " + i);
        Git(path, "checkout", "-b", "feature/long-current-branch-for-toolbar-layout");
        var repo = Open(path);
        var prefs = Prefs.Instance;
        var previousSlots = prefs.ToolbarCustomActionSlots;
        var previousLayout = prefs.UseTwoColumnsLayoutInHistories;
        var previousZoom = prefs.HistoriesZoom;
        var toolbar = new RepositoryToolbar { DataContext = repo };
        var history = new Histories { DataContext = repo.Histories };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(toolbar);
        Grid.SetRow(history, 1);
        root.Children.Add(history);
        var window = new Window { Content = root, Width = 1920, Height = 950 };
        try
        {
            prefs.ToolbarCustomActionSlots = 1;
            window.Show();
            WaitUntil(() => repo.Histories.Commits.Count == 12, "Daily history did not load");
            var slots = Enumerable.Range(0, 3).Select(i => toolbar.FindControl<Button>($"CustomActionSlot{i}Button")).ToArray();
            Check(slots.Count(b => b.IsVisible) == 1 && slots[0].Tag == null, "New toolbars show just one Add action position by default");
            var defaults = new Prefs { ToolbarCustomActionSlots = 0 };
            Check(defaults.ToolbarCustomActionSlots == 1, "Action slot preference clamps to one at minimum");
            defaults.ToolbarCustomActionSlots = 10;
            Check(defaults.ToolbarCustomActionSlots == 3, "Action slot preference clamps to three at maximum");
            foreach (var (name, width) in new[] { ("FetchButton", 56.0), ("FetchWithSubmodulesButton", 94),
                ("PullWithSubmodulesButton", 82), ("UpdateSubmodulesRecursivelyButton", 100), ("CustomActionSlot0Button", 80) })
                Check(Math.Abs(toolbar.FindControl<Button>(name).Width - width * 1.1) < 0.01, "Toolbar click target is 1.1x wider: " + name);
            Check(Math.Abs(toolbar.FindControl<ToggleButton>("FocusCurrentBranchButton").Width - 56 * 1.1) < 0.01 &&
                Math.Abs(toolbar.FindControl<Button>("PullSubmoduleOptionsButton").Width - 18 * 1.1) < 0.01,
                "Focus toggle and submodule options targets are widened too");
            foreach (var name in new[] { "Build", "Test", "Deploy" })
                repo.Settings.CustomActions.Add(new CustomAction { Name = name, Executable = "cmd.exe" });
            Invoke(toolbar, "RefreshCustomActionSlots");
            foreach (var count in new[] { 3, 2, 1 })
            {
                prefs.ToolbarCustomActionSlots = count;
                PumpFor(40);
                Check(slots.Count(b => b.IsVisible) == count && repo.GetCustomActions(CustomActionScope.Repository).Count == 3,
                    "Changing shortcut count keeps all configured actions available: " + count);
            }
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                foreach (var width in new[] { 1920, 1440, 1024, 920 })
                {
                    window.Width = width;
                    foreach (var count in new[] { 1, 3 })
                    {
                        prefs.ToolbarCustomActionSlots = count;
                        PumpFor(80);
                        foreach (var button in toolbar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("toolbar_button")))
                        {
                            var pos = button.TranslatePoint(default, root).Value;
                            Check(pos.X >= -1 && pos.X + button.Bounds.Width <= root.Bounds.Width + 1 &&
                                pos.Y + button.Bounds.Height <= history.Bounds.Y + 1,
                                $"Widened toolbar target fits at {width}, slots={count}, theme={theme}: {button.Name}");
                        }
                        if (width is 1920 or 920)
                            CaptureUxFrame(window, $"daily-gui-{width}-slots{count}-{theme}");
                    }
                }
            }
            window.Width = 1440;
            prefs.ToolbarCustomActionSlots = 1;
            var splitter = history.FindControl<GridSplitter>("HistoryDetailsSplitter");
            foreach (var horizontal in new[] { false, true })
            {
                prefs.UseTwoColumnsLayoutInHistories = horizontal;
                PumpFor(100);
                Check(horizontal ? splitter.Bounds.Width == 8 : splitter.Bounds.Height == 8, "History/details divider has an eight-pixel drag target in both layouts");
                var before = horizontal ? splitter.Bounds.X : splitter.Bounds.Y;
                var center = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window).Value;
                var end = center + (horizontal ? new Vector(50, 0) : new Vector(0, 50));
                window.MouseDown(center, MouseButton.Left);
                window.MouseMove(end);
                window.MouseUp(end, MouseButton.Left);
                PumpFor(100);
                Check(Math.Abs((horizontal ? splitter.Bounds.X : splitter.Bounds.Y) - before) > 10, "Visible splitter grip actually resizes the panels");
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    foreach (var zoom in new[] { 1.0, 2.0 })
                    {
                        prefs.HistoriesZoom = zoom;
                        repo.Histories.SelectedCommits = [repo.Histories.Commits[1]];
                        PumpFor(80);
                        var outlines = history.GetVisualDescendants().OfType<Rectangle>().Where(r => r.Classes.Contains("head_subject_outline") && r.IsEffectivelyVisible).ToList();
                        Check(outlines.Count == 1 && outlines[0].FindAncestorOfType<DataGridRow>().DataContext is Commit { IsCurrentHead: true },
                            "The dashed message outline follows HEAD, not the selected historical commit");
                        var outline = outlines[0];
                        Check(outline.StrokeDashArray.SequenceEqual(new double[] { 4, 3 }) && !outline.IsHitTestVisible &&
                            outline.Bounds.Height <= outline.FindAncestorOfType<DataGridRow>().Bounds.Height + 1,
                            "HEAD uses a noninteractive dashed outline that fits at enlarged graph zoom");
                        Check(WorkspaceContrast(((ISolidColorBrush)splitter.Background).Color, ((ISolidColorBrush)history.FindResource("Brush.Contents")).Color) > 1.25,
                            "Splitter remains distinct from the history and detail surfaces");
                        CaptureUxFrame(window, $"daily-history-splitter-{horizontal}-{zoom}-{theme}");
                    }
                }
            }
        }
        finally
        {
            window.Close();
            repo.Close();
            prefs.ToolbarCustomActionSlots = previousSlots;
            prefs.UseTwoColumnsLayoutInHistories = previousLayout;
            prefs.HistoriesZoom = previousZoom;
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
}
