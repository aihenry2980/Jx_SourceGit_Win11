using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Fetch : Command
    {
        public Fetch(string repo, string remote, bool noTags, bool force, bool prune = false, bool recurseSubmodules = false)
        {
            _remote = remote;

            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder.Append("fetch --progress --verbose ");
            builder.Append(noTags ? "--no-tags " : "--tags ");
            if (force)
                builder.Append("--force ");
            if (prune)
                builder.Append("--prune ");
            if (recurseSubmodules)
                builder.Append("--recurse-submodules ");
            builder.Append(remote);

            Args = builder.ToString();
        }

        public Fetch(string repo, string remote, bool noTags, bool force, bool prune, bool recurseSubmodules, IEnumerable<string> refspecs)
        {
            _remote = remote;

            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder.Append("fetch --progress --verbose ");
            builder.Append(noTags ? "--no-tags " : "--tags ");
            if (force)
                builder.Append("--force ");
            if (prune)
                builder.Append("--prune ");
            if (recurseSubmodules)
                builder.Append("--recurse-submodules ");
            builder.Append(remote);

            foreach (var refspec in refspecs)
            {
                if (!string.IsNullOrWhiteSpace(refspec))
                    builder.Append(' ').Append(refspec.Quoted());
            }

            Args = builder.ToString();
        }

        public Fetch(string repo, string remote, bool recurseSubmodules = false, bool prune = false)
        {
            _remote = remote;

            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;

            var builder = new StringBuilder(256);
            builder.Append("fetch --progress --verbose ");
            if (prune)
                builder.Append("--prune ");
            if (recurseSubmodules)
                builder.Append("--recurse-submodules ");
            builder.Append(remote);

            Args = builder.ToString();
        }

        public Fetch(string repo, Models.Branch local, Models.Branch remote)
        {
            _remote = remote.Remote;

            WorkingDirectory = repo;
            Context = repo;
            Args = $"fetch --progress --verbose {remote.Remote} {remote.Name}:{local.Name}";
        }

        public async Task<bool> RunAsync()
        {
            SSHKey = await new Config(WorkingDirectory).GetAsync($"remote.{_remote}.sshkey").ConfigureAwait(false);
            return await ExecAsync().ConfigureAwait(false);
        }

        private readonly string _remote;
    }
}
