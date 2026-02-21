using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

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

        public bool CanStopCountdown
        {
            get => _canStopCountdown;
            private set => SetProperty(ref _canStopCountdown, value);
        }

        public override bool ShowOptions => false;
        public override double PopupWidth => 980;
        public override bool AllowCancelWhenRunning => true;
        public override bool AllowContentInteractionWhenRunning => true;

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

            Description = "Live git output. Auto-closes in 9 seconds after success unless you stop countdown.";
        }

        public void StopCountdown()
        {
            if (_countdownCts == null)
                return;

            _countdownCts?.Cancel();
            CanStopCountdown = false;
            ProgressDescription = "Done. Auto-close stopped.";
        }

        public override async Task<bool> Sure()
        {
            ProgressDescription = $"Running: {Title}";
            CanStopCountdown = false;

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

            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();
            try
            {
                CanStopCountdown = true;
                for (var seconds = 9; seconds > 0; seconds -= 3)
                {
                    var desc = $"Done. Closing in {seconds}s...";
                    if (Dispatcher.UIThread.CheckAccess())
                        ProgressDescription = desc;
                    else
                        await Dispatcher.UIThread.InvokeAsync(() => ProgressDescription = desc);

                    await Task.Delay(3000, _countdownCts.Token).ConfigureAwait(false);
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                if (Dispatcher.UIThread.CheckAccess())
                    CanStopCountdown = false;
                else
                    await Dispatcher.UIThread.InvokeAsync(() => CanStopCountdown = false);
                _countdownCts?.Dispose();
                _countdownCts = null;
            }
        }

        private readonly Repository _repo = null;
        private readonly ToolbarRecursiveOperationKind _kind;
        private CommandLog _log = null;
        private bool _canStopCountdown = false;
        private CancellationTokenSource _countdownCts = null;
    }
}
