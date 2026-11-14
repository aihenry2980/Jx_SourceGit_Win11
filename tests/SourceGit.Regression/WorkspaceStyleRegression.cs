using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

internal static partial class Program
{
    private static void TestWorkspaceStyle()
    {
        var path = NewRepository("workspace presentation");
        foreach (var subject in new[] { "Create repository", "Add network configuration", "Improve module discovery", "Fix retry handling", "Update workspace settings" })
        {
            File.WriteAllText(Path.Combine(path, "settings.txt"), subject);
            Git(path, "add", ".");
            Git(path, "commit", "-m", subject);
        }
        Git(path, "branch", "feature/network");
        Git(path, "branch", "release/stable", "HEAD~2");
        Git(path, "update-ref", "refs/remotes/origin/main", "HEAD");
        for (var i = 0; i < 256; i++)
            File.WriteAllText(Path.Combine(path, $"draft-{i}.txt"), "pending");
        var repo = Open(path);
        var prefs = SourceGit.ViewModels.Preferences.Instance;
        var oldWidth = repo.SidebarWidth;
        var oldFontSize = prefs.DefaultFontSize;
        var view = new SourceGit.Views.Repository { DataContext = repo };
        var toolbar = new SourceGit.Views.RepositoryToolbar { DataContext = repo };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(view, 1);
        layout.Children.Add(toolbar);
        layout.Children.Add(view);
        var window = new Window { Content = layout, Width = 1440, Height = 900 };
        window.Show();
        try
        {
            WaitUntil(() => repo.LocalChangesCount == 256, "Workspace preview changes did not load");
            repo.SidebarWidth = new GridLength(300);
            repo.PresetBranchExactNames = "main\nfeature/network";
            repo.PresetBranchContainsPatterns = "release/";
            repo.PresetBranchExcludeNames = "archive/obsolete";
            repo.ApplyPresetBranchFilter();
            WaitUntil(() => repo.Histories.Commits.Count >= 5, "Workspace preview history did not load");
            repo.Histories.SelectedCommits = [repo.Histories.Commits[1]];
            var nav = view.FindControl<ListBox>("RepositoryNavigation");
            var apply = view.FindControl<Button>("ApplyPresetBranchFilterButton");
            var exact = view.FindControl<TextBox>("PresetBranchExactNameInputBox");

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                window.Width = 1440;
                repo.SidebarWidth = new GridLength(300);
                PumpFor(200);
                var subject = (ISolidColorBrush)Application.Current.FindResource("Brush.CommitSubject");
                var contents = (ISolidColorBrush)view.FindResource("Brush.Contents");
                Check(subject.Opacity == 1 && WorkspaceContrast(subject.Color, contents.Color) >= 4.5,
                    "Ordinary history text has readable contrast without reducing opacity: " + theme);
                var head = new SourceGit.Models.Commit { Decorators = [new SourceGit.Models.Decorator { Type = SourceGit.Models.DecoratorType.CurrentBranchHead }] };
                var headText = (ISolidColorBrush)SourceGit.Converters.CommitConverters.SubjectToBrush.Convert(head, typeof(IBrush), null, null);
                foreach (var isSelected in new[] { false, true })
                {
                    var headBackground = (ISolidColorBrush)SourceGit.Converters.CommitConverters.HeadSubjectBackground.Convert(
                        new object[] { head, isSelected }, typeof(IBrush), null, null);
                    Check(WorkspaceContrast(headText.Color, headBackground.Color) >= 4.5,
                        $"HEAD stays distinct and readable in {theme}, selected={isSelected}");
                    Check(theme != ThemeVariant.Dark || headBackground.Color.R < 80,
                        "Dark HEAD background does not reuse a bright light-theme surface");
                }
                var merged = new SourceGit.Models.Commit { IsMerged = true };
                var sha = (ISolidColorBrush)SourceGit.Converters.CommitConverters.SHAForeground.Convert(merged, typeof(IBrush), null, null);
                Check(WorkspaceContrast(sha.Color, contents.Color) >= 4.5, "Current-branch SHA is readable in " + theme);
                var selected = (ListBoxItem)nav.ContainerFromIndex(0);
                Check(selected.BorderThickness.Left == 3 && Equals(selected.BorderBrush, view.FindResource("Brush.Accent")),
                    "Active sidebar view has a stable accent rail: " + theme);
                var selection = selected.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("view_mode"));
                Check(selection.Background is ISolidColorBrush brush && brush.Opacity == 0.14,
                    "Sidebar selection uses a quiet tint rather than a saturated slab: " + theme);
                exact.Focus();
                PumpFor(50);
                var field = exact.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("preset_rule_field"));
                var innerBorder = exact.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
                Check(Equals(field.BorderBrush, view.FindResource("Brush.Accent")) && innerBorder.BorderThickness == new Thickness(0),
                    "Focused filter has one outline, not nested focus rectangles: " + theme);

