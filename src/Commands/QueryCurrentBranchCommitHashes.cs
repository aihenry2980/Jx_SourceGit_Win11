using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryCurrentBranchCommitHashes : Command
    {
        public QueryCurrentBranchCommitHashes(string repo, ulong sinceTimestamp)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"log --since=@{sinceTimestamp} --format=%H";
        }

        public async Task<HashSet<string>> GetResultAsync()
        {
            var outs = new HashSet<string>();

            try
            {
                var result = await ReadToEndAndKillOnCancelAsync().ConfigureAwait(false);
                if (!result.IsSuccess || CancellationToken.IsCancellationRequested)
                    return outs;

                foreach (var line in result.StdOut.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries))
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    if (line.Length > 8)
                        outs.Add(line);
                }
            }
            catch
            {
                // Ignore exceptions;
            }

            return outs;
        }
    }
}
