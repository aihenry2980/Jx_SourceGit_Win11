using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class QuerySubmodulePointerChanges : Command
    {
        public class Change
        {
            public string OldSHA { get; set; } = string.Empty;
            public string NewSHA { get; set; } = string.Empty;
        }

        [GeneratedRegex(@"^:(\d{6}) (\d{6}) ([0-9a-f]{40}) ([0-9a-f]{40}) [A-Z]\d*\t(.+)$")]
        private static partial Regex REG_FORMAT();

        public QuerySubmodulePointerChanges(string repo, bool cached, IEnumerable<string> paths)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder();
            builder.Append("diff --raw ");
            if (cached)
                builder.Append("--cached ");

            builder.Append("-- ");
            foreach (var path in paths)
                builder.Append(path.Quoted()).Append(' ');

            Args = builder.ToString().TrimEnd();
        }

        public QuerySubmodulePointerChanges(string repo, string oldRevision, string newRevision)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder();
            builder.Append("diff --raw ");
            if (!string.IsNullOrWhiteSpace(oldRevision))
                builder.Append(oldRevision).Append(' ');
            if (!string.IsNullOrWhiteSpace(newRevision))
                builder.Append(newRevision);

            Args = builder.ToString().TrimEnd();
        }

        public async Task<Dictionary<string, Change>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            var outs = new Dictionary<string, Change>();
            if (!rs.IsSuccess || string.IsNullOrWhiteSpace(rs.StdOut))
                return outs;

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT().Match(line);
                if (!match.Success)
                    continue;

                var oldMode = match.Groups[1].Value;
                var newMode = match.Groups[2].Value;
                if (oldMode != "160000" && newMode != "160000")
                    continue;

                outs[match.Groups[5].Value] = new Change()
                {
                    OldSHA = match.Groups[3].Value,
                    NewSHA = match.Groups[4].Value,
                };
            }

            return outs;
        }
    }
}
