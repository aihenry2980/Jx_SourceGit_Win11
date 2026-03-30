using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
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
                    ApplyExtensionFilter();
            }
        }

        public bool HasRecentHiddenExtensions => RecentHiddenExtensions.Count > 0;

        public AvaloniaList<RepositoryEntry> Repositories { get; } = [];
        public AvaloniaList<string> RecentHiddenExtensions { get; } = [];

        public RecursiveLocalChanges(Repository repo)
        {
            _repo = repo;
            _summaryText = "Shows parent-repository changes and changed submodules recursively.";
            ReloadRecentHiddenExtensions();
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
            var parsed = ParseHiddenExtensions(_hiddenExtensionFilterText);
            if (parsed.Count == 0)
                return;

            if (Preferences.Instance.RecordRecursiveLocalChangesHiddenExtensions(parsed))
                Preferences.Instance.Save();

            ReloadRecentHiddenExtensions();
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
            CommitHiddenExtensionFilterUsage();
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
                RecentHiddenExtensions.Add(ext);

            OnPropertyChanged(nameof(HasRecentHiddenExtensions));
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
    }
}
