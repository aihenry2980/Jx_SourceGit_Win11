using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Pull : Command
    {
        public Pull(string repo, string remote, string branch, bool useRebase, bool allowSubmoduleRecursion = true)
        {
            _remote = remote;
            Configure(repo, remote, branch, useRebase, allowSubmoduleRecursion);
        }

        public Pull(string repo, Models.Remote remote, Models.Branch remoteBranch, bool useRebase, bool allowSubmoduleRecursion = true)
        {
            SSHKey = remote.PrivateSSHKey;
            Configure(repo, remote.Name, remoteBranch?.Name, useRebase, allowSubmoduleRecursion);
        }

        public async Task<bool> RunAsync()
        {
            return (await RunWithResultAsync().ConfigureAwait(false)).IsSuccess;
        }

        public async Task<Result> RunWithResultAsync()
        {
            if (!string.IsNullOrEmpty(_remote))
                SSHKey = await new Config(WorkingDirectory).GetAsync($"remote.{_remote}.sshkey").ConfigureAwait(false);

            Log?.AppendLine($"$ git {Args}\n");
            var result = await ReadToEndAndKillOnCancelAsync().ConfigureAwait(false);

            AppendOutput(result.StdOut);
            AppendOutput(result.StdErr);
            Log?.AppendLine(string.Empty);

            return result;
        }

        private void Configure(string repo, string remote, string branch, bool useRebase, bool allowSubmoduleRecursion)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder.Append("pull --verbose --progress ");
            if (!allowSubmoduleRecursion)
                builder.Append("--no-recurse-submodules ");

            builder
                .Append("--rebase=")
                .Append(useRebase ? "true" : "false")
                .Append(' ')
                .Append(remote);

            if (!string.IsNullOrEmpty(branch))
                builder.Append(' ').Append(branch);

            Args = builder.ToString();
        }

        private void AppendOutput(string content)
        {
            if (string.IsNullOrEmpty(content))
                return;

            var normalized = content.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            foreach (var line in lines)
            {
                if (!string.IsNullOrEmpty(line))
                    Log?.AppendLine(line);
            }
        }

        private readonly string _remote;
    }
}
