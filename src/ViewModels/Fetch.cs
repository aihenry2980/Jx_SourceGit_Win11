using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class Fetch : Popup
    {
        public List<Models.Remote> Remotes
        {
            get => _repo.Remotes;
        }

        public bool IsFetchAllRemoteVisible
        {
            get;
        }

        public bool FetchAllRemotes
        {
            get => _fetchAllRemotes;
            set
            {
                if (SetProperty(ref _fetchAllRemotes, value) && IsFetchAllRemoteVisible)
                    _repo.UIStates.FetchAllRemotes = value;
            }
        }

        public Models.Remote SelectedRemote
        {
            get;
            set;
        }

        public bool NoTags
        {
            get => _repo.UIStates.FetchWithoutTags;
            set => _repo.UIStates.FetchWithoutTags = value;
        }

        public bool Force
        {
            get => _repo.UIStates.EnableForceOnFetch;
            set => _repo.UIStates.EnableForceOnFetch = value;
        }

        public Fetch(Repository repo, Models.Remote preferredRemote = null)
        {
            _repo = repo;
            _repo.UIStates.FetchWithoutTags = true;
            IsFetchAllRemoteVisible = repo.Remotes.Count > 1 && preferredRemote == null;
            _fetchAllRemotes = IsFetchAllRemoteVisible && _repo.UIStates.FetchAllRemotes;
            CanTerminate = true;

            if (preferredRemote != null)
            {
                SelectedRemote = preferredRemote;
            }
            else if (!string.IsNullOrEmpty(_repo.Settings.DefaultRemote))
            {
                var def = _repo.Remotes.Find(r => r.Name == _repo.Settings.DefaultRemote);
                SelectedRemote = def ?? _repo.Remotes[0];
            }
            else
            {
                SelectedRemote = _repo.Remotes[0];
            }
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();

            var navigateToUpstreamHEAD = _repo.IsHistoriesVisible &&
                _repo.Histories.SelectedCommits.Count == 1 &&
                _repo.Histories.SelectedCommits[0].IsCurrentHead;

            var notags = _repo.UIStates.FetchWithoutTags;
            var force = _repo.UIStates.EnableForceOnFetch;
            var log = _repo.CreateLog("Fetch");
            Use(log);
            var gitStopwatch = Stopwatch.StartNew();

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            log.SetCancelAction(Terminate);
            var succeeded = true;

            if (FetchAllRemotes)
            {
                foreach (var remote in _repo.Remotes)
                {
                    succeeded &= await new Commands.Fetch(_repo.FullPath, remote.Name, notags, force)
                        .WithCancellation(token)
                        .Use(log)
                        .RunAsync();

                    if (token.IsCancellationRequested || !succeeded)
                        break;
                }
            }
            else
            {
                succeeded = await new Commands.Fetch(_repo.FullPath, SelectedRemote.Name, notags, force)
                    .WithCancellation(token)
                    .Use(log)
                    .RunAsync();
            }

            gitStopwatch.Stop();
            log.Complete(succeeded && !token.IsCancellationRequested);

            if (navigateToUpstreamHEAD && !token.IsCancellationRequested)
            {
                var upstream = _repo.CurrentBranch?.Upstream;
                if (!string.IsNullOrEmpty(upstream))
                {
                    var upstreamHead = await new Commands.QueryRevisionByRefName(_repo.FullPath, upstream.Substring(13)).GetResultAsync();
                    _repo.NavigateToCommit(upstreamHead, true);
                }
            }

            if (!token.IsCancellationRequested)
            {
                var refreshDuration = await _repo.MarkFetchedAndMeasureRefreshAsync();
                _repo.ShowFetchDurationToast(gitStopwatch.Elapsed, refreshDuration);
            }

            _cancellation = null;
            return true;
        }

        public override void Terminate()
        {
            // Just fire cancel event and UI will auto wait the `Sure` complete
            var _ = _cancellation?.CancelAsync();
        }

        private readonly Repository _repo = null;
        private bool _fetchAllRemotes = false;
        private CancellationTokenSource _cancellation = null;
    }
}
