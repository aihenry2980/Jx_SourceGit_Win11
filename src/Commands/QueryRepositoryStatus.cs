using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class QueryRepositoryStatus : Command
    {
        [GeneratedRegex(@"\+(\d+) \-(\d+)")]
        private static partial Regex REG_BRANCH_AB();

        public QueryRepositoryStatus(string repo)
        {
            WorkingDirectory = repo;
            RaiseError = false;
        }

        public async Task<Models.RepositoryStatus> GetResultAsync()
        {
            Args = "--no-optional-locks status --porcelain=v2 -z -b -uall --ignore-submodules=all";
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return null;

            var status = new Models.RepositoryStatus();
            var sha1 = string.Empty;
            var head = string.Empty;
            foreach (var record in rs.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var line in record.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                        sha1 = line.Substring(13).Trim();
                    else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                        head = line.Substring(14).Trim();
                    else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
                        ParseTrackStatus(status, line.Substring(12).Trim());
                    else if (line.StartsWith("1 ", StringComparison.Ordinal) ||
                             line.StartsWith("2 ", StringComparison.Ordinal) ||
                             line.StartsWith("u ", StringComparison.Ordinal) ||
                             line.StartsWith("? ", StringComparison.Ordinal))
                        status.LocalChanges++;
                }
            }

            if (string.IsNullOrEmpty(head))
                return null;

            status.CurrentBranch = head.Equals("(detached)", StringComparison.Ordinal)
                ? sha1.Length > 10 ? $"({sha1.Substring(0, 10)})" : "-"
                : head;

            return status;
        }

        private void ParseTrackStatus(Models.RepositoryStatus status, string input)
        {
            var match = REG_BRANCH_AB().Match(input);
            if (match.Success)
            {
                status.Ahead = int.Parse(match.Groups[1].Value);
                status.Behind = int.Parse(match.Groups[2].Value);
            }
        }
    }
}
