using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class RecursiveLocalChanges : ObservableObject
    {
        public class RepositoryEntry : ObservableObject
        {
            public bool IsRoot { get; set; } = false;
            public string RepositoryPath { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ChangeCountText => $"{Changes.Count} change{(Changes.Count == 1 ? string.Empty : "s")}";
            public string ExpandToggleText => IsExpanded ? "Fold" : "Show";
            public bool CanToggleExpanded => Changes.Count > 0;

            public List<Models.Change> AllChanges { get; set; } = [];

            public List<Models.Change> Changes
            {
                get => _changes;
                set
                {
                    if (SetProperty(ref _changes, value))
                    {
                        OnPropertyChanged(nameof(ChangeCountText));
                        OnPropertyChanged(nameof(CanToggleExpanded));
                    }
                }
            }

            public bool IsExpanded
            {
                get => _isExpanded;
                set
                {
                    if (SetProperty(ref _isExpanded, value))
                        OnPropertyChanged(nameof(ExpandToggleText));
                }
            }

            private List<Models.Change> _changes = [];
            private bool _isExpanded = true;
        }

        public class HiddenExtensionTag
        {
            public string Extension { get; }
            public IBrush Background { get; }
            public IBrush BorderBrush { get; }
            public IBrush Foreground { get; }

            public HiddenExtensionTag(string extension)
            {
                Extension = extension;
                var palette = s_tagPalettes[Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(extension)) % s_tagPalettes.Length];
                Background = new SolidColorBrush(Color.Parse(palette.Background));
                BorderBrush = new SolidColorBrush(Color.Parse(palette.Border));
                Foreground = new SolidColorBrush(Color.Parse(palette.Foreground));
            }
        }

        public string Title => "Local Changes Recursively";

        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasRepositories
        {
            get => _hasRepositories;
            set => SetProperty(ref _hasRepositories, value);
        }

        public bool ShowEmptyState
        {
            get => _showEmptyState;
            set => SetProperty(ref _showEmptyState, value);
        }

        public string HiddenExtensionFilterText
        {
            get => _hiddenExtensionFilterText;
            set
            {
                if (SetProperty(ref _hiddenExtensionFilterText, value))
                {
                    ReloadActiveHiddenExtensions();
                    ApplyExtensionFilter();
                }
            }
        }

        public string HiddenExtensionInputText
        {
            get => _hiddenExtensionInputText;
            set => SetProperty(ref _hiddenExtensionInputText, value);
        }

        public bool HasActiveHiddenExtensions => ActiveHiddenExtensions.Count > 0;
        public bool HasRecentHiddenExtensions => RecentHiddenExtensions.Count > 0;

        public AvaloniaList<RepositoryEntry> Repositories { get; } = [];
        public AvaloniaList<HiddenExtensionTag> ActiveHiddenExtensions { get; } = [];
        public AvaloniaList<HiddenExtensionTag> RecentHiddenExtensions { get; } = [];

        public RecursiveLocalChanges(Repository repo)
        {
            _repo = repo;
            _summaryText = "Shows parent-repository changes and changed submodules recursively.";
            ReloadRecentHiddenExtensions();
            _hiddenExtensionFilterText = string.Join(' ', Preferences.Instance.GetRecursiveLocalChangesRecentHiddenExtensions());
            ReloadActiveHiddenExtensions();
        }

        public async Task RefreshAsync()
        {
            if (IsLoading)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                ShowEmptyState = false;
                SummaryText = "Scanning parent repository and submodules recursively...";
            });

            try
            {
                var entries = new List<RepositoryEntry>();
                await CollectRepoChangesAsync(_repo.FullPath, true, entries).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _allEntries.Clear();
                    _allEntries.AddRange(entries);
                    ApplyExtensionFilter();
                    IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                App.LogException(ex);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HasRepositories = Repositories.Count > 0;
                    ShowEmptyState = Repositories.Count == 0;
                    SummaryText = "Failed to load recursive local changes.";
                    IsLoading = false;
                });
            }
        }

        public void CommitHiddenExtensionFilterUsage()
        {
            var additions = ParseHiddenExtensions(_hiddenExtensionInputText);
            if (additions.Count == 0)
                return;

            var parsed = ParseHiddenExtensions(_hiddenExtensionFilterText);
            foreach (var extension in additions)
            {
                if (!parsed.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    parsed.Add(extension);
            }

            HiddenExtensionInputText = string.Empty;
            HiddenExtensionFilterText = string.Join(' ', parsed);
            RecordActiveHiddenExtensionFilterUsage(parsed);
        }

        public void AppendHiddenExtensionFilter(string extension)
        {
            var normalized = NormalizeExtension(extension);
            if (string.IsNullOrEmpty(normalized))
                return;

            var parsed = ParseHiddenExtensions(_hiddenExtensionFilterText);
            if (!parsed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                parsed.Add(normalized);

            HiddenExtensionFilterText = string.Join(' ', parsed);
            RecordActiveHiddenExtensionFilterUsage(parsed);
        }

        public void RemoveHiddenExtensionFilter(string extension)
        {
            var normalized = NormalizeExtension(extension);
            if (string.IsNullOrEmpty(normalized))
                return;

            var parsed = ParseHiddenExtensions(_hiddenExtensionFilterText);
            parsed.RemoveAll(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            HiddenExtensionFilterText = string.Join(' ', parsed);
        }

        public void ForgetRecentHiddenExtension(string extension)
        {
            RemoveHiddenExtensionFilter(extension);

            var normalized = NormalizeExtension(extension);
            if (string.IsNullOrEmpty(normalized))
                return;

            if (Preferences.Instance.RemoveRecursiveLocalChangesHiddenExtension(normalized))
                Preferences.Instance.Save();

            ReloadRecentHiddenExtensions();
        }

        public async Task RevertRepositoryChangesAsync(RepositoryEntry entry)
        {
            if (entry == null || entry.Changes.Count == 0)
                return;

            await RevertChangesAsync(entry.RepositoryPath, entry.DisplayName, entry.Changes).ConfigureAwait(false);
        }

        public async Task RevertSingleChangeAsync(RepositoryEntry entry, Models.Change change)
        {
            if (entry == null || change == null)
                return;

            await RevertChangesAsync(entry.RepositoryPath, entry.DisplayName, [change]).ConfigureAwait(false);
        }

        private async Task CollectRepoChangesAsync(string repoPath, bool isRoot, List<RepositoryEntry> entries)
        {
            var changes = await new Commands.QueryLocalChanges(repoPath, _repo.IncludeUntracked, true, true)
                .GetResultAsync()
                .ConfigureAwait(false);

            await MarkSubmodulePointerChangesAsync(repoPath, changes).ConfigureAwait(false);
            changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));

            if (changes.Count > 0)
            {
                var relative = Path.GetRelativePath(_repo.FullPath, repoPath).Replace('\\', '/');
                entries.Add(new RepositoryEntry()
                {
                    IsRoot = isRoot,
                    RepositoryPath = repoPath,
                    DisplayName = isRoot ? "Parent repository" : relative,
                    Description = repoPath,
                    AllChanges = changes,
                    Changes = changes,
                });
            }

            if (!File.Exists(Path.Combine(repoPath, ".gitmodules")))
                return;

            var submodules = await new Commands.QuerySubmodules(repoPath).GetResultAsync().ConfigureAwait(false);
            foreach (var submodule in submodules)
            {
                if (submodule.Status == Models.SubmoduleStatus.NotInited)
                    continue;

                var submodulePath = Path.Combine(repoPath, submodule.Path);
                if (!Directory.Exists(submodulePath))
                    continue;

                await CollectRepoChangesAsync(submodulePath, false, entries).ConfigureAwait(false);
            }
        }

        private async Task RevertChangesAsync(string repoPath, string displayName, List<Models.Change> changes)
        {
            if (changes.Count == 0)
                return;

            using var lockWatcher = _repo.LockWatcher();
            var log = _repo.CreateLog($"Revert Changes in '{displayName}'");

            try
            {
                await RevertSelectedChangesToHeadAsync(repoPath, changes, log).ConfigureAwait(false);
            }
            finally
            {
                log.Complete();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _repo.MarkWorkingCopyDirtyManually();
                _repo.MarkSubmodulesDirtyManually();
            });

            await RefreshAsync().ConfigureAwait(false);
        }

        private static async Task RevertSelectedChangesToHeadAsync(string repoPath, List<Models.Change> changes, Models.ICommandLog log)
        {
            var targetPaths = new HashSet<string>(StringComparer.Ordinal);
            var hasStagedChanges = false;

            foreach (var change in changes)
            {
                if (!string.IsNullOrWhiteSpace(change.Path))
                    targetPaths.Add(change.Path);

                if (!string.IsNullOrWhiteSpace(change.OriginalPath))
                    targetPaths.Add(change.OriginalPath);

                hasStagedChanges |= change.Index != Models.ChangeState.None;
            }

            if (targetPaths.Count == 0)
                return;

            if (hasStagedChanges)
            {
                var pathSpecFile = Path.GetTempFileName();
                try
                {
                    await File.WriteAllLinesAsync(pathSpecFile, targetPaths).ConfigureAwait(false);
                    await new Commands.Reset(repoPath, pathSpecFile)
                        .Use(log)
                        .ExecAsync()
                        .ConfigureAwait(false);
                }
                finally
                {
                    File.Delete(pathSpecFile);
                }
            }

            // Query again after unstaging. Staged additions/renames become normal
            // worktree changes, so the existing discard helper can safely clean them.
            var refreshed = await new Commands.QueryLocalChanges(repoPath, true, true, true)
                .GetResultAsync()
                .ConfigureAwait(false);

            var matched = refreshed
                .Where(change =>
                    targetPaths.Contains(change.Path) ||
                    (!string.IsNullOrWhiteSpace(change.OriginalPath) && targetPaths.Contains(change.OriginalPath)))
                .ToList();

            if (matched.Count > 0)
                await Commands.Discard.ChangesAsync(repoPath, matched, log).ConfigureAwait(false);
        }

        private static async Task MarkSubmodulePointerChangesAsync(string repoPath, List<Models.Change> changes)
        {
            if (changes.Count == 0)
                return;

            var paths = new List<string>();
            foreach (var change in changes)
            {
                if (!string.IsNullOrWhiteSpace(change.Path))
                    paths.Add(change.Path);
            }

            if (paths.Count == 0)
                return;

            var indexChanges = await new Commands.QuerySubmodulePointerChanges(repoPath, true, paths).GetResultAsync().ConfigureAwait(false);
            var workTreeChanges = await new Commands.QuerySubmodulePointerChanges(repoPath, false, paths).GetResultAsync().ConfigureAwait(false);

            foreach (var change in changes)
            {
                var hasIndex = indexChanges.TryGetValue(change.Path, out var index);
                var hasWorkTree = workTreeChanges.TryGetValue(change.Path, out var workTree);
                change.IsSubmodulePointerChange = hasIndex || hasWorkTree;

                if (hasIndex)
                {
                    change.IndexSubmodulePointerOldSHA = index.OldSHA;
                    change.IndexSubmodulePointerNewSHA = index.NewSHA;
                }

                if (hasWorkTree)
                {
                    change.WorkTreeSubmodulePointerOldSHA = workTree.OldSHA;
                    change.WorkTreeSubmodulePointerNewSHA = workTree.NewSHA;
                }
            }
        }

        private void ReloadRecentHiddenExtensions()
        {
            RecentHiddenExtensions.Clear();
            foreach (var ext in Preferences.Instance.GetRecursiveLocalChangesRecentHiddenExtensions())
                RecentHiddenExtensions.Add(new HiddenExtensionTag(ext));

            OnPropertyChanged(nameof(HasRecentHiddenExtensions));
        }

        private void ReloadActiveHiddenExtensions()
        {
            ActiveHiddenExtensions.Clear();
            foreach (var ext in ParseHiddenExtensions(_hiddenExtensionFilterText))
                ActiveHiddenExtensions.Add(new HiddenExtensionTag(ext));

            OnPropertyChanged(nameof(HasActiveHiddenExtensions));
        }

        private void RecordActiveHiddenExtensionFilterUsage(List<string> parsed)
        {
            if (parsed.Count == 0)
                return;

            if (Preferences.Instance.RecordRecursiveLocalChangesHiddenExtensions(parsed))
                Preferences.Instance.Save();

            ReloadRecentHiddenExtensions();
        }

        private void ApplyExtensionFilter()
        {
            var hidden = ParseHiddenExtensions(_hiddenExtensionFilterText);
            var hiddenSet = hidden.Count == 0
                ? null
                : new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);

            var visibleEntries = new List<RepositoryEntry>();
            foreach (var entry in _allEntries)
            {
                var visibleChanges = hiddenSet == null
                    ? entry.AllChanges
                    : entry.AllChanges.FindAll(change => !ShouldHideChange(change, hiddenSet));

                entry.Changes = visibleChanges;

                if (visibleChanges.Count == 0)
                    continue;

                visibleEntries.Add(entry);
            }

            Repositories.Clear();
            foreach (var entry in visibleEntries)
                Repositories.Add(entry);

            HasRepositories = visibleEntries.Count > 0;
            ShowEmptyState = visibleEntries.Count == 0;

            if (_allEntries.Count == 0)
            {
                SummaryText = "No local changes were found in the parent repository or initialized submodules.";
                return;
            }

            if (hidden.Count == 0)
            {
                SummaryText = $"Showing local changes from {visibleEntries.Count} repositories recursively. Only submodules with changes are listed.";
                return;
            }

            var hiddenText = string.Join(", ", hidden);
            SummaryText = visibleEntries.Count == 0
                ? $"No recursive local changes remain after hiding {hiddenText}."
                : $"Showing local changes from {visibleEntries.Count} repositories recursively after hiding {hiddenText}.";
        }

        private static bool ShouldHideChange(Models.Change change, HashSet<string> hiddenExtensions)
        {
            var extension = NormalizeExtension(Path.GetExtension(change.Path));
            return !string.IsNullOrEmpty(extension) && hiddenExtensions.Contains(extension);
        }

        private static List<string> ParseHiddenExtensions(string raw)
        {
            var outs = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return outs;

            var parts = raw.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var normalized = NormalizeExtension(part);
                if (!string.IsNullOrEmpty(normalized) && dedupe.Add(normalized))
                    outs.Add(normalized);
            }

            return outs;
        }

        private static string NormalizeExtension(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var ext = raw.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".", StringComparison.Ordinal))
                ext = "." + ext;

            return ext.Length > 1 ? ext : string.Empty;
        }

        private readonly Repository _repo;
        private readonly List<RepositoryEntry> _allEntries = [];
        private string _summaryText = string.Empty;
        private bool _isLoading = false;
        private bool _hasRepositories = false;
        private bool _showEmptyState = false;
        private string _hiddenExtensionFilterText = string.Empty;
        private string _hiddenExtensionInputText = string.Empty;

        private static readonly (string Background, string Border, string Foreground)[] s_tagPalettes =
        [
            ("#1A1D6FDD", "#661D6FDD", "#FF1D4ED8"),
            ("#1A2F855A", "#662F855A", "#FF276749"),
            ("#1AB7791F", "#66B7791F", "#FF8A5A12"),
            ("#1AB91C1C", "#66B91C1C", "#FFB91C1C"),
            ("#1A7C3AED", "#667C3AED", "#FF6D28D9"),
            ("#1A0891B2", "#660891B2", "#FF0E7490"),
            ("#1AC2410C", "#66C2410C", "#FF9A3412"),
            ("#1A0F766E", "#660F766E", "#FF0F766E"),
        ];
    }
}
