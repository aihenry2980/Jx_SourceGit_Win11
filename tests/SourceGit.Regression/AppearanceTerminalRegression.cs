using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Prefs = SourceGit.ViewModels.Preferences;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void TestAppearanceAndTerminal()
    {
        var path = NewRepository("appearance terminal ; safe directory");
        for (var i = 0; i < 4; i++)
            Git(path, "commit", "--allow-empty", "-m", "Appearance commit " + i);
        var prefs = Prefs.Instance;
        var previousText = prefs.MachineTagText;
        var previousBackground = prefs.MachineTagBackgroundColor;
        var previousForeground = prefs.MachineTagForegroundColor;
        var previousAccent = prefs.MainAccentColor;
        var previousAvatars = prefs.UserAvatars;
        var previousZoom = prefs.HistoriesZoom;
        var previousTheme = Application.Current.RequestedThemeVariant;
        var previousLocale = prefs.Locale;
        var previousShell = (prefs.ShellOrTerminalType, prefs.ShellOrTerminalPath, prefs.ShellOrTerminalArgs);
        var repo = Open(path);
        repo.SidebarWidth = new GridLength(300);
        var view = new Repository { DataContext = repo };
        var toolbar = new RepositoryToolbar { DataContext = repo };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(toolbar);
        Grid.SetRow(view, 1);
        layout.Children.Add(view);
        var window = new Window { Content = layout, Width = 1920, Height = 1000 };
        var settings = new ReadOnlyPreferencesPreview();
        static Color ColorOf(IBrush brush) => ((ISolidColorBrush)brush).Color;
        try
        {
            using var start = new System.Diagnostics.Process { StartInfo = SourceGit.Native.OS.CreateWindowsTerminalStartInfo(path) };
            Check(start.StartInfo.FileName == "wt.exe" && start.StartInfo.WorkingDirectory == Path.GetFullPath(path) &&
                start.StartInfo.ArgumentList.SequenceEqual(new[] { "-d", "." }) && !start.StartInfo.UseShellExecute,
                "Windows Terminal uses a working directory without interpolating repository paths into commands");
            Check((prefs.ShellOrTerminalType, prefs.ShellOrTerminalPath, prefs.ShellOrTerminalArgs) == previousShell, "Explicit Windows Terminal does not replace the configured default terminal");
            var terminal = toolbar.FindControl<Button>("WindowsTerminalButton");
            Check(HotKeyManager.GetHotKey(terminal) is { Key: Key.F12, KeyModifiers: KeyModifiers.None } &&
                terminal.IsVisible == OperatingSystem.IsWindows(), "Windows Terminal is a separate Windows-only toolbar action with F12");
            prefs.MachineTagText = string.Empty;
            prefs.MachineTagBackgroundColor = 0;
            prefs.MachineTagForegroundColor = 0;
            prefs.UserAvatars = [];
            prefs.HistoriesZoom = 1;
            window.Show();
            WaitUntil(() => repo.Histories.Commits.Count == 4, "Appearance history did not load");
            var head = repo.Histories.Commits[0];
            repo.Histories.SelectedCommits = [head];
            PumpFor(150);
            var tag = view.FindControl<Border>("MachineIdentityTag");
            var tagText = (TextBlock)tag.Child;
            Check(tagText.Text == Environment.MachineName && ColorOf(tag.Background) == Color.FromUInt32(prefs.MainAccentColor),
                "Machine label defaults to the real computer name and main accent background");
            prefs.MainAccentColor = 0xFF7428AC;
            PumpFor(50);
            Check(ColorOf(tag.Background) == Color.FromUInt32(prefs.MainAccentColor), "Default machine label follows live accent changes");
            prefs.MachineTagText = "WORK PC";
            prefs.MachineTagBackgroundColor = 0xFFE1E7EB;
            prefs.MachineTagForegroundColor = 0xFF24313B;
            PumpFor(50);
            Check(tagText.Text == "WORK PC" && ColorOf(tag.Background) == Color.FromUInt32(0xFFE1E7EB) &&
                ColorOf(tagText.Foreground) == Color.FromUInt32(0xFF24313B), "Machine label text and both colors update existing repository views");
            prefs.MainAccentColor = 0xFF178A73;
            PumpFor(50);
            Check(ColorOf(tag.Background) == Color.FromUInt32(0xFFE1E7EB), "A custom machine background remains independent of accent changes");
            prefs.MachineTagForegroundColor = 0;
            Check(ColorOf(prefs.MachineTagForegroundBrush) == Colors.Black, "Light machine backgrounds select automatic black text");
            prefs.MachineTagBackgroundColor = 0xFF152534;
            Check(ColorOf(prefs.MachineTagForegroundBrush) == Colors.White, "Dark machine backgrounds select automatic white text");
            prefs.MachineTagText = "   ";
            Check(prefs.MachineTagLabel == Environment.MachineName, "Blank custom labels fall back to the machine name");
            prefs.MachineTagText = "WORK PC";

            var identity = prefs.UserAvatars[0];
            identity.Email = "  " + head.Author.Email.ToUpperInvariant() + "  ";
            identity.NameColor = 0xFF9A3874;
            PumpFor(70);
            Check(ColorOf(prefs.GetUserNameBrush(head.Author.Email)) == Color.FromUInt32(identity.NameColor) && identity.ImageData.Length == 0,
                "Author colors match normalized emails and work without a custom avatar image");
            TextBlock[] AuthorLabels() => view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.DataContext is Commit && t.Text == head.Author.Name && t.IsEffectivelyVisible).ToArray();
            Check(AuthorLabels().Length >= 4 && AuthorLabels().All(t => ColorOf(t.Foreground) == Color.FromUInt32(identity.NameColor)),
                "Configured author names are colored in every realized history row");
            var authorRuns = view.GetVisualDescendants().OfType<TextBlock>()
                .SelectMany(t => t.Inlines?.OfType<Run>() ?? []).Where(r => r.Text == head.Author.Name).ToArray();
            Check(authorRuns.Length > 0 && authorRuns.All(r => ColorOf(r.Foreground) == Color.FromUInt32(identity.NameColor)),
                "Commit information uses the same author name color as the graph");
            identity.NameColor = 0xFF286488;
            PumpFor(50);
            Check(AuthorLabels().All(t => ColorOf(t.Foreground) == Color.FromUInt32(identity.NameColor)),
                "Changing an author color refreshes existing rows immediately");
            identity.Email = "someone-else@example.invalid";
            PumpFor(50);
            Check(prefs.GetUserNameBrush(head.Author.Email) == null && AuthorLabels().All(t => ColorOf(t.Foreground) != Color.FromUInt32(identity.NameColor)),
                "Changing the configured email removes the color from the previous identity");
            identity.Email = head.Author.Email;

            settings.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
            settings.Show();
            PumpFor(100);
            settings.FindControl<TextBox>("MachineTagTextInput").Text = "DEV PC";
            PumpFor(50);
            Check(prefs.MachineTagText == "DEV PC" && tagText.Text == "DEV PC", "Settings text edits preview the machine label without reopening repositories");
            var context = (JsonSerializerContext)typeof(SourceGit.App).Assembly.GetType("SourceGit.JsonCodeGen").GetProperty("Default").GetValue(null);
            var type = context.GetTypeInfo(typeof(Prefs));
            var saved = (Prefs)JsonSerializer.Deserialize(JsonSerializer.Serialize(prefs, type), type);
            Check(saved.MachineTagText == "DEV PC" && saved.MachineTagBackgroundColor == prefs.MachineTagBackgroundColor &&
                saved.MachineTagForegroundColor == prefs.MachineTagForegroundColor && saved.UserAvatars[0].NameColor == identity.NameColor,
                "Machine label settings and author name colors survive preference serialization");
            var old = (Prefs)JsonSerializer.Deserialize("{}", type);
            Check(old.MachineTagText.Length == 0 && old.MachineTagBackgroundColor == 0 && old.MachineTagForegroundColor == 0 &&
                old.UserAvatars.All(a => a.NameColor == 0), "Older preference files retain automatic machine colors and unmodified author names");
            var reset = settings.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), SourceGit.App.Text("Preferences.Appearance.MachineTagReset")));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(50);
            Check(prefs.MachineTagBackgroundColor == 0 && prefs.MachineTagForegroundColor == 0 && ColorOf(tag.Background) == Color.FromUInt32(prefs.MainAccentColor),
                "Machine color reset restores accent background and automatic contrast");

            repo.Settings.CustomActions.Add(new CustomAction { Name = "Build", Executable = "cmd.exe", Color = 0xFFFF0044 });
            Invoke(toolbar, "RefreshCustomActionSlots");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                foreach (var accent in new uint[] { 0xFF7428AC, 0xFF178A73, 0xFFF5D966 })
                {
                    prefs.MainAccentColor = accent;
                    PumpFor(80);
                    var shaCells = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("commit_sha_cell") && b.IsEffectivelyVisible).ToArray();
                    var headSha = shaCells.Single(b => b.DataContext is Commit { IsCurrentHead: true });
                    var shaText = headSha.GetVisualDescendants().OfType<CommitSubjectPresenter>().Single();
                    Check(ColorOf(headSha.Background) == Color.FromUInt32(accent) && WorkspaceContrast(ColorOf(shaText.Foreground), ColorOf(headSha.Background)) >= 4.5,
                        "HEAD SHA follows the live accent with readable text in " + theme);
                    Check(shaCells.Where(b => b.DataContext is Commit { IsCurrentHead: false }).All(b => ColorOf(b.Background).A == 0),
                        "Changing SHA accent keeps ordinary commits unfilled in " + theme);
                    var buttons = toolbar.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("toolbar_button") && b.IsEffectivelyVisible).ToArray();
                    Check(buttons.All(b => b.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                        .All(p => ColorOf(p.Fill) == Color.FromUInt32(accent))), "Every toolbar icon, including custom actions, follows main accent: " + theme + "/" + accent);
                    var fetchLabel = toolbar.FindControl<Button>("FetchButton").GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("toolbar_label"));
                    var pullLabel = toolbar.FindControl<Button>("PullButton").GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("toolbar_label"));
                    var updateLabel = toolbar.FindControl<Button>("UpdateSubmodulesRecursivelyButton").GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("toolbar_label"));
                    Check(ColorOf(fetchLabel.Foreground) != ColorOf(pullLabel.Foreground) && ColorOf(pullLabel.Foreground) != ColorOf(updateLabel.Foreground),
                        "Toolbar labels retain functional categories independently of accent: " + theme);
                }
                foreach (var width in new[] { 1920, 920 })
                {
                    window.Width = width;
                    PumpFor(100);
                    CaptureUxFrame(window, $"appearance-terminal-{width}-{theme}");
                }
                foreach (var zoom in new[] { 1.0, 2.0 })
                {
                    window.Width = 1920;
                    prefs.HistoriesZoom = zoom;
                    PumpFor(80);
                    var outline = view.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Classes.Contains("head_subject_outline") && r.IsEffectivelyVisible);
                    var subject = ((Grid)outline.Parent).Children.OfType<CommitSubjectPresenter>().Single();
                    var formatted = new FormattedText(head.HistoryDisplaySubject, System.Globalization.CultureInfo.CurrentCulture,
                        Avalonia.Media.FlowDirection.LeftToRight, new Typeface(subject.FontFamily, FontStyle.Normal, subject.FontWeight), subject.FontSize, subject.Foreground);
                    CaptureUxFrame(window, $"head-outline-{zoom}-{theme}");
                    Check(subject.Margin.Left >= 12 && subject.Margin.Right >= 12 && outline.Bounds.Height >= formatted.Height + 3,
                        $"HEAD outline has horizontal and vertical breathing room without inflating ordinary rows: {zoom}, outline={outline.Bounds.Height}, text={formatted.Height}");
                }
                settings.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
                PumpFor(50);
                CaptureUxFrame(settings, "appearance-settings-" + theme);
                settings.FindControl<TabItem>("UserAvatarsTab").IsSelected = true;
                PumpFor(50);
                CaptureUxFrame(settings, "author-color-settings-" + theme);
            }
            var resetAuthor = settings.FindControl<ItemsControl>("UserAvatarSlots").GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.DataContext, identity) && Equals(ToolTip.GetTip(b), SourceGit.App.Text("Preferences.Avatars.ResetNameColor")));
            resetAuthor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(50);
            Check(identity.NameColor == 0 && prefs.GetUserNameBrush(head.Author.Email) == null && identity.Email == head.Author.Email,
                "Reset author color keeps the identity and restores theme-aware defaults");
        }
        finally
        {
            settings.Close();
            window.Close();
            repo.Close();
            prefs.MachineTagText = previousText;
            prefs.MachineTagBackgroundColor = previousBackground;
            prefs.MachineTagForegroundColor = previousForeground;
            prefs.MainAccentColor = previousAccent;
            prefs.UserAvatars = previousAvatars;
            prefs.HistoriesZoom = previousZoom;
            prefs.Locale = previousLocale;
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }
}
