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

        public void ConfigureForcePushAfterSuccess(
            Models.Remote remote,
            Models.Branch remoteBranch,
            bool setUpstream = false)
        {
            _forcePushRemote = remote;
            _forcePushRemoteBranch = remoteBranch;
            _setUpstreamAfterForcePush = setUpstream;
        }

        public static bool CanForcePushAfterRebase(Repository repo, Models.Branch source)
        {
            return TryResolveForcePushTarget(repo, source, true, out _, out _, out _);
        }

        public static bool CanCheckoutRebaseAndForcePush(
            Repository repo,
            Models.Branch source,
            Models.Branch target)
        {
            return GetCheckoutRebaseAndForcePushDisabledReason(repo, source, target) == null;
        }

        public static string GetCheckoutRebaseAndForcePushDisabledReason(
            Repository repo,
            Models.Branch source,
            Models.Branch target)
        {
            if (repo == null || repo.IsBare)
                return "This operation requires a non-bare repository";
            if (source is not { IsLocal: true, IsDetachedHead: false })
                return "Select a local branch";
            if (target == null)
                return "Configure a valid Rebase Base Branch";
            if (source.FullName.Equals(target.FullName, StringComparison.Ordinal))
                return "The source branch is already the Rebase Base Branch";
            if (source.HasWorktree)
                return "This branch is checked out in another worktree";
            if (!source.IsCurrent && repo.LocalChangesCount > 0)
                return "A clean working copy is required before checking out another branch";
            if (!TryResolveForcePushTarget(repo, source, false, out _, out _, out _))
                return "Set an upstream branch, configure Default Remote, or use a repository with one remote";

            return null;
        }

        public static async Task StartForcePushAfterRebaseAsync(
            Repository repo,
            Models.Branch source,
            Models.Branch target)
        {
            if (!TryResolveForcePushTarget(repo, source, true, out var remote, out var remoteBranch, out var setUpstream))
            {
                repo?.SendNotification($"Branch `{source?.Name}` has no valid force-push destination.", true);
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
            operation.ConfigureForcePushAfterSuccess(remote, remoteBranch, setUpstream);
            await repo.ShowAndStartPopupAsync(operation);
        }

        public static async Task StartCheckoutRebaseAndForcePushAsync(
            Repository repo,
            Models.Branch source,
            Models.Branch target)
        {
            if (!CanCheckoutRebaseAndForcePush(repo, source, target) ||
                !TryResolveForcePushTarget(repo, source, false, out var remote, out var remoteBranch, out var setUpstream))
            {
                repo?.SendNotification($"Branch `{source?.Name}` cannot be checked out, rebased, and force-pushed.", true);
                return;
            }

            var checkoutStep = source.IsCurrent ? string.Empty : $"Checkout: {source.Name}\n";
            var message =
                checkoutStep +
                $"Rebase: {source.Name} onto {target.FriendlyName}\n" +
                $"Force push: {source.Name} -> {remote.Name}/{remoteBranch.Name}\n\n" +
                "This rewrites remote history using --force-with-lease.";
            var confirmed = await App.AskConfirmAsync(message, Models.ConfirmButtonType.YesNo);
            if (!confirmed || !repo.CanCreatePopup())
                return;

            var operation = new Rebase(repo, source, target, false)
            {
                _checkoutBeforeRebase = !source.IsCurrent,
            };
            operation.ConfigureForcePushAfterSuccess(remote, remoteBranch, setUpstream);
            await repo.ShowAndStartPopupAsync(operation);
        }

        public override async Task<bool> Sure()
        {
            var forcePushAfterSuccess = _forcePushRemote != null && _forcePushRemoteBranch != null;
            _repo.ClearCommitMessage();
            ProgressDescription = _checkoutBeforeRebase ? $"Checking out {Current.Name} ..." :
                forcePushAfterSuccess ? "Rebasing before force push ..." : "Rebasing ...";

            var logName = _checkoutBeforeRebase ? "Checkout, Rebase & Force Push" :
                forcePushAfterSuccess ? "Rebase & Force Push" : "Rebase";
            var log = _repo.CreateLog(logName);
            Use(log);

            bool succ;
            var checkoutSucceeded = !_checkoutBeforeRebase;
            using (var lockWatcher = _repo.LockWatcher())
            {
                if (_checkoutBeforeRebase)
                {
                    log.AppendLine($"=== Checkout `{Current.Name}` ===");
                    succ = await new Commands.Checkout(_repo.FullPath)
                        .Use(log)
                        .BranchAsync(Current.Name, false);
                    checkoutSucceeded = succ;
                }
                else
                {
                    succ = true;
                }

                if (succ)
                {
                    ProgressDescription = forcePushAfterSuccess ? "Rebasing before force push ..." : "Rebasing ...";
                    succ = await new Commands.Rebase(_repo.FullPath, _revision, AutoStash, NoVerify)
                        .Use(log)
                        .ExecAsync();
                }
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
                    _setUpstreamAfterForcePush,
                    true).Use(log).RunAsync();
            }

            if (succ)
                _repo.RefreshSuperProjectSubmodulePointer();

            log.Complete();

            if (_checkoutBeforeRebase && checkoutSucceeded)
                _repo.RefreshAfterCheckoutBranch(Current);

            if (forcePushAfterSuccess)
            {
                _repo.MarkBranchesDirtyManually();
                if (succ)
                    _repo.SendNotification($"Rebased `{Current.Name}` and force-pushed it to `{_forcePushRemote.Name}/{_forcePushRemoteBranch.Name}`.");
                else if (!checkoutSucceeded)
                    _repo.SendNotification($"Checkout of `{Current.Name}` failed. Rebase and force push were skipped.", true);
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
            bool requireCurrent,
            out Models.Remote remote,
            out Models.Branch remoteBranch,
            out bool setUpstream)
        {
            remote = null;
            remoteBranch = null;
            setUpstream = false;

            if (repo == null ||
                source is not { IsLocal: true, IsDetachedHead: false } ||
                (requireCurrent && !source.IsCurrent))
            {
                return false;
            }

            if (!source.IsUpstreamGone && !string.IsNullOrWhiteSpace(source.Upstream))
            {
                var resolvedRemoteBranch = repo.Branches.Find(x =>
                    !x.IsLocal &&
                    string.Equals(x.FullName, source.Upstream, StringComparison.Ordinal));
                if (resolvedRemoteBranch != null)
                {
                    var resolvedRemote = repo.Remotes.Find(x =>
                        string.Equals(x.Name, resolvedRemoteBranch.Remote, StringComparison.Ordinal));
                    if (resolvedRemote != null)
                    {
                        remote = resolvedRemote;
                        remoteBranch = resolvedRemoteBranch;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(repo.Settings?.DefaultRemote))
            {
                remote = repo.Remotes.Find(x =>
                    string.Equals(x.Name, repo.Settings.DefaultRemote, StringComparison.Ordinal));
            }

            if (remote == null && repo.Remotes.Count == 1)
                remote = repo.Remotes[0];
            if (remote == null)
                return false;

            var remoteName = remote.Name;
            remoteBranch = repo.Branches.Find(x =>
                !x.IsLocal &&
                string.Equals(x.Remote, remoteName, StringComparison.Ordinal) &&
                string.Equals(x.Name, source.Name, StringComparison.Ordinal));
            remoteBranch ??= new Models.Branch
            {
                Name = source.Name,
                FullName = $"refs/remotes/{remoteName}/{source.Name}",
                Remote = remoteName,
            };
            setUpstream = !string.Equals(source.Upstream, remoteBranch.FullName, StringComparison.Ordinal);
            return true;
        }

        private readonly Repository _repo;
        private readonly string _revision;
        private RebaseTestingState _testingState = RebaseTestingState.Disabled;
        private Models.Remote _forcePushRemote = null;
        private Models.Branch _forcePushRemoteBranch = null;
        private bool _setUpstreamAfterForcePush = false;
        private bool _checkoutBeforeRebase = false;
    }
}
