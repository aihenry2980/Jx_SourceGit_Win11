using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace SourceGit.ViewModels
{
    public class Pull : Popup
    {
        public List<Models.Remote> Remotes => _repo.Remotes;
        public Models.Branch Current { get; }
        public bool PreferQuickPath { get; set; } = false;
        public bool AllowQuickPathFallback { get; set; } = true;

        public bool HasSpecifiedRemoteBranch
        {
            get;
            private set;
        }

        public Models.Remote SelectedRemote
        {
            get => _selectedRemote;
            set
            {
                if (SetProperty(ref _selectedRemote, value))
                    PostRemoteSelected();
            }
        }

        public List<Models.Branch> RemoteBranches
        {
            get => _remoteBranches;
            private set => SetProperty(ref _remoteBranches, value);
        }

        [Required(ErrorMessage = "Remote branch to pull is required!!!")]
        public Models.Branch SelectedBranch
        {
            get => _selectedBranch;
            set => SetProperty(ref _selectedBranch, value, true);
        }

        public bool DiscardLocalChanges
        {
            get;
            set;
        } = false;

        public bool UseRebase
        {
            get => _useRebase;
            set
            {
                if (SetProperty(ref _useRebase, value))
                    _repo.UIStates.PreferRebaseInsteadOfMerge = value;
            }
        }

        public Pull(Repository repo, Models.Branch specifiedRemoteBranch, bool? initialUseRebase = null)
        {
            _repo = repo;
            Current = repo.CurrentBranch;
            _useRebase = initialUseRebase ?? _repo.UIStates.PreferRebaseInsteadOfMerge;

            if (specifiedRemoteBranch != null)
            {
                _selectedRemote = repo.Remotes.Find(x => x.Name == specifiedRemoteBranch.Remote);
                _selectedBranch = specifiedRemoteBranch;

                var branches = new List<Models.Branch>();
                foreach (var branch in _repo.Branches)
                {
                    if (branch.Remote == specifiedRemoteBranch.Remote)
                        branches.Add(branch);
                }

                _remoteBranches = branches;
                HasSpecifiedRemoteBranch = true;
            }
            else
            {
                Models.Remote autoSelectedRemote = null;
                if (!string.IsNullOrEmpty(Current.Upstream))
                {
                    var remoteNameEndIdx = Current.Upstream.IndexOf('/', 13);
                    if (remoteNameEndIdx > 0)
                    {
                        var remoteName = Current.Upstream.Substring(13, remoteNameEndIdx - 13);
                        autoSelectedRemote = _repo.Remotes.Find(x => x.Name == remoteName);
                    }
                }

                if (autoSelectedRemote == null)
                {
                    Models.Remote remote = null;
                    if (!string.IsNullOrEmpty(_repo.Settings.DefaultRemote))
                        remote = _repo.Remotes.Find(x => x.Name == _repo.Settings.DefaultRemote);
                    _selectedRemote = remote ?? _repo.Remotes[0];
                }
                else
                {
                    _selectedRemote = autoSelectedRemote;
                }

                PostRemoteSelected();
                HasSpecifiedRemoteBranch = false;
            }
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();

            var log = _repo.CreateLog("Pull");
            Use(log);
            var rs = await ExecuteAsync(log, true);
            log.Complete();

            if (_repo.SelectedViewIndex == 0)
            {
                var head = await new Commands.QueryRevisionByRefName(_repo.FullPath, "HEAD").GetResultAsync();
                _repo.NavigateToCommit(head, true);
            }

            return rs;
        }

        public async Task<bool> ExecuteAsync(Models.ICommandLog log, bool autoUpdateSubmodules, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var changes = await new Commands.CountLocalChanges(_repo.FullPath, false).GetResultAsync();
            var needPopStash = false;
            if (changes > 0)
            {
                if (DiscardLocalChanges)
                {
                    await Commands.Discard.AllAsync(_repo.FullPath, false, false, log);
                }
                else
                {
                    var succ = await new Commands.Stash(_repo.FullPath)
                    {
                        CancellationToken = cancellationToken,
                    }.Use(log).PushAsync("PULL_AUTO_STASH", false);
                    if (!succ)
                        return false;

                    needPopStash = true;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                return false;

            var branchName = _selectedBranch.Name;

            var rs = await RunPullWithAutoRevertAsync(branchName, changes, log, cancellationToken);
            if (!rs)
                return false;

            if (cancellationToken.IsCancellationRequested)
                return false;

            if (autoUpdateSubmodules)
                await _repo.AutoUpdateSubmodulesAsync(log);

            if (needPopStash)
            {
                var stash = new Commands.Stash(_repo.FullPath)
                {
                    CancellationToken = cancellationToken,
                }.Use(log);
                var popped = await stash.PopAsync("stash@{0}");
                if (!popped && !await TryAutoResolveStashPopConflictsAsync(stash, log))
                    return false;
            }

            return true;
        }

        private async Task<bool> RunPullWithAutoRevertAsync(string branchName, int localChangesCount, Models.ICommandLog log, CancellationToken cancellationToken)
        {
            if (PreferQuickPath)
            {
                var quickPulled = await TryRunQuickPullAsync(branchName, localChangesCount, log, cancellationToken);
                if (quickPulled.HasValue)
                    return quickPulled.Value;
            }

            var cmd = new Commands.Pull(
                _repo.FullPath,
                _selectedRemote.Name,
                branchName,
                UseRebase)
            {
                CancellationToken = cancellationToken,
            }.Use(log);
            var result = await cmd.RunWithResultAsync();
            if (result.IsSuccess)
                return true;

            if (await TryAutoRevertPullConflictedFilesAndRetryAsync(branchName, log, cancellationToken, result))
                return true;

            RaiseCommandFailure(result);
            return false;
        }

        private async Task<bool?> TryRunQuickPullAsync(
            string branchName,
            int localChangesCount,
            Models.ICommandLog log,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested ||
                localChangesCount != 0 ||
                UseRebase ||
                _repo?.CurrentBranch == null ||
                _selectedRemote == null ||
                _selectedBranch == null ||
                string.IsNullOrWhiteSpace(branchName) ||
                string.IsNullOrWhiteSpace(_repo.CurrentBranch.Upstream) ||
                !_repo.CurrentBranch.Upstream.Equals(_selectedBranch.FullName, System.StringComparison.Ordinal))
            {
                return null;
            }

            log?.AppendLine("Attempting quick pull path: fetch upstream and fast-forward only.");

            var refspec = $"refs/heads/{branchName}:refs/remotes/{_selectedRemote.Name}/{branchName}";
            var fetch = new Commands.Fetch(_repo.FullPath, _selectedRemote.Name, true, false, false, false, [refspec])
            {
                RaiseError = !AllowQuickPathFallback,
                CancellationToken = cancellationToken,
            }.Use(log);
            if (!await fetch.RunAsync())
            {
                if (!AllowQuickPathFallback)
                    return false;

                log?.AppendLine("[fallback] Quick pull fetch step failed. Falling back to normal pull.");
                log?.AppendLine(string.Empty);
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
                return false;

            var merge = new Commands.Merge(_repo.FullPath, _selectedBranch.FriendlyName, "--ff-only", false)
            {
                RaiseError = !AllowQuickPathFallback,
                CancellationToken = cancellationToken,
            }.Use(log);
            if (!await merge.ExecAsync())
            {
                if (!AllowQuickPathFallback)
                    return false;

                log?.AppendLine("[fallback] Fast-forward only merge was not possible. Falling back to normal pull.");
                log?.AppendLine(string.Empty);
                return null;
            }

            _repo.RefreshBranches();
            _repo.RefreshCommits(true);
            _repo.RefreshWorkingCopyChanges();
            log?.AppendLine("Quick pull path completed.");
            log?.AppendLine(string.Empty);
            return true;
        }

        private async Task<bool> TryAutoRevertPullConflictedFilesAndRetryAsync(
            string branchName,
            Models.ICommandLog log,
            CancellationToken cancellationToken,
            Commands.Command.Result failed)
        {
            var conflictedPaths = ExtractOverwrittenPaths(failed);
            if (conflictedPaths.Count == 0)
                return false;

            var changes = await new Commands.QueryLocalChanges(_repo.FullPath).GetResultAsync();
            var matched = changes.FindAll(change =>
                conflictedPaths.Contains(change.Path) &&
                Preferences.Instance.ShouldAutoRevertPullConflictFile(change.Path));
            if (matched.Count == 0)
                return false;

            log.AppendLine($"Auto-reverting {matched.Count} configured pull-conflict file(s) and retrying pull.");
            await Commands.Discard.ChangesAsync(_repo.FullPath, matched, log);

            var retry = new Commands.Pull(
                _repo.FullPath,
                _selectedRemote.Name,
                branchName,
                UseRebase)
            {
                CancellationToken = cancellationToken,
            }.Use(log);
            var retried = await retry.RunWithResultAsync();
            if (!retried.IsSuccess)
            {
                RaiseCommandFailure(retried);
                return false;
            }

            App.SendNotification(_repo.FullPath, $"Auto-reverted {matched.Count} configured pull-conflict file(s) and retried pull.");
            return true;
        }

        private async Task<bool> TryAutoResolveStashPopConflictsAsync(Commands.Stash stash, Models.ICommandLog log)
        {
            var changes = await new Commands.QueryLocalChanges(_repo.FullPath).GetResultAsync();
            var conflicted = changes.FindAll(change => change.IsConflicted);
            if (conflicted.Count == 0)
                return false;

            var matched = conflicted.FindAll(change => Preferences.Instance.ShouldAutoRevertPullConflictFile(change.Path));
            if (matched.Count == 0 || matched.Count != conflicted.Count)
                return false;

            log.AppendLine($"Auto-reverting {matched.Count} configured stash-pop conflict file(s) to keep the pulled version.");
            await Commands.Discard.RestoreToHeadAsync(_repo.FullPath, matched.Select(x => x.Path), log);
            await stash.DropAsync("stash@{0}");
            App.SendNotification(_repo.FullPath, $"Auto-reverted {matched.Count} configured stash-pop conflict file(s).");
            return true;
        }

        private static HashSet<string> ExtractOverwrittenPaths(Commands.Command.Result result)
        {
            var outs = new HashSet<string>(System.StringComparer.Ordinal);
            var lines = (result.StdErr + "\n" + result.StdOut).Replace("\r\n", "\n").Split('\n');
            var collecting = false;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    collecting = false;
                    continue;
                }

                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("Your local changes to the following files would be overwritten by ", System.StringComparison.Ordinal) ||
                    trimmed.StartsWith("The following untracked working tree files would be overwritten by ", System.StringComparison.Ordinal))
                {
                    collecting = true;
                    continue;
                }

                if (!collecting)
                    continue;

                if (raw.Length > 0 && char.IsWhiteSpace(raw[0]))
                {
                    outs.Add(trimmed);
                    continue;
                }

                collecting = false;
            }

            return outs;
        }

        private void RaiseCommandFailure(Commands.Command.Result result)
        {
            var message = (result.StdErr + "\n" + result.StdOut).Trim();
            App.RaiseException(_repo.FullPath, string.IsNullOrEmpty(message) ? "Git pull failed." : message);
        }

        private void PostRemoteSelected()
        {
            var remoteName = _selectedRemote.Name;
            var branches = new List<Models.Branch>();
            foreach (var branch in _repo.Branches)
            {
                if (branch.Remote == remoteName)
                    branches.Add(branch);
            }

            RemoteBranches = branches;

            var autoSelectedBranch = false;
            if (!string.IsNullOrEmpty(Current.Upstream) &&
                Current.Upstream.StartsWith($"refs/remotes/{remoteName}/", System.StringComparison.Ordinal))
            {
                foreach (var branch in branches)
                {
                    if (Current.Upstream == branch.FullName)
                    {
                        SelectedBranch = branch;
                        autoSelectedBranch = true;
                        break;
                    }
                }
            }

            if (!autoSelectedBranch)
            {
                foreach (var branch in branches)
                {
                    if (Current.Name == branch.Name)
                    {
                        SelectedBranch = branch;
                        autoSelectedBranch = true;
                        break;
                    }
                }
            }

            if (!autoSelectedBranch)
                SelectedBranch = null;
        }

        private readonly Repository _repo = null;
        private Models.Remote _selectedRemote = null;
        private List<Models.Branch> _remoteBranches = null;
        private Models.Branch _selectedBranch = null;
        private bool _useRebase = false;
    }
}
