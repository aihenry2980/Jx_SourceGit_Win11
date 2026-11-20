using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
    private static void TestRaisedToolbar()
    {
        var path = NewRepository("raised toolbar");
        for (var i = 0; i < 3; i++)
            Git(path, "commit", "--allow-empty", "-m", "Toolbar presentation " + i);
        var repo = Open(path);
        repo.Settings.CustomActions.Add(new CustomAction { Name = "Build", Executable = "cmd.exe" });
        var prefs = Prefs.Instance;
        var oldSlots = prefs.ToolbarCustomActionSlots;
        var oldTheme = Application.Current.RequestedThemeVariant;
        var toolbar = new RepositoryToolbar { DataContext = repo };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(toolbar);
        var history = new Histories { DataContext = repo.Histories };
        Grid.SetRow(history, 1);
        root.Children.Add(history);
        var window = new Window { Content = root, Width = 1920, Height = 580 };
        static Border Shell(Button button) => button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToolbarButtonShell");
        static ContentPresenter Presenter(Button button) => button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
        static bool IsRecessed(Button button) => button.Background is ILinearGradientBrush face &&
            face.GradientStops[0].Color.R + face.GradientStops[0].Color.G + face.GradientStops[0].Color.B <
            face.GradientStops[^1].Color.R + face.GradientStops[^1].Color.G + face.GradientStops[^1].Color.B &&
            Shell(button).BoxShadow[0].IsInset;
        static Color Pixel(Control visual, int y)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(visual.Bounds.Width), (int)Math.Ceiling(visual.Bounds.Height)));
            bitmap.Render(visual);
            var bytes = new byte[4];
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(4, y, 1, 1), pin.AddrOfPinnedObject(), 4, 4);
                var r = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2;
                return Color.FromArgb(bytes[3], bytes[r], bytes[1], bytes[2 - r]);
            }
            finally { pin.Free(); }
        }
        try
        {
            prefs.ToolbarCustomActionSlots = 1;
            window.Show();
            WaitUntil(() => repo.Histories.Commits.Count == 3, "Toolbar preview history did not load");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                window.MouseMove(new Point(4, 300));
                foreach (var width in new[] { 1920, 1440, 1024, 920 })
                {
                    window.Width = width;
                    PumpFor(80);
                    var buttons = toolbar.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.IsEffectivelyVisible && (b.Classes.Contains("toolbar_button") || b.Name is "PullSubmoduleOptionsButton" or "FocusCurrentBranchButton")).ToArray();
                    Check(buttons.Length >= 18 && buttons.All(b => Shell(b).Background is ILinearGradientBrush && Shell(b).BoxShadow.Count > 0),
                        $"Every toolbar action, custom action and scope control has a raised surface: {theme}, {width}");
                    Check(buttons.All(b => b.TranslatePoint(default, root).Value is var p && p.X >= -1 &&
                        p.X + b.Bounds.Width <= root.Bounds.Width + 1 && p.Y + b.Bounds.Height <= history.Bounds.Y + 1),
                        "Raised surfaces preserve existing targets and responsive toolbar placement");
                    Check(buttons.All(b => Presenter(b).GetVisualDescendants().OfType<TextBlock>().All(t =>
                        t.TranslatePoint(default, b).Value.Y >= -1 && t.TranslatePoint(default, b).Value.Y + t.Bounds.Height <= b.Bounds.Height + 1)),
                        "Toolbar labels fit vertically inside the raised faces without clipping");
                    CaptureUxFrame(window, $"raised-toolbar-{theme}-{width}");
                }
                window.Width = 1920;
                var fetch = toolbar.FindControl<Button>("FetchButton");
                PumpFor(60);
                var shell = Shell(fetch);
                Check(Pixel(shell, 3) != Pixel(shell, (int)shell.Bounds.Height - 4), "The bevel gradient is visible in rendered pixels");
                var idle = shell.Background;
                var size = fetch.Bounds.Size;
                window.MouseMove(fetch.TranslatePoint(new Point(fetch.Bounds.Width / 2, 15), window).Value);
                PumpFor(40);
                Check(!ReferenceEquals(shell.Background, idle) && Equals(shell.BorderBrush, toolbar.FindResource("Brush.Accent")) && fetch.Bounds.Size == size,
                    "Hover brightens the face and accent edge without resizing the button");
                window.MouseMove(new Point(4, 300));
                PumpFor(40);
                Check(ReferenceEquals(shell.Background, idle), "Leaving a button restores its raised surface");

                var group = (Border)toolbar.FindControl<StackPanel>("FetchToolbarGroup").Parent;
                Invoke(toolbar, "BeginToolbarGroupFeedback", group);
                PumpFor(320);
                Check(shell.Background is ISolidColorBrush transparent && transparent.Color.A == 0 && group.Classes.Contains("busy"),
                    "Raised faces let the existing group pulse show through while fetching");
                CaptureUxFrame(window, "raised-toolbar-busy-" + theme);
                Invoke(toolbar, "EndToolbarGroupFeedback", group);
                PumpFor(60);
                Check(ReferenceEquals(shell.Background, idle), "Completion restores the raised face automatically");

                var focusButton = toolbar.FindControl<Avalonia.Controls.Primitives.ToggleButton>("FocusCurrentBranchButton");
                focusButton.IsChecked = true;
                PumpFor(240);
                CaptureUxFrame(window, "raised-toolbar-focus-" + theme);
                Check(IsRecessed(focusButton),
                    "Checked Focus reads as a latched recessed switch");
                focusButton.IsChecked = false;

                var probe = new Button { Width = 88, Height = 46, Content = new TextBlock { Text = "Surface", FontSize = 12 } };
                probe.Classes.Add("toolbar_button");
                var maintenance = toolbar.FindControl<StackPanel>("MaintenanceToolbarGroup");
                maintenance.Children.Add(probe);
                PumpFor(60);
                var probeSize = probe.Bounds.Size;
                var probePoint = probe.TranslatePoint(new Point(30, 12), window).Value;
                window.MouseMove(probePoint);
                window.MouseDown(probePoint, MouseButton.Left);
                PumpFor(240);
                Check(IsRecessed(probe) &&
                    Presenter(probe).RenderTransform.Value.M32 == 1 && probe.Bounds.Size == probeSize,
                    "Pressing reverses the bevel, uses an inset shadow and moves only content by one pixel");
                CaptureUxFrame(window, "raised-toolbar-pressed-" + theme);
                window.MouseUp(probePoint, MouseButton.Left);
                window.MouseMove(new Point(4, 300));
                probe.IsEnabled = false;
                PumpFor(240);
                Check(Presenter(probe).Opacity == 0.4 && Shell(probe).Background is ISolidColorBrush && probe.Bounds.Size == probeSize,
                    "Disabled buttons are visibly muted without changing their target dimensions");
                maintenance.Children.Remove(probe);
            }
        }
        finally { window.Close(); repo.Close(); prefs.ToolbarCustomActionSlots = oldSlots; Application.Current.RequestedThemeVariant = oldTheme; }
    }
}
