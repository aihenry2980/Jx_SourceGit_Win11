using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class CommitHistoryDiffStat
    {
        public int ChangedFileCount { get; set; } = 0;
        public bool HasSubmodulePointerChange { get; set; } = false;
        public int RegularFileChangeCount { get; set; } = 0;
        public int AddedFileChangeCount { get; set; } = 0;
        public int ModifiedFileChangeCount { get; set; } = 0;
        public int SubmodulePointerChangeCount { get; set; } = 0;
        public List<string> SubmodulePaths { get; set; } = [];
        public bool HasRenameOrCopyChange { get; set; } = false;
        public bool HasTypeChange { get; set; } = false;
    }

    public partial class QueryCommitSubmodulePointerFlags : Command
    {
        [GeneratedRegex(@"^:(\d{6}) (\d{6}) ([0-9a-f]+) ([0-9a-f]+) ([A-Z])\d*\t(.+)$")]
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

        public QueryCommitSubmodulePointerFlags(string repo, IReadOnlyList<string> commits)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder();
            builder.Append("log --no-walk --raw --no-abbrev --no-show-signature --format=%H");
            foreach (var commit in commits)
                builder.Append(' ').Append(commit);

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
                using var registration = CancellationToken.Register(() => TryKillProcessTree(proc));
                var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken);

                string currentCommit = null;
                while (await proc.StandardOutput.ReadLineAsync(CancellationToken).ConfigureAwait(false) is { } line)
                {
                    CancellationToken.ThrowIfCancellationRequested();

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
                    var status = match.Groups[5].Value;
                    if (oldMode == "160000" || newMode == "160000")
                    {
                        stat.HasSubmodulePointerChange = true;
                        var path = NormalizeRawPath(match.Groups[6].Value);
                        if (!stat.SubmodulePaths.Exists(x => x.Equals(path, StringComparison.Ordinal)))
                        {
                            stat.SubmodulePaths.Add(path);
                            stat.SubmodulePointerChangeCount++;
                        }
                    }
                    else
                    {
                        stat.RegularFileChangeCount++;
                        if (status == "A")
                            stat.AddedFileChangeCount++;
                        else if (status == "M")
                            stat.ModifiedFileChangeCount++;
                    }

                    if (status is "R" or "C")
                        stat.HasRenameOrCopyChange = true;

                    if (oldMode != newMode && oldMode != "000000" && newMode != "000000")
                        stat.HasTypeChange = true;
                }

                await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
                await stderrTask.ConfigureAwait(false);

                foreach (var stat in outs.Values)
                    stat.SubmodulePaths.Sort(StringComparer.Ordinal);
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                outs.Clear();
            }
            catch (Exception e)
            {
                App.RaiseException(Context, $"Failed to query commit diff stats. Reason: {e.Message}");
            }

            return outs;
        }

        private static string NormalizeRawPath(string rawPath)
        {
            var separator = rawPath.LastIndexOf('\t');
            var path = separator >= 0 ? rawPath[(separator + 1)..] : rawPath;
            return path.Trim().Trim('"');
        }
    }
}
