using System;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QuerySubmoduleSuperProjectPointer : Command
    {
        public QuerySubmoduleSuperProjectPointer(string repo, string path)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"ls-tree HEAD -- {path.Quoted()}";
        }

        public async Task<string> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess || string.IsNullOrWhiteSpace(rs.StdOut))
                return null;

            var line = rs.StdOut.Trim();
            var tabIdx = line.IndexOf('\t');
            if (tabIdx >= 0)
                line = line.Substring(0, tabIdx);

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0].Equals("160000", StringComparison.Ordinal) && parts[1].Equals("commit", StringComparison.Ordinal))
                return parts[2];

            return null;
        }
    }
}
