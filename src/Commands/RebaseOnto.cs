using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class RebaseOnto : Command
    {
        public RebaseOnto(string repo, string newBase, string upstream, string branch)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder
                .Append("rebase --onto ")
                .Append(newBase)
                .Append(' ')
                .Append(upstream)
                .Append(' ')
                .Append(branch);

            Args = builder.ToString();
        }

        public async Task<bool> RunAsync()
        {
            return await ExecAsync().ConfigureAwait(false);
        }
    }
}
