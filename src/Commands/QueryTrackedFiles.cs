using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryTrackedFiles : Command
    {
        public QueryTrackedFiles(string repo, string pathSpec)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"ls-files -- {pathSpec.Quoted()}";
        }

        public async Task<List<string>> GetResultAsync()
        {
            var outs = new List<string>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess || string.IsNullOrWhiteSpace(rs.StdOut))
                return outs;

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var one = line.Trim();
                if (!string.IsNullOrEmpty(one))
                    outs.Add(one);
            }

            return outs;
        }
    }
}
