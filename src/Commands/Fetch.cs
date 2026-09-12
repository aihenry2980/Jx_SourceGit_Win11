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
            Configure(repo, remote, noTags, force, prune, recurseSubmodules, null);
        }

        public Fetch(string repo, Models.Remote remote, bool noTags, bool force, bool prune = false, bool recurseSubmodules = false)
        {
            SSHKey = remote.PrivateSSHKey;
            Configure(repo, remote.Name, noTags, force, prune, recurseSubmodules, null);
        }

        public Fetch(string repo, string remote, bool noTags, bool force, bool prune, bool recurseSubmodules, IEnumerable<string> refspecs)
        {
            _remote = remote;
            Configure(repo, remote, noTags, force, prune, recurseSubmodules, refspecs);
        }

        public Fetch(string repo, Models.Remote remote, bool noTags, bool force, bool prune, bool recurseSubmodules, IEnumerable<string> refspecs)
        {
            SSHKey = remote.PrivateSSHKey;
            Configure(repo, remote.Name, noTags, force, prune, recurseSubmodules, refspecs);
        }

        public Fetch(string repo, string remote, bool recurseSubmodules = false, bool prune = false)
        {
            _remote = remote;
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;
            Args = BuildArgs(remote, false, false, prune, recurseSubmodules, null, false);
        }

        public Fetch(string repo, Models.Remote remote)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RaiseError = false;
            Args = $"fetch --progress --verbose {remote.Name}";
        }

        public Fetch(string repo, Models.Remote remote, Models.Branch remoteBranch, Models.Branch local)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            Args = $"fetch --progress --verbose {remote.Name} {remoteBranch.Name}:{local.Name}";
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
            if (!string.IsNullOrEmpty(_remote))
                SSHKey = await new Config(WorkingDirectory).GetAsync($"remote.{_remote}.sshkey").ConfigureAwait(false);

            return await ExecAsync().ConfigureAwait(false);
        }

        private void Configure(string repo, string remote, bool noTags, bool force, bool prune, bool recurseSubmodules, IEnumerable<string> refspecs)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = BuildArgs(remote, noTags, force, prune, recurseSubmodules, refspecs, true);
        }

        private static string BuildArgs(string remote, bool noTags, bool force, bool prune, bool recurseSubmodules, IEnumerable<string> refspecs, bool includeTags)
        {
            var builder = new StringBuilder(512);
            builder.Append("fetch --progress --verbose ");
            if (includeTags)
                builder.Append(noTags ? "--no-tags " : "--tags ");
            if (force)
                builder.Append("--force ");
            if (prune)
                builder.Append("--prune ");
            if (recurseSubmodules)
                builder.Append("--recurse-submodules ");
            else
                builder.Append("--no-recurse-submodules ");
            builder.Append(remote);

            if (refspecs != null)
            {
                foreach (var refspec in refspecs)
                {
                    if (!string.IsNullOrWhiteSpace(refspec))
                        builder.Append(' ').Append(refspec.Quoted());
                }
            }

            return builder.ToString();
        }

        private readonly string _remote;
    }
}
