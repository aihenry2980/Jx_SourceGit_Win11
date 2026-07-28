using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public abstract class InProgressContext
    {
        public string Name
        {
            get;
            protected set;
        }

        public async Task ContinueAsync(CommandLog log)
        {
            if (_continueCmd != null)
                await _continueCmd.Use(log).ExecAsync();
        }

        public async Task SkipAsync(CommandLog log)
        {
            if (_skipCmd != null)
                await _skipCmd.Use(log).ExecAsync();
        }

        public async Task AbortAsync(CommandLog log)
        {
            if (_abortCmd != null)
                await _abortCmd.Use(log).ExecAsync();

            OnAborted();
        }

        protected virtual void OnAborted()
        {
        }

        protected Commands.Command _continueCmd = null;
        protected Commands.Command _skipCmd = null;
        protected Commands.Command _abortCmd = null;
    }

    public class CherryPickInProgress : InProgressContext
    {
        public Models.Commit Head
        {
            get;
        }

        public string HeadName
        {
            get;
        }

        public static async Task<CherryPickInProgress> CreateAsync(Repository repo)
        {
            var headSHA = File.ReadAllText(Path.Combine(repo.GitDir, "CHERRY_PICK_HEAD")).Trim();
            var head = await new Commands.QuerySingleCommit(repo.FullPath, headSHA).GetResultAsync();
            return new CherryPickInProgress(repo, head ?? new Models.Commit() { SHA = headSHA });
        }

        private CherryPickInProgress(Repository repo, Models.Commit head)
        {
            Name = "Cherry-Pick";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.None,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" cherry-pick --continue",
            };

            _skipCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "cherry-pick --skip",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "cherry-pick --abort",
            };

            Head = head;
            HeadName = Head.GetFriendlyName();
        }
    }

    public class RebaseInProgress : InProgressContext
    {
        public string HeadName
        {
            get;
        }

        public string BaseName
        {
            get;
        }

        public Models.Commit StoppedAt
        {
            get;
        }

        public Models.Commit Onto
        {
            get;
        }

        public static async Task<RebaseInProgress> CreateAsync(Repository repo)
        {
            var headName = File.ReadAllText(Path.Combine(repo.GitDir, "rebase-merge", "head-name")).Trim();
            if (headName.StartsWith("refs/heads/"))
                headName = headName.Substring(11);
            else if (headName.StartsWith("refs/tags/"))
                headName = headName.Substring(10);

            var stoppedSHAPath = Path.Combine(repo.GitDir, "rebase-merge", "stopped-sha");
            var stoppedSHA = File.Exists(stoppedSHAPath)
                ? File.ReadAllText(stoppedSHAPath).Trim()
                : await new Commands.QueryRevisionByRefName(repo.FullPath, headName).GetResultAsync();

            Models.Commit stoppedAt = null;
            if (!string.IsNullOrEmpty(stoppedSHA))
                stoppedAt = await new Commands.QuerySingleCommit(repo.FullPath, stoppedSHA).GetResultAsync() ?? new Models.Commit() { SHA = stoppedSHA };

            var ontoSHA = File.ReadAllText(Path.Combine(repo.GitDir, "rebase-merge", "onto")).Trim();
            var onto = await new Commands.QuerySingleCommit(repo.FullPath, ontoSHA).GetResultAsync() ?? new Models.Commit() { SHA = ontoSHA };
            return new RebaseInProgress(repo, headName, stoppedAt, onto);
        }

        private RebaseInProgress(Repository repo, string headName, Models.Commit stoppedAt, Models.Commit onto)
        {
            _gitDir = repo.GitDir;
            Name = "Rebase";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.RebaseEditor,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" rebase --continue",
            };

            _skipCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "rebase --skip",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "rebase --abort",
                RaiseError = false,
            };

            HeadName = headName;
            StoppedAt = stoppedAt;
            Onto = onto;
            BaseName = Onto.GetFriendlyName();
        }

        protected override void OnAborted()
        {
            var rebaseMergeDir = Path.Combine(_gitDir, "rebase-merge");
            if (Directory.Exists(rebaseMergeDir))
                Directory.Delete(rebaseMergeDir, true);

            var rebaseApplyDir = Path.Combine(_gitDir, "rebase-apply");
            if (Directory.Exists(rebaseApplyDir))
                Directory.Delete(rebaseApplyDir, true);

            var jobFile = Path.Combine(_gitDir, "sourcegit.interactive_rebase");
            if (File.Exists(jobFile))
                File.Delete(jobFile);
        }

        private readonly string _gitDir;
    }

    public class RevertInProgress : InProgressContext
    {
        public Models.Commit Head
        {
            get;
        }

        public static async Task<RevertInProgress> CreateAsync(Repository repo)
        {
            var headSHA = File.ReadAllText(Path.Combine(repo.GitDir, "REVERT_HEAD")).Trim();
            var head = await new Commands.QuerySingleCommit(repo.FullPath, headSHA).GetResultAsync();
            return new RevertInProgress(repo, head ?? new Models.Commit() { SHA = headSHA });
        }

        private RevertInProgress(Repository repo, Models.Commit head)
        {
            Name = "Revert";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.None,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" revert --continue",
            };

            _skipCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "revert --skip",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "revert --abort",
            };

            Head = head;
        }
    }

    public class MergeInProgress : InProgressContext
    {
        public string Current
        {
            get;
        }

        public Models.Commit Source
        {
            get;
        }

        public string SourceName
        {
            get;
        }

        public static async Task<MergeInProgress> CreateAsync(Repository repo)
        {
            var current = await new Commands.QueryCurrentBranch(repo.FullPath).GetResultAsync();
            var sourceSHA = File.ReadAllText(Path.Combine(repo.GitDir, "MERGE_HEAD")).Trim();
            var source = await new Commands.QuerySingleCommit(repo.FullPath, sourceSHA).GetResultAsync();
            return new MergeInProgress(repo, current, source ?? new Models.Commit() { SHA = sourceSHA });
        }

        private MergeInProgress(Repository repo, string current, Models.Commit source)
        {
            Name = "Merge";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.None,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" merge --continue",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "merge --abort",
            };

            Current = current;
            Source = source;
            SourceName = Source.GetFriendlyName();
        }
    }
}
