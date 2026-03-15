using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class CommitHistoryDiffStat
    {
        public int ChangedFileCount { get; set; } = 0;
        public bool HasSubmodulePointerChange { get; set; } = false;
    }

    public partial class QueryCommitSubmodulePointerFlags : Command
    {
        [GeneratedRegex(@"^:(\d{6}) (\d{6}) ([0-9a-f]+) ([0-9a-f]+) [A-Z]\d*\t(.+)$")]
        private static partial Regex REG_RAW_FORMAT();

        public QueryCommitSubmodulePointerFlags(string repo, string limits)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder();
            builder.Append("log --raw --no-abbrev --no-show-signature --format=%H ");
            builder.Append(limits);
            Args = builder.ToString();
        }

        public async Task<Dictionary<string, CommitHistoryDiffStat>> GetResultAsync()
        {
            var outs = new Dictionary<string, CommitHistoryDiffStat>(StringComparer.Ordinal);

            try
            {
                using var proc = new Process();
                proc.StartInfo = CreateGitStartInfo(true);
                proc.Start();

                string currentCommit = null;
                while (await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (line[0] != ':')
                    {
                        currentCommit = line.Trim();
                        continue;
                    }

                    var match = REG_RAW_FORMAT().Match(line);
                    if (!match.Success || string.IsNullOrEmpty(currentCommit))
                        continue;

                    if (!outs.TryGetValue(currentCommit, out var stat))
                    {
                        stat = new CommitHistoryDiffStat();
                        outs[currentCommit] = stat;
                    }

                    stat.ChangedFileCount++;

                    var oldMode = match.Groups[1].Value;
                    var newMode = match.Groups[2].Value;
                    if (oldMode == "160000" || newMode == "160000")
                        stat.HasSubmodulePointerChange = true;
                }

                await proc.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                App.RaiseException(Context, $"Failed to query commit diff stats. Reason: {e.Message}");
            }

            return outs;
        }
    }
}
