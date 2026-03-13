using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

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
        public class SubmoduleSelectionItem : ObservableObject
        {
            public string Path
            {
                get;
            }

            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }

            public SubmoduleSelectionItem(string path, bool isSelected)
            {
                Path = path;
                _isSelected = isSelected;
            }

            private bool _isSelected;
        }

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

        public bool IsSubmoduleSelectionVisible => _kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively;
        public bool HasSubmodulesToSelect => SubmoduleSelections.Count > 0;

        public AvaloniaList<SubmoduleSelectionItem> SubmoduleSelections
        {
            get;
        } = [];

        public override bool ShowOptions => _kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively;
        public override double PopupWidth => 1040;
        public override bool AllowCancelWhenRunning => true;
        public override bool AllowContentInteractionWhenRunning => true;

        public override bool CanStartDirectly()
        {
            return _kind != ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively;
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

            if (_kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively)
            {
                var saved = _repo.Settings?.GetRecursiveSubmoduleUpdateTargets() ?? [];
                var savedSet = new HashSet<string>(saved, StringComparer.Ordinal);
                var defaultSelectAll = savedSet.Count == 0;

                foreach (var submodule in _repo.Submodules)
                    SubmoduleSelections.Add(new SubmoduleSelectionItem(submodule.Path, defaultSelectAll || savedSet.Contains(submodule.Path)));
            }
        }

        public void SelectAllSubmodules()
        {
            foreach (var item in SubmoduleSelections)
                item.IsSelected = true;
        }

        public void ClearSubmoduleSelection()
        {
            foreach (var item in SubmoduleSelections)
                item.IsSelected = false;
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
                List<string> selectedTargets = null;
                if (_kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively)
                {
                    selectedTargets = [];
                    foreach (var item in SubmoduleSelections)
                    {
                        if (item.IsSelected)
                            selectedTargets.Add(item.Path);
                    }

                    if (_repo.Settings != null)
                    {
                        _repo.Settings.SetRecursiveSubmoduleUpdateTargets(selectedTargets);
                        await _repo.Settings.SaveAsync();
                    }
                }

                succ = _kind switch
                {
                    ToolbarRecursiveOperationKind.FetchAndPruneRecursively => await _repo.RunFetchRecursivelyAsync(true, log),
                    ToolbarRecursiveOperationKind.FetchRecursively => await _repo.RunFetchRecursivelyAsync(false, log),
                    ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively => await _repo.RunUpdateSubmodulesRecursivelyAsync(log, selectedTargets),
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
