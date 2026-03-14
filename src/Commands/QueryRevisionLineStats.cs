using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class QueryRevisionLineStats : Command
    {
        public class Stat
        {
            public string Added { get; set; } = string.Empty;
            public string Deleted { get; set; } = string.Empty;
        }

        [GeneratedRegex(@"^([0-9\-]+)\s+([0-9\-]+)\s+(.+)$")]
        private static partial Regex REG_FORMAT();

        public QueryRevisionLineStats(string repo, string based, string target)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"diff --numstat {based} {target}";
            RaiseError = false;
        }

        public async Task<Dictionary<string, Stat>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            var outs = new Dictionary<string, Stat>(StringComparer.Ordinal);
            if (!rs.IsSuccess || string.IsNullOrWhiteSpace(rs.StdOut))
                return outs;

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT().Match(line);
                if (!match.Success)
                    continue;

                outs[match.Groups[3].Value] = new Stat
                {
                    Added = match.Groups[1].Value,
                    Deleted = match.Groups[2].Value,
                };
            }

            return outs;
        }

        public static async Task ApplyAsync(string repo, string based, string target, IReadOnlyList<Models.Change> changes)
        {
            if (string.IsNullOrWhiteSpace(repo) ||
                string.IsNullOrWhiteSpace(based) ||
                string.IsNullOrWhiteSpace(target) ||
                changes == null ||
                changes.Count == 0)
                return;

            var stats = await new QueryRevisionLineStats(repo, based, target).GetResultAsync().ConfigureAwait(false);
            foreach (var change in changes)
            {
                if (change == null || string.IsNullOrWhiteSpace(change.Path))
                    continue;

                if (!stats.TryGetValue(change.Path, out var stat))
                    continue;

                change.AddedLines = stat.Added;
                change.DeletedLines = stat.Deleted;
            }
        }
    }
}
