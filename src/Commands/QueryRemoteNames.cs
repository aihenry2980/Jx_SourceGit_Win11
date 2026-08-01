using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryRemoteNames : Command
    {
        public QueryRemoteNames(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;
            Args = "remote";
        }

        public async Task<List<string>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return [];

            var remotes = new List<string>();
            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var name = line.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    remotes.Add(name);
            }

            return remotes;
        }
    }
}
