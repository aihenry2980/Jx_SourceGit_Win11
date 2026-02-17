using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public enum ToolbarRecursiveOperationKind
    {
        FetchAndPruneRecursively,
        FetchRecursively,
        UpdateSubmodulesRecursively,
    }

    public class ToolbarRecursiveOperation : Popup
    {
        public string Title
        {
            get;
        }

        public string Description
        {
            get;
        }

        public CommandLog Log
        {
            get => _log;
            private set => SetProperty(ref _log, value);
        }

        public override bool ShowOptions => false;
        public override double PopupWidth => 820;

        public override bool CanStartDirectly()
        {
            return true;
        }

        public ToolbarRecursiveOperation(Repository repo, ToolbarRecursiveOperationKind kind)
        {
            _repo = repo;
            _kind = kind;

            Title = kind switch
            {
                ToolbarRecursiveOperationKind.FetchAndPruneRecursively => App.Text("Repository.FetchAndPruneRecursively"),
                ToolbarRecursiveOperationKind.FetchRecursively => App.Text("Repository.FetchRecursively"),
                ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively => App.Text("Repository.UpdateSubmodulesRecursively"),
                _ => "Operation",
            };

            Description = "Live git output. This popup auto-closes 3 seconds after success.";
        }

        public override async Task<bool> Sure()
        {
            ProgressDescription = $"Running: {Title}";

            var log = _repo.CreateLog(Title);
            Log = log;
            Use(log);

            bool succ;
            try
            {
                succ = _kind switch
                {
                    ToolbarRecursiveOperationKind.FetchAndPruneRecursively => await _repo.RunFetchRecursivelyAsync(true, log),
                    ToolbarRecursiveOperationKind.FetchRecursively => await _repo.RunFetchRecursivelyAsync(false, log),
                    ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively => await _repo.RunUpdateSubmodulesRecursivelyAsync(log),
                    _ => false,
                };
            }
            finally
            {
                log.Complete();
            }

            if (!succ)
            {
                ProgressDescription = "Failed. Review the log output above.";
                return false;
            }

            for (var deciseconds = 30; deciseconds > 0; deciseconds--)
            {
                ProgressDescription = $"Done. Closing in {deciseconds / 10.0:F1}s...";
                await Task.Delay(100);
            }

            return true;
        }

        private readonly Repository _repo = null;
        private readonly ToolbarRecursiveOperationKind _kind;
        private CommandLog _log = null;
    }
}
