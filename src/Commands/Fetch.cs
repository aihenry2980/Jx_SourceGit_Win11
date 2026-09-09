using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Fetch : Command
    {
        public Task<bool> RunAsync()
        {
            return ExecAsync();
        }

        public Fetch(string repo, Models.Remote remote, bool noTags, bool force)
            : this(repo, remote.Name, noTags, force)
        {
            SSHKey = remote.PrivateSSHKey;
        }

        public Fetch(string repo, string remote, bool noTags, bool force, bool prune = false, bool recurseSubmodules = false)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder.Append("fetch --progress --verbose ");
            builder.Append(noTags ? "--no-tags " : "--tags ");
            if (force)
                builder.Append("--force ");
            if (prune)
                builder.Append("--prune ");
            builder.Append(recurseSubmodules ? "--recurse-submodules " : "--no-recurse-submodules ");
            builder.Append(remote);
            Args = builder.ToString();
        }

        public Fetch(string repo, string remote, bool noTags, bool force, bool prune, bool recurseSubmodules, IEnumerable<string> refspecs)
            : this(repo, remote, noTags, force, prune, recurseSubmodules)
        {
            var builder = new StringBuilder(Args);
            foreach (var refspec in refspecs)
            {
                if (!string.IsNullOrWhiteSpace(refspec))
                    builder.Append(' ').Append(refspec.Quoted());
            }

            Args = builder.ToString();
        }

        public Fetch(string repo, Models.Remote remote)
            : this(repo, remote.Name)
        {
            SSHKey = remote.PrivateSSHKey;
        }

        public Fetch(string repo, string remote, bool recurseSubmodules = false, bool prune = false)
        {
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;

            var builder = new StringBuilder(256);
            builder.Append("fetch --progress --verbose ");
            if (prune)
                builder.Append("--prune ");
            builder.Append(recurseSubmodules ? "--recurse-submodules " : "--no-recurse-submodules ");
            builder.Append(remote);
            Args = builder.ToString();
        }

        public Fetch(string repo, Models.Remote remote, Models.Branch remoteBranch, Models.Branch local)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            Args = $"fetch --progress --verbose {remote.Name} {remoteBranch.Name}:{local.Name}";
        }
    }
}
