using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Push : Command
    {
        public Push(string repo, string local, string remote, string remoteBranch, bool withTags, bool checkSubmodules, bool track, bool force, bool noVerify)
        {
            _remote = remote;
            Configure(repo, local, remote, remoteBranch, withTags, checkSubmodules, track, force, noVerify);
        }

        public Push(string repo, Models.Branch local, Models.Remote remote, Models.Branch remoteBranch, bool withTags, bool checkSubmodules, bool track, bool force, bool noVerify)
        {
            SSHKey = remote.PrivateSSHKey;
            _knownLocalBranch = local.IsLocal && !local.IsDetachedHead;
            Configure(repo, local.Name, remote.Name, remoteBranch.Name, withTags, checkSubmodules, track, force, noVerify);
        }

        public Push(string repo, Models.Commit revision, Models.Remote remote, Models.Branch remoteBranch, bool force)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;

            var builder = new StringBuilder(1024);
            builder.Append("push --progress --verbose ");
            if (force)
                builder.Append("--force-with-lease ");

            builder.Append(remote.Name).Append(' ').Append(revision.SHA).Append(':').Append(remoteBranch.Name);
            Args = builder.ToString();
        }

        public Push(string repo, Models.Remote remote, string refname, bool isDelete)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;

            var builder = new StringBuilder(512);
            builder.Append("push ");
            if (isDelete)
                builder.Append("--delete ");
            builder.Append(remote.Name).Append(' ').Append(refname);

            Args = builder.ToString();
        }

        public Push(string repo, string remote, string refname, bool isDelete)
        {
            _remote = remote;
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder.Append("push ");
            if (isDelete)
                builder.Append("--delete ");
            builder.Append(remote).Append(' ').Append(refname);
            Args = builder.ToString();
        }

        public async Task<bool> RunAsync()
        {
            if (!string.IsNullOrEmpty(_remote))
                SSHKey = await new Config(WorkingDirectory).GetAsync($"remote.{_remote}.sshkey").ConfigureAwait(false);

            return await ExecAsync().ConfigureAwait(false);
        }

        private void Configure(string repo, string local, string remote, string remoteBranch, bool withTags, bool checkSubmodules, bool track, bool force, bool noVerify)
        {
            WorkingDirectory = repo;
            Context = repo;
            _source = local;
            _destination = remoteBranch;
            _destinationRemote = remote;
            _force = force;

            var builder = new StringBuilder(1024);
            builder.Append("push --progress --verbose ");
            if (withTags)
                builder.Append("--tags ");
            if (checkSubmodules)
                builder.Append("--recurse-submodules=check ");
            if (track)
                builder.Append("-u ");
            if (force)
                builder.Append("--force-with-lease ");
            if (noVerify)
                builder.Append("--no-verify ");

            builder.Append(remote).Append(' ').Append(local).Append(':').Append(remoteBranch);
            Args = builder.ToString();
        }

        protected override async Task<bool> ConfirmBeforeExecutionAsync()
        {
            if (string.IsNullOrEmpty(_source) || string.IsNullOrEmpty(_destination))
                return true;

            var localName = BranchName(_source);
            var remoteName = BranchName(_destination);
            if (string.Equals(localName, remoteName, StringComparison.Ordinal))
                return true;

            if (!_knownLocalBranch)
            {
                // Some callers push a revision (e.g. undo); only named local branches need this warning.
                var branches = await new QueryBranches(WorkingDirectory).GetResultAsync().ConfigureAwait(false);
                var local = branches.Find(b => b.IsLocal && !b.IsDetachedHead &&
                    (_source == "HEAD" ? b.IsCurrent : b.Name == localName));
                if (local == null)
                {
                    if (_source.Length is 40 or 64 && _source.All(char.IsAsciiHexDigit))
                        return true;

                    Log?.AppendLine("Push canceled: unable to verify the local branch name.");
                    return false;
                }

                localName = local.Name;
                if (string.Equals(localName, remoteName, StringComparison.Ordinal))
                    return true;
            }

            if (CancellationToken.IsCancellationRequested)
                return false;

            var confirmed = await App.AskConfirmBranchPushAsync(WorkingDirectory, localName,
                _destinationRemote, remoteName, _force, CancellationToken).ConfigureAwait(false);
            if (!confirmed)
                Log?.AppendLine("Push canceled: branch-name mismatch was not confirmed.");
            return confirmed;
        }

        private static string BranchName(string name) => name.StartsWith("refs/heads/", StringComparison.Ordinal)
            ? name.Substring("refs/heads/".Length) : name;

        private readonly string _remote;
        private readonly bool _knownLocalBranch;
        private string _source;
        private string _destination;
        private string _destinationRemote;
        private bool _force;
    }
}
