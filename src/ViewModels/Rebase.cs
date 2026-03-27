using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
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

        public Rebase(Repository repo, Models.Branch current, Models.Branch on)
        {
            _repo = repo;
            _revision = on.Head;
            Current = current;
            On = on;
            AutoStash = true;
        }

        public Rebase(Repository repo, Models.Branch current, Models.Commit on)
        {
            _repo = repo;
            _revision = on.SHA;
            Current = current;
            On = on;
            AutoStash = true;
        }

        public override async Task<bool> Sure()
        {
            _repo.ClearCommitMessage();
            ProgressDescription = "Rebasing ...";

            var log = _repo.CreateLog("Rebase");
            Use(log);

            bool succ;
            using (var lockWatcher = _repo.LockWatcher())
            {
                succ = await new Commands.Rebase(_repo.FullPath, _revision, AutoStash)
                    .Use(log)
                    .ExecAsync();
            }

            if (succ && UpdateSubmodulesRecursivelyAfterOperation)
            {
                log.AppendLine("=== Update submodules recursively after rebase ===");
                succ = await _repo.RunUpdateSubmodulesRecursivelyAsync(log).ConfigureAwait(false);
            }

            if (succ)
                _repo.RefreshSuperProjectSubmodulePointer();

            log.Complete();
            return succ;
        }

        private readonly Repository _repo;
        private readonly string _revision;
    }
}
