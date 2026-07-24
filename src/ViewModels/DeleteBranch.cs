using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class DeleteBranch : Popup
    {
        public Models.Branch Target
        {
            get;
        }

        public bool Force
        {
            get;
            set;
        }

        public DeleteBranch(Repository repo, Models.Branch branch)
        {
            _repo = repo;
            Target = branch;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Deleting branch...";

            var log = _repo.CreateLog("Delete Branch");
            Use(log);

            var succ = false;
            if (Target.IsLocal)
            {
                succ = await new Commands.Branch(_repo.FullPath, Target.Name)
                    .Use(log)
                    .DeleteLocalAsync(Force);

                if (succ)
                    _repo.UIStates.RemoveHistoryFilter(Target.FullName, Models.FilterType.LocalBranch);
            }
            else
            {
                succ = await DeleteRemoteBranchAsync(Target, log);
                if (succ)
                    _repo.UIStates.RemoveHistoryFilter(Target.FullName, Models.FilterType.RemoteBranch);
            }

            log.Complete();
            _repo.MarkBranchesDirtyManually();
            return succ;
        }

        private async Task<bool> DeleteRemoteBranchAsync(Models.Branch branch, CommandLog log)
        {
            var exists = await new Commands.Remote(_repo.FullPath)
                .HasBranchAsync(branch.Remote, branch.Name)
                .ConfigureAwait(false);

            if (exists)
                return await new Commands.Push(_repo.FullPath, branch.Remote, $"refs/heads/{branch.Name}", true)
                    .Use(log)
                    .RunAsync()
                    .ConfigureAwait(false);
            else
                return await new Commands.Branch(_repo.FullPath, branch.Name)
                    .Use(log)
                    .DeleteRemoteAsync(branch.Remote, Force)
                    .ConfigureAwait(false);
        }

        private readonly Repository _repo = null;
    }
}
