using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryHeadReflog : Command
    {
        public class Entry
        {
            public int Index { get; set; } = 0;
            public string SHA { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
        }

        public QueryHeadReflog(string repo, int limit = 4)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"reflog -n {Math.Max(1, limit)} --format=%H%x00%gs HEAD";
        }

        public async Task<List<Entry>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            var outs = new List<Entry>();
            if (!rs.IsSuccess || string.IsNullOrWhiteSpace(rs.StdOut))
                return outs;

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\0', 2, StringSplitOptions.None);
                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                    continue;

                outs.Add(new Entry()
                {
                    Index = i,
                    SHA = parts[0],
                    Summary = parts.Length > 1 ? parts[1].Trim() : string.Empty,
                });
            }

            return outs;
        }
    }
}
