using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    public enum RebaseTestingState
    {
        Disabled = 0,
        Testing,
        WillCauseConflicts,
        UnknownError,
        NoConflicts,
    }

    public class Rebase : Popup
    {
        public Models.Branch Current
        {
            get;
            private set;
        }

        public object On
        {
            get;
            private set;
        }

        public bool AutoStash
        {
            get;
            set;
        }

        public bool UpdateSubmodulesRecursivelyAfterOperation
        {
            get;
            set;
        } = false;

        public bool NoVerify
        {
            get;
            set;
        }

        public RebaseTestingState TestingState
        {
            get => _testingState;
            private set => SetProperty(ref _testingState, value);
        }

        public override bool ShowOptions => _forcePushRemote == null;

        public Rebase(Repository repo, Models.Branch current, Models.Branch on, bool testForConflicts = true)
        {
            _repo = repo;
            _revision = on.Head;
            Current = current;
            On = on;
            AutoStash = true;

            if (testForConflicts)
                Test();
        }

        public Rebase(Repository repo, Models.Branch current, Models.Commit on, bool testForConflicts = true)
        {
            _repo = repo;
            _revision = on.SHA;
            Current = current;
            On = on;
            AutoStash = true;

            if (testForConflicts)
                Test();
        }

        public void ConfigureForcePushAfterSuccess(Models.Remote remote, Models.Branch remoteBranch)
        {
            _forcePushRemote = remote;
            _forcePushRemoteBranch = remoteBranch;
        }

        public static bool CanForcePushAfterRebase(Repository repo, Models.Branch source)
        {
            return TryResolveForcePushTarget(repo, source, out _, out _);
        }

        public static async Task StartForcePushAfterRebaseAsync(
            Repository repo,
            Models.Branch source,
            Models.Branch target)
        {
            if (!TryResolveForcePushTarget(repo, source, out var remote, out var remoteBranch))
            {
                repo?.SendNotification($"Branch `{source?.Name}` has no valid upstream for force push.", true);
                return;
            }

            var message =
                $"{App.Text("BranchCM.Rebase", source.Name, target.FriendlyName)}\n" +
                $"{App.Text("Push.Force")}: {source.Name} -> {remote.Name}/{remoteBranch.Name}\n\n" +
                "This rewrites remote history using --force-with-lease.";
            var confirmed = await App.AskConfirmAsync(message, Models.ConfirmButtonType.YesNo);
            if (!confirmed || !repo.CanCreatePopup())
                return;

            var operation = new Rebase(repo, source, target, false);
            operation.ConfigureForcePushAfterSuccess(remote, remoteBranch);
            await repo.ShowAndStartPopupAsync(operation);
        }

        public override async Task<bool> Sure()
        {
            var forcePushAfterSuccess = _forcePushRemote != null && _forcePushRemoteBranch != null;
            _repo.ClearCommitMessage();
            ProgressDescription = forcePushAfterSuccess ? "Rebasing before force push ..." : "Rebasing ...";

            var log = _repo.CreateLog(forcePushAfterSuccess ? "Rebase & Force Push" : "Rebase");
            Use(log);

            bool succ;
            using (var lockWatcher = _repo.LockWatcher())
            {
                succ = await new Commands.Rebase(_repo.FullPath, _revision, AutoStash, NoVerify)
                    .Use(log)
                    .ExecAsync();
            }

            if (succ && UpdateSubmodulesRecursivelyAfterOperation)
            {
                log.AppendLine("=== Update submodules recursively after rebase ===");
                succ = await _repo.RunUpdateSubmodulesRecursivelyAsync(log).ConfigureAwait(false);
            }

            var rebaseSucceeded = succ;
            if (succ && forcePushAfterSuccess)
            {
                ProgressDescription = $"Force pushing {Current.Name} -> {_forcePushRemote.Name}/{_forcePushRemoteBranch.Name} ...";
                log.AppendLine($"=== Force push `{Current.Name}` to `{_forcePushRemote.Name}/{_forcePushRemoteBranch.Name}` with lease ===");

                using var lockWatcher = _repo.LockWatcher();
                succ = await new Commands.Push(
                    _repo.FullPath,
                    Current.Name,
                    _forcePushRemote.Name,
                    _forcePushRemoteBranch.Name,
                    false,
                    _repo.Submodules.Count > 0,
                    false,
                    true).Use(log).RunAsync();
            }

            if (succ)
                _repo.RefreshSuperProjectSubmodulePointer();

            log.Complete();

            if (forcePushAfterSuccess)
            {
                _repo.MarkBranchesDirtyManually();
                if (succ)
                    _repo.SendNotification($"Rebased `{Current.Name}` and force-pushed it to `{_forcePushRemote.Name}/{_forcePushRemoteBranch.Name}`.");
                else if (rebaseSucceeded)
                    _repo.SendNotification($"Force push of `{Current.Name}` failed. Review the repository log for details.", true);
                else
                    _repo.SendNotification($"Rebase of `{Current.Name}` failed. Force push was skipped.", true);

                return true;
            }

            return succ;
        }

        private void Test()
        {
            if (Native.OS.GitVersion < Models.GitVersions.REPLAY)
                return;

            var head = Current.Head;
            TestingState = RebaseTestingState.Testing;
            Task.Run(async () =>
            {
                var mergeBase = await new Commands.MergeBase(_repo.FullPath, head, _revision)
                    .GetResultAsync()
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(mergeBase))
                {
                    Dispatcher.UIThread.Post(() => TestingState = RebaseTestingState.UnknownError);
                    return;
                }
                else if (head.Equals(mergeBase, StringComparison.Ordinal))
                {
                    Dispatcher.UIThread.Post(() => TestingState = RebaseTestingState.NoConflicts);
                    return;
                }

                var exitCode = await new Commands.Replay(_repo.FullPath, _revision, $"{mergeBase}..{head}")
                    .GetExitCodeAsync()
                    .ConfigureAwait(false);

                Dispatcher.UIThread.Post(() => TestingState = exitCode switch
                {
                    0 => RebaseTestingState.NoConflicts,
                    1 => RebaseTestingState.WillCauseConflicts,
                    _ => RebaseTestingState.UnknownError,
                });
            });
        }

        private static bool TryResolveForcePushTarget(
            Repository repo,
            Models.Branch source,
            out Models.Remote remote,
            out Models.Branch remoteBranch)
        {
            remote = null;
            remoteBranch = null;

            if (repo == null ||
                source is not { IsLocal: true, IsCurrent: true, IsDetachedHead: false, IsUpstreamGone: false } ||
                string.IsNullOrWhiteSpace(source.Upstream))
            {
                return false;
            }

            var resolvedRemoteBranch = repo.Branches.Find(x =>
                !x.IsLocal &&
                string.Equals(x.FullName, source.Upstream, StringComparison.Ordinal));
            if (resolvedRemoteBranch == null)
                return false;

            remoteBranch = resolvedRemoteBranch;
            var remoteName = resolvedRemoteBranch.Remote;
            remote = repo.Remotes.Find(x => string.Equals(x.Name, remoteName, StringComparison.Ordinal));
            return remote != null;
        }

        private readonly Repository _repo;
        private readonly string _revision;
        private RebaseTestingState _testingState = RebaseTestingState.Disabled;
        private Models.Remote _forcePushRemote = null;
        private Models.Branch _forcePushRemoteBranch = null;
    }
}
