namespace SourceGit.Commands
{
    public class Restore : Command
    {
        public Restore(
            string repo,
            string pathspecFile,
            string source = "",
            bool staged = false,
            bool worktree = true,
            bool recurseSubmodules = true)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new System.Text.StringBuilder("restore --progress ");
            if (staged)
                builder.Append("--staged ");
            if (worktree)
                builder.Append("--worktree ");
            if (recurseSubmodules)
                builder.Append("--recurse-submodules ");
            if (!string.IsNullOrWhiteSpace(source))
                builder.Append("--source=").Append(source.Quoted()).Append(' ');

            builder.Append("--pathspec-from-file=").Append(pathspecFile.Quoted());
            Args = builder.ToString();
        }
    }
}
