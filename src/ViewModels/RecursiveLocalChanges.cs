using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class RecursiveLocalChanges : ObservableObject
    {
        public class RepositoryEntry
        {
            public bool IsRoot { get; set; } = false;
            public string RepositoryPath { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ChangeCountText => $"{Changes.Count} change{(Changes.Count == 1 ? string.Empty : "s")}";
            public List<Models.Change> Changes { get; set; } = [];
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

        public AvaloniaList<RepositoryEntry> Repositories { get; } = [];

        public RecursiveLocalChanges(Repository repo)
        {
            _repo = repo;
            _summaryText = "Shows parent-repository changes and changed submodules recursively.";
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

                var summary = entries.Count == 0
                    ? "No local changes were found in the parent repository or initialized submodules."
                    : $"Showing local changes from {entries.Count} repositories recursively. Only submodules with changes are listed.";

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Repositories.Clear();
                    foreach (var entry in entries)
                        Repositories.Add(entry);

                    HasRepositories = entries.Count > 0;
                    ShowEmptyState = entries.Count == 0;
                    SummaryText = summary;
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

        private readonly Repository _repo;
        private string _summaryText = string.Empty;
        private bool _isLoading = false;
        private bool _hasRepositories = false;
        private bool _showEmptyState = false;
    }
}
