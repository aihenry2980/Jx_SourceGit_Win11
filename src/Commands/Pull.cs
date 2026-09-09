using System.Text;

using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Pull : Command
    {
        public Pull(string repo, Models.Remote remote, Models.Branch remoteBranch, bool useRebase)
            : this(repo, remote.Name, remoteBranch?.Name, useRebase)
        {
            SSHKey = remote.PrivateSSHKey;
        }

        public Pull(string repo, string remote, string remoteBranch, bool useRebase)
        {
            WorkingDirectory = repo;
            Context = repo;
            _remote = remote;

            var builder = new StringBuilder(512);
            builder
                .Append("pull --verbose --progress --rebase=")
                .Append(useRebase ? "true" : "false")
                .Append(' ')
                .Append(remote);

            if (!string.IsNullOrEmpty(remoteBranch))
                builder.Append(' ').Append(remoteBranch);

            Args = builder.ToString();
        }
        public async Task<bool> RunAsync()
        {
            return (await RunWithResultAsync().ConfigureAwait(false)).IsSuccess;
        }

        public async Task<Result> RunWithResultAsync()
        {
            SSHKey = await new Config(WorkingDirectory).GetAsync($"remote.{_remote}.sshkey").ConfigureAwait(false);

            Log?.AppendLine($"$ git {Args}\n");
            var result = await ReadToEndAndKillOnCancelAsync().ConfigureAwait(false);

            AppendOutput(result.StdOut);
            AppendOutput(result.StdErr);
            Log?.AppendLine(string.Empty);

            return result;
        }

        private readonly string _remote;

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
    }
}
