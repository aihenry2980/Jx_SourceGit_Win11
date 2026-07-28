using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Histories : ObservableObject
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
            get => _repo?.UIStates?.IsAuthorColumnVisibleInHistory ?? true;
            set
            {
                if (_repo?.UIStates is { } states && states.IsAuthorColumnVisibleInHistory != value)
                {
                    states.IsAuthorColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSHAColumnVisible
        {
            get => _repo?.UIStates?.IsSHAColumnVisibleInHistory ?? true;
            set
            {
                if (_repo?.UIStates is { } states && states.IsSHAColumnVisibleInHistory != value)
                {
                    states.IsSHAColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDateTimeColumnVisible
        {
            get => IsCommitTimeColumnVisible;
            set => IsCommitTimeColumnVisible = value;
        }

        public bool IsAuthorTimeColumnVisible
        {
            get => _repo?.UIStates?.IsAuthorTimeColumnVisibleInHistory ?? false;
            set
            {
                if (_repo?.UIStates is { } states && states.IsAuthorTimeColumnVisibleInHistory != value)
                {
                    states.IsAuthorTimeColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCommitTimeColumnVisible
        {
            get => _repo?.UIStates?.IsCommitTimeColumnVisibleInHistory ?? true;
            set
            {
                if (_repo?.UIStates is { } states && states.IsCommitTimeColumnVisibleInHistory != value)
                {
                    states.IsCommitTimeColumnVisibleInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<Models.Commit> Commits
        {
            get => _commits;
            set => SetCommits(value, true);
        }

        public void ApplySnapshot(List<Models.Commit> commits, Models.CommitGraph graph)
        {
            Graph = graph;
            SetCommits(commits, false);
        }

        public Models.CommitGraph Graph
        {
            get => _graph;
            set => SetProperty(ref _graph, value);
        }

        public Models.CommitGraphHighlighting GraphHighlighting
        {
            get => _repo?.UIStates?.GraphHighlighting ?? Models.CommitGraphHighlighting.All;
            set
            {
                if (_repo?.UIStates is { } states && states.GraphHighlighting != value)
                {
                    states.GraphHighlighting = value;
                    OnPropertyChanged(nameof(HighlightCurrentBranchOnly));
                    GenerateGraph(_commits);
                }
            }
        }

        public bool HighlightCurrentBranchOnly
        {
            get => GraphHighlighting == Models.CommitGraphHighlighting.CurrentBranchOnly;
            set => GraphHighlighting = value ? Models.CommitGraphHighlighting.CurrentBranchOnly : Models.CommitGraphHighlighting.All;
        }

        public List<Models.Commit> SelectedCommits
        {
            get => _selectedCommits;
            set
            {
                var oldCount = _selectedCommits.Count;
                if (SetProperty(ref _selectedCommits, value) && oldCount + value.Count > 0)
                    PostSelectedCommitsChanged();
            }
        }

        public long NavigationId
        {
            get => _navigationId;
            private set => SetProperty(ref _navigationId, value);
        }

        public object DetailContext
        {
            get => _detailContext;
            set
            {
                if (SetProperty(ref _detailContext, value))
                    OnPropertyChanged(nameof(IsOpenAsStandaloneVisible));
            }
        }

        public Models.Bisect Bisect
        {
            get => _bisect;
            private set => SetProperty(ref _bisect, value);
        }

        public Models.Branch CurrentBranch
        {
            get => _repo.CurrentBranch;
        }

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get => _repo.IssueTrackers;
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
            get => _isCollapseDetails ? new GridLength(28, GridUnitType.Pixel) : _bottomArea;
            set
            {
                if (!Preferences.Instance.UseTwoColumnsLayoutInHistories && !_isCollapseDetails)
                    SetProperty(ref _bottomArea, value);
            }
        }

        public double AuthorColumnWidth
        {
            get => _repo?.UIStates?.AuthorColumnWidth ?? 240;
            set
            {
                if (_repo?.UIStates is { } states)
                    states.AuthorColumnWidth = value;
            }
        }

        public bool IsOpenAsStandaloneVisible
        {
            get => DetailContext is CommitDetail or RevisionCompare;
        }

        public bool IsCollapseDetails
        {
            get => _isCollapseDetails;
            set
            {
                if (!Preferences.Instance.UseTwoColumnsLayoutInHistories && SetProperty(ref _isCollapseDetails, value))
                {
                    OnPropertyChanged(nameof(TopArea));
                    OnPropertyChanged(nameof(BottomArea));
                }
            }
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
            _isDisposed = true;
            CancelPendingDetailLoad();
            _commits = [];
            _graph = null;
            _selectedCommits = [];
            if (_detailContext is CommitDetail commitDetail)
                commitDetail.Dispose();
            _detailContext = null;
            _repo = null;
        }

        public void NotifyCurrentBranchChanged()
        {
            OnPropertyChanged(nameof(CurrentBranch));
        }

        public async Task<Models.BisectState> UpdateBisectInfoAsync()
        {
            var repo = _repo;
            if (repo == null)
                return Models.BisectState.None;

            var test = Path.Combine(repo.GitDir, "BISECT_START");
            if (!File.Exists(test))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_repo == repo)
                        Bisect = null;
                });
                return Models.BisectState.None;
            }

            var head = await new Commands.QueryRevisionByRefName(repo.FullPath, "HEAD").GetResultAsync();
            var info = new Models.Bisect();
            var markedHead = false;
            var dir = Path.Combine(repo.GitDir, "refs", "bisect");
            if (Directory.Exists(dir))
            {
                var files = new DirectoryInfo(dir).GetFiles();
                foreach (var file in files)
                {
                    var sha = File.ReadAllText(file.FullName).Trim();
                    if (!markedHead)
                        markedHead = head?.Equals(sha, StringComparison.Ordinal) == true;

                    if (file.Name.StartsWith("bad"))
                        info.Bads.Add(sha);
                    else if (file.Name.StartsWith("good"))
                        info.Goods.Add(sha);
                    else if (file.Name.StartsWith("skip"))
                        info.Skipped.Add(sha);
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_repo == repo)
                    Bisect = info;
            });

            if (info.Bads.Count == 0)
                return Models.BisectState.WaitingForFirstBad;

            if (markedHead)
                return Models.BisectState.WaitingForCheckoutAnother;

            if (info.Goods.Count == 0)
                return Models.BisectState.WaitingForFirstGood;

            return Models.BisectState.WaitingForMark;
        }

        public void NavigateTo(string commitSHA)
        {
            var commit = _commits.Find(x => x.SHA.StartsWith(commitSHA, StringComparison.Ordinal));
            if (commit != null)
            {
                SelectedCommits = [commit];
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
                    SelectedCommits = [];

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

        public void OpenOriginRemoteURL()
        {
            if (!string.IsNullOrWhiteSpace(OriginRemoteURL))
                Native.OS.OpenBrowser(OriginRemoteURL);
        }

        public void ApplyQuickFind(string query)
        {
            if (_commits == null || _commits.Count == 0)
                return;

            UpdateQuickFindMatches(query);
            if (string.IsNullOrWhiteSpace(query))
                return;

            foreach (var commit in _commits)
            {
                if (commit.IsQuickFindMatched)
                {
                    SelectedCommits = [commit];
                    return;
                }
            }
        }

        public bool NavigateQuickFind(bool forward)
        {
            if (_commits == null || _commits.Count == 0)
                return false;

            var selectedIndex = -1;
            if (_selectedCommits.Count == 1)
            {
                var selectedSHA = _selectedCommits[0].SHA;
                selectedIndex = _commits.FindIndex(x => x.SHA.Equals(selectedSHA, StringComparison.Ordinal));
            }

            var count = _commits.Count;
            var startIndex = selectedIndex >= 0 ? selectedIndex : (forward ? -1 : 0);
            for (var offset = 1; offset <= count; offset++)
            {
                var index = forward ?
                    (startIndex + offset) % count :
                    (startIndex - offset + count * 2) % count;
                var commit = _commits[index];
                if (!commit.IsQuickFindMatched)
                    continue;

                SelectedCommits = [commit];
                return true;
            }

            return false;
        }

        public async Task<Models.Commit> GetCommitAsync(string sha)
        {
            return await new Commands.QuerySingleCommit(_repo.FullPath, sha)
                .GetResultAsync()
                .ConfigureAwait(false);
        }

        public void CheckoutCommitDetached(Models.Commit c)
        {
            if (!c.IsCurrentHead && _repo.CanCreatePopup())
                _repo.ShowPopup(new CheckoutDetached(_repo, c));
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
                    _repo.ShowPopup(new CheckoutDetached(_repo, commit));
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

        private void PostCommitsChanged()
        {
            if (_selectedCommits.Count == 0)
                return;

            if (_commits.Count == 0 || _selectedCommits.Count > 20)
            {
                SelectedCommits = [];
                return;
            }

            var set = new HashSet<string>();
            foreach (var c in _selectedCommits)
                set.Add(c.SHA);

            var selected = new List<Models.Commit>();
            foreach (var c in _commits)
            {
                if (set.Contains(c.SHA))
                {
                    selected.Add(c);
                    set.Remove(c.SHA);
                    if (set.Count == 0)
                        break;
                }
            }

            SelectedCommits = selected;
        }

        private void PostSelectedCommitsChanged()
        {
            if (_ignoreSelectionChange || _isDisposed || _repo == null)
                return;

            if (_selectedCommits.Count == 0)
            {
                CancelPendingDetailLoad();
                _repo.SearchCommitContext.Selected = null;
                DetailContext = new Models.Null();
            }
            else if (_selectedCommits.Count == 1)
            {
                var c = _selectedCommits[0];
                if (_repo.SearchCommitContext.Selected == null || !_repo.SearchCommitContext.Selected.SHA.Equals(c.SHA, StringComparison.Ordinal))
                    _repo.SearchCommitContext.Selected = _repo.SearchCommitContext.Results?.Find(x => x.SHA.Equals(c.SHA, StringComparison.Ordinal));

                NavigationId++;
                QueueCommitDetailLoad(c);
            }
            else if (_selectedCommits.Count == 2)
            {
                CancelPendingDetailLoad();
                _repo.SearchCommitContext.Selected = null;

                if (_detailContext is RevisionCompare compare)
                    compare.SetTargets(_selectedCommits[1], _selectedCommits[0]);
                else
                    DetailContext = new RevisionCompare(_repo, _selectedCommits[1], _selectedCommits[0]);
            }
            else
            {
                CancelPendingDetailLoad();
                _repo.SearchCommitContext.Selected = null;
                DetailContext = new Models.Count(_selectedCommits.Count);
            }

            if (_repo.UIStates?.GraphHighlighting >= Models.CommitGraphHighlighting.SelectedCommitsOnly)
                GenerateGraph(_commits);
        }

        private void GenerateGraph(List<Models.Commit> commits, bool commitsChanged = false)
        {
            var states = _repo?.UIStates;
            if (_isDisposed || states == null)
                return;

            var firstParentOnly = states.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly);
            var highlighting = states.GraphHighlighting;
            var extraHeads = new HashSet<string>();

            if (highlighting >= Models.CommitGraphHighlighting.SelectedCommitsOnly)
            {
                foreach (var c in _selectedCommits)
                    extraHeads.Add(c.SHA);
            }

            Graph = Models.CommitGraph.Generate(commits, commitsChanged, firstParentOnly, highlighting, extraHeads);
        }

        private void SetCommits(List<Models.Commit> commits, bool generateGraph)
        {
            if (_isDisposed || _repo?.UIStates == null)
            {
                SetProperty(ref _commits, commits, nameof(Commits));
                return;
            }

            if (generateGraph)
                GenerateGraph(commits, true);

            if (SetProperty(ref _commits, commits, nameof(Commits)))
            {
                UpdateQuickFindMatches(_repo?.HistoryQuickFindAppliedText ?? string.Empty);
                PostCommitsChanged();
            }
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
                        _isDisposed ||
                        _repo == null ||
                        !ReferenceEquals(_detailLoadDebounce, cts) ||
                        _selectedCommits.Count != 1 ||
                        !_selectedCommits[0].SHA.Equals(commit.SHA, StringComparison.Ordinal))
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
        private List<Models.Commit> _selectedCommits = [];
        private Models.Bisect _bisect = null;
        private long _navigationId = 0;
        private object _detailContext = new Models.Null();
        private bool _ignoreSelectionChange = false;
        private CancellationTokenSource _detailLoadDebounce = null;
        private bool _isDisposed = false;

        private GridLength _leftArea = new(1, GridUnitType.Star);
        private GridLength _rightArea = new(1, GridUnitType.Star);
        private GridLength _topArea = new(1, GridUnitType.Star);
        private GridLength _bottomArea = new(1, GridUnitType.Star);
        private bool _isCollapseDetails = false;
    }
}