                foreach (var fieldBorder in view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("preset_rule_field")))
                    Check(Equals(fieldBorder.Background, view.FindResource("Brush.Contents")), "Filter field follows the surface theme");
                foreach (var chip in view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("preset_rule_chip")))
                {
                    var remove = chip.GetVisualDescendants().OfType<Button>().Single();
                    var pos = remove.TranslatePoint(default, chip).Value;
                    Check(remove.Bounds.Width >= 18 && pos.X >= 0 && pos.X + remove.Bounds.Width <= chip.Bounds.Width,
                        "Rule remove target is comfortably sized and contained");
                }
                Check(apply.IsEffectivelyEnabled && repo.IsPresetBranchFilterEditorExpanded,
                    "Visual changes preserve always-available Apply and the expanded editor");
                CaptureUxFrame(window, "workspace-style-" + theme);
                var addAction = toolbar.FindControl<Button>("CustomActionSlot0Button");
                var addLabel = addAction.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Add action");
                Check(!addLabel.TextLayout.TextLines.Any(l => l.HasCollapsed) && addLabel.Opacity == 1,
                    "Add action has a complete label and does not look disabled");

                foreach (var name in new[] { "FetchToolbarGroup", "PullToolbarGroup" })
                {
                    var group = (Border)toolbar.FindControl<StackPanel>(name).Parent;
                    Check(group.Background is ISolidColorBrush idle && idle.Color.A == 0 && group.BorderThickness == new Thickness(1, 0, 0, 0),
                        "Idle toolbar group uses a thin neutral separator: " + name);
                    Invoke(toolbar, "BeginToolbarGroupFeedback", group);
                    PumpFor(320);
                    Check(group.Classes.Contains("busy") && group.Background is ISolidColorBrush busy && busy.Color.A > 0,
                        "Running toolbar group retains its colored pulse: " + name);
                    CaptureUxFrame(window, "workspace-busy-" + name + "-" + theme);
                    Invoke(toolbar, "EndToolbarGroupFeedback", group);
                    PumpFor(100);
                    Check(!group.Classes.Contains("busy") && group.Background is ISolidColorBrush stopped && stopped.Color.A == 0,
                        "Completed toolbar group returns to neutral: " + name);
                }

                window.Width = 1024;
                repo.SidebarWidth = new GridLength(250);
                PumpFor(150);
                var localChanges = nav.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == SourceGit.App.Text("WorkingCopy"));
                Check(localChanges.TextLayout.Width <= localChanges.Bounds.Width + 1 && !localChanges.TextLayout.TextLines.Any(l => l.HasCollapsed),
                    "Local Changes title is no longer squeezed by the secondary action row");
                foreach (var name in new[] { "FetchButton", "FetchWithSubmodulesButton", "PullButton", "PullTopRepositoryButton", "PullWithSubmodulesButton", "UpdateSubmodulesRecursivelyButton" })
                {
                    var button = toolbar.FindControl<Button>(name);
                    var pos = button.TranslatePoint(default, layout).Value;
                    Check(pos.X >= 0 && pos.X + button.Bounds.Width <= layout.Bounds.Width + 1 &&
                        pos.Y + button.Bounds.Height <= view.Bounds.Y + 1, "Toolbar action fits without overlapping workspace: " + name);
                }
                CaptureUxFrame(window, "workspace-style-1024-" + theme);
            }

            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            window.Width = 1440;
            repo.SidebarWidth = new GridLength(350);
            prefs.DefaultFontSize = 16;
            PumpFor(150);
            Check(apply.IsEffectivelyVisible && apply.Bounds.Width > 0, "Apply remains visible with larger UI text");
            CaptureUxFrame(window, "workspace-style-large-text");

            var list = new ListBox { Classes = { "repo_left_content_list" }, ItemsSource = new[] { "settings.txt", "network/client.cs" }, SelectedIndex = 0 };
            window.Content = list;
            PumpFor(100);
            var item = list.ContainerFromIndex(0);
            var indicator = item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionIndicator");
            Check(indicator.IsVisible && indicator.Width == 3 && !indicator.IsHitTestVisible, "Shared lists use a noninteractive selection rail");
            list.SelectedIndex = 1;
            PumpFor(50);
            Check(!indicator.IsVisible && list.ContainerFromIndex(1).GetVisualDescendants().OfType<Border>()
                .Single(b => b.Name == "PART_SelectionIndicator").IsVisible, "Selection rail moves with keyboard/programmatic selection");
        }
        finally
        {
            window.Close();
            repo.SidebarWidth = oldWidth;
            prefs.DefaultFontSize = oldFontSize;
            repo.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static double WorkspaceContrast(Color first, Color second)
    {
        static double Channel(byte c) => c / 255.0 <= 0.04045 ? c / 255.0 / 12.92 : Math.Pow((c / 255.0 + 0.055) / 1.055, 2.4);
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
