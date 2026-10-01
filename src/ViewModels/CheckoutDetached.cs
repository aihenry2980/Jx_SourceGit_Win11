using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class CheckoutDetached : Popup
    {
        public object Target
        {
            get;
        }

        public string BranchName { get; private set; }
        public bool CreatesBranch => Target is Models.Commit;
        public string Title => App.Text(CreatesBranch ? "CreateBranch.Title" : "CheckoutDetached");

        public bool HasLocalChanges
        {
            get => _repo.LocalChangesCount > 0;
        }

        public Models.DealWithLocalChanges DealWithLocalChanges
        {
            get;
            set;
        }

        public CheckoutDetached(Repository repo, Models.Commit commit)
        {
            _repo = repo;
            _revision = commit.SHA;

            Target = commit;
            BranchName = GetUniqueBranchName(Models.RefName.FromCommit(commit.SHA, commit.Subject), repo.Branches);
            DealWithLocalChanges = Preferences.Instance.UseStashAndReapplyByDefault ?
                Models.DealWithLocalChanges.StashAndReapply :
                Models.DealWithLocalChanges.DoNothing;
        }

        public CheckoutDetached(Repository repo, Models.Tag tag)
        {
            _repo = repo;
            _revision = tag.SHA;

            Target = tag;
            DealWithLocalChanges = Preferences.Instance.UseStashAndReapplyByDefault ?
                Models.DealWithLocalChanges.StashAndReapply :
                Models.DealWithLocalChanges.DoNothing;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            if (CreatesBranch)
            {
                var branches = await new Commands.QueryBranches(_repo.FullPath).GetResultAsync();
                BranchName = GetUniqueBranchName(BranchName, branches);
                OnPropertyChanged(nameof(BranchName));
            }
            ProgressDescription = CreatesBranch ? $"Checkout New Branch '{BranchName}' ..." : $"Checkout Commit '{_revision}' ...";

            var log = _repo.CreateLog("Checkout Commit");
            Use(log);

            if (_repo.CurrentBranch is { IsDetachedHead: true } &&
                !(CreatesBranch && _repo.CurrentBranch.Head.Equals(_revision, System.StringComparison.Ordinal)))
            {
                var refs = await new Commands.QueryRefsContainsCommit(_repo.FullPath, _repo.CurrentBranch.Head).GetResultAsync();
                if (refs.Count == 0)
                {
                    var msg = App.Text("Checkout.WarnLostCommits");
                    var shouldContinue = await App.AskConfirmAsync(msg);
                    if (!shouldContinue)
                        return true;
                }
            }

            var succ = false;
            var needPop = false;

            if (DealWithLocalChanges == Models.DealWithLocalChanges.DoNothing)
            {
                succ = await CheckoutTargetAsync(log, false);
            }
            else if (DealWithLocalChanges == Models.DealWithLocalChanges.StashAndReapply)
            {
                var changes = await new Commands.CountLocalChanges(_repo.FullPath, false).GetResultAsync();
                if (changes > 0)
                {
                    succ = await new Commands.Stash(_repo.FullPath)
                        .Use(log)
                        .PushAsync("CHECKOUT_AUTO_STASH", false);
                    if (!succ)
                    {
                        log.Complete();
                        _repo.MarkWorkingCopyDirtyManually();
                        return false;
                    }

                    needPop = true;
                }

                succ = await CheckoutTargetAsync(log, false);
            }
            else
            {
                succ = await CheckoutTargetAsync(log, true);
            }

            if (succ)
            {
                await _repo.AutoUpdateSubmodulesAsync(log);

                if (needPop)
                    await new Commands.Stash(_repo.FullPath)
                        .Use(log)
                        .PopAsync("stash@{0}");

                if (Target is Models.Commit commit)
                {
                    _repo.RefreshAfterCreateBranch(new Models.Branch
                    {
                        Name = BranchName,
                        FullName = $"refs/heads/{BranchName}",
                        Head = _revision,
                        CommitterDate = commit.CommitterTime,
                        IsLocal = true,
                    }, true);
                }
                else
                {
                    _repo.RefreshWorkingCopyChanges();
                }
                _repo.RefreshSuperProjectSubmodulePointer();
            }

            log.Complete();
            return succ;
        }

        private Task<bool> CheckoutTargetAsync(CommandLog log, bool force)
        {
            var command = new Commands.Checkout(_repo.FullPath).Use(log);
            return CreatesBranch
                ? command.BranchAsync(BranchName, _revision, force, false)
                : command.CommitAsync(_revision, force);
        }

        private static string GetUniqueBranchName(string name, System.Collections.Generic.List<Models.Branch> branches)
        {
            var candidate = name;
            for (var index = 2; branches.Exists(b => b.IsLocal &&
                (b.Name.Equals(candidate, System.StringComparison.Ordinal) ||
                 b.Name.StartsWith(candidate + "/", System.StringComparison.Ordinal) ||
                 candidate.StartsWith(b.Name + "/", System.StringComparison.Ordinal))); index++)
                candidate = $"{name}-{index}";

            return candidate;
        }

        private readonly Repository _repo = null;
        private readonly string _revision = string.Empty;
    }
}
