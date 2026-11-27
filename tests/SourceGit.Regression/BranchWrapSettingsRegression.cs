using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.Views;
using Prefs = SourceGit.ViewModels.Preferences;

internal static partial class Program
{
    private static void TestBranchWrapSettings()
    {
        var context = (JsonSerializerContext)typeof(SourceGit.App).Assembly.GetType("SourceGit.JsonCodeGen").GetProperty("Default").GetValue(null);
        var preferenceType = (JsonTypeInfo<Prefs>)context.GetTypeInfo(typeof(Prefs));
        var defaults = JsonSerializer.Deserialize("{}", preferenceType);
        Check(defaults.BranchNameWrapLength == 50 && BranchLabelLayout.GetLineBreak(new string('a', 50)) == -1 &&
            BranchLabelLayout.GetLineBreak(new string('a', 51)) > 0, "Old preference files use the new 50-character wrap default");
        defaults.BranchNameWrapLength = -1;
        Check(defaults.BranchNameWrapLength == BranchLabelLayout.MinimumWrapLength, "Invalid wrap length is clamped to the slider minimum");
        defaults.BranchNameWrapLength = int.MaxValue;
        Check(defaults.BranchNameWrapLength == BranchLabelLayout.MaximumWrapLength, "Invalid wrap length is clamped to the slider maximum");
        var unicode = string.Concat(Enumerable.Repeat("e\u0301", 51));
        Check(BranchLabelLayout.GetLineBreak(unicode, 50) > 0 && BranchLabelLayout.GetLineBreak(unicode, 51) == -1,
            "Configurable thresholds still count Unicode text elements rather than UTF-16 code units");

        var path = NewRepository("live branch wrap settings");
        for (var i = 0; i < 16; i++)
            Git(path, "commit", "--allow-empty", "-m", "Commit " + i);
        const string name = "feature/WRAP-12345/adjust-this-branch-label-in-real-time";
        Git(path, "checkout", "-b", name);
        Git(path, "update-ref", "refs/remotes/origin/" + name, "HEAD");
        Git(path, "branch", "short", "HEAD~3");
        Git(path, "tag", "version/this-long-tag-is-not-affected-by-the-branch-wrap-slider", "HEAD~3");
        var prefs = Prefs.Instance;
        var previousLength = prefs.BranchNameWrapLength;
        var previousZoom = prefs.HistoriesZoom;
        var previousCompact = prefs.CompactTrackingBranches;
        var readonlyField = typeof(Prefs).GetField("_isReadonly", BindingFlags.Instance | BindingFlags.NonPublic);
        var previousReadonly = readonlyField.GetValue(prefs);
        var repo = Open(path);
        var view = new Histories { DataContext = repo.Histories };
        var window = new Window { Content = view, Width = 1450, Height = 850 };
        var settings = new ReadOnlyPreferencesPreview();
        var itemsField = typeof(CommitRefsPresenter).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            using var watcher = repo.LockWatcher();
            prefs.CompactTrackingBranches = true;
            prefs.HistoriesZoom = 1;
            window.Show();
            WaitUntil(() => repo.Histories.Commits.Count == 16, "Live wrap history did not load");
            settings.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
            settings.Show();
            Wait((Task)Invoke(repo, "RefreshCommitsAsync", false));
            WaitUntil(() => !repo.Histories.IsLoading && !repo.Histories.IsBackfilling, "Initial history refresh and backfill did not settle");
            PumpFor(500);
            var slider = settings.FindControl<Slider>("BranchNameWrapLengthSlider");
            var counter = settings.FindControl<TextBlock>("BranchNameWrapLengthValue");
            var grid = view.FindControl<HistoriesCommitList>("CommitListContainer");
            var graph = view.FindControl<SourceGit.Views.CommitGraph>("CommitGraph");
            var commits = repo.Histories.Commits;
            DataGridRow Row(int index) => grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>()
                .Single(r => r.Index == index && r.IsVisible);
            CommitRefsPresenter Refs(int index) => Row(index).FindDescendantOfType<CommitRefsPresenter>();
            CommitRefsPresenter.RenderItem BranchItem() => ((List<CommitRefsPresenter.RenderItem>)itemsField.GetValue(Refs(0)))
                .Single(item => item.RawLabel == name && item.IsHead);
            Check(slider.Minimum == BranchLabelLayout.MinimumWrapLength && slider.Maximum == BranchLabelLayout.MaximumWrapLength &&
                slider.TickFrequency == 1 && slider.IsSnapToTickEnabled, "Appearance exposes an integer slider with the same range as stored preferences");

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                foreach (var locale in new[] { "en_US", "zh_CN" })
                {
                    SourceGit.App.SetLocale(locale);
                    foreach (var length in new[] { 25, 80, 10, 120, 50 })
                    {
                        slider.SetCurrentValue(RangeBase.ValueProperty, (double)length);
                        PumpFor(100);
                        var expanded = name.Length > length;
                        Check(prefs.BranchNameWrapLength == length && counter.Text == length.ToString() &&
                            grid.BranchNameWrapLength == length && Refs(0).BranchNameWrapLength == length,
                            $"Slider, counter, presenter and row estimates update together at {length} in {locale}/{theme}");
                        Check((BranchItem().LineBreak >= 0) == expanded && (Row(0).Bounds.Height > grid.RowMetrics.CompactHeight) == expanded,
                            "Sliding back and forth immediately grows or shrinks only the required branch row");
                        Check(BranchItem().RawLabel == name && Refs(0).MeasuredContentHeight <= Row(0).Bounds.Height + 1 &&
                            Math.Abs(Row(3).Bounds.Height - grid.RowMetrics.CompactHeight) < 1,
                            "Full branch names fit while short branches and long tags stay on compact rows");
                        Check(ReferenceEquals(commits, repo.Histories.Commits), "Dragging the wrap setting does not reload Git history");
                        foreach (var row in grid.FindDescendantOfType<DataGridRowsPresenter>().Children.OfType<DataGridRow>().Where(r => r.IsVisible))
                        {
                            var center = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), graph).Value.Y;
                            var anchor = graph.Layout.GetY(row.Index + 0.5) - graph.Layout.StartY + graph.Layout.OffsetY;
                            Check(Math.Abs(center - anchor) < 1, "Graph anchors remain aligned during live wrap adjustment");
                        }
                        if (length is 25 or 80)
                        {
                            CaptureUxFrame(window, $"wrap-slider-history-{length}-{locale}-{theme}");
                            CaptureUxFrame(settings, $"wrap-slider-settings-{length}-{locale}-{theme}");
                        }
                    }
                }
            }

            slider.SetCurrentValue(RangeBase.ValueProperty, 120.0);
            PumpFor(100);
            var thumb = slider.FindDescendantOfType<Thumb>();
            var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), settings).Value;
            var target = start.WithX(start.X - (slider.Bounds.Width - thumb.Bounds.Width) * 0.9);
            settings.MouseDown(start, MouseButton.Left);
            settings.MouseMove(target);
            PumpFor(100);
            Check(prefs.BranchNameWrapLength < name.Length && BranchItem().LineBreak > 0,
                "Real thumb dragging updates the graph before releasing the mouse");
            settings.MouseUp(target, MouseButton.Left);

            prefs.HistoriesZoom = 2;
            foreach (var length in new[] { 80, 25, 80 })
            {
                slider.SetCurrentValue(RangeBase.ValueProperty, (double)length);
                PumpFor(100);
                Check((BranchItem().LineBreak >= 0) == (name.Length > length) && Refs(0).MeasuredContentHeight <= Row(0).Bounds.Height + 1,
                    "Live wrap adjustment also fits at enlarged graph zoom");
            }

            slider.SetCurrentValue(RangeBase.ValueProperty, 63.0);
            prefs.SetCanModify();
            prefs.Save();
            var saved = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(_root, "preference.json")), preferenceType);
            Check(saved.BranchNameWrapLength == 63, "Wrap slider choice survives saving and reloading preferences");
        }
        finally
        {
            settings.Close();
            window.Close();
            repo.Close();
            prefs.BranchNameWrapLength = previousLength;
            prefs.HistoriesZoom = previousZoom;
            prefs.CompactTrackingBranches = previousCompact;
            prefs.Save();
            readonlyField.SetValue(prefs, previousReadonly);
            SourceGit.App.SetLocale("en_US");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private sealed class ReadOnlyPreferencesPreview : SourceGit.Views.Preferences
    {
        // UI tests must not write the machine's global Git configuration on close.
        protected override void OnClosing(WindowClosingEventArgs e) { }
    }
}
