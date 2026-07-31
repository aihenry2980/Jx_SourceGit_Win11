using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryFirstParentCommitHashes : Command
    {
        public QueryFirstParentCommitHashes(string repo, string revision)
        {
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;
            Args = $"rev-list --first-parent {revision}";
        }

        public async Task<List<string>> GetResultAsync()
        {
            var outs = new List<string>();
            var result = await ReadToEndAsync().ConfigureAwait(false);
            if (!result.IsSuccess)
                return outs;

            foreach (var line in result.StdOut.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 8)
                    outs.Add(line);
            }

            return outs;
        }
    }
}
