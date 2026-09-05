using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class RepositoryPerformanceProbe : Command
    {
        public RepositoryPerformanceProbe(string repo, string args)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = args;
            RaiseError = false;
        }

        public Task<Result> GetResultAsync()
        {
            return ReadToEndAsync();
        }
    }
}
