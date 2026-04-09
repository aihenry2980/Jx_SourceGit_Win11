using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Histories : ObservableObject, IDisposable
    {
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsBackfilling
        {
            get => _isBackfilling;
            set => SetProperty(ref _isBackfilling, value);
        }

        public bool IsAuthorColumnVisible
        {
            get => _repo.UIStates.IsAuthorColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsAuthorColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsAuthorColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSHAColumnVisible
        {
            get => _repo.UIStates.IsSHAColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsSHAColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsSHAColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDateTimeColumnVisible
        {
            get => _repo.UIStates.IsDateTimeColumnVisibleInHistory;
            set
            {
                if (_repo.UIStates.IsDateTimeColumnVisibleInHistory != value)
                {
                    _repo.UIStates.IsDateTimeColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<Models.Commit> Commits
        {
            get => _commits;
            set
            {
                var lastSelected = SelectedCommit;
                if (SetProperty(ref _commits, value))
                {
                    UpdateQuickFindMatches(_repo?.HistoryQuickFindAppliedText ?? string.Empty);
                    if (value.Count > 0 && lastSelected != null)
                        SelectedCommit = value.Find(x => x.SHA.Equals(lastSelected.SHA, StringComparison.Ordinal));
                }
            }
        }

        public Models.CommitGraph Graph
        {
            get => _graph;
            set => SetProperty(ref _graph, value);
        }

        public Models.Commit SelectedCommit
        {
            get => _selectedCommit;
            set => SetProperty(ref _selectedCommit, value);
        }

        public long NavigationId
        {
            get => _navigationId;
            private set => SetProperty(ref _navigationId, value);
        }

        public IDisposable DetailContext
        {
            get => _detailContext;
            set => SetProperty(ref _detailContext, value);
        }

        public Models.Bisect Bisect
        {
            get => _bisect;
            private set => SetProperty(ref _bisect, value);
        }

        public GridLength LeftArea
        {
            get => _leftArea;
            set => SetProperty(ref _leftArea, value);
        }

        public GridLength RightArea
        {
            get => _rightArea;
            set => SetProperty(ref _rightArea, value);
        }

        public GridLength TopArea
        {
            get => _topArea;
            set => SetProperty(ref _topArea, value);
        }

        public GridLength BottomArea
        {
            get => _bottomArea;
            set => SetProperty(ref _bottomArea, value);
        }

        public string OriginRemoteURL
        {
            get
            {
                if (_repo?.Remotes is not { Count: > 0 } remotes)
                    return string.Empty;

                var origin = remotes.Find(x => x.Name.Equals("origin", StringComparison.Ordinal));
                if (origin != null && !string.IsNullOrWhiteSpace(origin.URL))
                    return origin.URL;

                if (!string.IsNullOrWhiteSpace(_repo.Settings?.DefaultRemote))
                {
                    var preferred = remotes.Find(x => x.Name.Equals(_repo.Settings.DefaultRemote, StringComparison.Ordinal));
                    if (preferred != null && !string.IsNullOrWhiteSpace(preferred.URL))
                        return preferred.URL;
                }

                return remotes[0]?.URL ?? string.Empty;
            }
        }

        public Histories(Repository repo)
        {
            _repo = repo;
            _commitDetailSharedData = new CommitDetailSharedData();
        }

        public void Dispose()
        {
            CancelPendingDetailLoad();
            Commits = [];
            _repo = null;
            _graph = null;
            _selectedCommit = null;
            _detailContext?.Dispose();
            _detailContext = null;
        }

        public Models.BisectState UpdateBisectInfo()
        {
            var test = Path.Combine(_repo.GitDir, "BISECT_START");
            if (!File.Exists(test))
            {
                Bisect = null;
                return Models.BisectState.None;
            }

            var info = new Models.Bisect();
            var dir = Path.Combine(_repo.GitDir, "refs", "bisect");
            if (Directory.Exists(dir))
            {
                var files = new DirectoryInfo(dir).GetFiles();
                foreach (var file in files)
                {
                    if (file.Name.StartsWith("bad"))
                        info.Bads.Add(File.ReadAllText(file.FullName).Trim());
                    else if (file.Name.StartsWith("good"))
                        info.Goods.Add(File.ReadAllText(file.FullName).Trim());
                }
            }

            Bisect = info;

            if (info.Bads.Count == 0 || info.Goods.Count == 0)
                return Models.BisectState.WaitingForRange;
            else
                return Models.BisectState.Detecting;
        }

        public void NavigateTo(string commitSHA)
        {
            var commit = _commits.Find(x => x.SHA.StartsWith(commitSHA, StringComparison.Ordinal));
            if (commit != null)
            {
                CancelPendingDetailLoad();
                SelectedCommit = commit;
                NavigationId = _navigationId + 1;
                QueueCommitDetailLoad(commit);
                return;
            }

            Task.Run(async () =>
            {
                var c = await new Commands.QuerySingleCommit(_repo.FullPath, commitSHA)
                    .GetResultAsync()
                    .ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    CancelPendingDetailLoad();
                    _ignoreSelectionChange = true;
                    SelectedCommit = null;

                    if (_detailContext is CommitDetail detail)
                    {
                        detail.Commit = c;
                    }
                    else
                    {
                        var commitDetail = new CommitDetail(_repo, _commitDetailSharedData);
                        commitDetail.Commit = c;
                        DetailContext = commitDetail;
                    }

                    _ignoreSelectionChange = false;
                });
            });
        }

        public void Select(IList commits)
        {
            if (_ignoreSelectionChange)
                return;

            if (commits.Count == 0)
            {
                CancelPendingDetailLoad();
                _repo.SearchCommitContext.Selected = null;
                DetailContext = null;
            }
            else if (commits.Count == 1)
            {
                var commit = (commits[0] as Models.Commit)!;
                if (_repo.SearchCommitContext.Selected == null || !_repo.SearchCommitContext.Selected.SHA.Equals(commit.SHA, StringComparison.Ordinal))
                    _repo.SearchCommitContext.Selected = _repo.SearchCommitContext.Results?.Find(x => x.SHA.Equals(commit.SHA, StringComparison.Ordinal));

                SelectedCommit = commit;
                NavigationId = _navigationId + 1;
                QueueCommitDetailLoad(commit);
            }
            else if (commits.Count == 2)
            {
                CancelPendingDetailLoad();
                _repo.SearchCommitContext.Selected = null;

                var end = commits[0] as Models.Commit;
                var start = commits[1] as Models.Commit;
                DetailContext = new RevisionCompare(_repo, start, end);
            }
            else
            {
                CancelPendingDetailLoad();
                _repo.SearchCommitContext.Selected = null;
                DetailContext = new Models.Count(commits.Count);
            }
        }

        public void OpenOriginRemoteURL()
        {
            if (!string.IsNullOrWhiteSpace(OriginRemoteURL))
                Native.OS.OpenBrowser(OriginRemoteURL);
        }

        public void ApplyQuickFind(string query)
        {
            if (_commits == null || _commits.Count == 0)
                return;

            if (!UpdateQuickFindMatches(query))
                return;

            Commits = [.. _commits];
        }

        public async Task<Models.Commit> GetCommitAsync(string sha)
        {
            return await new Commands.QuerySingleCommit(_repo.FullPath, sha)
                .GetResultAsync()
                .ConfigureAwait(false);
        }

        public async Task<bool> CheckoutBranchByDecoratorAsync(Models.Decorator decorator)
        {
            if (decorator == null)
                return false;

            if (decorator.Type == Models.DecoratorType.CurrentBranchHead ||
                decorator.Type == Models.DecoratorType.CurrentCommitHead)
                return true;

            if (decorator.Type == Models.DecoratorType.LocalBranchHead)
            {
                var b = _repo.Branches.Find(x => x.Name == decorator.Name);
                if (b == null)
                    return false;

                await _repo.CheckoutBranchAsync(b);
                return true;
            }

            if (decorator.Type == Models.DecoratorType.RemoteBranchHead)
            {
                var rb = _repo.Branches.Find(x => x.FriendlyName == decorator.Name);
                if (rb == null)
                    return false;

                var lb = _repo.Branches.Find(x => x.IsLocal && x.Upstream == rb.FullName);
                if (lb == null || lb.Ahead.Count > 0)
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new CreateBranch(_repo, rb));
                }
                else if (lb.Behind.Count > 0)
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new CheckoutAndFastForward(_repo, lb, rb));
                }
                else if (!lb.IsCurrent)
                {
                    await _repo.CheckoutBranchAsync(lb);
                }

                return true;
            }

            return false;
        }

        public async Task CheckoutBranchByCommitAsync(Models.Commit commit)
        {
            if (commit.IsCurrentHead)
                return;

            Models.Branch firstRemoteBranch = null;
            foreach (var d in commit.Decorators)
            {
                if (d.Type == Models.DecoratorType.LocalBranchHead)
                {
                    var b = _repo.Branches.Find(x => x.Name == d.Name);
                    if (b == null)
                        continue;

                    await _repo.CheckoutBranchAsync(b);
                    return;
                }

                if (d.Type == Models.DecoratorType.RemoteBranchHead)
                {
                    var rb = _repo.Branches.Find(x => x.FriendlyName == d.Name);
                    if (rb == null)
                        continue;

                    var lb = _repo.Branches.Find(x => x.IsLocal && x.Upstream == rb.FullName);
                    if (lb != null && lb.Behind.Count > 0 && lb.Ahead.Count == 0)
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new CheckoutAndFastForward(_repo, lb, rb));
                        return;
                    }

                    firstRemoteBranch ??= rb;
                }
            }

            if (_repo.CanCreatePopup())
            {
                if (firstRemoteBranch != null)
                    _repo.ShowPopup(new CreateBranch(_repo, firstRemoteBranch));
                else if (!_repo.IsBare)
                    _repo.ShowPopup(new CheckoutCommit(_repo, commit));
            }
        }

        public async Task CherryPickAsync(Models.Commit commit)
        {
            if (_repo.CanCreatePopup())
            {
                if (commit.Parents.Count <= 1)
                {
                    _repo.ShowPopup(new CherryPick(_repo, [commit]));
                }
                else
                {
                    var parents = new List<Models.Commit>();
                    foreach (var sha in commit.Parents)
                    {
                        var parent = _commits.Find(x => x.SHA.Equals(sha, StringComparison.Ordinal));
                        if (parent == null)
                            parent = await new Commands.QuerySingleCommit(_repo.FullPath, sha).GetResultAsync();

                        if (parent != null)
                            parents.Add(parent);
                    }

                    _repo.ShowPopup(new CherryPick(_repo, commit, parents));
                }
            }
        }

        public async Task RewordHeadAsync(Models.Commit head)
        {
            if (_repo.CanCreatePopup())
            {
                var message = await new Commands.QueryCommitFullMessage(_repo.FullPath, head.SHA).GetResultAsync();
                _repo.ShowPopup(new Reword(_repo, head, message));
            }
        }

        public async Task SquashOrFixupHeadAsync(Models.Commit head, bool fixup)
        {
            if (head.Parents.Count == 1)
            {
                var parent = await new Commands.QuerySingleCommit(_repo.FullPath, head.Parents[0]).GetResultAsync();
                if (parent == null)
                    return;

                string message = await new Commands.QueryCommitFullMessage(_repo.FullPath, head.Parents[0]).GetResultAsync();
                if (!fixup)
                {
                    var headMessage = await new Commands.QueryCommitFullMessage(_repo.FullPath, head.SHA).GetResultAsync();
                    message = $"{message}\n\n{headMessage}";
                }

                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new SquashOrFixupHead(_repo, parent, message, fixup));
            }
        }

        public async Task DropHeadAsync(Models.Commit head)
        {
            var parent = _commits.Find(x => x.SHA.Equals(head.Parents[0]));
            if (parent == null)
                parent = await new Commands.QuerySingleCommit(_repo.FullPath, head.Parents[0]).GetResultAsync();

            if (parent != null && _repo.CanCreatePopup())
                _repo.ShowPopup(new DropHead(_repo, head, parent));
        }

        public async Task InteractiveRebaseAsync(Models.Commit commit, Models.InteractiveRebaseAction act)
        {
            var prefill = new InteractiveRebasePrefill(commit.SHA, act);
            var start = act switch
            {
                Models.InteractiveRebaseAction.Squash or Models.InteractiveRebaseAction.Fixup => $"{commit.SHA}~~",
                _ => $"{commit.SHA}~",
            };

            var on = await new Commands.QuerySingleCommit(_repo.FullPath, start).GetResultAsync();
            if (on == null)
                _repo.SendNotification($"Can not squash current commit into parent!", true);
            else
                await App.ShowDialog(new InteractiveRebase(_repo, on, prefill));
        }

        public bool CanMergeSelectedCommitsToOne(IReadOnlyList<Models.Commit> selected)
        {
            return TryBuildMergeSelectedCommitsToOnePlan(selected, out _, out _);
        }

        public async Task MergeSelectedCommitsToOneAsync(IReadOnlyList<Models.Commit> selected)
        {
            if (!TryBuildMergeSelectedCommitsToOnePlan(selected, out var on, out var prefills))
            {
                App.RaiseException(_repo.FullPath, "Can not merge selected commits into one commit.");
                return;
            }

            await App.ShowDialog(new InteractiveRebase(_repo, on, prefills, prefills[^1].SHA));
        }

        public async Task<string> GetCommitFullMessageAsync(Models.Commit commit)
        {
            return await new Commands.QueryCommitFullMessage(_repo.FullPath, commit.SHA)
                .GetResultAsync()
                .ConfigureAwait(false);
        }

        public async Task<Models.Commit> CompareWithHeadAsync(Models.Commit commit)
        {
            var head = _commits.Find(x => x.IsCurrentHead);
            if (head == null)
            {
                _repo.SearchCommitContext.Selected = null;
                head = await new Commands.QuerySingleCommit(_repo.FullPath, "HEAD").GetResultAsync();
                if (head != null)
                    DetailContext = new RevisionCompare(_repo, commit, head);

                return null;
            }

            return head;
        }

        public void CompareWithWorktree(Models.Commit commit)
        {
            DetailContext = new RevisionCompare(_repo, commit, null);
        }

        private bool TryBuildMergeSelectedCommitsToOnePlan(IReadOnlyList<Models.Commit> selected, out Models.Commit on, out List<InteractiveRebasePrefill> prefills)
        {
            on = null;
            prefills = null;

            if (_repo?.CurrentBranch == null || selected == null || selected.Count < 2 || _commits.Count == 0)
                return false;

            var indexBySHA = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < _commits.Count; i++)
                indexBySHA[_commits[i].SHA] = i;

            var ordered = new List<Models.Commit>(selected.Count);
            foreach (var commit in selected)
            {
                if (commit == null || !commit.IsMerged || commit.Parents.Count != 1 || !indexBySHA.ContainsKey(commit.SHA))
                    return false;

                ordered.Add(commit);
            }

            ordered.Sort((l, r) => indexBySHA[l.SHA].CompareTo(indexBySHA[r.SHA]));

            for (var i = 0; i < ordered.Count - 1; i++)
            {
                if (!ordered[i].Parents[0].Equals(ordered[i + 1].SHA, StringComparison.Ordinal))
                    return false;
            }

            var target = ordered[^1];
            on = _commits.Find(x => x.SHA.Equals(target.Parents[0], StringComparison.Ordinal));
            if (on == null)
                return false;

            prefills = new List<InteractiveRebasePrefill>(ordered.Count);
            for (var i = 0; i < ordered.Count - 1; i++)
                prefills.Add(new InteractiveRebasePrefill(ordered[i].SHA, Models.InteractiveRebaseAction.Squash));

            prefills.Add(new InteractiveRebasePrefill(target.SHA, Models.InteractiveRebaseAction.Reword));
            return true;
        }

        private bool UpdateQuickFindMatches(string query)
        {
            var changed = false;
            foreach (var commit in _commits)
            {
                var matched = commit.MatchesHistoryQuickFind(query);
                if (commit.IsQuickFindMatched != matched)
                {
                    commit.IsQuickFindMatched = matched;
                    changed = true;
                }
            }

            return changed;
        }

        private void QueueCommitDetailLoad(Models.Commit commit)
        {
            if (commit == null)
                return;

            if (_detailContext is CommitDetail existing &&
                existing.Commit != null &&
                existing.Commit.SHA.Equals(commit.SHA, StringComparison.Ordinal))
            {
                return;
            }

            CancelPendingDetailLoad();
            var cts = new CancellationTokenSource();
            _detailLoadDebounce = cts;
            _ = LoadCommitDetailAsync(commit, cts);
        }

        private async Task LoadCommitDetailAsync(Models.Commit commit, CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested)
                    return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested ||
                        !ReferenceEquals(_detailLoadDebounce, cts) ||
                        _selectedCommit == null ||
                        !_selectedCommit.SHA.Equals(commit.SHA, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (_detailContext is CommitDetail detail)
                    {
                        detail.Commit = commit;
                    }
                    else
                    {
                        var commitDetail = new CommitDetail(_repo, _commitDetailSharedData);
                        commitDetail.Commit = commit;
                        DetailContext = commitDetail;
                    }
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                // Expected while quickly scanning history.
            }
            finally
            {
                if (ReferenceEquals(_detailLoadDebounce, cts))
                    _detailLoadDebounce = null;

                cts.Dispose();
            }
        }

        private void CancelPendingDetailLoad()
        {
            if (_detailLoadDebounce == null)
                return;

            _detailLoadDebounce.Cancel();
            _detailLoadDebounce.Dispose();
            _detailLoadDebounce = null;
        }

        private Repository _repo = null;
        private CommitDetailSharedData _commitDetailSharedData = null;
        private bool _isLoading = true;
        private bool _isBackfilling = false;
        private List<Models.Commit> _commits = new List<Models.Commit>();
        private Models.CommitGraph _graph = null;
        private Models.Commit _selectedCommit = null;
        private Models.Bisect _bisect = null;
        private long _navigationId = 0;
        private IDisposable _detailContext = null;
        private bool _ignoreSelectionChange = false;
        private CancellationTokenSource _detailLoadDebounce = null;

        private GridLength _leftArea = new GridLength(1, GridUnitType.Star);
        private GridLength _rightArea = new GridLength(1, GridUnitType.Star);
        private GridLength _topArea = new GridLength(1, GridUnitType.Star);
        private GridLength _bottomArea = new GridLength(1, GridUnitType.Star);
    }
}
