using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class RevertSubmoduleChanges : Popup
    {
        public string Submodule
        {
            get;
        }

        public bool IncludeModified
        {
            get;
            set;
        } = true;

        public bool IncludeUntracked
        {
            get;
            set;
        } = true;

        public bool IncludeIgnored
        {
            get;
            set;
        } = false;

        public bool IncludeNestedSubmodules
        {
            get;
            set;
        } = true;

        public RevertSubmoduleChanges(Repository repo, Models.Submodule submodule)
        {
            _repo = repo;
            Submodule = submodule.Path;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = $"Revert changes in submodule '{Submodule}' ...";

            var log = _repo.CreateLog($"Revert Submodule '{Submodule}'");
            Use(log);

            var submoduleRoot = Native.OS.GetAbsPath(_repo.FullPath, Submodule);
            if (Directory.Exists(submoduleRoot))
            {
                await Commands.Discard.AllAsync(submoduleRoot, IncludeModified, IncludeUntracked, IncludeIgnored, log);

                if (IncludeNestedSubmodules)
                    await RevertNestedSubmodulesAsync(submoduleRoot, log);
            }

            var update = true;
            if (IncludeModified)
            {
                update = await new Commands.Submodule(_repo.FullPath)
                    .Use(log)
                    .UpdateAsync([Submodule], true, false);
            }

            log.Complete();
            _repo.MarkSubmodulesDirtyManually();
            _repo.MarkWorkingCopyDirtyManually();
            return update;
        }

        private async Task RevertNestedSubmodulesAsync(string submoduleRoot, Models.ICommandLog log)
        {
            if (IncludeModified)
            {
                await new Commands.Command()
                {
                    WorkingDirectory = submoduleRoot,
                    Context = submoduleRoot,
                    Args = "submodule foreach --recursive \"git reset --hard\"",
                    RaiseError = false,
                }.Use(log).ExecAsync().ConfigureAwait(false);
            }

            if (IncludeUntracked || IncludeIgnored)
            {
                var cleanMode = IncludeIgnored ? "-fdx" : "-fd";
                await new Commands.Command()
                {
                    WorkingDirectory = submoduleRoot,
                    Context = submoduleRoot,
                    Args = $"submodule foreach --recursive \"git clean {cleanMode}\"",
                    RaiseError = false,
                }.Use(log).ExecAsync().ConfigureAwait(false);
            }
        }

        private readonly Repository _repo = null;
    }
}
