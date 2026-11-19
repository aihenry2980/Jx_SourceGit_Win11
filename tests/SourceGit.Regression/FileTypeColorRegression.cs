using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Prefs = SourceGit.ViewModels.Preferences;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void TestCustomFileTypeColors()
    {
        var prefs = Prefs.Instance;
        var previousRules = prefs.FileTypeColors;
        var previousLocale = prefs.Locale;
        var previousFontSize = prefs.DefaultFontSize;
        var previousTheme = Application.Current.RequestedThemeVariant;
        var readonlyField = typeof(Prefs).GetField("_isReadonly", BindingFlags.Instance | BindingFlags.NonPublic);
        var previousReadonly = readonlyField.GetValue(prefs);
        var context = (JsonSerializerContext)typeof(SourceGit.App).Assembly.GetType("SourceGit.JsonCodeGen").GetProperty("Default").GetValue(null);
        var type = context.GetTypeInfo(typeof(Prefs));
        var settings = new ReadOnlyPreferencesPreview();
        var changes = Enumerable.Range(0, 10).Select(i => new Change { Path = $"src/file.type{i}", WorkTree = ChangeState.Modified }).ToList();
        changes.AddRange([new Change { Path = "legacy.c" }, new Change { Path = "README" }, new Change { Path = "assets/image.png" }]);
        var view = new ChangeCollectionView { Changes = changes };
        var window = new Window { Content = view, Width = 640, Height = 600 };
        static Color ColorOf(IBrush brush) => ((ISolidColorBrush)brush).Color;
        Border Tag(string text) => view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("file_type_tag") && b.IsEffectivelyVisible &&
            b.GetVisualDescendants().OfType<TextBlock>().Single().Text == text);
        try
        {
            var defaults = (Prefs)JsonSerializer.Deserialize("{}", type);
            Check(defaults.FileTypeColors.Length == 10 && defaults.FindFileTypeColor("test.CPP").Color == 0xFFB42332 &&
                defaults.FindFileTypeColor("test.hpp").Color == 0xFF245EB7, "Old preference files get ten editable slots with existing C/C++ and header defaults");
            defaults.FileTypeColors = Enumerable.Range(0, 20).Select(i => new FileTypeColor { Extension = "type" + i }).ToArray();
            Check(defaults.FileTypeColors.Length == 10 && defaults.FindFileTypeColor("file.type10") == null, "Imported rules enforce the ten-extension maximum");
            defaults.FileTypeColors = [null];
            Check(defaults.FileTypeColors.Length == 10 && defaults.FileTypeColors.All(r => r != null), "Short, null-containing imports are safely padded with empty rules");
            defaults.FileTypeColors = null;
            Check(defaults.FileTypeColors.Length == 10, "Null rule collections cannot break rendering");

            prefs.FileTypeColors = Enumerable.Range(0, 10).Select(i => new FileTypeColor
                { Extension = "type" + i, Color = 0xFF243458u + (uint)i * 0x00070103u }).ToArray();
            window.Show();
            settings.FindControl<TabItem>("FileTypeColorsTab").IsSelected = true;
            settings.Show();
            PumpFor(100);
            var slots = settings.FindControl<ItemsControl>("FileTypeColorSlots");
            Check(slots.Items.Count == 10, "Settings exposes exactly ten extension-and-color editors");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var mode in new[] { ChangeViewMode.Tree, ChangeViewMode.Grid, ChangeViewMode.List })
            {
                Application.Current.RequestedThemeVariant = theme;
                view.ViewMode = mode;
                PumpFor(60);
                for (var i = 0; i < 10; i++)
                    Check(ColorOf(Tag("TYPE" + i).Background) == Color.FromUInt32(prefs.FileTypeColors[i].Color),
                        $"Custom extension {i} is colored in {mode}/{theme}");
                Check(ColorOf(Tag("C").Background) == Color.FromUInt32(FileTypeColor.DEFAULT_COLOR) &&
                    ColorOf(Tag("FILE").Background) == Color.FromUInt32(FileTypeColor.DEFAULT_COLOR),
                    "Unmatched extensions and extensionless files stay gray, without hardcoded C overrides");
                foreach (var color in new uint[] { 0xFFF5D966, 0xFF153649 })
                {
                    prefs.FileTypeColors[1].Color = color;
                    PumpFor(40);
                    var tag = Tag("TYPE1");
                    var label = tag.GetVisualDescendants().OfType<TextBlock>().Single();
                    Check(ColorOf(tag.Background) == Color.FromUInt32(color) && WorkspaceContrast(ColorOf(label.Foreground), ColorOf(tag.Background)) >= 4.5,
                        "Editing a color updates existing tags and contrasting text immediately: " + mode + "/" + theme);
                }
                CaptureUxFrame(window, "custom-file-types-" + mode + "-" + theme);
            }
            var first = prefs.FileTypeColors[0];
            var input = slots.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, first));
            input.Text = " .PnG ";
            PumpFor(50);
            Check(first.ExtensionKey == "PNG" && ColorOf(Tag("PNG").Background) == Color.FromUInt32(first.Color),
                "Extension edits apply live and normalize whitespace, leading dots and letter case");
            var swatch = slots.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, first) && b.Flyout is Flyout);
            swatch.Flyout.ShowAt(swatch);
            PumpFor(50);
            var picker = (SourceGit.Views.ColorPicker)((Flyout)swatch.Flyout).Content;
            picker.Value = 0xFF9A0089;
            PumpFor(40);
            Check(first.Color == picker.Value && ColorOf(Tag("PNG").Background) == Color.FromUInt32(picker.Value),
                "The settings color picker writes through to rules and visible file tags");
            swatch.Flyout.Hide();

            prefs.FileTypeColors[9].Extension = "*.PNG";
            prefs.FileTypeColors[9].Color = 0xFFFFFF00;
            PumpFor(50);
            Check(ColorOf(Tag("PNG").Background) == Colors.Yellow, "Later duplicate rules override earlier rows instead of being ignored");
            prefs.FileTypeColors[9].Extension = "file";
            Check(prefs.FindFileTypeColor("README") == null && prefs.FindFileTypeColor("example.FILE") == prefs.FileTypeColors[9],
                "A file extension rule does not accidentally match the FILE label for extensionless files");
            prefs.FileTypeColors[9].Color = 0x00123456;
            Check(prefs.FileTypeColors[9].Color == 0xFF123456, "Imported transparent colors are made opaque for reliable tag contrast");
            prefs.SetCanModify();
            prefs.Save();
            var saved = (Prefs)JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(_root, "preference.json")), type);
            Check(saved.FileTypeColors.Length == 10 && saved.FindFileTypeColor("asset.png").Color == first.Color &&
                saved.FindFileTypeColor("data.file").Color == 0xFF123456, "All ten rule slots survive real preference save and reload");
            var json = JsonSerializer.Serialize(prefs, type);
            Check(!json.Contains("\"ExtensionKey\"") && !json.Contains("\"BackgroundBrush\"") && !json.Contains("\"FileTypeColorsRevision\""),
                "Only user choices, not brush objects or refresh counters, are serialized");
            foreach (var language in new[] { "en_US", "zh_CN" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                prefs.Locale = language;
                Application.Current.RequestedThemeVariant = theme;
                prefs.DefaultFontSize = 16;
                PumpFor(70);
                Check(slots.GetVisualDescendants().OfType<TextBox>().Count(t => t.IsEffectivelyVisible) == 10 && settings.Bounds.Height < 720,
                    "All ten rows remain visible without clipping in localized settings: " + language + "/" + theme);
                CaptureUxFrame(settings, "file-type-color-settings-" + language + "-" + theme);
            }
            var clear = slots.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, first) && b.Flyout == null);
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(50);
            Check(first.Extension.Length == 0 && ColorOf(Tag("PNG").Background) == Color.FromUInt32(FileTypeColor.DEFAULT_COLOR),
                "Clearing a row removes its live mapping and restores gray fallback");
            var reset = settings.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), SourceGit.App.Text("Preferences.FileTypes.Reset")));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(50);
            Check(prefs.FileTypeColors.Length == 10 && ColorOf(Tag("C").Background) == Color.FromUInt32(0xFFB42332),
                "Restore defaults resets all ten slots and immediately updates existing lists");
            var revision = prefs.FileTypeColorsRevision;
            first.Color = 0xFFFF0000;
            Check(prefs.FileTypeColorsRevision == revision, "Replaced rules no longer retain preference change subscriptions");
        }
        finally
        {
            settings.Close();
            window.Close();
            prefs.FileTypeColors = previousRules;
            prefs.Locale = previousLocale;
            prefs.DefaultFontSize = previousFontSize;
            Application.Current.RequestedThemeVariant = previousTheme;
            prefs.Save();
            readonlyField.SetValue(prefs, previousReadonly);
        }
    }
}
