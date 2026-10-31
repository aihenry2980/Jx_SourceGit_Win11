using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

internal static partial class Program
{
    private static void TestFilterEncodingUx()
    {
        var path = NewRepository("filter encoding ux");
        foreach (var name in new[] { "one.txt", "two.txt" })
            File.WriteAllText(Path.Combine(path, name), "before");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "initial");
        Git(path, "branch", "feature/test");
        Git(path, "branch", "external");
        Git(path, "remote", "add", "origin", path);
        var repo = Open(path);
        Window window = null;
        try
        {
            repo.PresetBranchExactNames = "main";
            repo.ApplyPresetBranchFilter();
            var draft = repo.PresetBranchFilterDraft;
            Check(!draft.HasChanges, "Filter draft starts from applied rules");
            var view = new SourceGit.Views.Repository { DataContext = repo };
            window = new Window { Content = view, Width = 1100, Height = 850 };
            window.Show();
            PumpFor(200);
            repo.IsSearchingCommits = true;
            PumpFor(50);
            var branches = view.FindControl<SourceGit.Views.BranchTree>("LocalBranchTree");
            branches.Height = 1;
            repo.IsSearchingCommits = false;
            PumpFor(100);
            Check(branches.Bounds.Height > 1 && view.FindControl<ScrollViewer>("DashboardScrollViewer").IsVisible,
                "Returning from commit search recalculates the normal sidebar layout");
            var exact = view.FindControl<TextBox>("PresetBranchExactNameInputBox");
            var contains = view.FindControl<TextBox>("PresetBranchContainsPatternInputBox");
            var exclude = view.FindControl<TextBox>("PresetBranchExcludeNameInputBox");
            var apply = view.FindControl<Button>("ApplyPresetBranchFilterButton");
            exact.Text = "feature/test";
            PumpFor(50);
            Check(draft.HasChanges && apply.IsEnabled && repo.PresetBranchExactNames == "main",
                "Unfinished input stays editable until it becomes a confirmed item");
            Invoke(view, "OnPresetBranchExactNameInputKeyDown", exact, new KeyEventArgs { Key = Key.Enter });
            Check(draft.ExactNames.Contains("feature/test") && string.IsNullOrEmpty(draft.ExactInput) &&
                repo.PresetBranchExactNames == "main\nfeature/test" && !draft.HasChanges,
                "Enter adds and applies an exact-name item without clicking Apply");
            contains.Text = "feature/";
            Invoke(view, "OnPresetBranchContainsPatternInputKeyDown", contains, new KeyEventArgs { Key = Key.Space });
            exclude.Text = "external";
            Invoke(view, "CommitPresetBranchSuggestion", repo, exclude, "external");
            Check(repo.PresetBranchContainsPatterns == "feature/" && repo.PresetBranchExcludeNames == "external" && !draft.HasChanges,
                "Space and suggestion selection apply contains and exclusion items immediately");
            Check(repo.UIStates.HistoryFilters.Any(f => f.Pattern == "refs/heads/feature/test"),
                "Auto-application updates active graph filters as well as saved rules");
            Invoke(view, "OnClearPresetBranchExactNames", exact, new RoutedEventArgs());
            Check(string.IsNullOrEmpty(draft.ExactNames) && string.IsNullOrEmpty(repo.PresetBranchExactNames) && !draft.HasChanges,
                "Clearing exact-name items updates active rules immediately");
            draft.ExactNames = "main";
            exact.Text = "feature/test";
            Invoke(view, "OnApplyPresetBranchFilter", apply, new RoutedEventArgs());
            Check(!draft.HasChanges && repo.PresetBranchExactNames == "main\nfeature/test" &&
                repo.PresetBranchContainsPatterns == "feature/" && repo.PresetBranchExcludeNames == "external",
                "Apply consumes unfinished input and commits all three rule types together");
            Check(repo.IsPresetBranchFilterEditorExpanded, "Applying leaves the filter editor expanded");
            Check(!draft.HasChanges && apply.IsEffectivelyEnabled, "Apply remains enabled with unchanged rules");
            Git(path, "update-ref", "refs/remotes/origin/feature/new", "HEAD");
            Wait(InvokeTask(repo, "RefreshBranchesAsync"));
            Check(repo.Branches.Any(b => b.FullName == "refs/remotes/origin/feature/new") && !draft.HasChanges,
                "A newly discovered remote branch does not require editing the filter draft");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(repo.UIStates.HistoryFilters.Any(f => f.Pattern == "refs/remotes/origin/feature/new" &&
                f.Mode == SourceGit.Models.FilterMode.Included) && !draft.HasChanges && apply.IsEffectivelyEnabled,
                "Reapplying unchanged rules adopts the new matching remote branch and stays enabled");
            Invoke(view, "OnRemovePresetBranchExactName", new Button { Tag = "main" }, new RoutedEventArgs());
            Check(!draft.HasChanges && repo.PresetBranchExactNames == "feature/test", "Removing a chip immediately applies the remaining rules");
            draft.ContainsInput = "pending";
            repo.SetPresetBranchFilterMode(true);
            Check(repo.IsShowingAllBranches && draft.HasChanges, "All mode preserves pending edits");
            repo.SetPresetBranchFilterMode(false);
            Check(!repo.IsShowingAllBranches && draft.ExactNames == "feature/test" && !repo.PresetBranchExactNames.Contains("main"),
                "By-rules mode uses applied settings without consuming draft edits");
            draft.ContainsInput = "pending";
            repo.AddBranchToPresetFilter(repo.Branches.Single(b => b.Name == "external"));
            Check(draft.ExactNames.Contains("external") && !draft.ExactNames.Contains("main") &&
                draft.ContainsInput == "pending" && string.IsNullOrEmpty(draft.ExcludeNames),
                "Graph add-to-filter synchronizes draft while retaining unrelated pending edits");
            draft.ExcludeNames = "blocked";
            Check(repo.PresetBranchExcludeNames == "blocked" && repo.PresetBranchContainsPatterns == "feature/" && draft.ContainsInput == "pending",
                "Applying changed items never consumes unfinished text in another field");
            foreach (var locale in new[] { "en_US", "zh_CN" })
            {
                SourceGit.App.SetLocale(locale);
                PumpFor(120);
                Check(apply.Bounds.Width > 0 && apply.IsEffectivelyVisible, "Apply remains visible in " + locale);
                CaptureUxFrame(window, "filter-draft-" + locale);
            }
            var chips = view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("preset_rule_chip")).ToList();
            Check(chips.Count >= 3 && chips.All(chip => Equals(chip.Background, chips[0].Background) && Equals(chip.BorderBrush, chips[0].BorderBrush)),
                "Exact, contains and exclusion rule chips share a neutral background and border");
            var markers = chips.SelectMany(chip => chip.GetVisualDescendants().OfType<TextBlock>())
                .Where(text => text.Classes.Contains("rule_kind")).Select(text => text.Text).Distinct().ToArray();
            Check(markers.Contains("=") && markers.Contains("*") && markers.Contains("-"),
                "Rule markers distinguish exact, contains and exclusion matching without branch colors");
            Invoke(view, "OnResetPresetBranchFilterDraft", apply, new RoutedEventArgs());
            Check(!draft.HasChanges && draft.ExactNames == repo.PresetBranchExactNames && string.IsNullOrEmpty(draft.ContainsInput),
                "Discard clears unfinished input while retaining already auto-applied rules");
            Check(repo.PresetBranchExcludeNames == "blocked", "Discarding text does not undo confirmed items");
            repo.SetPresetBranchFilterMode(true);
            exact.Text = "main";
            Invoke(view, "OnPresetBranchExactNameInputKeyDown", exact, new KeyEventArgs { Key = Key.Enter });
            Check(!repo.IsShowingAllBranches && !draft.HasChanges && repo.PresetBranchExactNames.Contains("main"),
                "Adding an item in All mode automatically activates the updated branch filter");
            repo.SetPresetBranchFilterMode(true);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!repo.IsShowingAllBranches && !draft.HasChanges && apply.IsEffectivelyEnabled,
                "Apply also reactivates unchanged rules from All mode");
            exact.Text = "pasted/one pasted/two pasted/one";
            PumpFor(60);
            Check(!draft.HasChanges && string.IsNullOrEmpty(exact.Text) &&
                repo.Settings.GetPresetBranchExactNameList().Count(n => n == "pasted/one") == 1 &&
                repo.Settings.GetPresetBranchExactNameList().Contains("pasted/two"),
                "Pasting several delimited items applies them automatically and removes duplicates");
            Invoke(view, "OnRemovePresetBranchContainsPattern", new Button { Tag = "feature/" }, new RoutedEventArgs());
            Invoke(view, "OnRemovePresetBranchExcludeName", new Button { Tag = "blocked" }, new RoutedEventArgs());
            Check(string.IsNullOrEmpty(repo.PresetBranchContainsPatterns) && string.IsNullOrEmpty(repo.PresetBranchExcludeNames),
                "Removing contains and exclusion chips immediately updates both rule groups");
            contains.Text = "feature/";
            Invoke(view, "OnPresetBranchContainsPatternInputKeyDown", contains, new KeyEventArgs { Key = Key.Enter });
            exclude.Text = "external";
            Invoke(view, "CommitPresetBranchSuggestion", repo, exclude, "external");
            Invoke(view, "OnClearPresetBranchContainsPatterns", contains, new RoutedEventArgs());
            Invoke(view, "OnClearPresetBranchExcludeNames", exclude, new RoutedEventArgs());
            Check(string.IsNullOrEmpty(repo.PresetBranchContainsPatterns) && string.IsNullOrEmpty(repo.PresetBranchExcludeNames) && !draft.HasChanges,
                "Clearing contains and exclusion groups applies without needing the Apply button");
            window.Close();
            SourceGit.App.SetLocale("en_US");

            foreach (var name in new[] { "one.txt", "two.txt" })
                File.WriteAllText(Path.Combine(path, name), "after");
            var flow = repo.SubmoduleCommitFlow;
            Wait(flow.RefreshAsync());
            for (var i = 0; i < 100 && flow.Changes.Count != 2; i++)
                PumpFor(50);
            var one = flow.Changes.Single(c => c.Path == "one.txt");
            one.IsCommitFlowIncluded = false;
            flow.NotifyCommitIncludeChanged();
            flow.SelectedChanges = [one];
            Check(flow.EncodingSelectionSummary == "Highlighted files: 1" && flow.EncodingSelectionToolTip == "one.txt" &&
                flow.CommitIncludeSummary.Contains("1/2 checked"), "Highlighted encoding scope is separate from checked commit scope");
            var flowView = new SourceGit.Views.SubmoduleCommitFlow { DataContext = flow };
            window = new Window { Content = flowView, Width = 1280, Height = 800 };
            window.Show();
            foreach (var width in new[] { 1280, 920 })
            {
                window.Width = width;
                PumpFor(150);
                var toolbar = flowView.FindControl<Border>("EncodingToolbar");
                var diff = flowView.FindControl<ContentControl>("EncodingDiffHost");
                var button = flowView.FindControl<Button>("ConvertEncodingButton");
                Check(toolbar.Bounds.Bottom <= diff.Bounds.Top && diff.Bounds.Height > 100,
                    "Encoding toolbar has a separate row and cannot overlap diff at " + width);
                var position = button.TranslatePoint(default, flowView).Value;
                Check(position.X >= 0 && position.X + button.Bounds.Width <= flowView.Bounds.Width + 1,
                    "Encoding action stays inside the panel at " + width);
                CaptureUxFrame(window, "encoding-toolbar-" + width);
            }
            flow.SelectedChanges = flow.Changes.ToList();
            Check(flow.EncodingSelectionSummary == "Highlighted files: 2" && flow.DetectedEncodingSummary == "Per-file encoding",
                "Multi-selection does not misrepresent the first file encoding as all files");
            flow.SelectedChanges = [one];
            flow.SelectedSaveEncoding = "UTF-8 BOM";
            var otherBytes = File.ReadAllBytes(Path.Combine(path, "two.txt"));
            Wait(flow.SaveSelectedChangesWithEncodingAsync());
            Check(File.ReadAllBytes(Path.Combine(path, "one.txt")).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }) &&
                File.ReadAllBytes(Path.Combine(path, "two.txt")).SequenceEqual(otherBytes),
                "Encoding conversion rewrites only the highlighted file, not the checked file");
            window.Close();
        }
        finally
        {
            window?.Close();
            SourceGit.App.SetLocale("en_US");
            repo.Close();
        }
    }

    private static void CaptureUxFrame(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        frame.Save(Path.Combine(_root, name + ".png"));
    }
}
