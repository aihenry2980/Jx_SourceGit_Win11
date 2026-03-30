using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class QueryRevisionTreeEntries : Command
    {
        [GeneratedRegex(@"^(\d+)\s+(\w+)\s+([0-9a-f]+)\t(.+)$")]
        private static partial Regex REG_FORMAT();

        public QueryRevisionTreeEntries(string repo, string revision)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"ls-tree -r {revision}";
        }

        public async Task<List<Models.RevisionTreeEntry>> GetResultAsync()
        {
            var outs = new List<Models.RevisionTreeEntry>();

            try
            {
                using var proc = new Process();
                proc.StartInfo = CreateGitStartInfo(true);
                proc.Start();

                while (await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    var match = REG_FORMAT().Match(line);
                    if (!match.Success)
                        continue;

                    outs.Add(new Models.RevisionTreeEntry()
                    {
                        Mode = match.Groups[1].Value,
                        Type = match.Groups[2].Value,
                        SHA = match.Groups[3].Value,
                        Path = match.Groups[4].Value,
                    });
                }

                await proc.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions.
            }

            return outs;
        }
    }
}
