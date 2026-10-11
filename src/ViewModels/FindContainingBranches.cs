using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class ContainingBranch : ObservableObject
    {
        public Models.Branch Branch { get; init; }
        public string Name => Branch.FriendlyName;
        public bool IsLocal => Branch.IsLocal;
        public string Kind => App.Text(IsLocal ? "FindContainingBranches.Local" : "FindContainingBranches.Remote");
        public IBrush Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        private IBrush _color;
    }

    public class FindContainingBranches : ObservableObject, IDisposable
    {
        public string SHA { get; }
        public string Subject { get; }
        public string RepositoryPath => _repo.FullPath;

        public IReadOnlyList<ContainingBranch> VisibleResults
        {
            get => _visibleResults;
            private set
            {
                SetProperty(ref _visibleResults, value);
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    FilterResults();
            }
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set
            {
                SetProperty(ref _isSearching, value);
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        public bool IsEmpty => !IsSearching && VisibleResults.Count == 0;

        public FindContainingBranches(Repository repo, Models.Commit commit)
        {
            _repo = repo;
            SHA = commit.SHA;
            Subject = commit.Subject;
        }

        public async Task SearchAsync()
        {
            if (_disposed || IsSearching)
                return;

            IsSearching = true;
            Status = App.Text("FindContainingBranches.Searching");
            _results.Clear();
            FilterResults();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _cancellation = cancellation;
            var token = cancellation.Token;
            try
            {
                var refs = await Task.Run(() => new Commands.QueryRefsContainsCommit(_repo.FullPath, SHA)
                {
                    CancellationToken = token,
                }.GetResultAsync(true));
                token.ThrowIfCancellationRequested();
                if (_disposed)
                    return;
                if (_repo.UIStates == null)
                {
                    Status = App.Text("FindContainingBranches.Unavailable");
                    return;
                }

                var knownBranches = _repo.Branches.Where(b => !string.IsNullOrEmpty(b.FullName)).ToDictionary(b => b.FullName, StringComparer.Ordinal);
                var processed = 0;
                foreach (var reference in refs)
                {
                    token.ThrowIfCancellationRequested();
                    if (_disposed)
                        return;
                    if (_repo.UIStates == null)
                    {
                        Status = App.Text("FindContainingBranches.Unavailable");
                        return;
                    }
                    if (reference.IsTag)
                        continue;

                    var isLocal = reference.Type == Models.DecoratorType.LocalBranchHead;
                    var fullName = (isLocal ? "refs/heads/" : "refs/remotes/") + reference.Name;
                    knownBranches.TryGetValue(fullName, out var branch);
                    if (branch == null)
                    {
                        var parts = reference.Name.Split('/', 2);
                        if (!isLocal && parts.Length != 2)
                            continue;
                        branch = new Models.Branch
                        {
                            IsLocal = isLocal,
                            FullName = fullName,
                            Name = isLocal ? reference.Name : parts[1],
                            Remote = isLocal ? string.Empty : parts[0],
                        };
                    }

                    _results.Add(new ContainingBranch
                    {
                        Branch = branch,
                        Color = new SolidColorBrush(Avalonia.Media.Color.FromUInt32(_repo.GetEffectiveBranchDisplayColor(branch))),
                    });
                    // Keep cancellation and window input responsive in repositories with many refs.
                    if (++processed % 8 == 0)
                        await Task.Delay(1, token);
                }
                _results = _results.OrderByDescending(branch => branch.IsLocal).ThenBy(branch => branch.Name, StringComparer.Ordinal).ToList();
                FilterResults();
                Status = App.Text(_results.Count == 0 ? "FindContainingBranches.None" : "FindContainingBranches.Found", _results.Count);
            }
            catch (OperationCanceledException)
            {
                if (!_disposed)
                {
                    _results.Clear();
                    FilterResults();
                    Status = App.Text(_cancelRequested ? "FindContainingBranches.Canceled" : "FindContainingBranches.TimedOut");
                }
            }
            catch (Exception e)
            {
                if (!_disposed)
                    Status = App.Text("FindContainingBranches.Failed", e.Message);
            }
            finally
            {
                _cancellation = null;
                _cancelRequested = false;
                IsSearching = false;
            }
        }

        public bool AddToFilter(ContainingBranch result)
        {
            if (_disposed || !_repo.AddBranchToPresetFilter(result.Branch))
            {
                Status = App.Text("FindContainingBranches.Unavailable");
                return false;
            }

            foreach (var branch in _results.Where(branch => branch.Branch.Name.Equals(result.Branch.Name, StringComparison.Ordinal)))
                branch.Color = new SolidColorBrush(Avalonia.Media.Color.FromUInt32(_repo.GetEffectiveBranchDisplayColor(branch.Branch)));
            Status = App.Text("FindContainingBranches.Added", result.Branch.Name);
            return true;
        }

        public void Cancel()
        {
            _cancelRequested = true;
            if (_cancellation != null)
                _ = _cancellation.CancelAsync();
        }

        public void Dispose()
        {
            _disposed = true;
            Cancel();
        }

        private void FilterResults()
        {
            var query = SearchText.Trim();
            VisibleResults = _results.Where(result => result.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private readonly Repository _repo;
        private List<ContainingBranch> _results = [];
        private IReadOnlyList<ContainingBranch> _visibleResults = Array.Empty<ContainingBranch>();
        private string _searchText = string.Empty;
        private string _status = string.Empty;
        private bool _isSearching = false;
        private bool _cancelRequested = false;
        private bool _disposed = false;
        private CancellationTokenSource _cancellation;
    }
}
