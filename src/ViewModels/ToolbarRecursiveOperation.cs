using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public enum ToolbarRecursiveOperationKind
    {
        PullAndUpdateSubmodulesRecursively,
        PullUpdateAndFetchPruneRecursively,
        FetchAndPruneRecursively,
        FetchRecursively,
        UpdateSubmodulesRecursively,
        RestoreCleanStateRecursively,
    }

    public class ToolbarRecursiveOperation : Popup
    {
        private enum ToolbarRecursiveOperationMode
        {
            Run,
            ConfigureSelectionOnly,
        }

        private enum CombinedSyncPhase
        {
            Pull,
            UpdateSubmodules,
            FetchAndPrune,
        }

        private enum CombinedSyncPhaseState
        {
            Pending,
            Running,
            Succeeded,
            Skipped,
            Failed,
            Canceled,
        }

        internal enum SubmoduleRunState
        {
            Pending,
            Running,
            Succeeded,
            SkippedByUser,
            SkippedAutomatically,
            SkippedNotInitialized,
            Failed,
            Canceled,
        }

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

        public class SubmoduleRunItem : ObservableObject
        {
            public string Path
            {
                get;
            }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (!SetProperty(ref _isSelected, value))
                        return;

                    if (!_hasFinalState)
                        _state = value ? SubmoduleRunState.Pending : SubmoduleRunState.SkippedByUser;

                    OnPropertyChanged(nameof(IsCheckedVisible));
                    OnPropertyChanged(nameof(RowBackground));
                    OnPropertyChanged(nameof(RowBorderBrush));
                    OnPropertyChanged(nameof(PathForeground));
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                    OnPropertyChanged(nameof(SelectionBackground));
                }
            }

            public bool IsCheckedVisible => IsSelected;
            public bool IsStatusVisible => _state is not SubmoduleRunState.Pending and not SubmoduleRunState.Running;
            public bool CanOpenUpdateDiff => _state == SubmoduleRunState.Succeeded;
            public bool IsReadOnlyStatusVisible => IsStatusVisible && !CanOpenUpdateDiff;
            public bool IsRunning => _state == SubmoduleRunState.Running;
            public double HierarchyIndentWidth => Math.Min(36, _hierarchyDepth * 14);
            public Thickness HierarchyIndentMargin => new Thickness(HierarchyIndentWidth, 0, 0, 0);
            public IBrush RowBackground => IsSelected ? s_boardSelectedRowBackgroundBrush : s_boardSkippedRowBackgroundBrush;
            public IBrush RowBorderBrush => IsSelected ? s_boardSelectedRowBorderBrush : s_boardSkippedRowBorderBrush;
            public IBrush PathForeground => IsSelected ? s_boardSelectedPathForegroundBrush : s_boardSkippedPathForegroundBrush;
            public IBrush SelectionBorderBrush => IsSelected ? s_selectionCheckedBorderBrush : s_selectionUncheckedBorderBrush;
            public IBrush SelectionBackground => IsSelected ? s_selectionCheckedBackgroundBrush : s_selectionUncheckedBackgroundBrush;
            public IBrush SelectionCheckBrush => s_selectionCheckBrush;
            public IBrush RunningIndicatorBrush => s_runningIndicatorBrush;
            public string StatusText => _state switch
            {
                SubmoduleRunState.Succeeded => "Done",
                SubmoduleRunState.SkippedByUser => "Skipped",
                SubmoduleRunState.SkippedAutomatically => "No need to update",
                SubmoduleRunState.SkippedNotInitialized => "Not initialized",
                SubmoduleRunState.Failed => "Failed",
                SubmoduleRunState.Canceled => "Canceled",
                _ => string.Empty,
            };
            public IBrush StatusBackground => _state switch
            {
                SubmoduleRunState.Succeeded => s_statusSucceededBackgroundBrush,
                SubmoduleRunState.SkippedByUser => s_statusSkippedByUserBackgroundBrush,
                SubmoduleRunState.SkippedAutomatically => s_statusSkippedAutomaticallyBackgroundBrush,
                SubmoduleRunState.SkippedNotInitialized => s_statusNotInitializedBackgroundBrush,
                SubmoduleRunState.Failed => s_statusFailedBackgroundBrush,
                SubmoduleRunState.Canceled => s_statusCanceledBackgroundBrush,
                _ => Brushes.Transparent,
            };
            public IBrush StatusBorderBrush => _state switch
            {
                SubmoduleRunState.Succeeded => s_statusSucceededBorderBrush,
                SubmoduleRunState.SkippedByUser => s_statusSkippedByUserBorderBrush,
                SubmoduleRunState.SkippedAutomatically => s_statusSkippedAutomaticallyBorderBrush,
                SubmoduleRunState.SkippedNotInitialized => s_statusNotInitializedBorderBrush,
                SubmoduleRunState.Failed => s_statusFailedBorderBrush,
                SubmoduleRunState.Canceled => s_statusCanceledBorderBrush,
                _ => Brushes.Transparent,
            };
            public IBrush StatusForeground => _state switch
            {
                SubmoduleRunState.Succeeded => s_statusSucceededForegroundBrush,
                SubmoduleRunState.SkippedByUser => s_statusSkippedByUserForegroundBrush,
                SubmoduleRunState.SkippedAutomatically => s_statusSkippedAutomaticallyForegroundBrush,
                SubmoduleRunState.SkippedNotInitialized => s_statusNotInitializedForegroundBrush,
                SubmoduleRunState.Failed => s_statusFailedForegroundBrush,
                SubmoduleRunState.Canceled => s_statusCanceledForegroundBrush,
                _ => Brushes.Transparent,
            };
            public string StatusToolTip => CanOpenUpdateDiff ? "Show diff from before update" : null;
            public string RepositoryPath { get; private set; } = string.Empty;
            public string BeforeRevision { get; private set; } = string.Empty;
            public string AfterRevision { get; private set; } = string.Empty;

            public SubmoduleRunItem(string path, bool isSelected, int hierarchyDepth = 0)
            {
                Path = path;
                _hierarchyDepth = Math.Max(0, hierarchyDepth);
                _isSelected = isSelected;
                _state = isSelected ? SubmoduleRunState.Pending : SubmoduleRunState.SkippedByUser;
            }

            internal void SetState(SubmoduleRunState state)
            {
                if (_state == state)
                    return;

                _state = state;
                _hasFinalState = state is not SubmoduleRunState.Pending and not SubmoduleRunState.Running;
                if (state != SubmoduleRunState.Succeeded)
                    ClearUpdateDiff();

                OnPropertyChanged(nameof(IsStatusVisible));
                OnPropertyChanged(nameof(CanOpenUpdateDiff));
                OnPropertyChanged(nameof(IsReadOnlyStatusVisible));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBackground));
                OnPropertyChanged(nameof(StatusBorderBrush));
                OnPropertyChanged(nameof(StatusForeground));
                OnPropertyChanged(nameof(StatusToolTip));
            }

            internal void SetUpdateDiff(string repositoryPath, string beforeRevision, string afterRevision)
            {
                RepositoryPath = repositoryPath ?? string.Empty;
                BeforeRevision = beforeRevision ?? string.Empty;
                AfterRevision = afterRevision ?? string.Empty;
            }

            private bool _isSelected;
            private bool _hasFinalState;
            private SubmoduleRunState _state;
            private readonly int _hierarchyDepth;

            private void ClearUpdateDiff()
            {
                RepositoryPath = string.Empty;
                BeforeRevision = string.Empty;
                AfterRevision = string.Empty;
            }
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

        public bool CanCancelOperation => _runCancellation is { IsCancellationRequested: false };
        public bool IsLogVisible => _mode == ToolbarRecursiveOperationMode.Run && Log != null;
        public bool AreOperationButtonsVisible => _mode == ToolbarRecursiveOperationMode.Run && Log != null;
        public bool KeepWindowOpen
        {
            get => _keepWindowOpen;
            set => SetProperty(ref _keepWindowOpen, value);
        }
        public bool IsFetchModeIndicatorVisible =>
            _mode == ToolbarRecursiveOperationMode.Run &&
            (_kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively ||
             _kind == ToolbarRecursiveOperationKind.FetchRecursively);
        public string FetchModeText => _kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively ? "PRUNE ON" : "PRUNE OFF";
        public IBrush FetchModeBackground => _kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively
            ? s_pruneEnabledBackgroundBrush
            : s_pruneDisabledBackgroundBrush;
        public IBrush FetchModeBorderBrush => _kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively
            ? s_pruneEnabledBorderBrush
            : s_pruneDisabledBorderBrush;
        public IBrush FetchModeForeground => _kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively
            ? s_pruneEnabledForegroundBrush
            : s_pruneDisabledForegroundBrush;
        public bool IsCombinedSyncPhaseVisible =>
            _mode == ToolbarRecursiveOperationMode.Run &&
            (_kind == ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively ||
             _kind == ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively);
        public bool IsSubmoduleSelectionVisible => _showSubmoduleSelection;
        public bool HasSubmodulesToSelect => SubmoduleSelections.Count > 0;
        public bool ShowEmbeddedHeader
        {
            get => _showEmbeddedHeader;
            set => SetProperty(ref _showEmbeddedHeader, value);
        }
        public bool IsSingleOperationSubmoduleProgressVisible =>
            _mode == ToolbarRecursiveOperationMode.Run &&
            !IsCombinedSyncPhaseVisible &&
            _currentSubmoduleTotal > 0 &&
            !string.IsNullOrEmpty(_currentSubmoduleName);
        public bool IsSubmodulePhaseDetailsVisible =>
            IsCombinedSyncPhaseVisible &&
            _currentCombinedPhase == CombinedSyncPhase.UpdateSubmodules &&
            !string.IsNullOrEmpty(_currentSubmoduleName);
        public bool IsSubmoduleProgressBoardVisible =>
            IsSubmoduleOperationKind &&
            SubmoduleRunItems.Count > 0;
        public bool PreferTallWindow => IsSubmoduleProgressBoardVisible;
        public int SubmoduleBoardRowSpan => IsLogVisible ? 1 : 3;
        public double SubmoduleBoardMaxHeight => IsLogVisible ? 380 : 720;
        public bool ShowSelectionActionButtons => _showSubmoduleSelection && Log == null;
        public bool ShowCloseAfterCompletion => _mode == ToolbarRecursiveOperationMode.Run && Log != null && _summaryFailed == 0;
        public bool ShowRetryAfterCompletion => _mode == ToolbarRecursiveOperationMode.Run && Log != null && _summaryFailed > 0;
        public bool IsLegacyCombinedSyncPhaseVisible => IsCombinedSyncPhaseVisible && !IsSubmoduleProgressBoardVisible;
        public bool IsLegacySingleOperationSubmoduleProgressVisible =>
            IsSingleOperationSubmoduleProgressVisible &&
            !IsSubmoduleProgressBoardVisible;
        public bool CanEditSubmoduleSelection => _showSubmoduleSelection && !_submoduleSelectionLocked;
        public bool AreSubmoduleStatusesVisible => _submoduleSelectionLocked;
        public bool IsOperationSummaryVisible => _mode == ToolbarRecursiveOperationMode.Run && _summaryTotal > 0;
        public string CurrentSubmoduleName => _currentSubmoduleName;
        public string CurrentSubmoduleProgressText => _currentSubmoduleTotal <= 0 ? string.Empty : $"{_currentSubmoduleDone}/{_currentSubmoduleTotal}";
        public string OperationSummaryTitle => _kind == ToolbarRecursiveOperationKind.FetchRecursively || _kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively
            ? "Fetch summary"
            : "Submodule summary";
        public string OperationSummaryTotalText => $"{_summaryTotal} selected";
        public string OperationSummarySucceededText => $"{_summarySucceeded} done";
        public IReadOnlyList<int> OperationSummarySucceededDots => BuildSummaryDots(_summarySucceeded);
        public string OperationSummarySkippedByUserText => $"{_summarySkippedByUser} skipped by user";
        public IReadOnlyList<int> OperationSummarySkippedByUserDots => BuildSummaryDots(_summarySkippedByUser);
        public string OperationSummarySkippedAutomaticallyText =>
            _kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively ||
            _kind == ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively ||
            _kind == ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively
                ? $"{_summarySkippedAutomatically} no need to update"
                : $"{_summarySkippedAutomatically} skipped automatically";
        public IReadOnlyList<int> OperationSummarySkippedAutomaticallyDots => BuildSummaryDots(_summarySkippedAutomatically);
        public bool IsNotInitializedSummaryVisible =>
            _kind == ToolbarRecursiveOperationKind.FetchRecursively ||
            _kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively ||
            _kind == ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively ||
            _summarySkippedNotInitialized > 0;
        public string OperationSummarySkippedNotInitializedText => $"{_summarySkippedNotInitialized} not initialized";
        public IReadOnlyList<int> OperationSummarySkippedNotInitializedDots => BuildSummaryDots(_summarySkippedNotInitialized);
        public string OperationSummaryFailedText => $"{_summaryFailed} failed";
        public IReadOnlyList<int> OperationSummaryFailedDots => BuildSummaryDots(_summaryFailed);

        public IBrush PullPhaseBackground => GetPhaseBackground(CombinedSyncPhase.Pull);
        public IBrush PullPhaseBorderBrush => GetPhaseBorderBrush(CombinedSyncPhase.Pull);
        public IBrush PullPhaseForeground => GetPhaseForeground(CombinedSyncPhase.Pull);
        public double PullPhaseOpacity => GetPhaseOpacity(CombinedSyncPhase.Pull);
        public string PullPhaseStatusText => GetPhaseStatusText(CombinedSyncPhase.Pull);
        public IBrush PullPhaseStatusForeground => GetPhaseStatusForeground(CombinedSyncPhase.Pull);
        public IBrush UpdateSubmodulesPhaseBackground => GetPhaseBackground(CombinedSyncPhase.UpdateSubmodules);
        public IBrush UpdateSubmodulesPhaseBorderBrush => GetPhaseBorderBrush(CombinedSyncPhase.UpdateSubmodules);
        public IBrush UpdateSubmodulesPhaseForeground => GetPhaseForeground(CombinedSyncPhase.UpdateSubmodules);
        public double UpdateSubmodulesPhaseOpacity => GetPhaseOpacity(CombinedSyncPhase.UpdateSubmodules);
        public string UpdateSubmodulesPhaseStatusText => GetPhaseStatusText(CombinedSyncPhase.UpdateSubmodules);
        public IBrush UpdateSubmodulesPhaseStatusForeground => GetPhaseStatusForeground(CombinedSyncPhase.UpdateSubmodules);
        public IBrush FetchAndPrunePhaseBackground => GetPhaseBackground(CombinedSyncPhase.FetchAndPrune);
        public IBrush FetchAndPrunePhaseBorderBrush => GetPhaseBorderBrush(CombinedSyncPhase.FetchAndPrune);
        public IBrush FetchAndPrunePhaseForeground => GetPhaseForeground(CombinedSyncPhase.FetchAndPrune);
        public double FetchAndPrunePhaseOpacity => GetPhaseOpacity(CombinedSyncPhase.FetchAndPrune);
        public string FetchAndPrunePhaseStatusText => GetPhaseStatusText(CombinedSyncPhase.FetchAndPrune);
        public IBrush FetchAndPrunePhaseStatusForeground => GetPhaseStatusForeground(CombinedSyncPhase.FetchAndPrune);

        public AvaloniaList<SubmoduleSelectionItem> SubmoduleSelections
        {
            get;
        } = [];

        public AvaloniaList<SubmoduleRunItem> SubmoduleRunItems
        {
            get;
        } = [];

        public SubmoduleSelectionItem SelectedSubmoduleSelection
        {
            get => _selectedSubmoduleSelection;
            set => SetProperty(ref _selectedSubmoduleSelection, value);
        }

        public override bool ShowOptions => _showSubmoduleSelection;
        public override double PopupWidth => 1040;
        public override bool AllowCancelWhenRunning => true;
        public override bool AllowContentInteractionWhenRunning => true;

        public override bool CanStartDirectly()
        {
            return !_showSubmoduleSelection;
        }

        public void OpenSubmoduleUpdateDiff(SubmoduleRunItem item)
        {
            if (item == null || !item.CanOpenUpdateDiff)
                return;

            if (string.IsNullOrWhiteSpace(item.RepositoryPath) ||
                string.IsNullOrWhiteSpace(item.BeforeRevision) ||
                string.IsNullOrWhiteSpace(item.AfterRevision))
            {
                _repo.SendNotification($"No previous checkout is available to compare for `{item.Path}`.");
                return;
            }

            if (string.Equals(item.BeforeRevision, item.AfterRevision, StringComparison.OrdinalIgnoreCase))
            {
                _repo.SendNotification($"No revision difference is available for `{item.Path}`.");
                return;
            }

            App.ShowWindow(new Compare(
                item.RepositoryPath,
                item.BeforeRevision,
                item.AfterRevision,
                $"Before {ShortRevision(item.BeforeRevision)}",
                $"After {ShortRevision(item.AfterRevision)}"));
        }

        private bool IsSubmoduleOperationKind =>
            _kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively ||
            _kind == ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively ||
            _kind == ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively;

        public ToolbarRecursiveOperation(Repository repo, ToolbarRecursiveOperationKind kind, bool forceChooseSubmodules = false, bool configureSelectionOnly = false)
        {
            _repo = repo;
            _kind = kind;
            _mode = configureSelectionOnly ? ToolbarRecursiveOperationMode.ConfigureSelectionOnly : ToolbarRecursiveOperationMode.Run;
            _showSubmoduleSelection =
                kind == ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively ||
                (kind == ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively &&
                    repo.Submodules.Count > 0 &&
                    (forceChooseSubmodules || repo.Settings?.NeedsRecursiveSubmoduleUpdateTargetsConfiguration() == true)) ||
                (kind == ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively &&
                    repo.Submodules.Count > 0 &&
                    (forceChooseSubmodules || repo.Settings?.NeedsRecursiveSubmoduleUpdateTargetsConfiguration() == true));

            Title = _mode switch
            {
                ToolbarRecursiveOperationMode.ConfigureSelectionOnly => "Choose submodules for Sync All",
                _ => kind switch
                {
                    ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively => "Pull + Submodules",
                    ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively => "Pull + Submodules + F+Prune",
                    ToolbarRecursiveOperationKind.FetchAndPruneRecursively => App.Text("Repository.FetchAndPruneRecursively"),
                    ToolbarRecursiveOperationKind.FetchRecursively => App.Text("Repository.FetchRecursively"),
                    ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively => App.Text("Repository.UpdateSubmodulesRecursively"),
                    ToolbarRecursiveOperationKind.RestoreCleanStateRecursively => "Restore Clean State",
                    _ => "Operation",
                },
            };

            Description = _mode switch
            {
                ToolbarRecursiveOperationMode.ConfigureSelectionOnly => "Choose which submodules Sync All should update. This selection is remembered for this repository.",
                _ => kind switch
                {
                    ToolbarRecursiveOperationKind.RestoreCleanStateRecursively => "Dangerous: discards tracked changes in the parent repository and initialized submodules, keeps untracked files, and restores submodules to the parent-recorded commits.",
                    _ => $"Live git output with syntax highlighting. Auto-closes in {GetAutoCloseCountdownSeconds()} seconds after success unless you stop countdown.",
                },
            };
            ResetCombinedPhaseStates();

            if (_showSubmoduleSelection)
            {
                var saved = _repo.Settings?.GetRecursiveSubmoduleUpdateTargets() ?? [];
                var savedSet = new HashSet<string>(saved, StringComparer.Ordinal);
                var defaultSelectAll = savedSet.Count == 0;
                var submodules = GetSubmodulesForSelection();
                var hierarchyDepths = BuildSubmoduleHierarchyDepths(submodules);

                foreach (var submodule in submodules)
                {
                    hierarchyDepths.TryGetValue(submodule.Path, out var depth);
                    SubmoduleSelections.Add(new SubmoduleSelectionItem(submodule.Path, defaultSelectAll || savedSet.Contains(submodule.Path)));
                    SubmoduleRunItems.Add(new SubmoduleRunItem(submodule.Path, defaultSelectAll || savedSet.Contains(submodule.Path), depth));
                }

                if (SubmoduleSelections.Count > 0)
                    SelectedSubmoduleSelection = SubmoduleSelections[0];
            }
        }

        public void SelectAllSubmodules()
        {
            foreach (var item in SubmoduleSelections)
                item.IsSelected = true;

            foreach (var item in SubmoduleRunItems)
                item.IsSelected = true;
        }

        public void ClearSubmoduleSelection()
        {
            foreach (var item in SubmoduleSelections)
                item.IsSelected = false;

            foreach (var item in SubmoduleRunItems)
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

        public void CancelOperation()
        {
            if (_runCancellation is not { IsCancellationRequested: false })
                return;

            _cancelRequested = true;
            ProgressDescription = "Canceling...";
            _runCancellation.Cancel();
            OnPropertyChanged(nameof(CanCancelOperation));
        }

        public override async Task<bool> Sure()
        {
            ProgressDescription = $"Running: {Title}";
            CanStopCountdown = false;
            _cancelRequested = false;
            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            OnPropertyChanged(nameof(CanCancelOperation));
            _submoduleSelectionLocked = true;
            OnPropertyChanged(nameof(CanEditSubmoduleSelection));
            OnPropertyChanged(nameof(AreSubmoduleStatusesVisible));
            ResetSubmodulePhaseProgress();
            ResetOperationSummary();
            ResetCombinedPhaseStates();

            if (IsCombinedSyncPhaseVisible)
                SetPhaseState(CombinedSyncPhase.Pull, CombinedSyncPhaseState.Running);

            if (IsCombinedSyncPhaseVisible)
                StartPhaseBlinking();

            CommandLog log = null;
            if (_mode == ToolbarRecursiveOperationMode.Run)
            {
                log = _repo.CreateLog(Title);
                Log = log;
                OnPropertyChanged(nameof(IsLogVisible));
                OnPropertyChanged(nameof(AreOperationButtonsVisible));
                OnPropertyChanged(nameof(SubmoduleBoardRowSpan));
                OnPropertyChanged(nameof(SubmoduleBoardMaxHeight));
                OnPropertyChanged(nameof(ShowSelectionActionButtons));
                OnPropertyChanged(nameof(ShowCloseAfterCompletion));
                OnPropertyChanged(nameof(ShowRetryAfterCompletion));
                Use(log);
            }

            bool succ;
            try
            {
                List<string> selectedTargets = null;
                if (_showSubmoduleSelection)
                {
                    selectedTargets = [];
                    foreach (var item in SubmoduleRunItems)
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
                else if (
                    _kind == ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively ||
                    _kind == ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively ||
                    ((_kind == ToolbarRecursiveOperationKind.FetchAndPruneRecursively ||
                      _kind == ToolbarRecursiveOperationKind.FetchRecursively) &&
                     _repo.Settings?.HasConfiguredRecursiveSubmoduleUpdateTargets == true))
                {
                    selectedTargets = _repo.Settings?.GetRecursiveSubmoduleUpdateTargets() ?? [];
                }

                if (_mode == ToolbarRecursiveOperationMode.ConfigureSelectionOnly)
                {
                    App.SendNotification(_repo.FullPath, "Sync All submodule selection saved.");
                    return true;
                }

                InitializeSubmoduleRunItems(selectedTargets);

                succ = _kind switch
                {
                    ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively => await _repo.RunPullAndUpdateSubmodulesRecursivelyAsync(log, SetCombinedPhase, selectedTargets, _runCancellation.Token, UpdateSubmoduleProgress),
                    ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively => await _repo.RunPullUpdateAndFetchPruneRecursivelyAsync(log, SetCombinedPhase, selectedTargets, _runCancellation.Token, UpdateSubmoduleProgress),
                    ToolbarRecursiveOperationKind.FetchAndPruneRecursively => await _repo.RunFetchRecursivelyAsync(true, log, false, selectedTargets, _runCancellation.Token, UpdateSubmoduleProgress),
                    ToolbarRecursiveOperationKind.FetchRecursively => await _repo.RunFetchRecursivelyAsync(false, log, false, selectedTargets, _runCancellation.Token, UpdateSubmoduleProgress),
                    ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively => await _repo.RunUpdateSubmodulesRecursivelyAsync(log, selectedTargets, false, _runCancellation.Token, UpdateSubmoduleProgress),
                    ToolbarRecursiveOperationKind.RestoreCleanStateRecursively => await _repo.RunRestoreCleanStateRecursivelyAsync(log, _runCancellation.Token),
                    _ => false,
                };
            }
            finally
            {
                StopPhaseBlinking();
                _runCancellation?.Dispose();
                _runCancellation = null;
                OnPropertyChanged(nameof(CanCancelOperation));
                if (_mode == ToolbarRecursiveOperationMode.Run)
                    log?.Complete();
            }

            if (_cancelRequested)
            {
                MarkRunningSubmoduleCanceled();
                if (IsCombinedSyncPhaseVisible)
                    SetPhaseState(_currentCombinedPhase, CombinedSyncPhaseState.Canceled);

                ProgressDescription = "Canceled.";
                return true;
            }

            if (!succ)
            {
                if (IsCombinedSyncPhaseVisible)
                    SetPhaseState(_currentCombinedPhase, CombinedSyncPhaseState.Failed);

                ProgressDescription = "Failed. Review the log output above.";
                return false;
            }

            if (IsCombinedSyncPhaseVisible)
                SetPhaseState(_currentCombinedPhase, CombinedSyncPhaseState.Succeeded);

            if (KeepWindowOpen)
            {
                ProgressDescription = "Done. Pinned open.";
                return false;
            }

            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();
            try
            {
                var countdownSeconds = GetAutoCloseCountdownSeconds();
                CanStopCountdown = true;
                for (var seconds = countdownSeconds; seconds > 0; seconds--)
                {
                    var desc = $"Done. Closing in {seconds}s...";
                    if (Dispatcher.UIThread.CheckAccess())
                        ProgressDescription = desc;
                    else
                        await Dispatcher.UIThread.InvokeAsync(() => ProgressDescription = desc);

                    await Task.Delay(1000, _countdownCts.Token).ConfigureAwait(false);
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

        private void SetCombinedPhase(int phase)
        {
            var mapped = phase switch
            {
                1 => CombinedSyncPhase.UpdateSubmodules,
                2 => CombinedSyncPhase.FetchAndPrune,
                _ => CombinedSyncPhase.Pull,
            };

            if (Dispatcher.UIThread.CheckAccess())
                ApplyCombinedPhase(mapped);
            else
                Dispatcher.UIThread.Post(() => ApplyCombinedPhase(mapped));
        }

        private void ApplyCombinedPhase(CombinedSyncPhase phase)
        {
            if (_currentCombinedPhase == phase && GetPhaseState(phase) == CombinedSyncPhaseState.Running)
                return;

            if (GetPhaseState(_currentCombinedPhase) == CombinedSyncPhaseState.Running)
                SetPhaseState(_currentCombinedPhase, CombinedSyncPhaseState.Succeeded, false);

            _currentCombinedPhase = phase;
            SetPhaseState(phase, CombinedSyncPhaseState.Running, false);
            NotifyAllPhaseProperties();
        }

        private CombinedSyncPhaseState GetPhaseState(CombinedSyncPhase phase)
        {
            return phase switch
            {
                CombinedSyncPhase.Pull => _pullPhaseState,
                CombinedSyncPhase.UpdateSubmodules => _updateSubmodulesPhaseState,
                _ => _fetchAndPrunePhaseState,
            };
        }

        private void SetPhaseState(CombinedSyncPhase phase, CombinedSyncPhaseState state, bool notify = true)
        {
            switch (phase)
            {
                case CombinedSyncPhase.Pull:
                    _pullPhaseState = state;
                    break;
                case CombinedSyncPhase.UpdateSubmodules:
                    _updateSubmodulesPhaseState = state;
                    break;
                default:
                    _fetchAndPrunePhaseState = state;
                    break;
            }

            if (notify)
                NotifyAllPhaseProperties();
        }

        private void ResetCombinedPhaseStates()
        {
            _currentCombinedPhase = CombinedSyncPhase.Pull;
            _pullPhaseState = CombinedSyncPhaseState.Pending;
            _updateSubmodulesPhaseState = CombinedSyncPhaseState.Pending;
            _fetchAndPrunePhaseState = _kind == ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively
                ? CombinedSyncPhaseState.Skipped
                : CombinedSyncPhaseState.Pending;
            NotifyAllPhaseProperties();
        }

        private void NotifyAllPhaseProperties()
        {
            OnPropertyChanged(nameof(PullPhaseBackground));
            OnPropertyChanged(nameof(PullPhaseBorderBrush));
            OnPropertyChanged(nameof(PullPhaseForeground));
            OnPropertyChanged(nameof(PullPhaseOpacity));
            OnPropertyChanged(nameof(PullPhaseStatusText));
            OnPropertyChanged(nameof(PullPhaseStatusForeground));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseBackground));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseBorderBrush));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseForeground));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseOpacity));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseStatusText));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseStatusForeground));
            OnPropertyChanged(nameof(FetchAndPrunePhaseBackground));
            OnPropertyChanged(nameof(FetchAndPrunePhaseBorderBrush));
            OnPropertyChanged(nameof(FetchAndPrunePhaseForeground));
            OnPropertyChanged(nameof(FetchAndPrunePhaseOpacity));
            OnPropertyChanged(nameof(FetchAndPrunePhaseStatusText));
            OnPropertyChanged(nameof(FetchAndPrunePhaseStatusForeground));
            OnPropertyChanged(nameof(IsSubmodulePhaseDetailsVisible));
            OnPropertyChanged(nameof(IsLegacyCombinedSyncPhaseVisible));
        }

        private IBrush GetPhaseBackground(CombinedSyncPhase phase)
        {
            return GetPhaseState(phase) switch
            {
                CombinedSyncPhaseState.Running => s_activePhaseBackgroundBrush,
                CombinedSyncPhaseState.Succeeded => s_successPhaseBackgroundBrush,
                CombinedSyncPhaseState.Skipped => s_inactivePhaseBackgroundBrush,
                CombinedSyncPhaseState.Failed => s_failedPhaseBackgroundBrush,
                CombinedSyncPhaseState.Canceled => s_canceledPhaseBackgroundBrush,
                _ => s_inactivePhaseBackgroundBrush,
            };
        }

        private IBrush GetPhaseBorderBrush(CombinedSyncPhase phase)
        {
            return GetPhaseState(phase) switch
            {
                CombinedSyncPhaseState.Running => s_activePhaseBorderBrush,
                CombinedSyncPhaseState.Succeeded => s_successPhaseBorderBrush,
                CombinedSyncPhaseState.Skipped => s_inactivePhaseBorderBrush,
                CombinedSyncPhaseState.Failed => s_failedPhaseBorderBrush,
                CombinedSyncPhaseState.Canceled => s_canceledPhaseBorderBrush,
                _ => s_inactivePhaseBorderBrush,
            };
        }

        private IBrush GetPhaseForeground(CombinedSyncPhase phase)
        {
            return GetPhaseState(phase) switch
            {
                CombinedSyncPhaseState.Running => s_activePhaseForegroundBrush,
                CombinedSyncPhaseState.Succeeded => s_successPhaseForegroundBrush,
                CombinedSyncPhaseState.Skipped => s_inactivePhaseForegroundBrush,
                CombinedSyncPhaseState.Failed => s_failedPhaseForegroundBrush,
                CombinedSyncPhaseState.Canceled => s_canceledPhaseForegroundBrush,
                _ => s_inactivePhaseForegroundBrush,
            };
        }

        private double GetPhaseOpacity(CombinedSyncPhase phase)
        {
            if (GetPhaseState(phase) != CombinedSyncPhaseState.Running)
                return 1.0;

            return 0.62 + 0.38 * (0.5 + 0.5 * Math.Sin(_phasePulse));
        }

        private string GetPhaseStatusText(CombinedSyncPhase phase)
        {
            return GetPhaseState(phase) switch
            {
                CombinedSyncPhaseState.Running => "running",
                CombinedSyncPhaseState.Succeeded => "done",
                CombinedSyncPhaseState.Skipped => "skipped",
                CombinedSyncPhaseState.Failed => "failed",
                CombinedSyncPhaseState.Canceled => "canceled",
                _ => "pending",
            };
        }

        private IBrush GetPhaseStatusForeground(CombinedSyncPhase phase)
        {
            return GetPhaseState(phase) switch
            {
                CombinedSyncPhaseState.Running => s_runningPhaseStatusBrush,
                CombinedSyncPhaseState.Succeeded => s_successPhaseStatusBrush,
                CombinedSyncPhaseState.Skipped => s_pendingPhaseStatusBrush,
                CombinedSyncPhaseState.Failed => s_failedPhaseStatusBrush,
                CombinedSyncPhaseState.Canceled => s_canceledPhaseStatusBrush,
                _ => s_pendingPhaseStatusBrush,
            };
        }

        private void StartPhaseBlinking()
        {
            StopPhaseBlinking();
            _phasePulse = 0;
            _phaseBlinkTimer = DispatcherTimer.Run(() =>
            {
                _phasePulse += 0.35;
                OnPropertyChanged(nameof(PullPhaseOpacity));
                OnPropertyChanged(nameof(UpdateSubmodulesPhaseOpacity));
                OnPropertyChanged(nameof(FetchAndPrunePhaseOpacity));
                return true;
            }, TimeSpan.FromMilliseconds(90));
        }

        private void StopPhaseBlinking()
        {
            _phaseBlinkTimer?.Dispose();
            _phaseBlinkTimer = null;
            _phasePulse = Math.PI / 2;
            OnPropertyChanged(nameof(PullPhaseOpacity));
            OnPropertyChanged(nameof(UpdateSubmodulesPhaseOpacity));
            OnPropertyChanged(nameof(FetchAndPrunePhaseOpacity));
        }

        private void UpdateSubmoduleProgress(Models.RecursiveOperationProgress progress)
        {
            if (progress == null)
                return;

            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplySubmoduleProgress(progress);
            }
            else
            {
                Dispatcher.UIThread.Post(() => ApplySubmoduleProgress(progress));
            }
        }

        private void ApplySubmoduleProgress(Models.RecursiveOperationProgress progress)
        {
            var previousSkippedAutomatically = _summarySkippedAutomatically;
            var previousSkippedNotInitialized = _summarySkippedNotInitialized;
            _summaryTotal = progress.Total;
            _summarySucceeded = progress.Succeeded;
            _summarySkippedByUser = progress.SkippedByUser;
            _summarySkippedAutomatically = progress.SkippedAutomatically;
            _summarySkippedNotInitialized = progress.SkippedNotInitialized;
            _summaryFailed = progress.Failed;
            _currentSubmoduleDone = progress.Succeeded + progress.SkippedAutomatically + progress.SkippedNotInitialized + progress.Failed;
            _currentSubmoduleTotal = progress.Total;
            _currentSubmoduleName = progress.CurrentTarget ?? string.Empty;
            ApplySubmoduleRunItemState(progress, previousSkippedAutomatically, previousSkippedNotInitialized);
            OnPropertyChanged(nameof(IsOperationSummaryVisible));
            OnPropertyChanged(nameof(OperationSummaryTitle));
            OnPropertyChanged(nameof(OperationSummaryTotalText));
            OnPropertyChanged(nameof(OperationSummarySucceededText));
            OnPropertyChanged(nameof(OperationSummarySucceededDots));
            OnPropertyChanged(nameof(OperationSummarySkippedByUserText));
            OnPropertyChanged(nameof(OperationSummarySkippedByUserDots));
            OnPropertyChanged(nameof(OperationSummarySkippedAutomaticallyText));
            OnPropertyChanged(nameof(OperationSummarySkippedAutomaticallyDots));
            OnPropertyChanged(nameof(IsNotInitializedSummaryVisible));
            OnPropertyChanged(nameof(OperationSummarySkippedNotInitializedText));
            OnPropertyChanged(nameof(OperationSummarySkippedNotInitializedDots));
            OnPropertyChanged(nameof(OperationSummaryFailedText));
            OnPropertyChanged(nameof(OperationSummaryFailedDots));
            OnPropertyChanged(nameof(ShowCloseAfterCompletion));
            OnPropertyChanged(nameof(ShowRetryAfterCompletion));
            OnPropertyChanged(nameof(CurrentSubmoduleName));
            OnPropertyChanged(nameof(CurrentSubmoduleProgressText));
            OnPropertyChanged(nameof(IsSubmodulePhaseDetailsVisible));
            OnPropertyChanged(nameof(IsSingleOperationSubmoduleProgressVisible));
            OnPropertyChanged(nameof(IsLegacySingleOperationSubmoduleProgressVisible));
        }

        private void ResetSubmodulePhaseProgress()
        {
            _currentSubmoduleDone = 0;
            _currentSubmoduleTotal = 0;
            _currentSubmoduleName = string.Empty;
            OnPropertyChanged(nameof(CurrentSubmoduleName));
            OnPropertyChanged(nameof(CurrentSubmoduleProgressText));
            OnPropertyChanged(nameof(IsSubmodulePhaseDetailsVisible));
            OnPropertyChanged(nameof(IsSingleOperationSubmoduleProgressVisible));
            OnPropertyChanged(nameof(IsLegacySingleOperationSubmoduleProgressVisible));
        }

        private void ResetOperationSummary()
        {
            _summaryTotal = 0;
            _summarySucceeded = 0;
            _summarySkippedByUser = 0;
            _summarySkippedAutomatically = 0;
            _summarySkippedNotInitialized = 0;
            _summaryFailed = 0;
            OnPropertyChanged(nameof(IsOperationSummaryVisible));
            OnPropertyChanged(nameof(OperationSummaryTitle));
            OnPropertyChanged(nameof(OperationSummaryTotalText));
            OnPropertyChanged(nameof(OperationSummarySucceededText));
            OnPropertyChanged(nameof(OperationSummarySucceededDots));
            OnPropertyChanged(nameof(OperationSummarySkippedByUserText));
            OnPropertyChanged(nameof(OperationSummarySkippedByUserDots));
            OnPropertyChanged(nameof(OperationSummarySkippedAutomaticallyText));
            OnPropertyChanged(nameof(OperationSummarySkippedAutomaticallyDots));
            OnPropertyChanged(nameof(IsNotInitializedSummaryVisible));
            OnPropertyChanged(nameof(OperationSummarySkippedNotInitializedText));
            OnPropertyChanged(nameof(OperationSummarySkippedNotInitializedDots));
            OnPropertyChanged(nameof(OperationSummaryFailedText));
            OnPropertyChanged(nameof(OperationSummaryFailedDots));
            OnPropertyChanged(nameof(ShowCloseAfterCompletion));
            OnPropertyChanged(nameof(ShowRetryAfterCompletion));
        }

        private static IReadOnlyList<int> BuildSummaryDots(int count)
        {
            if (count <= 0)
                return [];

            var dots = new int[count];
            for (var i = 0; i < count; i++)
                dots[i] = i;
            return dots;
        }

        private int GetAutoCloseCountdownSeconds()
        {
            var seconds = _repo.Settings?.SuccessfulOperationAutoCloseSeconds ?? 5;
            return Math.Clamp(seconds, 1, 60);
        }

        private void InitializeSubmoduleRunItems(List<string> selectedTargets)
        {
            SubmoduleRunItems.Clear();

            if (!IsSubmoduleOperationKind)
            {
                OnPropertyChanged(nameof(IsSubmoduleProgressBoardVisible));
                return;
            }

            HashSet<string> selectedSet = null;
            if (selectedTargets != null)
                selectedSet = new HashSet<string>(selectedTargets, StringComparer.Ordinal);
            var submodules = GetSubmodulesForSelection();
            var hierarchyDepths = BuildSubmoduleHierarchyDepths(submodules);

            foreach (var submodule in submodules)
            {
                var isSelected = selectedSet == null || selectedSet.Contains(submodule.Path);
                hierarchyDepths.TryGetValue(submodule.Path, out var depth);
                SubmoduleRunItems.Add(new SubmoduleRunItem(submodule.Path, isSelected, depth));
            }

            OnPropertyChanged(nameof(IsSubmoduleProgressBoardVisible));
            OnPropertyChanged(nameof(IsLegacyCombinedSyncPhaseVisible));
            OnPropertyChanged(nameof(IsLegacySingleOperationSubmoduleProgressVisible));
        }

        private void ApplySubmoduleRunItemState(
            Models.RecursiveOperationProgress progress,
            int previousSkippedAutomatically,
            int previousSkippedNotInitialized)
        {
            if (string.IsNullOrEmpty(progress.CurrentTarget))
                return;

            foreach (var item in SubmoduleRunItems)
            {
                if (!string.Equals(item.Path, progress.CurrentTarget, StringComparison.Ordinal))
                    continue;

                switch (progress.CurrentState)
                {
                    case Models.RecursiveOperationTargetState.Running:
                        item.SetState(SubmoduleRunState.Running);
                        break;
                    case Models.RecursiveOperationTargetState.Succeeded:
                        item.SetState(SubmoduleRunState.Succeeded);
                        item.SetUpdateDiff(progress.CurrentRepositoryPath, progress.CurrentBeforeRevision, progress.CurrentAfterRevision);
                        break;
                    case Models.RecursiveOperationTargetState.Failed:
                        item.SetState(SubmoduleRunState.Failed);
                        break;
                    default:
                        item.SetState(progress.SkippedNotInitialized > previousSkippedNotInitialized
                            ? SubmoduleRunState.SkippedNotInitialized
                            : SubmoduleRunState.SkippedAutomatically);
                        break;
                }

                break;
            }
        }

        private static string ShortRevision(string revision)
        {
            if (string.IsNullOrEmpty(revision))
                return string.Empty;

            return revision.Length > 10 ? revision.Substring(0, 10) : revision;
        }

        private List<Models.Submodule> GetSubmodulesForSelection()
        {
            if (_repo.Submodules.Count > 0 || !_repo.MayHaveSubmodules())
                return _repo.Submodules;

            try
            {
                var depth = Preferences.Instance.RecursiveSubmoduleDisplayDepth;
                return new Commands.QuerySubmodules(_repo.FullPath, depth).GetResultAsync().GetAwaiter().GetResult();
            }
            catch
            {
                return _repo.Submodules;
            }
        }

        private static Dictionary<string, int> BuildSubmoduleHierarchyDepths(IReadOnlyList<Models.Submodule> submodules)
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var depths = new Dictionary<string, int>(comparer);

            foreach (var submodule in submodules)
            {
                var depth = 0;
                foreach (var candidate in submodules)
                {
                    if (IsSubmodulePathAncestor(candidate.Path, submodule.Path, comparison))
                        depth++;
                }

                depths[submodule.Path] = depth;
            }

            return depths;
        }

        private static bool IsSubmodulePathAncestor(string maybeAncestor, string path, StringComparison comparison)
        {
            if (string.IsNullOrWhiteSpace(maybeAncestor) ||
                string.IsNullOrWhiteSpace(path) ||
                string.Equals(maybeAncestor, path, comparison))
                return false;

            var prefix = maybeAncestor.EndsWith("/", StringComparison.Ordinal) ? maybeAncestor : $"{maybeAncestor}/";
            return path.StartsWith(prefix, comparison);
        }

        private void MarkRunningSubmoduleCanceled()
        {
            foreach (var item in SubmoduleRunItems)
            {
                if (item.IsRunning)
                    item.SetState(SubmoduleRunState.Canceled);
            }
        }

        private readonly Repository _repo = null;
        private readonly ToolbarRecursiveOperationKind _kind;
        private readonly ToolbarRecursiveOperationMode _mode;
        private readonly bool _showSubmoduleSelection;
        private bool _submoduleSelectionLocked = false;
        private CommandLog _log = null;
        private bool _canStopCountdown = false;
        private CancellationTokenSource _countdownCts = null;
        private CancellationTokenSource _runCancellation = null;
        private CombinedSyncPhase _currentCombinedPhase;
        private CombinedSyncPhaseState _pullPhaseState = CombinedSyncPhaseState.Pending;
        private CombinedSyncPhaseState _updateSubmodulesPhaseState = CombinedSyncPhaseState.Pending;
        private CombinedSyncPhaseState _fetchAndPrunePhaseState = CombinedSyncPhaseState.Pending;
        private IDisposable _phaseBlinkTimer = null;
        private double _phasePulse = Math.PI / 2;
        private bool _cancelRequested = false;
        private int _currentSubmoduleDone = 0;
        private int _currentSubmoduleTotal = 0;
        private string _currentSubmoduleName = string.Empty;
        private int _summaryTotal = 0;
        private int _summarySucceeded = 0;
        private int _summarySkippedByUser = 0;
        private int _summarySkippedAutomatically = 0;
        private int _summarySkippedNotInitialized = 0;
        private int _summaryFailed = 0;
        private bool _showEmbeddedHeader = true;
        private bool _keepWindowOpen = false;
        private SubmoduleSelectionItem _selectedSubmoduleSelection = null;

        private static readonly IBrush s_activePhaseBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#FF23B5D3"), 0.0),
                new GradientStop(Color.Parse("#FF1496B4"), 0.45),
                new GradientStop(Color.Parse("#FF0E7490"), 0.75),
                new GradientStop(Color.Parse("#FF095569"), 1.0),
            ],
        };
        private static readonly IBrush s_activePhaseBorderBrush = new SolidColorBrush(Color.Parse("#FF0A4F63"));
        private static readonly IBrush s_activePhaseForegroundBrush = new SolidColorBrush(Color.Parse("#FFFFE066"));
        private static readonly IBrush s_successPhaseBackgroundBrush = new SolidColorBrush(Color.Parse("#FFE2F8E8"));
        private static readonly IBrush s_successPhaseBorderBrush = new SolidColorBrush(Color.Parse("#FF2F855A"));
        private static readonly IBrush s_successPhaseForegroundBrush = new SolidColorBrush(Color.Parse("#FF1E5F3D"));
        private static readonly IBrush s_failedPhaseBackgroundBrush = new SolidColorBrush(Color.Parse("#FFFCE7E7"));
        private static readonly IBrush s_failedPhaseBorderBrush = new SolidColorBrush(Color.Parse("#FFB91C1C"));
        private static readonly IBrush s_failedPhaseForegroundBrush = new SolidColorBrush(Color.Parse("#FF8B1111"));
        private static readonly IBrush s_canceledPhaseBackgroundBrush = new SolidColorBrush(Color.Parse("#FFFFF2DE"));
        private static readonly IBrush s_canceledPhaseBorderBrush = new SolidColorBrush(Color.Parse("#FFB7791F"));
        private static readonly IBrush s_canceledPhaseForegroundBrush = new SolidColorBrush(Color.Parse("#FF8A5A12"));
        private static readonly IBrush s_inactivePhaseBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#10FFFFFF"), 0.0),
                new GradientStop(Color.Parse("#04FFFFFF"), 1.0),
            ],
        };
        private static readonly IBrush s_inactivePhaseBorderBrush = new SolidColorBrush(Color.Parse("#FFC4C4C4"));
        private static readonly IBrush s_inactivePhaseForegroundBrush = new SolidColorBrush(Color.Parse("#FF7A7A7A"));
        private static readonly IBrush s_pendingPhaseStatusBrush = new SolidColorBrush(Color.Parse("#FF8A8A8A"));
        private static readonly IBrush s_runningPhaseStatusBrush = new SolidColorBrush(Color.Parse("#FF0E7490"));
        private static readonly IBrush s_successPhaseStatusBrush = new SolidColorBrush(Color.Parse("#FF2F855A"));
        private static readonly IBrush s_failedPhaseStatusBrush = new SolidColorBrush(Color.Parse("#FFB91C1C"));
        private static readonly IBrush s_canceledPhaseStatusBrush = new SolidColorBrush(Color.Parse("#FFB7791F"));
        private static readonly IBrush s_pruneEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#1FD13438"));
        private static readonly IBrush s_pruneEnabledBorderBrush = new SolidColorBrush(Color.Parse("#FFD13438"));
        private static readonly IBrush s_pruneEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#FFD13438"));
        private static readonly IBrush s_pruneDisabledBackgroundBrush = new SolidColorBrush(Color.Parse("#140078D7"));
        private static readonly IBrush s_pruneDisabledBorderBrush = new SolidColorBrush(Color.Parse("#FF0078D7"));
        private static readonly IBrush s_pruneDisabledForegroundBrush = new SolidColorBrush(Color.Parse("#FF0078D7"));
        private static readonly IBrush s_boardSelectedRowBackgroundBrush = new SolidColorBrush(Color.Parse("#FFF8FBFF"));
        private static readonly IBrush s_boardSelectedRowBorderBrush = new SolidColorBrush(Color.Parse("#FFD7E7F6"));
        private static readonly IBrush s_boardSkippedRowBackgroundBrush = new SolidColorBrush(Color.Parse("#FFF7F7F7"));
        private static readonly IBrush s_boardSkippedRowBorderBrush = new SolidColorBrush(Color.Parse("#FFE5E7EB"));
        private static readonly IBrush s_boardSelectedPathForegroundBrush = new SolidColorBrush(Color.Parse("#FF0D5D86"));
        private static readonly IBrush s_boardSkippedPathForegroundBrush = new SolidColorBrush(Color.Parse("#FF7A7A7A"));
        private static readonly IBrush s_selectionCheckedBorderBrush = new SolidColorBrush(Color.Parse("#FF0D6E99"));
        private static readonly IBrush s_selectionCheckedBackgroundBrush = new SolidColorBrush(Color.Parse("#FFF4FBFF"));
        private static readonly IBrush s_selectionUncheckedBorderBrush = new SolidColorBrush(Color.Parse("#FF9CA3AF"));
        private static readonly IBrush s_selectionUncheckedBackgroundBrush = new SolidColorBrush(Color.Parse("#00FFFFFF"));
        private static readonly IBrush s_selectionCheckBrush = new SolidColorBrush(Color.Parse("#FF0D6E99"));
        private static readonly IBrush s_runningIndicatorBrush = new SolidColorBrush(Color.Parse("#FF0E7490"));
        private static readonly IBrush s_statusSucceededBackgroundBrush = new SolidColorBrush(Color.Parse("#142F855A"));
        private static readonly IBrush s_statusSucceededBorderBrush = new SolidColorBrush(Color.Parse("#662F855A"));
        private static readonly IBrush s_statusSucceededForegroundBrush = new SolidColorBrush(Color.Parse("#FF2F855A"));
        private static readonly IBrush s_statusSkippedByUserBackgroundBrush = new SolidColorBrush(Color.Parse("#141D6FDD"));
        private static readonly IBrush s_statusSkippedByUserBorderBrush = new SolidColorBrush(Color.Parse("#661D6FDD"));
        private static readonly IBrush s_statusSkippedByUserForegroundBrush = new SolidColorBrush(Color.Parse("#FF1D6FDD"));
        private static readonly IBrush s_statusSkippedAutomaticallyBackgroundBrush = new SolidColorBrush(Color.Parse("#14B7791F"));
        private static readonly IBrush s_statusSkippedAutomaticallyBorderBrush = new SolidColorBrush(Color.Parse("#66B7791F"));
        private static readonly IBrush s_statusSkippedAutomaticallyForegroundBrush = new SolidColorBrush(Color.Parse("#FF8A5A12"));
        private static readonly IBrush s_statusNotInitializedBackgroundBrush = new SolidColorBrush(Color.Parse("#143B82F6"));
        private static readonly IBrush s_statusNotInitializedBorderBrush = new SolidColorBrush(Color.Parse("#663B82F6"));
        private static readonly IBrush s_statusNotInitializedForegroundBrush = new SolidColorBrush(Color.Parse("#FF2563EB"));
        private static readonly IBrush s_statusFailedBackgroundBrush = new SolidColorBrush(Color.Parse("#14B91C1C"));
        private static readonly IBrush s_statusFailedBorderBrush = new SolidColorBrush(Color.Parse("#66B91C1C"));
        private static readonly IBrush s_statusFailedForegroundBrush = new SolidColorBrush(Color.Parse("#FFB91C1C"));
        private static readonly IBrush s_statusCanceledBackgroundBrush = new SolidColorBrush(Color.Parse("#FFFFF2DE"));
        private static readonly IBrush s_statusCanceledBorderBrush = new SolidColorBrush(Color.Parse("#FFB7791F"));
        private static readonly IBrush s_statusCanceledForegroundBrush = new SolidColorBrush(Color.Parse("#FF8A5A12"));
    }
}
