using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryBranchUpstream : Command
    {
        public QueryBranchUpstream(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;
            Args = "rev-parse --abbrev-ref --symbolic-full-name @{u}";
        }

        public async Task<string> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            return rs.IsSuccess ? rs.StdOut.Trim() : string.Empty;
        }
    }
}
