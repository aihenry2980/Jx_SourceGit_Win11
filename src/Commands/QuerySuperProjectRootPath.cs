using System.IO;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QuerySuperProjectRootPath : Command
    {
        public QuerySuperProjectRootPath(string path)
        {
            WorkingDirectory = path;
            Args = "rev-parse --show-superproject-working-tree";
        }

        public string GetResult()
        {
            return Parse(ReadToEnd());
        }

        public async Task<string> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            return Parse(rs);
        }

        private string Parse(Result rs)
        {
            if (!rs.IsSuccess)
                return null;

            var stdout = rs.StdOut.Trim();
            if (string.IsNullOrEmpty(stdout))
                return null;

            return Path.IsPathRooted(stdout) ? stdout : Path.GetFullPath(Path.Combine(WorkingDirectory, stdout));
        }
    }
}
