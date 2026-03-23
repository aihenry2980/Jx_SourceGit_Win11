using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class Reset : Popup
    {
        public Models.Branch Current
        {
            get;
        }

        public Models.Commit To
        {
            get;
        }

        public Models.ResetMode SelectedMode
        {
            get;
            set;
        }

        public bool UpdateSubmodulesRecursivelyAfterOperation
        {
            get;
            set;
        } = false;

        public Reset(Repository repo, Models.Branch current, Models.Commit to)
        {
            _repo = repo;
            Current = current;
            To = to;
            SelectedMode = Models.ResetMode.Supported[1];
        }

        public override async Task<bool> Sure()
        {
            ProgressDescription = $"Reset current branch to {To.SHA} ...";

            var log = _repo.CreateLog($"Reset HEAD to '{To.SHA}'");
            Use(log);

            bool succ;
            using (var lockWatcher = _repo.LockWatcher())
            {
                succ = await new Commands.Reset(_repo.FullPath, To.SHA, SelectedMode.Arg)
                    .Use(log)
                    .ExecAsync();
            }

            if (succ)
            {
                if (UpdateSubmodulesRecursivelyAfterOperation)
                {
                    log.AppendLine("=== Update submodules recursively after reset ===");
                    succ = await _repo.RunUpdateSubmodulesRecursivelyAsync(log).ConfigureAwait(false);
                }
                else
                {
                    await _repo.AutoUpdateSubmodulesAsync(log);
                }
            }

            log.Complete();
            return succ;
        }

        private readonly Repository _repo = null;
    }
}
