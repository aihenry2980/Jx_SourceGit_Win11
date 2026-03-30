using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CrossRepositoryCompare : ObservableObject
    {
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public string LeftRepoName
        {
            get => _leftRepoName;
            private set => SetProperty(ref _leftRepoName, value);
        }

        public string RightRepoName
        {
            get => _rightRepoName;
            private set => SetProperty(ref _rightRepoName, value);
        }

        public string LeftRepoPath
        {
            get => _leftRepoPath;
            private set => SetProperty(ref _leftRepoPath, value);
        }

        public string RightRepoPath
        {
            get => _rightRepoPath;
            private set => SetProperty(ref _rightRepoPath, value);
        }

        public string LeftBranchName
        {
            get => _leftBranchName;
            private set => SetProperty(ref _leftBranchName, value);
        }

        public string RightBranchName
        {
            get => _rightBranchName;
            private set => SetProperty(ref _rightBranchName, value);
        }

        public Models.Commit LeftHead
        {
            get => _leftHead;
            private set => SetProperty(ref _leftHead, value);
        }

        public Models.Commit RightHead
        {
            get => _rightHead;
            private set => SetProperty(ref _rightHead, value);
        }

        public int TotalChanges
        {
            get => _totalChanges;
            private set => SetProperty(ref _totalChanges, value);
        }

        public List<Models.Change> VisibleChanges
        {
            get => _visibleChanges;
            private set => SetProperty(ref _visibleChanges, value);
        }

        public List<Models.Change> SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetProperty(ref _selectedChanges, value))
                    RefreshSelectedChangeDetails();
            }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                    RefreshVisible();
            }
        }

        public bool HasSelectedChange => _selectedChange != null;

        public string SelectedChangePath => _selectedChange?.Path ?? string.Empty;

        public string SelectedChangeSummary => _selectedChangeSummary;

        public bool SelectedChangeIsSubmodule => _selectedChange?.IsSubmodulePointerChange == true;

        public string SelectedLeftValue => _selectedLeftValue;

        public string SelectedRightValue => _selectedRightValue;

        public string SelectedSubmoduleSHA => _selectedSubmoduleSHA;

        public CrossRepositoryCompare(
            Repository leftRepo,
            string leftRepoName,
            Repository rightRepo,
            string rightRepoName)
        {
            _leftRepo = leftRepo;
            _rightRepo = rightRepo;
            _leftRepoName = leftRepoName;
            _rightRepoName = rightRepoName;
            _leftRepoPath = leftRepo.FullPath;
            _rightRepoPath = rightRepo.FullPath;
            _leftBranchName = leftRepo.CurrentBranch?.FriendlyName ?? "HEAD";
            _rightBranchName = rightRepo.CurrentBranch?.FriendlyName ?? "HEAD";
            Refresh();
        }

        public void ClearSearchFilter()
        {
            SearchFilter = string.Empty;
        }

        public string GetLeftAbsPath(string path)
        {
            return Native.OS.GetAbsPath(_leftRepo.FullPath, path);
        }

        public string GetRightAbsPath(string path)
        {
            return Native.OS.GetAbsPath(_rightRepo.FullPath, path);
        }

        public void NavigateToLeft()
        {
            if (LeftHead != null)
                _leftRepo.NavigateToCommit(LeftHead.SHA);
        }

        public void NavigateToRight()
        {
            if (RightHead != null)
                _rightRepo.NavigateToCommit(RightHead.SHA);
        }

        private void Refresh()
        {
            IsLoading = true;
            VisibleChanges = [];
            SelectedChanges = [];

            Task.Run(async () =>
            {
                var leftHead = await new Commands.QuerySingleCommit(_leftRepo.FullPath, "HEAD").GetResultAsync().ConfigureAwait(false);
                var rightHead = await new Commands.QuerySingleCommit(_rightRepo.FullPath, "HEAD").GetResultAsync().ConfigureAwait(false);

                var changes = new List<Models.Change>();
                if (leftHead != null && rightHead != null)
                {
                    var leftEntries = await new Commands.QueryRevisionTreeEntries(_leftRepo.FullPath, leftHead.SHA).GetResultAsync().ConfigureAwait(false);
                    var rightEntries = await new Commands.QueryRevisionTreeEntries(_rightRepo.FullPath, rightHead.SHA).GetResultAsync().ConfigureAwait(false);
                    changes = BuildChanges(leftEntries, rightEntries);
                }

                var visible = FilterChanges(changes, _searchFilter);

                Dispatcher.UIThread.Post(() =>
                {
                    LeftHead = leftHead;
                    RightHead = rightHead;
                    _changes = changes;
                    TotalChanges = changes.Count;
                    VisibleChanges = visible;
                    IsLoading = false;
                    SelectedChanges = VisibleChanges.Count > 0 ? [VisibleChanges[0]] : [];
                });
            });
        }

        private void RefreshVisible()
        {
            if (_changes == null)
                return;

            VisibleChanges = FilterChanges(_changes, _searchFilter);
        }

        private void RefreshSelectedChangeDetails()
        {
            _selectedChange = _selectedChanges is { Count: > 0 } ? _selectedChanges[0] : null;
            OnPropertyChanged(nameof(HasSelectedChange));
            OnPropertyChanged(nameof(SelectedChangePath));
            OnPropertyChanged(nameof(SelectedChangeIsSubmodule));

            if (_selectedChange == null)
            {
                _selectedChangeSummary = string.Empty;
                _selectedLeftValue = string.Empty;
                _selectedRightValue = string.Empty;
                _selectedSubmoduleSHA = string.Empty;
            }
            else
            {
                _selectedChangeSummary = _selectedChange.Index switch
                {
                    Models.ChangeState.Added => $"Added in {RightRepoName}",
                    Models.ChangeState.Deleted => $"Only exists in {LeftRepoName}",
                    Models.ChangeState.TypeChanged => "Different object types between the two branch tips",
                    _ => "Content differs between the two branch tips",
                };

                if (_leftEntriesByPath.TryGetValue(_selectedChange.Path, out var left))
                    _selectedLeftValue = $"{left.Type} {ShortSha(left.SHA)}";
                else
                    _selectedLeftValue = "(missing)";

                if (_rightEntriesByPath.TryGetValue(_selectedChange.Path, out var right))
                    _selectedRightValue = $"{right.Type} {ShortSha(right.SHA)}";
                else
                    _selectedRightValue = "(missing)";

                if (_selectedChange.IsSubmodulePointerChange)
                {
                    var oldSha = _selectedChange.IndexSubmodulePointerOldSHA;
                    var newSha = _selectedChange.IndexSubmodulePointerNewSHA;
                    _selectedSubmoduleSHA = $"SHA {ShortSha(oldSha)} -> {ShortSha(newSha)}";
                }
                else
                {
                    _selectedSubmoduleSHA = string.Empty;
                }
            }

            OnPropertyChanged(nameof(SelectedChangeSummary));
            OnPropertyChanged(nameof(SelectedLeftValue));
            OnPropertyChanged(nameof(SelectedRightValue));
            OnPropertyChanged(nameof(SelectedSubmoduleSHA));
        }

        private static List<Models.Change> FilterChanges(List<Models.Change> changes, string filter)
        {
            if (changes == null)
                return [];

            if (string.IsNullOrWhiteSpace(filter))
                return changes;

            var visible = new List<Models.Change>();
            foreach (var change in changes)
            {
                if (change.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    visible.Add(change);
            }

            return visible;
        }

        private List<Models.Change> BuildChanges(List<Models.RevisionTreeEntry> leftEntries, List<Models.RevisionTreeEntry> rightEntries)
        {
            _leftEntriesByPath.Clear();
            _rightEntriesByPath.Clear();

            foreach (var entry in leftEntries)
                _leftEntriesByPath[entry.Path] = entry;

            foreach (var entry in rightEntries)
                _rightEntriesByPath[entry.Path] = entry;

            var paths = new HashSet<string>(_leftEntriesByPath.Keys, StringComparer.Ordinal);
            foreach (var path in _rightEntriesByPath.Keys)
                paths.Add(path);

            var changes = new List<Models.Change>();
            foreach (var path in paths)
            {
                _leftEntriesByPath.TryGetValue(path, out var left);
                _rightEntriesByPath.TryGetValue(path, out var right);

                if (left == null)
                {
                    var added = new Models.Change() { Path = path };
                    added.Set(Models.ChangeState.Added);
                    if (right.IsSubmodule)
                    {
                        added.IsSubmodulePointerChange = true;
                        added.IndexSubmodulePointerNewSHA = right.SHA;
                    }

                    changes.Add(added);
                    continue;
                }

                if (right == null)
                {
                    var deleted = new Models.Change() { Path = path };
                    deleted.Set(Models.ChangeState.Deleted);
                    if (left.IsSubmodule)
                    {
                        deleted.IsSubmodulePointerChange = true;
                        deleted.IndexSubmodulePointerOldSHA = left.SHA;
                    }

                    changes.Add(deleted);
                    continue;
                }

                if (left.Type != right.Type || left.Mode != right.Mode)
                {
                    var typeChanged = new Models.Change() { Path = path };
                    typeChanged.Set(Models.ChangeState.TypeChanged);
                    if (left.IsSubmodule || right.IsSubmodule)
                    {
                        typeChanged.IsSubmodulePointerChange = true;
                        typeChanged.IndexSubmodulePointerOldSHA = left.SHA;
                        typeChanged.IndexSubmodulePointerNewSHA = right.SHA;
                    }

                    changes.Add(typeChanged);
                    continue;
                }

                if (left.SHA == right.SHA)
                    continue;

                var modified = new Models.Change() { Path = path };
                modified.Set(Models.ChangeState.Modified);
                if (left.IsSubmodule || right.IsSubmodule)
                {
                    modified.IsSubmodulePointerChange = true;
                    modified.IndexSubmodulePointerOldSHA = left.SHA;
                    modified.IndexSubmodulePointerNewSHA = right.SHA;
                }

                changes.Add(modified);
            }

            changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));
            return changes;
        }

        private static string ShortSha(string sha)
        {
            if (string.IsNullOrEmpty(sha))
                return "(none)";

            return sha.Length > 10 ? sha.Substring(0, 10) : sha;
        }

        private readonly Repository _leftRepo;
        private readonly Repository _rightRepo;
        private readonly Dictionary<string, Models.RevisionTreeEntry> _leftEntriesByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Models.RevisionTreeEntry> _rightEntriesByPath = new(StringComparer.Ordinal);
        private bool _isLoading = true;
        private string _leftRepoName = string.Empty;
        private string _rightRepoName = string.Empty;
        private string _leftRepoPath = string.Empty;
        private string _rightRepoPath = string.Empty;
        private string _leftBranchName = string.Empty;
        private string _rightBranchName = string.Empty;
        private Models.Commit _leftHead = null;
        private Models.Commit _rightHead = null;
        private int _totalChanges = 0;
        private List<Models.Change> _changes = null;
        private List<Models.Change> _visibleChanges = null;
        private List<Models.Change> _selectedChanges = null;
        private string _searchFilter = string.Empty;
        private Models.Change _selectedChange = null;
        private string _selectedChangeSummary = string.Empty;
        private string _selectedLeftValue = string.Empty;
        private string _selectedRightValue = string.Empty;
        private string _selectedSubmoduleSHA = string.Empty;
    }
}
