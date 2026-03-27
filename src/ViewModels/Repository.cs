using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public partial class Repository : ObservableObject, Models.IRepository
    {
        private const int MAX_LOGS = 100;

        private enum HistoryRefreshMode
        {
            Full,
            FastAfterFetch,
        }

        private class CommitHistorySnapshot
        {
            public List<Models.Commit> Commits { get; set; } = [];
            public Models.CommitGraph Graph { get; set; } = null;
            public bool ShouldNotifyFoldControlChange { get; set; } = false;
        }

        public bool IsBare
        {
            get;
        }

        public string FullPath
        {
            get;
        }

        public string GitDir
        {
            get;
        }

        public Models.RepositorySettings Settings
        {
            get => _settings;
        }

        public Models.RepositoryUIStates UIStates
        {
            get => _uiStates;
        }

        public Models.GitFlow GitFlow
        {
            get;
            set;
        } = new();

        public Models.FilterMode HistoryFilterMode
        {
            get => _historyFilterMode;
            private set => SetProperty(ref _historyFilterMode, value);
        }

        public int IncludedHistoryFilterCount
        {
            get
            {
                if (_uiStates == null)
                    return 0;

                var count = 0;
                foreach (var filter in _uiStates.HistoryFilters)
                {
                    if (filter.Mode == Models.FilterMode.Included)
                        count++;
                }

                return count;
            }
        }

        public int ExcludedHistoryFilterCount
        {
            get
            {
                if (_uiStates == null)
                    return 0;

                var count = 0;
                foreach (var filter in _uiStates.HistoryFilters)
                {
                    if (filter.Mode == Models.FilterMode.Excluded)
                        count++;
                }

                return count;
            }
        }

        public bool CanFoldVisibleBranchesInGraph => _visibleFoldableBranchesCount > _visibleFoldedBranchesCount;

        public bool CanUnfoldBranchesInGraph => _foldedBranchFullNames.Count > 0;

        public bool HasAllowedSignersFile
        {
            get => _hasAllowedSignersFile;
        }

        public int SelectedViewIndex
        {
            get => _selectedViewIndex;
            set
            {
                if (SetProperty(ref _selectedViewIndex, value))
                {
                    if (value == 0 && _isSearchingCommits && IsLeftSidebarCompact)
                        IsLeftSidebarCompact = false;

                    SelectedView = value switch
                    {
                        1 => _workingCopy,
                        2 => _stashesPage,
                        _ => _histories,
                    };
                }
            }
        }

        public object SelectedView
        {
            get => _selectedView;
            set => SetProperty(ref _selectedView, value);
        }

        public bool EnableTopoOrderInHistory
        {
            get => _uiStates.EnableTopoOrderInHistory;
            set
            {
                if (value != _uiStates.EnableTopoOrderInHistory)
                {
                    _uiStates.EnableTopoOrderInHistory = value;
                    RefreshCommits();
                }
            }
        }

        public Models.HistoryShowFlags HistoryShowFlags
        {
            get => _uiStates.HistoryShowFlags;
            private set
            {
                if (value != _uiStates.HistoryShowFlags)
                {
                    _uiStates.HistoryShowFlags = value;
                    RefreshCommits();
                }
            }
        }

        public bool OnlyHighlightCurrentBranchInHistory
        {
            get => _uiStates.OnlyHighlightCurrentBranchInHistory;
            set
            {
                if (value != _uiStates.OnlyHighlightCurrentBranchInHistory)
                {
                    _uiStates.OnlyHighlightCurrentBranchInHistory = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool OnlyShowSPPCommitsInHistory
        {
            get => _uiStates.OnlyShowSPPCommitsInHistory;
            set
            {
                if (value != _uiStates.OnlyShowSPPCommitsInHistory)
                {
                    _uiStates.OnlyShowSPPCommitsInHistory = value;
                    RefreshCommits();
                }
            }
        }

        public bool IsLeftSidebarCompact
        {
            get => _uiStates.IsLeftSidebarCompact;
            set
            {
                if (value != _uiStates.IsLeftSidebarCompact)
                {
                    if (value && _isSearchingCommits)
                        IsSearchingCommits = false;

                    _uiStates.IsLeftSidebarCompact = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SidebarWidth));
                    OnPropertyChanged(nameof(SidebarSplitterWidth));
                }
            }
        }

        public GridLength SidebarWidth
        {
            get => IsLeftSidebarCompact ? new GridLength(68, GridUnitType.Pixel) : Preferences.Instance.Layout.RepositorySidebarWidth;
            set
            {
                if (IsLeftSidebarCompact)
                    return;

                if (Preferences.Instance.Layout.RepositorySidebarWidth != value)
                {
                    Preferences.Instance.Layout.RepositorySidebarWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        public GridLength SidebarSplitterWidth => IsLeftSidebarCompact ? new GridLength(0, GridUnitType.Pixel) : new GridLength(3, GridUnitType.Pixel);

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    RefreshBranchSidebarByCurrentFilters();
                    VisibleTags = BuildVisibleTags();
                    VisibleSubmodules = BuildVisibleSubmodules();
                }
            }
        }

        public List<Models.Remote> Remotes
        {
            get => _remotes;
            private set => SetProperty(ref _remotes, value);
        }

        public List<Models.Branch> Branches
        {
            get => _branches;
            private set
            {
                if (SetProperty(ref _branches, value))
                {
                    _presetBranchFilterMatchCacheVersion++;
                    _presetBranchFilterMatchCache = null;
                }
            }
        }

        public Models.Branch CurrentBranch
        {
            get => _currentBranch;
            private set
            {
                var oldHead = _currentBranch?.Head;
                if (SetProperty(ref _currentBranch, value))
                {
                    if (value != null && oldHead != _currentBranch.Head && _workingCopy is { UseAmend: true })
                        _workingCopy.UseAmend = false;

                    NotifyCurrentBranchVisualChanged();
                }
            }
        }

        public string CurrentBranchDisplayName => CurrentBranch?.FriendlyName ?? "--";

        public string CurrentBranchDisplayLabel => FormatCurrentBranchDisplayLabel(CurrentBranchDisplayName);

        public bool HasSuperProjectPointer => !string.IsNullOrEmpty(_superProjectSubmoduleSHA);

        public Color AccentColor => ResolvePageAccentColor();

        public Color AccentHoveredColor
        {
            get
            {
                var c = AccentColor;
                return Color.FromArgb(0x88, c.R, c.G, c.B);
            }
        }

        public IBrush AccentToolbarBackground
        {
            get
            {
                var c = AccentColor;
                if (Application.Current?.ActualThemeVariant == ThemeVariant.Dark)
                {
                    byte Darken(byte v) => (byte)Math.Clamp((int)Math.Round(v * 0.42 + 0x18 * 0.58), 0, 255);
                    return new SolidColorBrush(Color.FromArgb(0xFF, Darken(c.R), Darken(c.G), Darken(c.B)));
                }

                byte Lighten(byte v) => (byte)(v + (255 - v) * 0.82);
                return new SolidColorBrush(Color.FromArgb(0xFF, Lighten(c.R), Lighten(c.G), Lighten(c.B)));
            }
        }

        public IBrush CurrentBranchDisplayBackground
        {
            get
            {
                if (CurrentBranch == null)
                    return Brushes.Transparent;

                var raw = Color.FromUInt32(ResolveCurrentBranchDisplayColor());
                var alpha = CurrentBranch.IsLocal ? (byte)0xA0 : (byte)0x32;
                return new SolidColorBrush(Color.FromArgb(alpha, raw.R, raw.G, raw.B));
            }
        }

        public IBrush CurrentBranchDisplayForeground
        {
            get
            {
                if (CurrentBranch == null)
                    return Brushes.Black;

                var raw = Color.FromUInt32(ResolveCurrentBranchDisplayColor());
                var luminance = 0.2126 * raw.R + 0.7152 * raw.G + 0.0722 * raw.B;
                return luminance < 130 ? Brushes.White : Brushes.Black;
            }
        }

        public List<BranchTreeNode> LocalBranchTrees
        {
            get => _localBranchTrees;
            private set => SetProperty(ref _localBranchTrees, value);
        }

        public List<BranchTreeNode> RemoteBranchTrees
        {
            get => _remoteBranchTrees;
            private set => SetProperty(ref _remoteBranchTrees, value);
        }

        public List<Models.Worktree> Worktrees
        {
            get => _worktrees;
            private set => SetProperty(ref _worktrees, value);
        }

        public List<Models.Tag> Tags
        {
            get => _tags;
            private set => SetProperty(ref _tags, value);
        }

        public bool ShowTagsAsTree
        {
            get => _uiStates.ShowTagsAsTree;
            set
            {
                if (value != _uiStates.ShowTagsAsTree)
                {
                    _uiStates.ShowTagsAsTree = value;
                    VisibleTags = BuildVisibleTags();
                    OnPropertyChanged();
                }
            }
        }

        public object VisibleTags
        {
            get => _visibleTags;
            private set => SetProperty(ref _visibleTags, value);
        }

        public List<Models.Submodule> Submodules
        {
            get => _submodules;
            private set => SetProperty(ref _submodules, value);
        }

        public bool ShowSubmodulesAsTree
        {
            get => _uiStates.ShowSubmodulesAsTree;
            set
            {
                if (value != _uiStates.ShowSubmodulesAsTree)
                {
                    _uiStates.ShowSubmodulesAsTree = value;
                    VisibleSubmodules = BuildVisibleSubmodules();
                    OnPropertyChanged();
                }
            }
        }

        public object VisibleSubmodules
        {
            get => _visibleSubmodules;
            private set => SetProperty(ref _visibleSubmodules, value);
        }

        public int LocalChangesCount
        {
            get => _localChangesCount;
            private set
            {
                if (SetProperty(ref _localChangesCount, value))
                    NotifyCompactStatusChanged();
            }
        }

        public int StashesCount
        {
            get => _stashesCount;
            private set => SetProperty(ref _stashesCount, value);
        }

        public int LocalBranchesCount
        {
            get => _localBranchesCount;
            private set => SetProperty(ref _localBranchesCount, value);
        }

        public bool IsShowingAllBranches
        {
            get => _isShowingAllBranches;
            private set => SetProperty(ref _isShowingAllBranches, value);
        }

        public bool ShouldShowBranchPresetEmptyState
        {
            get => _shouldShowBranchPresetEmptyState;
            private set => SetProperty(ref _shouldShowBranchPresetEmptyState, value);
        }

        public bool IsPresetBranchFilterEditorExpanded
        {
            get => _isPresetBranchFilterEditorExpanded;
            set
            {
                if (SetProperty(ref _isPresetBranchFilterEditorExpanded, value))
                    UpdateShouldShowBranchPresetEmptyState();
            }
        }

        public string PresetBranchExactNames
        {
            get => _settings?.PresetBranchExactNames ?? string.Empty;
            set
            {
                if (_settings == null)
                    return;

                value ??= string.Empty;
                if (string.Equals(_settings.PresetBranchExactNames, value, StringComparison.Ordinal))
                    return;

                _settings.PresetBranchExactNames = value;
                InvalidatePresetBranchFilterMatchCache();
                OnPropertyChanged();
                OnPropertyChanged(nameof(PresetBranchFilterSummary));
                RebuildPresetBranchExactColorItems();
                SavePresetBranchFilterSettingsAsync();
            }
        }

        public string PresetBranchContainsPatterns
        {
            get => _settings?.PresetBranchContainsPatterns ?? string.Empty;
            set
            {
                if (_settings == null)
                    return;

                value ??= string.Empty;
                if (string.Equals(_settings.PresetBranchContainsPatterns, value, StringComparison.Ordinal))
                    return;

                _settings.PresetBranchContainsPatterns = value;
                InvalidatePresetBranchFilterMatchCache();
                OnPropertyChanged();
                OnPropertyChanged(nameof(PresetBranchFilterSummary));
                SavePresetBranchFilterSettingsAsync();
            }
        }

        public string PresetBranchExcludeNames
        {
            get => _settings?.PresetBranchExcludeNames ?? string.Empty;
            set
            {
                if (_settings == null)
                    return;

                value ??= string.Empty;
                if (string.Equals(_settings.PresetBranchExcludeNames, value, StringComparison.Ordinal))
                    return;

                _settings.PresetBranchExcludeNames = value;
                InvalidatePresetBranchFilterMatchCache();
                OnPropertyChanged();
                OnPropertyChanged(nameof(PresetBranchFilterSummary));
                SavePresetBranchFilterSettingsAsync();
            }
        }

        public string PresetBranchFilterSummary
        {
            get
            {
                var exactCount = _settings?.GetPresetBranchExactNameSet().Count ?? 0;
                var containsCount = _settings?.GetPresetBranchContainsRuleList().Count ?? 0;
                var excludeCount = _settings?.GetPresetBranchExcludeNameSet().Count ?? 0;
                return $"({exactCount}/{containsCount}/{excludeCount})";
            }
        }

        public AvaloniaList<PresetBranchExactColorItem> PresetBranchExactColorItems
        {
            get => _presetBranchExactColorItems;
        }

        public bool HasPresetBranchExactColorItems
        {
            get => _presetBranchExactColorItems.Count > 0;
        }

        public List<PresetBranchColorOption> PresetBranchColorOptions
        {
            get => PRESET_BRANCH_COLOR_OPTIONS;
        }

        public static IReadOnlyList<PresetBranchColorOption> BranchFilterColorOptions
        {
            get => PRESET_BRANCH_COLOR_OPTIONS;
        }

        public bool IncludeUntracked
        {
            get => _uiStates.IncludeUntrackedInLocalChanges;
            set
            {
                if (value != _uiStates.IncludeUntrackedInLocalChanges)
                {
                    _uiStates.IncludeUntrackedInLocalChanges = value;
                    OnPropertyChanged();
                    RefreshWorkingCopyChanges();
                }
            }
        }

        public bool IsSearchingCommits
        {
            get => _isSearchingCommits;
            set
            {
                if (SetProperty(ref _isSearchingCommits, value))
                {
                    if (value && IsLeftSidebarCompact)
                        IsLeftSidebarCompact = false;

                    if (value)
                        SelectedViewIndex = 0;
                    else
                        _searchCommitContext.EndSearch();
                }
            }
        }

        public int CurrentBranchAheadCount => CurrentBranch?.Ahead.Count ?? 0;

        public int CurrentBranchBehindCount => CurrentBranch?.Behind.Count ?? 0;

        public bool HasAheadStatus => CurrentBranchAheadCount > 0;

        public bool HasBehindStatus => CurrentBranchBehindCount > 0;

        public bool HasLocalChangesStatus => LocalChangesCount > 0;

        public bool HasInProgressStatus => InProgressContext != null;

        public bool HasStatusStripItems => HasAheadStatus || HasBehindStatus || HasLocalChangesStatus || HasInProgressStatus;

        public bool ShowCleanStatus => !HasStatusStripItems;

        public string HistoryQuickFindText
        {
            get => _historyQuickFindText;
            set
            {
                value ??= string.Empty;
                if (!SetProperty(ref _historyQuickFindText, value))
                    return;

                QueueHistoryQuickFindApply();
            }
        }

        public string HistoryQuickFindAppliedText
        {
            get => _historyQuickFindAppliedText;
            private set => SetProperty(ref _historyQuickFindAppliedText, value);
        }

        public long HistoryQuickFindFocusRequestId
        {
            get => _historyQuickFindFocusRequestId;
            private set => SetProperty(ref _historyQuickFindFocusRequestId, value);
        }

        public string InProgressStatusText
        {
            get
            {
                return InProgressContext switch
                {
                    RebaseInProgress => "1 rebase in progress",
                    MergeInProgress => "1 merge in progress",
                    CherryPickInProgress => "1 cherry-pick in progress",
                    RevertInProgress => "1 revert in progress",
                    { Name: { Length: > 0 } name } => $"1 {name.ToLowerInvariant()} in progress",
                    _ => string.Empty,
                };
            }
        }

        public SearchCommitContext SearchCommitContext
        {
            get => _searchCommitContext;
        }

        public void RequestHistoryQuickFindFocus()
        {
            if (SelectedViewIndex != 0)
                SelectedViewIndex = 0;

            HistoryQuickFindFocusRequestId++;
        }

        public void ClearHistoryQuickFind()
        {
            HistoryQuickFindText = string.Empty;
        }

        public bool IsLocalBranchGroupExpanded
        {
            get => _uiStates.IsLocalBranchesExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsLocalBranchesExpandedInSideBar)
                {
                    _uiStates.IsLocalBranchesExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsRemoteGroupExpanded
        {
            get => _uiStates.IsRemotesExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsRemotesExpandedInSideBar)
                {
                    _uiStates.IsRemotesExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsTagGroupExpanded
        {
            get => _uiStates.IsTagsExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsTagsExpandedInSideBar)
                {
                    _uiStates.IsTagsExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSubmoduleGroupExpanded
        {
            get => _uiStates.IsSubmodulesExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsSubmodulesExpandedInSideBar)
                {
                    _uiStates.IsSubmodulesExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsWorktreeGroupExpanded
        {
            get => _uiStates.IsWorktreeExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsWorktreeExpandedInSideBar)
                {
                    _uiStates.IsWorktreeExpandedInSideBar = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSortingLocalBranchByName
        {
            get => _uiStates.LocalBranchSortMode == Models.BranchSortMode.Name;
            set
            {
                _uiStates.LocalBranchSortMode = value ? Models.BranchSortMode.Name : Models.BranchSortMode.CommitterDate;
                OnPropertyChanged();

                RefreshBranchSidebarByCurrentFilters();
            }
        }

        public bool IsSortingRemoteBranchByName
        {
            get => _uiStates.RemoteBranchSortMode == Models.BranchSortMode.Name;
            set
            {
                _uiStates.RemoteBranchSortMode = value ? Models.BranchSortMode.Name : Models.BranchSortMode.CommitterDate;
                OnPropertyChanged();

                RefreshBranchSidebarByCurrentFilters();
            }
        }

        public bool IsSortingTagsByName
        {
            get => _uiStates.TagSortMode == Models.TagSortMode.Name;
            set
            {
                _uiStates.TagSortMode = value ? Models.TagSortMode.Name : Models.TagSortMode.CreatorDate;
                OnPropertyChanged();
                VisibleTags = BuildVisibleTags();
            }
        }

        public InProgressContext InProgressContext
        {
            get => _workingCopy?.InProgressContext;
        }

        public Models.BisectState BisectState
        {
            get => _bisectState;
            private set => SetProperty(ref _bisectState, value);
        }

        public bool IsBisectCommandRunning
        {
            get => _isBisectCommandRunning;
            private set => SetProperty(ref _isBisectCommandRunning, value);
        }

        public bool IsAutoFetching
        {
            get => _isAutoFetching;
            private set => SetProperty(ref _isAutoFetching, value);
        }

        public bool IsQuickFetching
        {
            get => _isQuickFetching;
            private set => SetProperty(ref _isQuickFetching, value);
        }

        public bool IsQuickPulling
        {
            get => _isQuickPulling;
            private set => SetProperty(ref _isQuickPulling, value);
        }

        public string AutoBackgroundOperationText
        {
            get => _autoBackgroundOperationText;
            private set => SetProperty(ref _autoBackgroundOperationText, value);
        }

        public bool IsFetchDurationToastVisible
        {
            get => _isFetchDurationToastVisible;
            private set => SetProperty(ref _isFetchDurationToastVisible, value);
        }

        public double FetchDurationToastOpacity
        {
            get => _fetchDurationToastOpacity;
            private set => SetProperty(ref _fetchDurationToastOpacity, value);
        }

        public string FetchDurationToastText
        {
            get => _fetchDurationToastText;
            private set => SetProperty(ref _fetchDurationToastText, value);
        }

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get;
        } = [];

        public AvaloniaList<CommandLog> Logs
        {
            get;
        } = [];

        public Repository(bool isBare, string path, string gitDir, string superProjectSubmoduleSHA = null)
        {
            IsBare = isBare;
            FullPath = path.Replace('\\', '/').TrimEnd('/');
            GitDir = gitDir.Replace('\\', '/').TrimEnd('/');
            _superProjectSubmoduleSHA = NormalizeSubmodulePointerSHA(superProjectSubmoduleSHA);

            var commonDirFile = Path.Combine(GitDir, "commondir");
            var isWorktree = GitDir.IndexOf("/worktrees/", StringComparison.Ordinal) > 0 &&
                          File.Exists(commonDirFile);

            if (isWorktree)
            {
                var commonDir = File.ReadAllText(commonDirFile).Trim();
                if (!Path.IsPathRooted(commonDir))
                    commonDir = new DirectoryInfo(Path.Combine(GitDir, commonDir)).FullName;

                _gitCommonDir = commonDir;
            }
            else
            {
                _gitCommonDir = GitDir;
            }
        }

        public void Open()
        {
            _settings = Models.RepositorySettings.Get(_gitCommonDir);
            _commitHistoryMetadataCache = Models.CommitHistoryMetadataCache.Load(_gitCommonDir);
            _uiStates = Models.RepositoryUIStates.Load(GitDir);
            _foldedBranchFullNames.Clear();
            _visibleFoldableBranchesCount = 0;
            _visibleFoldedBranchesCount = 0;
            NotifyFoldControlsChanged();
            Preferences.Instance.PropertyChanged -= OnPreferencesPropertyChanged;
            Preferences.Instance.PropertyChanged += OnPreferencesPropertyChanged;
            MigrateLegacyPresetBranchFiltersIfNeeded();
            IsShowingAllBranches = false;
            ShouldShowBranchPresetEmptyState = false;
            IsPresetBranchFilterEditorExpanded = true;
            _shouldApplyPresetBranchFilterOnInitialBranchLoad = true;
            RebuildPresetBranchExactColorItems();

            try
            {
                _watcher = new Models.Watcher(this, FullPath, _gitCommonDir);
            }
            catch (Exception ex)
            {
                App.RaiseException(string.Empty, $"Failed to start watcher for repository: '{FullPath}'. You may need to press 'F5' to refresh repository manually!\n\nReason: {ex.Message}");
            }

            _historyFilterMode = _uiStates.GetHistoryFilterMode();
            _histories = new Histories(this);
            _workingCopy = new WorkingCopy(this) { CommitMessage = _uiStates.LastCommitMessage };
            _stashesPage = new StashesPage(this);
            _searchCommitContext = new SearchCommitContext(this);

            if (Preferences.Instance.ShowLocalChangesByDefault)
            {
                _selectedView = _workingCopy;
                _selectedViewIndex = 1;
            }
            else
            {
                _selectedView = _histories;
                _selectedViewIndex = 0;
            }

            _lastFetchTime = DateTime.Now;
            EnsureAutoFetchTimerState();
            RefreshAll();
            RefreshSuperProjectSubmodulePointer();
        }

        public void Close()
        {
            SelectedView = null; // Do NOT modify. Used to remove exists widgets for GC.Collect
            Logs.Clear();

            _historyQuickFindDebounce?.Cancel();
            _historyQuickFindDebounce?.Dispose();
            _historyQuickFindDebounce = null;

            _uiStates.Unload(_workingCopy.CommitMessage);

            if (_cancellationRefreshBranches is { IsCancellationRequested: false })
                _cancellationRefreshBranches.Cancel();
            if (_cancellationRefreshTags is { IsCancellationRequested: false })
                _cancellationRefreshTags.Cancel();
            if (_cancellationRefreshWorkingCopyChanges is { IsCancellationRequested: false })
                _cancellationRefreshWorkingCopyChanges.Cancel();
            if (_cancellationRefreshCommits is { IsCancellationRequested: false })
                _cancellationRefreshCommits.Cancel();
            if (_cancellationRefreshStashes is { IsCancellationRequested: false })
                _cancellationRefreshStashes.Cancel();

            _autoFetchTimer?.Dispose();
            _autoFetchTimer = null;
            Preferences.Instance.PropertyChanged -= OnPreferencesPropertyChanged;

            _settings = null;
            _commitHistoryMetadataCache = null;
            _uiStates = null;
            _historyFilterMode = Models.FilterMode.None;
            _lastVisibleBranchesCount = 0;
            _isShowingAllBranches = false;
            _shouldShowBranchPresetEmptyState = false;
            _isPresetBranchFilterEditorExpanded = false;
            _shouldApplyPresetBranchFilterOnInitialBranchLoad = false;
            _foldedBranchFullNames.Clear();
            _visibleFoldableBranchesCount = 0;
            _visibleFoldedBranchesCount = 0;
            NotifyFoldControlsChanged();

            _watcher?.Dispose();
            _histories.Dispose();
            _workingCopy.Dispose();
            _stashesPage.Dispose();
            _searchCommitContext.Dispose();

            _watcher = null;
            _histories = null;
            _workingCopy = null;
            _stashesPage = null;

            _localChangesCount = 0;
            _stashesCount = 0;

            _remotes.Clear();
            _branches.Clear();
            _localBranchTrees.Clear();
            _remoteBranchTrees.Clear();
            _tags.Clear();
            _visibleTags = null;
            _submodules.Clear();
            _visibleSubmodules = null;
            _presetBranchExactColorItems.Clear();
        }

        public bool CanCreatePopup()
        {
            var page = GetOwnerPage();
            if (page == null)
                return false;

            return !_isAutoFetching && page.CanCreatePopup();
        }

        public void ShowPopup(Popup popup)
        {
            var page = GetOwnerPage();
            if (page != null)
                page.Popup = popup;
        }

        public async Task ShowAndStartPopupAsync(Popup popup)
        {
            var page = GetOwnerPage();
            page.Popup = popup;

            if (popup.CanStartDirectly())
                await page.ProcessPopupAsync();
        }

        public bool IsGitFlowEnabled()
        {
            return GitFlow is { IsValid: true } &&
                _branches.Find(x => x.IsLocal && x.Name.Equals(GitFlow.Master, StringComparison.Ordinal)) != null &&
                _branches.Find(x => x.IsLocal && x.Name.Equals(GitFlow.Develop, StringComparison.Ordinal)) != null;
        }

        public Models.GitFlowBranchType GetGitFlowType(Models.Branch b)
        {
            if (!IsGitFlowEnabled())
                return Models.GitFlowBranchType.None;

            var name = b.Name;
            if (name.StartsWith(GitFlow.FeaturePrefix, StringComparison.Ordinal))
                return Models.GitFlowBranchType.Feature;
            if (name.StartsWith(GitFlow.ReleasePrefix, StringComparison.Ordinal))
                return Models.GitFlowBranchType.Release;
            if (name.StartsWith(GitFlow.HotfixPrefix, StringComparison.Ordinal))
                return Models.GitFlowBranchType.Hotfix;
            return Models.GitFlowBranchType.None;
        }

        public bool IsLFSEnabled()
        {
            var path = Path.Combine(FullPath, ".git", "hooks", "pre-push");
            if (!File.Exists(path))
                return false;

            try
            {
                var content = File.ReadAllText(path);
                return content.Contains("git lfs pre-push");
            }
            catch
            {
                return false;
            }
        }

        public async Task InstallLFSAsync()
        {
            var log = CreateLog("Install LFS");
            var succ = await new Commands.LFS(FullPath).Use(log).InstallAsync();
            if (succ)
                App.SendNotification(FullPath, "LFS enabled successfully!");

            log.Complete();
        }

        public async Task<bool> TrackLFSFileAsync(string pattern, bool isFilenameMode)
        {
            var log = CreateLog("Track LFS");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .TrackAsync(pattern, isFilenameMode);

            if (succ)
                App.SendNotification(FullPath, $"Tracking successfully! Pattern: {pattern}");

            log.Complete();
            return succ;
        }

        public async Task<bool> LockLFSFileAsync(string remote, string path)
        {
            var log = CreateLog("Lock LFS File");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .LockAsync(remote, path);

            if (succ)
                App.SendNotification(FullPath, $"Lock file successfully! File: {path}");

            log.Complete();
            return succ;
        }

        public async Task<bool> UnlockLFSFileAsync(string remote, string path, bool force, bool notify)
        {
            var log = CreateLog("Unlock LFS File");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .UnlockAsync(remote, path, force);

            if (succ && notify)
                App.SendNotification(FullPath, $"Unlock file successfully! File: {path}");

            log.Complete();
            return succ;
        }

        public CommandLog CreateLog(string name)
        {
            var log = new CommandLog(name);
            Logs.Insert(0, log);
            while (Logs.Count > MAX_LOGS)
                Logs.RemoveAt(Logs.Count - 1);
            return log;
        }

        public void RefreshAll()
        {
            RefreshCommits();
            RefreshBranches();
            RefreshTags();
            RefreshSubmodules();
            RefreshWorktrees();
            RefreshWorkingCopyChanges();
            RefreshStashes();
            NotifyAccentColorChanged();

            Task.Run(async () =>
            {
                var issuetrackers = new List<Models.IssueTracker>();
                await new Commands.IssueTracker(FullPath, true).ReadAllAsync(issuetrackers, true).ConfigureAwait(false);
                await new Commands.IssueTracker(FullPath, false).ReadAllAsync(issuetrackers, false).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() =>
                {
                    IssueTrackers.Clear();
                    IssueTrackers.AddRange(issuetrackers);
                });

                var config = await new Commands.Config(FullPath).ReadAllAsync().ConfigureAwait(false);
                _hasAllowedSignersFile = config.TryGetValue("gpg.ssh.allowedsignersfile", out var allowedSignersFile) && !string.IsNullOrEmpty(allowedSignersFile);

                if (config.TryGetValue("gitflow.branch.master", out var masterName))
                    GitFlow.Master = masterName;
                if (config.TryGetValue("gitflow.branch.develop", out var developName))
                    GitFlow.Develop = developName;
                if (config.TryGetValue("gitflow.prefix.feature", out var featurePrefix))
                    GitFlow.FeaturePrefix = featurePrefix;
                if (config.TryGetValue("gitflow.prefix.release", out var releasePrefix))
                    GitFlow.ReleasePrefix = releasePrefix;
                if (config.TryGetValue("gitflow.prefix.hotfix", out var hotfixPrefix))
                    GitFlow.HotfixPrefix = hotfixPrefix;
            });
        }

        public async Task FetchAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return;
            }

            if (autoStart)
                await ShowAndStartPopupAsync(new Fetch(this));
            else
                ShowPopup(new Fetch(this));
        }

        public async Task QuickFetchAsync(bool onlyFilteredBranches = false)
        {
            if (!CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return;
            }

            var remote = GetPreferredRemoteName();
            if (string.IsNullOrEmpty(remote))
            {
                App.RaiseException(FullPath, "Can NOT determine a default remote for quick fetch.");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var refspecs = onlyFilteredBranches ? await BuildQuickFetchFilteredRefSpecsAsync(remote).ConfigureAwait(false) : null;
            if (onlyFilteredBranches && (refspecs == null || refspecs.Count == 0))
            {
                App.SendNotification(FullPath, $"Quick Fetch (Filtered) skipped because no included branch filters match remote '{remote}'.");
                ShowFetchDurationToast(stopwatch.Elapsed.TotalSeconds);
                return;
            }

            var operationName = onlyFilteredBranches ? "Quick Fetch (Filtered)" : "Quick Fetch";
            var log = CreateLog(operationName);
            var succ = false;
            AutoBackgroundOperationText = operationName;
            IsQuickFetching = true;

            try
            {
                succ = await (onlyFilteredBranches
                        ? new Commands.Fetch(FullPath, remote, true, false, false, false, refspecs)
                        : new Commands.Fetch(FullPath, remote, true, false))
                    .Use(log)
                    .RunAsync();
            }
            finally
            {
                IsQuickFetching = false;
                log.Complete();
            }

            if (succ)
            {
                MarkFetched();
                ShowFetchDurationToast(stopwatch.Elapsed.TotalSeconds);
            }
            else
                App.SendNotification(FullPath, $"{operationName} failed. Review the repository log for details.");
        }

        public string GetPreferredRemoteNameForToolbarCommandEditor()
        {
            return GetPreferredRemoteName();
        }

        public Task<List<string>> GetQuickFetchFilteredRefSpecsForToolbarCommandEditorAsync(string remoteName)
        {
            return BuildQuickFetchFilteredRefSpecsAsync(remoteName);
        }

        public List<string> GetFetchRemoteNamesForCurrentRepositoryForToolbarCommandEditor()
        {
            return GetFetchRemoteNamesForCurrentRepository();
        }

        public Task<List<string>> GetFetchRemoteNamesForRepositoryForToolbarCommandEditorAsync(string repoPath)
        {
            return GetFetchRemoteNamesForRepositoryAsync(repoPath);
        }

        public async Task FetchRecursivelyAsync(bool prune)
        {
            if (!CanCreatePopup())
                return;

            var log = CreateLog(prune ? "Fetch and Prune Recursively" : "Fetch Recursively");
            await RunFetchRecursivelyAsync(prune, log);
            log.Complete();
        }

        public async Task PullAsync(bool autoStart)
        {
            if (IsBare || !CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return;
            }

            if (_currentBranch == null)
            {
                App.RaiseException(FullPath, "Can NOT find current branch!!!");
                return;
            }

            var pull = new Pull(this, null, false);
            if (autoStart && pull.SelectedBranch != null)
                await ShowAndStartPopupAsync(pull);
            else
                ShowPopup(pull);
        }

        public async Task QuickPullAsync()
        {
            if (IsBare || !CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return;
            }

            if (_currentBranch == null)
            {
                App.RaiseException(FullPath, "Can NOT find current branch!!!");
                return;
            }

            var pull = new Pull(this, null, false)
            {
                PreferQuickPath = true,
                AllowQuickPathFallback = false,
            };

            if (pull.SelectedRemote == null || pull.SelectedBranch == null)
            {
                App.RaiseException(FullPath, "Can NOT determine a default remote branch for quick pull.");
                return;
            }

            var log = CreateLog("Quick Pull");
            AutoBackgroundOperationText = "Quick Pull";
            IsQuickPulling = true;
            var succ = false;

            try
            {
                using var lockWatcher = LockWatcher();
                succ = await pull.ExecuteAsync(log, false);
            }
            finally
            {
                IsQuickPulling = false;
                log.Complete();
            }

            if (succ)
                App.SendNotification(FullPath, "Quick Pull completed.");
            else
                App.SendNotification(FullPath, "Quick Pull failed. Review the repository log for details.");
        }

        public async Task<bool> RunDefaultPullAsync(Models.ICommandLog log, bool autoUpdateSubmodules, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (IsBare)
            {
                log?.AppendLine("Can not pull in a bare repository.");
                App.RaiseException(FullPath, "Can NOT pull in a bare repository.");
                return false;
            }

            if (_remotes.Count == 0)
            {
                log?.AppendLine("No remotes added to this repository.");
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return false;
            }

            if (_currentBranch == null)
            {
                log?.AppendLine("Can not find current branch.");
                App.RaiseException(FullPath, "Can NOT find current branch!!!");
                return false;
            }

            var pull = new Pull(this, null, false);
            if (pull.SelectedRemote == null || pull.SelectedBranch == null)
            {
                log?.AppendLine("No default remote branch is available for pull.");
                App.RaiseException(FullPath, "Can NOT determine a default remote branch for pull.");
                return false;
            }

            using var lockWatcher = LockWatcher();
            return await pull.ExecuteAsync(log, autoUpdateSubmodules, cancellationToken);
        }

        public async Task PushAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return;
            }

            if (_currentBranch == null)
            {
                App.RaiseException(FullPath, "Can NOT find current branch!!!");
                return;
            }

            if (autoStart)
                await ShowAndStartPopupAsync(new Push(this, null));
            else
                ShowPopup(new Push(this, null));
        }

        public void ApplyPatch()
        {
            if (CanCreatePopup())
                ShowPopup(new Apply(this));
        }

        public async Task ExecCustomActionAsync(Models.CustomAction action, object scopeTarget)
        {
            if (!CanCreatePopup())
                return;

            App.ShowWindow(new Views.ExecuteCustomActionWindow()
            {
                DataContext = new ExecuteCustomAction(this, action, scopeTarget),
            });

            await Task.CompletedTask;
        }

        public async Task CleanupAsync()
        {
            if (CanCreatePopup())
                await ShowAndStartPopupAsync(new Cleanup(this));
        }

        public void ClearFilter()
        {
            Filter = string.Empty;
        }

        public void ShowAllBranchesForSession()
        {
            if (IsShowingAllBranches)
                return;

            IsShowingAllBranches = true;
            RefreshBranchSidebarByCurrentFilters();
        }

        public void UsePresetBranchFilterForSession()
        {
            if (!IsShowingAllBranches)
                return;

            IsShowingAllBranches = false;
            RefreshBranchSidebarByCurrentFilters();
        }

        public void OpenPresetBranchFilterEditor()
        {
            IsPresetBranchFilterEditorExpanded = true;
        }

        public void ToggleFoldBranch(Models.Branch branch)
        {
            if (branch == null || string.IsNullOrWhiteSpace(branch.FullName))
                return;

            if (!_foldedBranchFullNames.Add(branch.FullName))
                _foldedBranchFullNames.Remove(branch.FullName);

            NotifyFoldControlsChanged();
            RefreshCommits();
        }

        public void FoldVisibleBranchesInGraph()
        {
            var visible = CollectVisibleFoldableBranchFullNamesInGraph();
            if (visible.Count == 0)
                return;

            var changed = false;
            foreach (var fullName in visible)
                changed |= _foldedBranchFullNames.Add(fullName);

            if (!changed)
                return;

            NotifyFoldControlsChanged();
            RefreshCommits();
        }

        public void UnfoldAllBranchesInGraph()
        {
            if (_foldedBranchFullNames.Count == 0)
                return;

            _foldedBranchFullNames.Clear();
            NotifyFoldControlsChanged();
            RefreshCommits();
        }

        public void ExcludeBranchInPresetFilter(string name)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(name))
                return;

            if (_settings.AddPresetBranchExcludeName(name))
            {
                OnPropertyChanged(nameof(PresetBranchExcludeNames));
                OnPropertyChanged(nameof(PresetBranchFilterSummary));
                SavePresetBranchFilterSettingsAsync();
            }

            ApplyPresetBranchFilter();
        }

        public void UpdatePresetBranchExactNameColor(string name, uint color)
        {
            if (_settings == null || !_settings.SetPresetBranchExactNameColor(name, color))
                return;

            foreach (var item in _presetBranchExactColorItems)
            {
                if (item.Name.Equals(name, StringComparison.Ordinal))
                {
                    item.Color = color;
                    break;
                }
            }

            SavePresetBranchFilterSettingsAsync();
        }

        public void ToggleShowAllBranchesAndApplyGraphFilter()
        {
            if (IsShowingAllBranches)
            {
                ApplyPresetBranchFilter();
            }
            else
            {
                ShowAllBranchesForSession();
                ClearHistoryFilters();
            }
        }

        public void ApplyPresetBranchFilter()
        {
            UsePresetBranchFilterForSession();

            var presetBranchFilter = GetPresetBranchFilterMatchCache();
            var exactNames = presetBranchFilter.ExactNames;
            var containsPatterns = presetBranchFilter.ContainsPatterns;
            var excludeNames = presetBranchFilter.ExcludeNames;
            var exactNameColors = _settings?.GetPresetBranchExactNameColorMap() ?? [];
            var hasIncludeRules = exactNames.Count > 0 || containsPatterns.Count > 0;
            var hasExcludeRules = excludeNames.Count > 0;
            var branchesByFullName = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);
            foreach (var branch in _branches)
                branchesByFullName[branch.FullName] = branch;

            _uiStates.HistoryFilters.Clear();
            HistoryFilterMode = Models.FilterMode.None;

            void AddIncludedHistoryFilter(string pattern, Models.FilterType type, uint color)
            {
                foreach (var exists in _uiStates.HistoryFilters)
                {
                    if (exists.Type != type || !exists.Pattern.Equals(pattern, StringComparison.Ordinal))
                        continue;

                    if (exists.Color == 0 && color != 0)
                        exists.Color = color;
                    return;
                }

                _uiStates.UpdateHistoryFilters(pattern, type, Models.FilterMode.Included, color);
            }

            void AddExcludedHistoryFilter(string pattern, Models.FilterType type)
            {
                _uiStates.UpdateHistoryFilters(pattern, type, Models.FilterMode.Excluded, 0);
            }

            if (!hasIncludeRules)
            {
                if (hasExcludeRules)
                {
                    foreach (var branch in _branches)
                    {
                        if (!excludeNames.Contains(branch.Name))
                            continue;

                        if (branch.IsLocal)
                            AddExcludedHistoryFilter(branch.FullName, Models.FilterType.LocalBranch);
                        else
                            AddExcludedHistoryFilter(branch.FullName, Models.FilterType.RemoteBranch);
                    }
                }

                SavePresetBranchFilterSettingsAsync();
                RefreshHistoryFilters(true);
                RefreshBranchSidebarByCurrentFilters();
                return;
            }

            foreach (var branch in _branches)
            {
                if (!presetBranchFilter.ShouldShow(branch.Name))
                    continue;

                var color = 0u;
                if (exactNames.Contains(branch.Name))
                    color = exactNameColors.GetValueOrDefault(branch.Name, Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR);

                if (branch.IsLocal)
                {
                    AddIncludedHistoryFilter(branch.FullName, Models.FilterType.LocalBranch, color);

                    if (!string.IsNullOrEmpty(branch.Upstream) &&
                        !branch.IsUpstreamGone &&
                        branchesByFullName.TryGetValue(branch.Upstream, out var upstreamBranch) &&
                        presetBranchFilter.ShouldShow(upstreamBranch.Name))
                    {
                        AddIncludedHistoryFilter(branch.Upstream, Models.FilterType.RemoteBranch, color);
                    }
                }
                else
                {
                    AddIncludedHistoryFilter(branch.FullName, Models.FilterType.RemoteBranch, color);
                }
            }

            EnsureIncludedBranchFiltersHaveColors();
            SavePresetBranchFilterSettingsAsync();
            RefreshHistoryFilters(true);
            RefreshBranchSidebarByCurrentFilters();
        }

        public void RefreshBranchSidebarByCurrentFilters()
        {
            if (_uiStates == null)
                return;

            var visibleBranches = GetVisibleBranchesByCurrentFilter();
            var shouldCleanupExpandedNodes = IsShowingAllBranches && string.IsNullOrEmpty(_filter);
            var builder = BuildBranchTree(visibleBranches, _remotes, shouldCleanupExpandedNodes);

            LocalBranchTrees = builder.Locals;
            RemoteBranchTrees = builder.Remotes;

            var localBranchesCount = 0;
            foreach (var b in visibleBranches)
            {
                if (b.IsLocal && !b.IsDetachedHead)
                    localBranchesCount++;
            }

            LocalBranchesCount = localBranchesCount;
            _lastVisibleBranchesCount = visibleBranches.Count;
            UpdateShouldShowBranchPresetEmptyState();
        }

        public IDisposable LockWatcher()
        {
            return _watcher?.Lock();
        }

        public void MarkBranchesDirtyManually()
        {
            _watcher?.MarkBranchUpdated();
            RefreshBranches();
            RefreshCommits();
            RefreshWorkingCopyChanges();
            RefreshWorktrees();
        }

        public void MarkTagsDirtyManually()
        {
            _watcher?.MarkTagUpdated();
            RefreshTags();
            RefreshCommits();
        }

        public void MarkWorkingCopyDirtyManually()
        {
            _watcher?.MarkWorkingCopyUpdated();
            RefreshWorkingCopyChanges();
        }

        public void MarkStashesDirtyManually()
        {
            _watcher?.MarkStashUpdated();
            RefreshStashes();
        }

        public void MarkSubmodulesDirtyManually()
        {
            _watcher?.MarkSubmodulesUpdated();
            RefreshSubmodules();
        }

        public void MarkFetched()
        {
            _lastFetchTime = DateTime.Now;
            RefreshBranches();
            RefreshCommits(true);
        }

        public void NavigateToCommit(string sha, bool isDelayMode = false)
        {
            if (isDelayMode)
            {
                _navigateToCommitDelayed = sha;
            }
            else
            {
                SelectedViewIndex = 0;
                _histories?.NavigateTo(sha);
            }
        }

        public void SetCommitMessage(string message)
        {
            if (_workingCopy is not null)
                _workingCopy.CommitMessage = message;
        }

        public void ClearCommitMessage()
        {
            if (_workingCopy is not null)
                _workingCopy.CommitMessage = string.Empty;
        }

        public Models.Commit GetSelectedCommitInHistory()
        {
            return (_histories?.DetailContext as CommitDetail)?.Commit;
        }

        public void NotifyAccentColorChanged()
        {
            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(AccentHoveredColor));
            OnPropertyChanged(nameof(AccentToolbarBackground));
        }

        public void ClearHistoryFilters()
        {
            _uiStates.HistoryFilters.Clear();
            HistoryFilterMode = Models.FilterMode.None;
            NotifyHistoryFilterIndicatorsChanged();

            ResetBranchTreeFilterMode(LocalBranchTrees);
            ResetBranchTreeFilterMode(RemoteBranchTrees);
            ResetTagFilterMode();
            RefreshCommits();
        }

        public void RemoveHistoryFilter(Models.HistoryFilter filter)
        {
            if (_uiStates.HistoryFilters.Remove(filter))
            {
                HistoryFilterMode = _uiStates.GetHistoryFilterMode();
                RefreshHistoryFilters(true);
            }
        }

        public void UpdateBranchNodeIsExpanded(BranchTreeNode node)
        {
            if (_uiStates == null || !string.IsNullOrWhiteSpace(_filter))
                return;

            if (node.IsExpanded)
            {
                if (!_uiStates.ExpandedBranchNodesInSideBar.Contains(node.Path))
                    _uiStates.ExpandedBranchNodesInSideBar.Add(node.Path);
            }
            else
            {
                _uiStates.ExpandedBranchNodesInSideBar.Remove(node.Path);
            }
        }

        public void SetTagFilterMode(Models.Tag tag, Models.FilterMode mode)
        {
            var changed = _uiStates.UpdateHistoryFilters(tag.Name, Models.FilterType.Tag, mode);
            if (changed)
                RefreshHistoryFilters(true);
        }

        public void SetBranchFilterMode(Models.Branch branch, Models.FilterMode mode, bool clearExists, bool refresh)
        {
            var node = FindBranchNode(branch.IsLocal ? _localBranchTrees : _remoteBranchTrees, branch.FullName);
            if (node != null)
                SetBranchFilterMode(node, mode, clearExists, refresh);
        }

        public void SetBranchFilterMode(BranchTreeNode node, Models.FilterMode mode, bool clearExists, bool refresh)
        {
            var isLocal = node.Path.StartsWith("refs/heads/", StringComparison.Ordinal);
            var tree = isLocal ? _localBranchTrees : _remoteBranchTrees;

            if (clearExists)
            {
                _uiStates.HistoryFilters.Clear();
                HistoryFilterMode = Models.FilterMode.None;
            }

            if (node.Backend is Models.Branch branch)
            {
                var type = isLocal ? Models.FilterType.LocalBranch : Models.FilterType.RemoteBranch;
                var changed = _uiStates.UpdateHistoryFilters(node.Path, type, mode);
                if (!changed)
                    return;

                if (isLocal && !string.IsNullOrEmpty(branch.Upstream) && !branch.IsUpstreamGone)
                    _uiStates.UpdateHistoryFilters(branch.Upstream, Models.FilterType.RemoteBranch, mode);
            }
            else
            {
                var type = isLocal ? Models.FilterType.LocalBranchFolder : Models.FilterType.RemoteBranchFolder;
                var changed = _uiStates.UpdateHistoryFilters(node.Path, type, mode);
                if (!changed)
                    return;

                _uiStates.RemoveBranchFiltersByPrefix(node.Path);
            }

            var parentType = isLocal ? Models.FilterType.LocalBranchFolder : Models.FilterType.RemoteBranchFolder;
            var cur = node;
            do
            {
                var lastSepIdx = cur.Path.LastIndexOf('/');
                if (lastSepIdx <= 0)
                    break;

                var parentPath = cur.Path.Substring(0, lastSepIdx);
                var parent = FindBranchNode(tree, parentPath);
                if (parent == null)
                    break;

                _uiStates.UpdateHistoryFilters(parent.Path, parentType, Models.FilterMode.None);
                cur = parent;
            } while (true);

            RefreshHistoryFilters(refresh);
        }

        public uint GetBranchFilterColor(Models.Branch branch)
        {
            if (branch == null)
                return Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR;

            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Pattern.Equals(branch.FullName, StringComparison.Ordinal))
                    return filter.Color == 0 ? Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR : filter.Color;
            }

            if (_settings != null)
            {
                var configured = _settings.GetPresetBranchConfiguredColorMap();
                if (configured.TryGetValue(branch.Name, out var color) && color != 0)
                    return color;
            }

            return Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR;
        }

        public void SetBranchDisplayColor(Models.Branch branch, uint color)
        {
            if (branch == null)
                return;

            var changed = false;
            if (_settings != null)
            {
                changed |= _settings.SetPresetBranchExactNameColor(branch.Name, color);
                if (changed)
                    SavePresetBranchFilterSettingsAsync();
            }

            if (_uiStates != null && _uiStates.GetHistoryFilterMode(branch.FullName) == Models.FilterMode.Included)
            {
                if (branch.IsLocal)
                {
                    changed |= _uiStates.UpdateHistoryFilters(branch.FullName, Models.FilterType.LocalBranch, Models.FilterMode.Included, color);
                    if (!string.IsNullOrEmpty(branch.Upstream) && !branch.IsUpstreamGone)
                        changed |= _uiStates.UpdateHistoryFilters(branch.Upstream, Models.FilterType.RemoteBranch, Models.FilterMode.Included, color);
                }
                else
                {
                    changed |= _uiStates.UpdateHistoryFilters(branch.FullName, Models.FilterType.RemoteBranch, Models.FilterMode.Included, color);
                }
            }

            if (changed)
                RefreshHistoryFilters(true);
            else
                RefreshCommits();

            if (CurrentBranch != null && branch.FullName.Equals(CurrentBranch.FullName, StringComparison.Ordinal))
                NotifyCurrentBranchVisualChanged();
        }

        public void SetBranchFilterColor(Models.Branch branch, uint color)
        {
            if (branch == null)
                return;

            var before = _uiStates.GetHistoryFilterMode(branch.FullName);
            SetBranchFilterMode(branch, Models.FilterMode.Included, false, false);

            var changed = before != Models.FilterMode.Included;
            if (branch.IsLocal)
            {
                changed |= _uiStates.UpdateHistoryFilters(branch.FullName, Models.FilterType.LocalBranch, Models.FilterMode.Included, color);
                if (!string.IsNullOrEmpty(branch.Upstream) && !branch.IsUpstreamGone)
                    changed |= _uiStates.UpdateHistoryFilters(branch.Upstream, Models.FilterType.RemoteBranch, Models.FilterMode.Included, color);
            }
            else
            {
                changed |= _uiStates.UpdateHistoryFilters(branch.FullName, Models.FilterType.RemoteBranch, Models.FilterMode.Included, color);
            }

            if (changed)
                RefreshHistoryFilters(true);

            if (CurrentBranch != null && branch.FullName.Equals(CurrentBranch.FullName, StringComparison.Ordinal))
                NotifyCurrentBranchVisualChanged();
        }

        public async Task StashAllAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            var popup = new StashChanges(this, null);
            if (autoStart)
                await ShowAndStartPopupAsync(popup);
            else
                ShowPopup(popup);
        }

        public async Task SkipMergeAsync()
        {
            if (_workingCopy != null)
                await _workingCopy.SkipMergeAsync();
        }

        public async Task AbortMergeAsync()
        {
            if (_workingCopy != null)
                await _workingCopy.AbortMergeAsync();
        }

        public List<(Models.CustomAction, CustomActionContextMenuLabel)> GetCustomActions(Models.CustomActionScope scope)
        {
            var actions = new List<(Models.CustomAction, CustomActionContextMenuLabel)>();

            foreach (var act in Preferences.Instance.CustomActions)
            {
                if (act.Scope == scope)
                    actions.Add((act, new CustomActionContextMenuLabel(act.Name, true)));
            }

            foreach (var act in _settings.CustomActions)
            {
                if (act.Scope == scope)
                    actions.Add((act, new CustomActionContextMenuLabel(act.Name, false)));
            }

            return actions;
        }

        public async Task ExecBisectCommandAsync(string subcmd)
        {
            using var lockWatcher = _watcher?.Lock();
            IsBisectCommandRunning = true;

            var log = CreateLog($"Bisect({subcmd})");

            var succ = await new Commands.Bisect(FullPath, subcmd).Use(log).ExecAsync();
            log.Complete();

            var head = await new Commands.QueryRevisionByRefName(FullPath, "HEAD").GetResultAsync();
            if (!succ)
                App.RaiseException(FullPath, log.Content.Substring(log.Content.IndexOf('\n')).Trim());
            else if (log.Content.Contains("is the first bad commit"))
                App.SendNotification(FullPath, log.Content.Substring(log.Content.IndexOf('\n')).Trim());

            MarkBranchesDirtyManually();
            NavigateToCommit(head, true);
            IsBisectCommandRunning = false;
        }

        public bool MayHaveSubmodules()
        {
            var modulesFile = Path.Combine(FullPath, ".gitmodules");
            var info = new FileInfo(modulesFile);
            return info.Exists && info.Length > 20;
        }

        private async Task MarkSubmodulePointerChangesAsync(List<Models.Change> changes)
        {
            if (changes == null || changes.Count == 0)
                return;

            if (_submodules.Count == 0)
                return;

            var submodulePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var submodule in _submodules)
            {
                if (!string.IsNullOrEmpty(submodule.Path))
                    submodulePaths.Add(submodule.Path);
            }

            if (submodulePaths.Count == 0)
                return;

            var indexChanges = await new Commands.QuerySubmodulePointerChanges(FullPath, true, submodulePaths).GetResultAsync().ConfigureAwait(false);
            var workTreeChanges = await new Commands.QuerySubmodulePointerChanges(FullPath, false, submodulePaths).GetResultAsync().ConfigureAwait(false);

            foreach (var change in changes)
            {
                change.IsSubmodulePointerChange = submodulePaths.Contains(change.Path);
                if (!change.IsSubmodulePointerChange)
                    continue;

                if (indexChanges.TryGetValue(change.Path, out var index))
                {
                    change.IndexSubmodulePointerOldSHA = index.OldSHA;
                    change.IndexSubmodulePointerNewSHA = index.NewSHA;
                }

                if (workTreeChanges.TryGetValue(change.Path, out var worktree))
                {
                    change.WorkTreeSubmodulePointerOldSHA = worktree.OldSHA;
                    change.WorkTreeSubmodulePointerNewSHA = worktree.NewSHA;
                }
            }
        }

        public void RefreshBranches()
        {
            if (_cancellationRefreshBranches is { IsCancellationRequested: false })
                _cancellationRefreshBranches.Cancel();

            _cancellationRefreshBranches = new CancellationTokenSource();
            var token = _cancellationRefreshBranches.Token;

            Task.Run(async () =>
            {
                var branches = await new Commands.QueryBranches(FullPath).GetResultAsync().ConfigureAwait(false);
                var remotes = await new Commands.QueryRemotes(FullPath).GetResultAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    Remotes = remotes;
                    Branches = branches;
                    CurrentBranch = branches.Find(x => x.IsCurrent);
                    RefreshBranchSidebarByCurrentFilters();
                    ApplyPresetBranchFilterIfNeededOnInitialLoad();

                    if (_workingCopy != null)
                        _workingCopy.HasRemotes = remotes.Count > 0;

                    var hasPendingPullOrPush = CurrentBranch?.IsTrackStatusVisible ?? false;
                    GetOwnerPage()?.ChangeDirtyState(Models.DirtyState.HasPendingPullOrPush, !hasPendingPullOrPush);
                });
            }, token);
        }

        public void RefreshWorktrees()
        {
            Task.Run(async () =>
            {
                var worktrees = await new Commands.Worktree(FullPath).ReadAllAsync().ConfigureAwait(false);
                if (worktrees.Count == 0)
                {
                    Dispatcher.UIThread.Invoke(() => Worktrees = worktrees);
                    return;
                }

                var cleaned = new List<Models.Worktree>();
                foreach (var worktree in worktrees)
                {
                    if (worktree.FullPath.Equals(FullPath, StringComparison.Ordinal) ||
                        worktree.FullPath.Equals(GitDir, StringComparison.Ordinal))
                        continue;

                    cleaned.Add(worktree);
                }

                Dispatcher.UIThread.Invoke(() => Worktrees = cleaned);
            });
        }

        public void RefreshTags()
        {
            if (_cancellationRefreshTags is { IsCancellationRequested: false })
                _cancellationRefreshTags.Cancel();

            _cancellationRefreshTags = new CancellationTokenSource();
            var token = _cancellationRefreshTags.Token;

            Task.Run(async () =>
            {
                var tags = await new Commands.QueryTags(FullPath).GetResultAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    Tags = tags;
                    VisibleTags = BuildVisibleTags();
                });
            }, token);
        }

        public void RefreshCommits()
        {
            RefreshCommits(false);
        }

        public void RefreshCommits(bool fastAfterFetch)
        {
            if (_cancellationRefreshCommits is { IsCancellationRequested: false })
                _cancellationRefreshCommits.Cancel();

            _cancellationRefreshCommits = new CancellationTokenSource();
            var token = _cancellationRefreshCommits.Token;
            var refreshMode = fastAfterFetch ? HistoryRefreshMode.FastAfterFetch : HistoryRefreshMode.Full;

            Task.Run(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_histories != null)
                    {
                        _histories.IsLoading = true;
                        _histories.IsBackfilling = false;
                    }
                });

                var fullLimits = BuildHistoryLimits(Preferences.Instance.MaxHistoryCommits);
                var quickLimits = refreshMode == HistoryRefreshMode.FastAfterFetch
                    ? BuildQuickHistoryLimits()
                    : string.Empty;

                if (!string.IsNullOrEmpty(quickLimits) && !quickLimits.Equals(fullLimits, StringComparison.Ordinal))
                {
                    var quickSnapshot = await QueryCommitHistorySnapshotAsync(quickLimits, false).ConfigureAwait(false);
                    if (!token.IsCancellationRequested && quickSnapshot != null && quickSnapshot.Commits.Count > 0)
                        await ApplyCommitHistorySnapshotAsync(quickSnapshot, token, false, true, false).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested)
                    return;

                var fullSnapshot = await QueryCommitHistorySnapshotAsync(fullLimits, true).ConfigureAwait(false);
                if (fullSnapshot != null)
                    await ApplyCommitHistorySnapshotAsync(fullSnapshot, token, false, false, true).ConfigureAwait(false);
            }, token);
        }

        private string BuildHistoryLimits(int maxCommits)
        {
            var builder = new StringBuilder();
            builder
                .Append('-').Append(maxCommits).Append(' ')
                .Append(_uiStates.BuildHistoryParams());

            var hasIncludedHistoryFilters = false;
            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Mode == Models.FilterMode.Included)
                {
                    hasIncludedHistoryFilters = true;
                    break;
                }
            }

            if (hasIncludedHistoryFilters)
            {
                builder.Append(" HEAD");
                if (!string.IsNullOrWhiteSpace(_superProjectSubmoduleSHA))
                    builder.Append(' ').Append(_superProjectSubmoduleSHA);
            }

            return builder.ToString();
        }

        private string BuildQuickHistoryLimits()
        {
            var fullCount = Preferences.Instance.MaxHistoryCommits;
            var quickCount = Math.Min(fullCount, 120);
            if (quickCount >= fullCount)
                return string.Empty;

            return BuildHistoryLimits(quickCount);
        }

        private async Task<CommitHistorySnapshot> QueryCommitHistorySnapshotAsync(string limits, bool pruneFoldState)
        {
            var commits = await new Commands.QueryCommits(FullPath, limits).GetResultAsync().ConfigureAwait(false);
            var commitDiffStats = new Dictionary<string, Commands.CommitHistoryDiffStat>(StringComparer.Ordinal);
            var allCached = _commitHistoryMetadataCache != null && commits.Count > 0;

            foreach (var commit in commits)
            {
                if (_commitHistoryMetadataCache != null && _commitHistoryMetadataCache.TryGet(commit.SHA, out var cached))
                {
                    commitDiffStats[commit.SHA] = new Commands.CommitHistoryDiffStat()
                    {
                        ChangedFileCount = cached.ChangedFileCount,
                        HasSubmodulePointerChange = cached.HasSubmodulePointerChange,
                    };
                }
                else
                {
                    allCached = false;
                }
            }

            if (!allCached)
            {
                commitDiffStats = await new Commands.QueryCommitSubmodulePointerFlags(FullPath, limits).GetResultAsync().ConfigureAwait(false);
                if (_commitHistoryMetadataCache != null)
                {
                    var cacheUpdates = new Dictionary<string, Models.CommitHistoryMetadata>(StringComparer.Ordinal);
                    foreach (var commit in commits)
                    {
                        if (commitDiffStats.TryGetValue(commit.SHA, out var stat))
                        {
                            cacheUpdates[commit.SHA] = new Models.CommitHistoryMetadata()
                            {
                                ChangedFileCount = stat.ChangedFileCount,
                                HasSubmodulePointerChange = stat.HasSubmodulePointerChange,
                            };
                        }
                        else
                        {
                            cacheUpdates[commit.SHA] = new Models.CommitHistoryMetadata();
                        }
                    }

                    _commitHistoryMetadataCache.UpdateRange(cacheUpdates);
                }
            }

            foreach (var commit in commits)
            {
                if (commitDiffStats.TryGetValue(commit.SHA, out var stat))
                {
                    commit.HasSubmodulePointerChange = stat.HasSubmodulePointerChange;
                    commit.ChangedFileCount = stat.ChangedFileCount;
                }
                else
                {
                    commit.HasSubmodulePointerChange = false;
                    commit.ChangedFileCount = 0;
                }
            }

            if (_uiStates.OnlyShowSPPCommitsInHistory)
                commits.RemoveAll(x => !x.HasSubmodulePointerChange);

            AttachSuperProjectPointerDecorator(commits);
            AttachParentRepositoryDecorator(commits);
            ApplyHistoryFilterColorsToDecorators(commits);
            var foldableBranchFullNames = BuildFoldableBranchFullNameSet(commits);
            var notifyFoldControlChange = false;
            if (pruneFoldState)
                notifyFoldControlChange = _foldedBranchFullNames.RemoveWhere(name => !foldableBranchFullNames.Contains(name)) > 0;

            ApplyFoldStateToDecorators(commits, foldableBranchFullNames);
            ApplyFoldedBranchRuns(commits, foldableBranchFullNames);

            return new CommitHistorySnapshot()
            {
                Commits = commits,
                Graph = Models.CommitGraph.Parse(commits, _uiStates.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly)),
                ShouldNotifyFoldControlChange = notifyFoldControlChange,
            };
        }

        private async Task ApplyCommitHistorySnapshotAsync(
            CommitHistorySnapshot snapshot,
            CancellationToken token,
            bool isLoading,
            bool isBackfilling,
            bool finalizeNavigation)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _histories == null)
                    return;

                if (snapshot.ShouldNotifyFoldControlChange)
                    NotifyFoldControlsChanged();

                _histories.IsLoading = isLoading;
                _histories.IsBackfilling = isBackfilling;
                _histories.Commits = snapshot.Commits;
                _histories.Graph = snapshot.Graph;
                UpdateVisibleFoldBranchStatesFromCurrentGraph();
                NotifyCurrentBranchVisualChanged();

                BisectState = _histories.UpdateBisectInfo();

                if (finalizeNavigation)
                {
                    if (!string.IsNullOrEmpty(_navigateToCommitDelayed))
                        NavigateToCommit(_navigateToCommitDelayed);

                    _navigateToCommitDelayed = string.Empty;
                }
            });
        }

        public void RefreshSubmodules()
        {
            if (!MayHaveSubmodules())
            {
                if (_submodules.Count > 0)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        var hadParentDecorator = ShouldAttachParentRepositoryDecorator();
                        Submodules = [];
                        VisibleSubmodules = BuildVisibleSubmodules();
                        if (hadParentDecorator != ShouldAttachParentRepositoryDecorator())
                            RefreshCommits();
                    });
                }

                return;
            }

            Task.Run(async () =>
            {
                var submodules = await new Commands.QuerySubmodules(FullPath).GetResultAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Invoke(() =>
                {
                    var hadParentDecorator = ShouldAttachParentRepositoryDecorator();
                    bool hasChanged = _submodules.Count != submodules.Count;
                    if (!hasChanged)
                    {
                        var old = new Dictionary<string, Models.Submodule>();
                        foreach (var module in _submodules)
                            old.Add(module.Path, module);

                        foreach (var module in submodules)
                        {
                            if (!old.TryGetValue(module.Path, out var exist))
                            {
                                hasChanged = true;
                                break;
                            }

                            hasChanged = !exist.SHA.Equals(module.SHA, StringComparison.Ordinal) ||
                                         !exist.Branch.Equals(module.Branch, StringComparison.Ordinal) ||
                                         !exist.URL.Equals(module.URL, StringComparison.Ordinal) ||
                                         exist.Status != module.Status;

                            if (hasChanged)
                                break;
                        }
                    }

                    if (hasChanged)
                    {
                        Submodules = submodules;
                        VisibleSubmodules = BuildVisibleSubmodules();
                        if (hadParentDecorator != ShouldAttachParentRepositoryDecorator())
                            RefreshCommits();
                    }
                });
            });
        }

        public void RefreshWorkingCopyChanges()
        {
            RefreshWorkingCopyChanges(false);
        }

        public void RefreshWorkingCopyChanges(bool bypassUntrackedCache)
        {
            if (IsBare)
                return;

            if (_cancellationRefreshWorkingCopyChanges is { IsCancellationRequested: false })
                _cancellationRefreshWorkingCopyChanges.Cancel();

            _cancellationRefreshWorkingCopyChanges = new CancellationTokenSource();
            var token = _cancellationRefreshWorkingCopyChanges.Token;
            var noOptionalLocks = Interlocked.Add(ref _queryLocalChangesTimes, 1) > 1;

            Task.Run(async () =>
            {
                var changes = await new Commands.QueryLocalChanges(
                    FullPath,
                    _uiStates.IncludeUntrackedInLocalChanges,
                    noOptionalLocks,
                    !bypassUntrackedCache)
                    .GetResultAsync()
                    .ConfigureAwait(false);

                if (_workingCopy == null || token.IsCancellationRequested)
                    return;

                await MarkSubmodulePointerChangesAsync(changes).ConfigureAwait(false);
                changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));
                _workingCopy.SetData(changes, token);

                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    LocalChangesCount = changes.Count;
                    OnPropertyChanged(nameof(InProgressContext));
                    NotifyCompactStatusChanged();
                    GetOwnerPage()?.ChangeDirtyState(Models.DirtyState.HasLocalChanges, changes.Count == 0);
                    RefreshSuperProjectSubmodulePointer();
                });
            }, token);
        }

        public void RefreshStashes()
        {
            if (IsBare)
                return;

            if (_cancellationRefreshStashes is { IsCancellationRequested: false })
                _cancellationRefreshStashes.Cancel();

            _cancellationRefreshStashes = new CancellationTokenSource();
            var token = _cancellationRefreshStashes.Token;

            Task.Run(async () =>
            {
                var stashes = await new Commands.QueryStashes(FullPath).GetResultAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (_stashesPage != null)
                        _stashesPage.Stashes = stashes;

                    StashesCount = stashes.Count;
                });
            }, token);
        }

        public void ToggleHistoryShowFlag(Models.HistoryShowFlags flag)
        {
            if (_uiStates.HistoryShowFlags.HasFlag(flag))
                HistoryShowFlags -= flag;
            else
                HistoryShowFlags |= flag;
        }

        public void CreateNewBranch()
        {
            if (_currentBranch == null)
            {
                App.RaiseException(FullPath, "Git cannot create a branch before your first commit.");
                return;
            }

            if (CanCreatePopup())
                ShowPopup(new CreateBranch(this, _currentBranch));
        }

        public async Task CheckoutBranchAsync(Models.Branch branch)
        {
            if (branch.IsLocal)
            {
                var worktree = _worktrees.Find(x => x.Branch.Equals(branch.FullName, StringComparison.Ordinal));
                if (worktree != null)
                {
                    OpenWorktree(worktree);
                    return;
                }
            }

            if (IsBare)
                return;

            if (!CanCreatePopup())
                return;

            if (branch.IsLocal)
            {
                await ShowAndStartPopupAsync(new Checkout(this, branch.Name));
            }
            else
            {
                foreach (var b in _branches)
                {
                    if (b.IsLocal &&
                        b.Upstream.Equals(branch.FullName, StringComparison.Ordinal) &&
                        b.Ahead.Count == 0)
                    {
                        if (b.Behind.Count > 0)
                            ShowPopup(new CheckoutAndFastForward(this, b, branch));
                        else if (!b.IsCurrent)
                            await CheckoutBranchAsync(b);

                        return;
                    }
                }

                ShowPopup(new CreateBranch(this, branch));
            }
        }

        public async Task CheckoutTagAsync(Models.Tag tag)
        {
            var c = await new Commands.QuerySingleCommit(FullPath, tag.SHA).GetResultAsync();
            if (c != null && _histories != null)
                await _histories.CheckoutBranchByCommitAsync(c);
        }

        public void DeleteBranch(Models.Branch branch)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteBranch(this, branch));
        }

        public void DeleteMultipleBranches(List<Models.Branch> branches, bool isLocal)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteMultipleBranches(this, branches, isLocal));
        }

        public void MergeMultipleBranches(List<Models.Branch> branches)
        {
            if (CanCreatePopup())
                ShowPopup(new MergeMultiple(this, branches));
        }

        public void CreateNewTag()
        {
            if (_currentBranch == null)
            {
                App.RaiseException(FullPath, "Git cannot create a branch before your first commit.");
                return;
            }

            if (CanCreatePopup())
                ShowPopup(new CreateTag(this, _currentBranch));
        }

        public void DeleteTag(Models.Tag tag)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteTag(this, tag));
        }

        public void AddRemote()
        {
            if (CanCreatePopup())
                ShowPopup(new AddRemote(this));
        }

        public void DeleteRemote(Models.Remote remote)
        {
            if (CanCreatePopup())
                ShowPopup(new DeleteRemote(this, remote));
        }

        public void AddSubmodule()
        {
            if (CanCreatePopup())
                ShowPopup(new AddSubmodule(this));
        }

        public void UpdateSubmodules()
        {
            if (CanCreatePopup())
                ShowPopup(new UpdateSubmodules(this, null));
        }

        public async Task UpdateSubmodulesRecursivelyAsync()
        {
            if (!CanCreatePopup())
                return;

            var log = CreateLog("Update Submodules Recursively");
            await RunUpdateSubmodulesRecursivelyAsync(log);
            log.Complete();
        }

        public async Task<bool> RunPullUpdateAndFetchPruneRecursivelyAsync(
            Models.ICommandLog log,
            Action<int> onPhaseChanged = null,
            List<string> selectedTargets = null,
            CancellationToken cancellationToken = default,
            Action<Models.RecursiveOperationProgress> onSubmoduleProgressChanged = null)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            onPhaseChanged?.Invoke(0);
            log?.AppendLine("=== Step 1/3: Pull ===");
            var pulled = await RunDefaultPullAsync(log, false, cancellationToken);
            if (!pulled)
            {
                log?.AppendLine("[failed] Pull step failed.");
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
                return false;

            onPhaseChanged?.Invoke(1);
            log?.AppendLine("=== Step 2/3: Update submodules recursively ===");
            var updated = await RunUpdateSubmodulesRecursivelyAsync(log, selectedTargets, true, cancellationToken, onSubmoduleProgressChanged);
            if (!updated)
            {
                log?.AppendLine("[failed] Recursive submodule update failed.");
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
                return false;

            onPhaseChanged?.Invoke(2);
            log?.AppendLine("=== Step 3/3: Fetch and prune recursively ===");
            var fetched = await RunFetchRecursivelyAsync(true, log, true, selectedTargets, cancellationToken, onSubmoduleProgressChanged);
            if (!fetched)
            {
                log?.AppendLine("[failed] Recursive fetch and prune failed.");
                return false;
            }

            return true;
        }

        public async Task<bool> RunPullAndUpdateSubmodulesRecursivelyAsync(
            Models.ICommandLog log,
            Action<int> onPhaseChanged = null,
            List<string> selectedTargets = null,
            CancellationToken cancellationToken = default,
            Action<Models.RecursiveOperationProgress> onSubmoduleProgressChanged = null)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            onPhaseChanged?.Invoke(0);
            log?.AppendLine("=== Step 1/2: Pull ===");
            var pulled = await RunDefaultPullAsync(log, false, cancellationToken);
            if (!pulled)
            {
                log?.AppendLine("[failed] Pull step failed.");
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
                return false;

            onPhaseChanged?.Invoke(1);
            log?.AppendLine("=== Step 2/2: Update submodules recursively ===");
            var updated = await RunUpdateSubmodulesRecursivelyAsync(log, selectedTargets, true, cancellationToken, onSubmoduleProgressChanged);
            if (!updated)
            {
                log?.AppendLine("[failed] Recursive submodule update failed.");
                return false;
            }

            return true;
        }

        public async Task<bool> RunFetchRecursivelyAsync(
            bool prune,
            Models.ICommandLog log,
            bool stopOnError = false,
            List<string> selectedTargets = null,
            CancellationToken cancellationToken = default,
            Action<Models.RecursiveOperationProgress> onProgressChanged = null)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (_remotes.Count == 0)
            {
                log?.AppendLine("No remotes added to this repository.");
                App.RaiseException(FullPath, "No remotes added to this repository!!!");
                return false;
            }

            using var lockWatcher = _watcher?.Lock();

            // Fast recursive toolbar mode: always skip tags to reduce fetch time.
            var noTags = true;
            var force = _uiStates.EnableForceOnFetch;
            var succ = true;
            var succeededTargets = 0;
            var skippedAutomaticallyTargets = 0;
            var skippedByUserTargets = 0;
            var skippedNotInitializedTargets = 0;
            var failedTargets = 0;
            var targets = new List<string>();
            if (selectedTargets == null)
            {
                foreach (var submodule in _submodules)
                {
                    if (!string.IsNullOrWhiteSpace(submodule.Path))
                        targets.Add(submodule.Path);
                }
            }
            else
            {
                var available = new HashSet<string>(StringComparer.Ordinal);
                foreach (var submodule in _submodules)
                {
                    if (!string.IsNullOrWhiteSpace(submodule.Path))
                        available.Add(submodule.Path);
                }

                foreach (var target in selectedTargets)
                {
                    if (!string.IsNullOrWhiteSpace(target) && available.Contains(target))
                        targets.Add(target);
                }

                skippedByUserTargets = Math.Max(0, available.Count - targets.Count);
            }

            var totalTargets = targets.Count;
            var completedTargets = 0;

            // Split recursive fetch into independent units so one hung submodule fetch does not
            // block the whole operation forever.
            log?.AppendLine("=== Fetch root repository ===");
            var rootRemoteNames = GetFetchRemoteNamesForCurrentRepository();
            foreach (var remoteName in rootRemoteNames)
            {
                var one = await RunSplitFetchUnitAsync(
                    FullPath,
                    remoteName,
                    noTags,
                    force,
                    prune,
                    false,
                    log,
                    "root",
                    stopOnError,
                    cancellationToken);
                if (!one)
                {
                    succ = false;
                    if (stopOnError)
                        return false;
                }
            }

            foreach (var submodulePath in targets)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;

                onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                {
                    Total = totalTargets,
                    Succeeded = succeededTargets,
                    SkippedByUser = skippedByUserTargets,
                    SkippedAutomatically = skippedAutomaticallyTargets,
                    SkippedNotInitialized = skippedNotInitializedTargets,
                    Failed = failedTargets,
                    CurrentTarget = submodulePath,
                    CurrentState = Models.RecursiveOperationTargetState.Running,
                });

                var submoduleRoot = Native.OS.GetAbsPath(FullPath, submodulePath).Replace('\\', '/');
                var gitDir = Path.Combine(submoduleRoot, ".git");
                if (!Directory.Exists(submoduleRoot) || (!Directory.Exists(gitDir) && !File.Exists(gitDir)))
                {
                    log?.AppendLine($"Skip submodule `{submodulePath}` (not initialized).");
                    skippedAutomaticallyTargets++;
                    skippedNotInitializedTargets++;
                    completedTargets++;
                    onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                    {
                        Total = totalTargets,
                        Succeeded = succeededTargets,
                        SkippedByUser = skippedByUserTargets,
                        SkippedAutomatically = skippedAutomaticallyTargets,
                        SkippedNotInitialized = skippedNotInitializedTargets,
                        Failed = failedTargets,
                        CurrentTarget = submodulePath,
                        CurrentState = Models.RecursiveOperationTargetState.Skipped,
                    });
                    continue;
                }

                var submoduleRemotes = await GetFetchRemoteNamesForRepositoryAsync(submoduleRoot);
                if (submoduleRemotes.Count == 0)
                {
                    log?.AppendLine($"Skip submodule `{submodulePath}` (no remotes).");
                    skippedAutomaticallyTargets++;
                    completedTargets++;
                    onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                    {
                        Total = totalTargets,
                        Succeeded = succeededTargets,
                        SkippedByUser = skippedByUserTargets,
                        SkippedAutomatically = skippedAutomaticallyTargets,
                        SkippedNotInitialized = skippedNotInitializedTargets,
                        Failed = failedTargets,
                        CurrentTarget = submodulePath,
                        CurrentState = Models.RecursiveOperationTargetState.Skipped,
                    });
                    continue;
                }

                log?.AppendLine($"=== Fetch submodule `{submodulePath}` ===");
                var submoduleSucceeded = true;
                foreach (var remoteName in submoduleRemotes)
                {
                    var one = await RunSplitFetchUnitAsync(
                        submoduleRoot,
                        remoteName,
                        noTags,
                        force,
                        prune,
                        true,
                        log,
                        $"submodule:{submodulePath}",
                        stopOnError,
                        cancellationToken);
                    if (!one)
                    {
                        submoduleSucceeded = false;
                        succ = false;
                        if (stopOnError)
                        {
                            failedTargets++;
                            onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                            {
                                Total = totalTargets,
                                Succeeded = succeededTargets,
                                SkippedByUser = skippedByUserTargets,
                                SkippedAutomatically = skippedAutomaticallyTargets,
                                SkippedNotInitialized = skippedNotInitializedTargets,
                                Failed = failedTargets,
                                CurrentTarget = submodulePath,
                                CurrentState = Models.RecursiveOperationTargetState.Failed,
                            });
                            return false;
                        }
                    }
                }

                if (submoduleSucceeded)
                    succeededTargets++;
                else
                    failedTargets++;

                completedTargets++;
                onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                {
                    Total = totalTargets,
                    Succeeded = succeededTargets,
                    SkippedByUser = skippedByUserTargets,
                    SkippedAutomatically = skippedAutomaticallyTargets,
                    SkippedNotInitialized = skippedNotInitializedTargets,
                    Failed = failedTargets,
                    CurrentTarget = submodulePath,
                    CurrentState = submoduleSucceeded ? Models.RecursiveOperationTargetState.Succeeded : Models.RecursiveOperationTargetState.Failed,
                });
            }

            if (succ)
                MarkFetched();

            return succ;
        }

        private List<string> GetFetchRemoteNamesForCurrentRepository()
        {
            var names = new List<string>();
            var preferred = GetPreferredRemoteName();
            if (!string.IsNullOrEmpty(preferred))
                names.Add(preferred);

            return names;
        }

        private async Task<List<string>> GetFetchRemoteNamesForRepositoryAsync(string repoPath)
        {
            var remotes = await new Commands.QueryRemotes(repoPath).GetResultAsync().ConfigureAwait(false);
            if (remotes.Count == 0)
                return [];

            var preferred = remotes.Find(x => x.Name.Equals("origin", StringComparison.Ordinal))?.Name ?? remotes[0].Name;
            return [preferred];
        }

        private async Task<bool> RunSplitFetchUnitAsync(
            string repoPath,
            string remoteName,
            bool noTags,
            bool force,
            bool prune,
            bool recurseSubmodules,
            Models.ICommandLog log,
            string scope,
            bool stopOnError,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(remoteName) || cancellationToken.IsCancellationRequested)
                return false;

            using var timeout = new CancellationTokenSource(SPLIT_FETCH_TIMEOUT);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            var cmd = new Commands.Fetch(repoPath, remoteName, noTags, force, prune, recurseSubmodules)
            {
                RaiseError = stopOnError,
                CancellationToken = linked.Token,
            };

            var ok = await cmd.Use(log).RunAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                log?.AppendLine($"[canceled] Fetch `{remoteName}` in `{scope}` was canceled.");
                return false;
            }

            if (timeout.IsCancellationRequested)
            {
                log?.AppendLine($"[timeout] Fetch `{remoteName}` in `{scope}` exceeded {SPLIT_FETCH_TIMEOUT.TotalMinutes:0} min and was terminated.");
                if (stopOnError)
                    App.RaiseException(repoPath, $"Fetch `{remoteName}` in `{scope}` timed out.");
                return false;
            }

            if (!ok)
                log?.AppendLine($"[failed] Fetch `{remoteName}` in `{scope}` failed.");

            return ok;
        }

        public async Task<bool> RunUpdateSubmodulesRecursivelyAsync(
            Models.ICommandLog log,
            List<string> selectedTargets = null,
            bool stopOnError = false,
            CancellationToken cancellationToken = default,
            Action<Models.RecursiveOperationProgress> onProgressChanged = null)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var targets = new List<string>();
            var skippedByUserTargets = 0;
            if (selectedTargets == null)
            {
                foreach (var submodule in _submodules)
                    targets.Add(submodule.Path);
            }
            else
            {
                var available = new HashSet<string>(StringComparer.Ordinal);
                foreach (var submodule in _submodules)
                    available.Add(submodule.Path);

                foreach (var target in selectedTargets)
                {
                    if (!string.IsNullOrWhiteSpace(target) && available.Contains(target))
                        targets.Add(target);
                }

                skippedByUserTargets = Math.Max(0, available.Count - targets.Count);
            }

            if (targets.Count == 0)
            {
                log?.AppendLine(selectedTargets == null ? "No submodules found." : "No submodules selected.");
                return true;
            }

            using var lockWatcher = _watcher?.Lock();
            var succ = true;
            var anyUpdated = false;
            var totalTargets = targets.Count;
            var completedTargets = 0;
            var succeededTargets = 0;
            var failedTargets = 0;

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                if (cancellationToken.IsCancellationRequested)
                    return false;

                using var timeout = new CancellationTokenSource(SPLIT_SUBMODULE_UPDATE_TIMEOUT);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
                var cmd = new Commands.Submodule(FullPath)
                {
                    RaiseError = stopOnError,
                    CancellationToken = linked.Token,
                };

                onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                {
                    Total = totalTargets,
                    Succeeded = succeededTargets,
                    SkippedByUser = skippedByUserTargets,
                    Failed = failedTargets,
                    CurrentTarget = target,
                    CurrentState = Models.RecursiveOperationTargetState.Running,
                });
                log?.AppendLine($"=== Update submodule `{target}` ===");
                var one = await cmd.Use(log).UpdateAsync([target], true, false).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    log?.AppendLine($"[canceled] Update `{target}` was canceled.");
                    return false;
                }

                if (timeout.IsCancellationRequested)
                {
                    log?.AppendLine($"[timeout] Update `{target}` exceeded {SPLIT_SUBMODULE_UPDATE_TIMEOUT.TotalMinutes:0} min and was terminated.");
                    succ = false;
                    failedTargets++;
                    completedTargets++;
                    onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                    {
                        Total = totalTargets,
                        Succeeded = succeededTargets,
                        SkippedByUser = skippedByUserTargets,
                        Failed = failedTargets,
                        CurrentTarget = target,
                        CurrentState = Models.RecursiveOperationTargetState.Failed,
                    });
                    if (stopOnError)
                    {
                        App.RaiseException(FullPath, $"Update `{target}` timed out.");
                        return false;
                    }
                    continue;
                }

                if (one)
                {
                    anyUpdated = true;
                    succeededTargets++;
                }
                else
                {
                    log?.AppendLine($"[failed] Update `{target}` failed.");
                    succ = false;
                    failedTargets++;
                    if (stopOnError)
                    {
                        completedTargets++;
                        onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                        {
                            Total = totalTargets,
                            Succeeded = succeededTargets,
                            SkippedByUser = skippedByUserTargets,
                            Failed = failedTargets,
                            CurrentTarget = target,
                            CurrentState = Models.RecursiveOperationTargetState.Failed,
                        });
                        return false;
                    }
                }

                completedTargets++;
                onProgressChanged?.Invoke(new Models.RecursiveOperationProgress
                {
                    Total = totalTargets,
                    Succeeded = succeededTargets,
                    SkippedByUser = skippedByUserTargets,
                    Failed = failedTargets,
                    CurrentTarget = target,
                    CurrentState = one ? Models.RecursiveOperationTargetState.Succeeded : Models.RecursiveOperationTargetState.Failed,
                });
            }

            if (anyUpdated)
                MarkSubmodulesDirtyManually();

            return succ;
        }

        public async Task AutoUpdateSubmodulesAsync(Models.ICommandLog log)
        {
            var submodules = await new Commands.QueryUpdatableSubmodules(FullPath, false).GetResultAsync();
            if (submodules.Count == 0)
                return;

            do
            {
                if (_settings.AskBeforeAutoUpdatingSubmodules)
                {
                    var builder = new StringBuilder();
                    builder.Append("\n\n");
                    foreach (var s in submodules)
                        builder.Append("- ").Append(s).Append('\n');
                    builder.Append("\n");

                    var msg = App.Text("Checkout.WarnUpdatingSubmodules", builder.ToString());
                    var shouldContinue = await App.AskConfirmAsync(msg, Models.ConfirmButtonType.YesNo);
                    if (!shouldContinue)
                        break;
                }

                await new Commands.Submodule(FullPath)
                    .Use(log)
                    .UpdateAsync(submodules);
            } while (false);
        }

        public void OpenSubmodule(string submodule)
        {
            var selfPage = GetOwnerPage();
            if (selfPage == null)
                return;

            var root = Path.GetFullPath(Path.Combine(FullPath, submodule));
            var normalizedPath = root.Replace('\\', '/').TrimEnd('/');
            var prefs = Preferences.Instance;
            var node = prefs.FindNode(normalizedPath);
            var isNew = node == null;
            if (isNew)
                node = prefs.FindOrAddNodeByRepositoryPath(normalizedPath, null, false, false);

            var desiredBookmark = selfPage.Node.Bookmark;
            var bookmarkChanged = node.Bookmark != desiredBookmark;
            if (bookmarkChanged)
                node.Bookmark = desiredBookmark;

            if (isNew || bookmarkChanged)
                prefs.Save();

            var superProjectSubmoduleSHA = _submodules.Find(x => x.Path.Equals(submodule, StringComparison.Ordinal))?.SHA;
            App.GetLauncher().OpenRepositoryInTab(node, null, superProjectSubmoduleSHA, selfPage);
        }

        public void UpdateSuperProjectSubmoduleSHA(string sha)
        {
            var normalized = NormalizeSubmodulePointerSHA(sha);
            if (_superProjectSubmoduleSHA == normalized)
                return;

            _superProjectSubmoduleSHA = normalized;
            OnPropertyChanged(nameof(HasSuperProjectPointer));
            RefreshCommits();
        }

        public void NavigateToSuperProjectPointerCommit()
        {
            if (string.IsNullOrEmpty(_superProjectSubmoduleSHA))
                return;

            NavigateToCommit(_superProjectSubmoduleSHA);
        }

        public void RefreshSuperProjectSubmodulePointer()
        {
            _ = ResolveSuperProjectSubmodulePointerAsync();
        }

        public void AddWorktree()
        {
            if (CanCreatePopup())
                ShowPopup(new AddWorktree(this));
        }

        public async Task PruneWorktreesAsync()
        {
            if (CanCreatePopup())
                await ShowAndStartPopupAsync(new PruneWorktrees(this));
        }

        public void OpenWorktree(Models.Worktree worktree)
        {
            var node = Preferences.Instance.FindNode(worktree.FullPath) ??
                new RepositoryNode
                {
                    Id = worktree.FullPath,
                    Name = Path.GetFileName(worktree.FullPath),
                    Bookmark = Preferences.Instance.FindUnusedBookmarkColor(),
                    IsRepository = true,
                };

            App.GetLauncher().OpenRepositoryInTab(node, null);
        }

        public async Task LockWorktreeAsync(Models.Worktree worktree)
        {
            using var lockWatcher = _watcher?.Lock();
            var log = CreateLog("Lock Worktree");
            var succ = await new Commands.Worktree(FullPath).Use(log).LockAsync(worktree.FullPath);
            if (succ)
                worktree.IsLocked = true;
            log.Complete();
        }

        public async Task UnlockWorktreeAsync(Models.Worktree worktree)
        {
            using var lockWatcher = _watcher?.Lock();
            var log = CreateLog("Unlock Worktree");
            var succ = await new Commands.Worktree(FullPath).Use(log).UnlockAsync(worktree.FullPath);
            if (succ)
                worktree.IsLocked = false;
            log.Complete();
        }

        public List<Models.OpenAIService> GetPreferredOpenAIServices()
        {
            var services = Preferences.Instance.OpenAIServices;
            if (services == null || services.Count == 0)
                return [];

            if (services.Count == 1)
                return [services[0]];

            var preferred = _settings.PreferredOpenAIService;
            var all = new List<Models.OpenAIService>();
            foreach (var service in services)
            {
                if (service.Name.Equals(preferred, StringComparison.Ordinal))
                    return [service];

                all.Add(service);
            }

            return all;
        }

        public void DiscardAllChanges()
        {
            if (CanCreatePopup())
                ShowPopup(new Discard(this));
        }

        public void ClearStashes()
        {
            if (CanCreatePopup())
                ShowPopup(new ClearStashes(this));
        }

        public async Task<bool> SaveCommitAsPatchAsync(Models.Commit commit, string folder, int index = 0)
        {
            var ignoredChars = new HashSet<char> { '/', '\\', ':', ',', '*', '?', '\"', '<', '>', '|', '`', '$', '^', '%', '[', ']', '+', '-' };
            var builder = new StringBuilder();
            builder.Append(index.ToString("D4"));
            builder.Append('-');

            var chars = commit.Subject.ToCharArray();
            var len = 0;
            foreach (var c in chars)
            {
                if (!ignoredChars.Contains(c))
                {
                    if (c == ' ' || c == '\t')
                        builder.Append('-');
                    else
                        builder.Append(c);

                    len++;

                    if (len >= 48)
                        break;
                }
            }
            builder.Append(".patch");

            var saveTo = Path.Combine(folder, builder.ToString());
            var log = CreateLog("Save Commit as Patch");
            var succ = await new Commands.FormatPatch(FullPath, commit.SHA, saveTo).Use(log).ExecAsync();
            log.Complete();
            return succ;
        }

        private LauncherPage GetOwnerPage()
        {
            var launcher = App.GetLauncher();
            if (launcher == null)
                return null;

            foreach (var page in launcher.Pages)
            {
                if (page.Node.Id.Equals(FullPath))
                    return page;
            }

            return null;
        }

        private List<Models.Branch> GetVisibleBranchesByCurrentFilter()
        {
            var presetBranchFilter = GetPresetBranchFilterMatchCache();

            var visibles = new List<Models.Branch>();
            foreach (var branch in _branches)
            {
                if (!IsShowingAllBranches &&
                    !presetBranchFilter.ShouldShow(branch.Name))
                    continue;

                if (!string.IsNullOrEmpty(_filter) && !branch.FullName.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                visibles.Add(branch);
            }

            return visibles;
        }

        private BranchTreeNode.Builder BuildBranchTree(List<Models.Branch> branches, List<Models.Remote> remotes, bool shouldCleanupExpandedNodes)
        {
            var builder = new BranchTreeNode.Builder(_uiStates.LocalBranchSortMode, _uiStates.RemoteBranchSortMode);
            if (string.IsNullOrEmpty(_filter))
            {
                builder.SetExpandedNodes(_uiStates.ExpandedBranchNodesInSideBar);
                builder.Run(branches, remotes, false);

                if (shouldCleanupExpandedNodes)
                {
                    foreach (var invalid in builder.InvalidExpandedNodes)
                        _uiStates.ExpandedBranchNodesInSideBar.Remove(invalid);
                }
            }
            else
            {
                builder.Run(branches, remotes, true);
            }

            var filterMap = _uiStates.GetHistoryFiltersMap();
            UpdateBranchTreeFilterMode(builder.Locals, filterMap);
            UpdateBranchTreeFilterMode(builder.Remotes, filterMap);
            return builder;
        }

        private object BuildVisibleTags()
        {
            switch (_uiStates.TagSortMode)
            {
                case Models.TagSortMode.CreatorDate:
                    _tags.Sort((l, r) => r.CreatorDate.CompareTo(l.CreatorDate));
                    break;
                default:
                    _tags.Sort((l, r) => Models.NumericSort.Compare(l.Name, r.Name));
                    break;
            }

            var visible = new List<Models.Tag>();
            if (string.IsNullOrEmpty(_filter))
            {
                visible.AddRange(_tags);
            }
            else
            {
                foreach (var t in _tags)
                {
                    if (t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(t);
                }
            }

            var filterMap = _uiStates.GetHistoryFiltersMap();
            UpdateTagFilterMode(filterMap);

            if (_uiStates.ShowTagsAsTree)
            {
                var tree = TagCollectionAsTree.Build(visible, _visibleTags as TagCollectionAsTree);
                foreach (var node in tree.Tree)
                    node.UpdateFilterMode(filterMap);
                return tree;
            }
            else
            {
                var list = new TagCollectionAsList(visible);
                foreach (var item in list.TagItems)
                    item.FilterMode = filterMap.GetValueOrDefault(item.Tag.Name, Models.FilterMode.None);
                return list;
            }
        }

        private object BuildVisibleSubmodules()
        {
            var visible = new List<Models.Submodule>();
            if (string.IsNullOrEmpty(_filter))
            {
                visible.AddRange(_submodules);
            }
            else
            {
                foreach (var s in _submodules)
                {
                    if (s.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(s);
                }
            }

            if (_uiStates.ShowSubmodulesAsTree)
                return SubmoduleCollectionAsTree.Build(visible, _visibleSubmodules as SubmoduleCollectionAsTree);
            else
                return new SubmoduleCollectionAsList() { Submodules = visible };
        }

        private void RefreshHistoryFilters(bool refresh)
        {
            EnsureIncludedBranchFiltersHaveColors();
            HistoryFilterMode = _uiStates.GetHistoryFilterMode();
            NotifyHistoryFilterIndicatorsChanged();
            NotifyCurrentBranchVisualChanged();
            if (!refresh)
                return;

            var map = _uiStates.GetHistoryFiltersMap();
            UpdateBranchTreeFilterMode(LocalBranchTrees, map);
            UpdateBranchTreeFilterMode(RemoteBranchTrees, map);
            UpdateTagFilterMode(map);
            RefreshCommits();
        }

        private void NotifyHistoryFilterIndicatorsChanged()
        {
            OnPropertyChanged(nameof(IncludedHistoryFilterCount));
            OnPropertyChanged(nameof(ExcludedHistoryFilterCount));
        }

        private HashSet<string> CollectVisibleFoldableBranchFullNamesInGraph()
        {
            var visible = new HashSet<string>(StringComparer.Ordinal);
            var commits = _histories?.Commits;
            if (commits is not { Count: > 0 })
                return visible;

            foreach (var commit in commits)
            {
                foreach (var decorator in commit.Decorators)
                {
                    if (!decorator.IsBranchFoldable)
                        continue;

                    if (TryGetDecoratorBranchFullName(decorator, out var fullName))
                        visible.Add(fullName);
                }
            }

            return visible;
        }

        private void UpdateVisibleFoldBranchStatesFromCurrentGraph()
        {
            var visible = CollectVisibleFoldableBranchFullNamesInGraph();
            _visibleFoldableBranchesCount = visible.Count;

            var foldedVisible = 0;
            foreach (var fullName in visible)
            {
                if (_foldedBranchFullNames.Contains(fullName))
                    foldedVisible++;
            }

            _visibleFoldedBranchesCount = foldedVisible;
            NotifyFoldControlsChanged();
        }

        private void NotifyFoldControlsChanged()
        {
            OnPropertyChanged(nameof(CanFoldVisibleBranchesInGraph));
            OnPropertyChanged(nameof(CanUnfoldBranchesInGraph));
        }

        private void NotifyCurrentBranchVisualChanged()
        {
            OnPropertyChanged(nameof(CurrentBranchDisplayName));
            OnPropertyChanged(nameof(CurrentBranchDisplayLabel));
            OnPropertyChanged(nameof(CurrentBranchDisplayBackground));
            OnPropertyChanged(nameof(CurrentBranchDisplayForeground));
            NotifyCompactStatusChanged();
        }

        private void NotifyCompactStatusChanged()
        {
            OnPropertyChanged(nameof(CurrentBranchAheadCount));
            OnPropertyChanged(nameof(CurrentBranchBehindCount));
            OnPropertyChanged(nameof(HasAheadStatus));
            OnPropertyChanged(nameof(HasBehindStatus));
            OnPropertyChanged(nameof(HasLocalChangesStatus));
            OnPropertyChanged(nameof(HasInProgressStatus));
            OnPropertyChanged(nameof(HasStatusStripItems));
            OnPropertyChanged(nameof(ShowCleanStatus));
            OnPropertyChanged(nameof(InProgressStatusText));
        }

        private uint ResolveCurrentBranchDisplayColor()
        {
            if (CurrentBranch == null)
                return Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR;

            var fallback = GetBranchFilterColor(CurrentBranch);
            var commits = _histories?.Commits;
            if (commits is not { Count: > 0 })
                return fallback;

            var head = commits.Find(x => x.IsCurrentHead);
            if (head == null)
                return fallback;

            foreach (var decorator in head.Decorators)
            {
                if (decorator.Color == 0)
                    continue;

                if (decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.LocalBranchHead)
                {
                    if (CurrentBranch.IsLocal && decorator.Name.Equals(CurrentBranch.Name, StringComparison.Ordinal))
                        return decorator.Color;
                }
                else if (decorator.Type == Models.DecoratorType.RemoteBranchHead)
                {
                    if (!CurrentBranch.IsLocal && decorator.Name.Equals(CurrentBranch.FriendlyName, StringComparison.Ordinal))
                        return decorator.Color;
                }
            }

            if (head.Color >= 0 &&
                head.Color < Models.CommitGraph.Pens.Count &&
                Models.CommitGraph.Pens[head.Color].Brush is ISolidColorBrush solid)
                return solid.Color.ToUInt32();

            return fallback;
        }

        private static string FormatCurrentBranchDisplayLabel(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "--";

            const int minChunk = 6;
            const int fallbackSplit = 24;
            if (name.Length <= fallbackSplit)
                return name;

            var mid = name.Length / 2;
            var best = -1;
            var bestDistance = int.MaxValue;
            for (var i = minChunk; i < name.Length - minChunk; i++)
            {
                var ch = name[i];
                if (ch != '-' && ch != '_')
                    continue;

                var distance = Math.Abs(i - mid);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            if (best >= 0)
                return name.Insert(best + 1, "\n");

            var split = Math.Min(fallbackSplit, name.Length - 1);
            if (split <= 0)
                return name;

            return name.Insert(split, "\n");
        }

        private void UpdateBranchTreeFilterMode(List<BranchTreeNode> nodes, Dictionary<string, Models.FilterMode> map)
        {
            foreach (var node in nodes)
            {
                node.FilterMode = map.GetValueOrDefault(node.Path, Models.FilterMode.None);

                if (!node.IsBranch)
                    UpdateBranchTreeFilterMode(node.Children, map);
            }
        }

        private void UpdateTagFilterMode(Dictionary<string, Models.FilterMode> map)
        {
            if (VisibleTags is TagCollectionAsTree tree)
            {
                foreach (var node in tree.Tree)
                    node.UpdateFilterMode(map);
            }
            else if (VisibleTags is TagCollectionAsList list)
            {
                foreach (var item in list.TagItems)
                    item.FilterMode = map.GetValueOrDefault(item.Tag.Name, Models.FilterMode.None);
            }
        }

        private void ResetBranchTreeFilterMode(List<BranchTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.FilterMode = Models.FilterMode.None;
                if (!node.IsBranch)
                    ResetBranchTreeFilterMode(node.Children);
            }
        }

        private void ResetTagFilterMode()
        {
            if (VisibleTags is TagCollectionAsTree tree)
            {
                var filters = new Dictionary<string, Models.FilterMode>();
                foreach (var node in tree.Tree)
                    node.UpdateFilterMode(filters);
            }
            else if (VisibleTags is TagCollectionAsList list)
            {
                foreach (var item in list.TagItems)
                    item.FilterMode = Models.FilterMode.None;
            }
        }

        private BranchTreeNode FindBranchNode(List<BranchTreeNode> nodes, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            foreach (var node in nodes)
            {
                if (node.Path.Equals(path, StringComparison.Ordinal))
                    return node;

                if (path.StartsWith(node.Path, StringComparison.Ordinal))
                {
                    var founded = FindBranchNode(node.Children, path);
                    if (founded != null)
                        return founded;
                }
            }

            return null;
        }

        private Color ResolvePageAccentColor()
        {
            var bookmark = GetOwnerPage()?.Node?.Bookmark ?? 0;
            if (Models.Bookmarks.Get(bookmark) is ISolidColorBrush solid)
                return solid.Color;

            return Color.FromUInt32(Preferences.Instance.MainAccentColor);
        }

        private void OnPreferencesPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Preferences.MainAccentColor))
                NotifyAccentColorChanged();
        }

        private string GetPreferredRemoteName()
        {
            if (_remotes.Count == 0)
                return string.Empty;

            if (!string.IsNullOrEmpty(_settings.DefaultRemote))
            {
                var preferred = _remotes.Find(x => x.Name.Equals(_settings.DefaultRemote, StringComparison.Ordinal));
                if (preferred != null)
                    return preferred.Name;
            }

            return _remotes[0].Name;
        }

        private async Task<List<string>> BuildQuickFetchFilteredRefSpecsAsync(string remoteName)
        {
            var branchNames = new HashSet<string>(StringComparer.Ordinal);
            if (_uiStates == null || string.IsNullOrEmpty(remoteName))
                return [];

            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Mode != Models.FilterMode.Included)
                    continue;

                switch (filter.Type)
                {
                    case Models.FilterType.LocalBranch:
                        AddQuickFetchLocalBranchTarget(filter.Pattern, remoteName, branchNames);
                        break;
                    case Models.FilterType.LocalBranchFolder:
                        AddQuickFetchBranchFolderTargets(filter.Pattern, remoteName, true, branchNames);
                        break;
                    case Models.FilterType.RemoteBranch:
                        AddQuickFetchRemoteBranchTarget(filter.Pattern, remoteName, branchNames);
                        break;
                    case Models.FilterType.RemoteBranchFolder:
                        AddQuickFetchBranchFolderTargets(filter.Pattern, remoteName, false, branchNames);
                        break;
                }
            }

            var results = new List<string>();
            if (branchNames.Count == 0)
                return results;

            var remote = new Commands.Remote(FullPath);
            foreach (var branchName in branchNames)
            {
                if (await remote.HasBranchAsync(remoteName, branchName).ConfigureAwait(false))
                    results.Add($"refs/heads/{branchName}:refs/remotes/{remoteName}/{branchName}");
            }

            return results;
        }

        private void AddQuickFetchBranchFolderTargets(string folderPattern, string remoteName, bool isLocalFolder, HashSet<string> branchNames)
        {
            if (string.IsNullOrEmpty(folderPattern))
                return;

            foreach (var branch in _branches)
            {
                if (branch == null || string.IsNullOrEmpty(branch.FullName) || branch.IsLocal != isLocalFolder)
                    continue;

                if (!IsBranchUnderFolder(branch.FullName, folderPattern))
                    continue;

                if (isLocalFolder)
                    AddQuickFetchLocalBranchTarget(branch, remoteName, branchNames);
                else
                    AddQuickFetchRemoteBranchTarget(branch.FullName, remoteName, branchNames);
            }
        }

        private void AddQuickFetchLocalBranchTarget(string fullName, string remoteName, HashSet<string> branchNames)
        {
            const string prefix = "refs/heads/";
            if (string.IsNullOrEmpty(fullName) || !fullName.StartsWith(prefix, StringComparison.Ordinal))
                return;

            var branch = _branches.Find(x => x.IsLocal && x.FullName.Equals(fullName, StringComparison.Ordinal));
            AddQuickFetchLocalBranchTarget(branch, remoteName, branchNames);
        }

        private void AddQuickFetchLocalBranchTarget(Models.Branch branch, string remoteName, HashSet<string> branchNames)
        {
            if (branch == null || string.IsNullOrEmpty(branch.Name))
                return;

            var remoteBranchName = string.Empty;
            if (!string.IsNullOrEmpty(branch.Upstream) &&
                branch.Upstream.StartsWith($"refs/remotes/{remoteName}/", StringComparison.Ordinal))
            {
                remoteBranchName = branch.Upstream.Substring($"refs/remotes/{remoteName}/".Length);
            }
            else
            {
                var sameNameRemote = _branches.Find(x =>
                    !x.IsLocal &&
                    x.Remote == remoteName &&
                    x.Name.Equals(branch.Name, StringComparison.Ordinal));
                if (sameNameRemote != null)
                    remoteBranchName = sameNameRemote.Name;
            }

            if (string.IsNullOrEmpty(remoteBranchName))
                return;

            branchNames.Add(remoteBranchName);
        }

        private static void AddQuickFetchRemoteBranchTarget(string fullName, string remoteName, HashSet<string> branchNames)
        {
            var prefix = $"refs/remotes/{remoteName}/";
            if (string.IsNullOrEmpty(fullName) || !fullName.StartsWith(prefix, StringComparison.Ordinal))
                return;

            var branchName = fullName.Substring(prefix.Length);
            if (string.IsNullOrEmpty(branchName))
                return;

            branchNames.Add(branchName);
        }

        private static bool IsBranchUnderFolder(string fullName, string folderPattern)
        {
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(folderPattern))
                return false;

            return fullName.Length > folderPattern.Length &&
                fullName.StartsWith(folderPattern, StringComparison.Ordinal) &&
                fullName[folderPattern.Length] == '/';
        }

        private void ShowFetchDurationToast(double seconds)
        {
            _fetchDurationToastCancellation?.Cancel();
            _fetchDurationToastCancellation?.Dispose();

            var cts = new CancellationTokenSource();
            _fetchDurationToastCancellation = cts;

            FetchDurationToastText = $"The last fetch costs {seconds:0.0} seconds";
            FetchDurationToastOpacity = 1.0;
            IsFetchDurationToastVisible = true;

            _ = FadeFetchDurationToastAsync(cts.Token);
        }

        private async Task FadeFetchDurationToastAsync(CancellationToken token)
        {
            const int durationMs = 3000;
            const int tickMs = 100;
            var startedAt = DateTime.UtcNow;

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var elapsed = (DateTime.UtcNow - startedAt).TotalMilliseconds;
                    if (elapsed >= durationMs)
                        break;

                    FetchDurationToastOpacity = Math.Max(0.0, 1.0 - elapsed / durationMs);
                    await Task.Delay(tickMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            FetchDurationToastOpacity = 0.0;
            IsFetchDurationToastVisible = false;
        }

        private void RebuildPresetBranchExactColorItems()
        {
            var names = _settings?.GetPresetBranchExactNameList() ?? [];
            var colors = _settings?.GetPresetBranchExactNameColorMap() ?? [];

            _presetBranchExactColorItems.Clear();
            foreach (var name in names)
            {
                var color = colors.GetValueOrDefault(name, Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR);
                _presetBranchExactColorItems.Add(new PresetBranchExactColorItem(name, color));
            }

            OnPropertyChanged(nameof(HasPresetBranchExactColorItems));
        }

        private static bool ShouldShowByPresetBranchFilters(string name, HashSet<string> exactNames, List<string> containsPatterns, HashSet<string> excludeNames)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            var hasIncludeRules = exactNames.Count > 0 || containsPatterns.Count > 0;
            var isExcluded = excludeNames.Contains(name);

            if (!hasIncludeRules)
                return !isExcluded;

            if (isExcluded)
                return false;

            if (exactNames.Contains(name))
                return true;

            foreach (var pattern in containsPatterns)
            {
                if (name.Contains(pattern, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private PresetBranchFilterMatchCache GetPresetBranchFilterMatchCache()
        {
            var exactRaw = _settings?.PresetBranchExactNames ?? string.Empty;
            var containsRaw = _settings?.PresetBranchContainsPatterns ?? string.Empty;
            var excludeRaw = _settings?.PresetBranchExcludeNames ?? string.Empty;

            if (_presetBranchFilterMatchCache != null &&
                _presetBranchFilterMatchCache.Version == _presetBranchFilterMatchCacheVersion &&
                _presetBranchFilterMatchCache.ExactRaw.Equals(exactRaw, StringComparison.Ordinal) &&
                _presetBranchFilterMatchCache.ContainsRaw.Equals(containsRaw, StringComparison.Ordinal) &&
                _presetBranchFilterMatchCache.ExcludeRaw.Equals(excludeRaw, StringComparison.Ordinal))
            {
                return _presetBranchFilterMatchCache;
            }

            var cache = new PresetBranchFilterMatchCache()
            {
                Version = _presetBranchFilterMatchCacheVersion,
                ExactRaw = exactRaw,
                ContainsRaw = containsRaw,
                ExcludeRaw = excludeRaw,
                ExactNames = _settings?.GetPresetBranchExactNameSet() ?? [],
                ContainsPatterns = _settings?.GetPresetBranchContainsRuleList() ?? [],
                ExcludeNames = _settings?.GetPresetBranchExcludeNameSet() ?? [],
            };

            var distinctNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var branch in _branches)
            {
                if (!string.IsNullOrWhiteSpace(branch?.Name))
                    distinctNames.Add(branch.Name);
            }

            foreach (var name in distinctNames)
            {
                if (ShouldShowByPresetBranchFilters(name, cache.ExactNames, cache.ContainsPatterns, cache.ExcludeNames))
                    cache.VisibleBranchNames.Add(name);
            }

            _presetBranchFilterMatchCache = cache;
            return cache;
        }

        private void InvalidatePresetBranchFilterMatchCache()
        {
            _presetBranchFilterMatchCacheVersion++;
            _presetBranchFilterMatchCache = null;
        }

        private static string NormalizeSubmodulePointerSHA(string sha)
        {
            if (string.IsNullOrWhiteSpace(sha))
                return string.Empty;

            var normalized = sha.Trim();
            if (normalized.Length != 40)
                return string.Empty;

            foreach (var c in normalized)
            {
                if (!Uri.IsHexDigit(c))
                    return string.Empty;
            }

            return normalized.ToLowerInvariant();
        }

        private bool ShouldAttachParentRepositoryDecorator()
        {
            return string.IsNullOrEmpty(_superProjectSubmoduleSHA) && _submodules.Count > 0;
        }

        private async Task ResolveSuperProjectSubmodulePointerAsync()
        {
            var resolved = string.Empty;

            try
            {
                var superProjectRoot = await new Commands.QuerySuperProjectRootPath(FullPath).GetResultAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(superProjectRoot))
                {
                    var normalizedSuperProjectRoot = superProjectRoot.Replace('\\', '/').TrimEnd('/');
                    var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (!normalizedSuperProjectRoot.Equals(FullPath, pathComparison))
                    {
                        var submodules = await new Commands.QuerySubmodules(normalizedSuperProjectRoot).GetResultAsync().ConfigureAwait(false);
                        foreach (var submodule in submodules)
                        {
                            if (string.IsNullOrWhiteSpace(submodule.Path))
                                continue;

                            var submoduleRoot = Path.GetFullPath(Path.Combine(normalizedSuperProjectRoot, submodule.Path)).Replace('\\', '/').TrimEnd('/');
                            if (!submoduleRoot.Equals(FullPath, pathComparison))
                                continue;

                            resolved = NormalizeSubmodulePointerSHA(submodule.SHA);
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Best-effort: keep existing behavior if auto-detection fails.
            }

            await Dispatcher.UIThread.InvokeAsync(() => UpdateSuperProjectSubmoduleSHA(resolved));
        }

        private void AttachSuperProjectPointerDecorator(List<Models.Commit> commits)
        {
            if (commits == null || commits.Count == 0)
                return;

            foreach (var commit in commits)
                commit.Decorators.RemoveAll(x => x.Type == Models.DecoratorType.SuperProjectPointer);

            if (string.IsNullOrEmpty(_superProjectSubmoduleSHA))
                return;

            var target = commits.Find(x => x.SHA.Equals(_superProjectSubmoduleSHA, StringComparison.OrdinalIgnoreCase));
            if (target == null)
                return;

            target.Decorators.Add(new Models.Decorator()
            {
                Type = Models.DecoratorType.SuperProjectPointer,
                Name = "SPP",
            });

            Models.Commit.SortDecorators(target.Decorators);
        }

        private void AttachParentRepositoryDecorator(List<Models.Commit> commits)
        {
            if (commits == null || commits.Count == 0)
                return;

            foreach (var commit in commits)
                commit.Decorators.RemoveAll(x => x.Type == Models.DecoratorType.ParentRepository);

            if (!ShouldAttachParentRepositoryDecorator())
                return;

            var target = commits.Find(x => x.IsCurrentHead);
            if (target == null)
                return;

            target.Decorators.Add(new Models.Decorator()
            {
                Type = Models.DecoratorType.ParentRepository,
                Name = "PARENT",
            });

            Models.Commit.SortDecorators(target.Decorators);
        }

        private void ApplyHistoryFilterColorsToDecorators(List<Models.Commit> commits)
        {
            if (commits == null || commits.Count == 0)
                return;

            var branchColors = new Dictionary<string, uint>(StringComparer.Ordinal);
            var includedBranches = new HashSet<string>(StringComparer.Ordinal);
            var branchesByFullName = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);
            var localBranchesByUpstream = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);
            foreach (var branch in _branches)
            {
                if (!string.IsNullOrWhiteSpace(branch.FullName))
                    branchesByFullName[branch.FullName] = branch;

                if (branch.IsLocal &&
                    !string.IsNullOrWhiteSpace(branch.Upstream) &&
                    !localBranchesByUpstream.ContainsKey(branch.Upstream))
                {
                    localBranchesByUpstream[branch.Upstream] = branch;
                }
            }
            if (_settings != null)
            {
                var configured = _settings.GetPresetBranchConfiguredColorMap();
                if (configured.Count > 0)
                {
                    foreach (var branch in _branches)
                    {
                        if (configured.TryGetValue(branch.Name, out var color) && color != 0)
                            branchColors[branch.FullName] = color;
                    }
                }
            }

            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Mode != Models.FilterMode.Included)
                {
                    continue;
                }

                if (filter.Type is Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch)
                {
                    includedBranches.Add(filter.Pattern);

                    if (filter.Color != 0)
                        branchColors[filter.Pattern] = filter.Color;
                }
            }

            var hasIncludedBranches = includedBranches.Count > 0;
            const uint incidentalBranchColor = 0x18808080;

            foreach (var commit in commits)
            {
                foreach (var decorator in commit.Decorators)
                {
                    decorator.Color = 0;
                    switch (decorator.Type)
                    {
                        case Models.DecoratorType.CurrentBranchHead:
                        case Models.DecoratorType.LocalBranchHead:
                            var localRefName = $"refs/heads/{decorator.Name}";
                            if (TryResolveBranchDisplayColor(localRefName, true, branchColors, branchesByFullName, localBranchesByUpstream, out var localColor))
                                decorator.Color = localColor;
                            else if (hasIncludedBranches && !ShouldKeepBranchVisibleColor(localRefName, true, includedBranches, branchesByFullName, localBranchesByUpstream))
                                decorator.Color = incidentalBranchColor;
                            break;
                        case Models.DecoratorType.RemoteBranchHead:
                            var remoteRefName = $"refs/remotes/{decorator.Name}";
                            if (TryResolveBranchDisplayColor(remoteRefName, false, branchColors, branchesByFullName, localBranchesByUpstream, out var remoteColor))
                                decorator.Color = remoteColor;
                            else if (hasIncludedBranches && !ShouldKeepBranchVisibleColor(remoteRefName, false, includedBranches, branchesByFullName, localBranchesByUpstream))
                                decorator.Color = incidentalBranchColor;
                            break;
                    }
                }
            }
        }

        private static bool TryResolveBranchDisplayColor(
            string fullRefName,
            bool isLocal,
            Dictionary<string, uint> branchColors,
            Dictionary<string, Models.Branch> branchesByFullName,
            Dictionary<string, Models.Branch> localBranchesByUpstream,
            out uint color)
        {
            color = 0;
            if (string.IsNullOrWhiteSpace(fullRefName))
                return false;

            if (branchColors.TryGetValue(fullRefName, out color))
                return true;

            if (isLocal)
            {
                if (branchesByFullName.TryGetValue(fullRefName, out var localBranch) &&
                    !string.IsNullOrWhiteSpace(localBranch.Upstream) &&
                    branchColors.TryGetValue(localBranch.Upstream, out color))
                {
                    return true;
                }
            }
            else
            {
                if (localBranchesByUpstream.TryGetValue(fullRefName, out var trackingLocal) &&
                    branchColors.TryGetValue(trackingLocal.FullName, out color))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureIncludedBranchFiltersHaveColors()
        {
            if (_uiStates == null || _uiStates.HistoryFilters.Count == 0)
                return;

            var branchesByFullName = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);
            var localBranchesByUpstream = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);
            foreach (var branch in _branches)
            {
                if (!string.IsNullOrWhiteSpace(branch.FullName))
                    branchesByFullName[branch.FullName] = branch;

                if (branch.IsLocal &&
                    !string.IsNullOrWhiteSpace(branch.Upstream) &&
                    !localBranchesByUpstream.ContainsKey(branch.Upstream))
                {
                    localBranchesByUpstream[branch.Upstream] = branch;
                }
            }

            var assignedByLogicalBranch = new Dictionary<string, uint>(StringComparer.Ordinal);
            var nextAutoColorIndex = 0;
            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Mode != Models.FilterMode.Included ||
                    filter.Type is not (Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch))
                {
                    continue;
                }

                var logicalBranchKey = ResolveBranchColorGroupKey(filter.Pattern, branchesByFullName, localBranchesByUpstream);
                if (string.IsNullOrWhiteSpace(logicalBranchKey))
                    logicalBranchKey = filter.Pattern;

                if (filter.Color != 0)
                {
                    assignedByLogicalBranch[logicalBranchKey] = filter.Color;
                    continue;
                }

                if (!assignedByLogicalBranch.TryGetValue(logicalBranchKey, out var color))
                {
                    color = s_autoHistoryFilterBranchColors[nextAutoColorIndex % s_autoHistoryFilterBranchColors.Length];
                    assignedByLogicalBranch[logicalBranchKey] = color;
                    nextAutoColorIndex++;
                }

                filter.Color = color;
            }
        }

        private static string ResolveBranchColorGroupKey(
            string fullRefName,
            Dictionary<string, Models.Branch> branchesByFullName,
            Dictionary<string, Models.Branch> localBranchesByUpstream)
        {
            if (string.IsNullOrWhiteSpace(fullRefName))
                return string.Empty;

            if (branchesByFullName.TryGetValue(fullRefName, out var branch))
            {
                if (branch.IsLocal)
                    return branch.FullName;

                if (localBranchesByUpstream.TryGetValue(fullRefName, out var trackingLocal))
                    return trackingLocal.FullName;

                return branch.FullName;
            }

            if (localBranchesByUpstream.TryGetValue(fullRefName, out var local))
                return local.FullName;

            return fullRefName;
        }

        private static bool ShouldKeepBranchVisibleColor(
            string fullRefName,
            bool isLocal,
            HashSet<string> includedBranches,
            Dictionary<string, Models.Branch> branchesByFullName,
            Dictionary<string, Models.Branch> localBranchesByUpstream)
        {
            if (string.IsNullOrWhiteSpace(fullRefName))
                return false;

            if (includedBranches.Contains(fullRefName))
                return true;

            if (isLocal)
            {
                if (branchesByFullName.TryGetValue(fullRefName, out var localBranch) &&
                    !string.IsNullOrWhiteSpace(localBranch.Upstream) &&
                    includedBranches.Contains(localBranch.Upstream))
                {
                    return true;
                }
            }
            else
            {
                if (localBranchesByUpstream.TryGetValue(fullRefName, out var trackingLocal) &&
                    includedBranches.Contains(trackingLocal.FullName))
                {
                    return true;
                }
            }

            return false;
        }

        private HashSet<string> BuildFoldableBranchFullNameSet(List<Models.Commit> commits)
        {
            var res = new HashSet<string>(StringComparer.Ordinal);
            if (commits == null || commits.Count == 0 || _branches.Count == 0)
                return res;

            var indexBySHA = new Dictionary<string, int>(commits.Count, StringComparer.Ordinal);
            var childCounts = new Dictionary<string, int>(commits.Count, StringComparer.Ordinal);
            for (var i = 0; i < commits.Count; i++)
            {
                var c = commits[i];
                indexBySHA[c.SHA] = i;
                childCounts.TryAdd(c.SHA, 0);
            }

            foreach (var commit in commits)
            {
                foreach (var parent in commit.Parents)
                {
                    if (childCounts.TryGetValue(parent, out var children))
                        childCounts[parent] = children + 1;
                }
            }

            foreach (var branch in _branches)
            {
                if (string.IsNullOrWhiteSpace(branch.FullName) || string.IsNullOrWhiteSpace(branch.Head))
                    continue;

                if (!indexBySHA.TryGetValue(branch.Head, out var headIndex))
                    continue;

                var cur = commits[headIndex];
                while (cur.Parents.Count > 0)
                {
                    var firstParent = cur.Parents[0];
                    if (!indexBySHA.TryGetValue(firstParent, out var parentIndex))
                        break;

                    var parentCommit = commits[parentIndex];
                    if (!CanFoldAwayCommit(parentCommit, childCounts))
                        break;

                    res.Add(branch.FullName);
                    break;
                }
            }

            return res;
        }

        private void ApplyFoldStateToDecorators(List<Models.Commit> commits, HashSet<string> foldableBranchFullNames)
        {
            if (commits == null || commits.Count == 0)
                return;

            foreach (var commit in commits)
            {
                foreach (var decorator in commit.Decorators)
                {
                    decorator.IsBranchFoldable = false;
                    decorator.IsBranchFolded = false;

                    if (!TryGetDecoratorBranchFullName(decorator, out var fullName))
                        continue;

                    decorator.IsBranchFoldable = foldableBranchFullNames.Contains(fullName);
                    decorator.IsBranchFolded = decorator.IsBranchFoldable && _foldedBranchFullNames.Contains(fullName);
                }
            }
        }

        private void ApplyFoldedBranchRuns(List<Models.Commit> commits, HashSet<string> foldableBranchFullNames)
        {
            if (commits == null || commits.Count == 0 || _foldedBranchFullNames.Count == 0)
                return;

            foreach (var commit in commits)
                commit.FoldedCommitsBelow = 0;

            var indexBySHA = new Dictionary<string, int>(commits.Count, StringComparer.Ordinal);
            var childCounts = new Dictionary<string, int>(commits.Count, StringComparer.Ordinal);
            for (var i = 0; i < commits.Count; i++)
            {
                var c = commits[i];
                indexBySHA[c.SHA] = i;
                childCounts.TryAdd(c.SHA, 0);
            }

            foreach (var commit in commits)
            {
                foreach (var parent in commit.Parents)
                {
                    if (childCounts.TryGetValue(parent, out var children))
                        childCounts[parent] = children + 1;
                }
            }

            var hiddenSHAs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fullName in _foldedBranchFullNames)
            {
                if (!foldableBranchFullNames.Contains(fullName))
                    continue;

                var branch = _branches.Find(x => x.FullName.Equals(fullName, StringComparison.Ordinal));
                if (branch == null || string.IsNullOrWhiteSpace(branch.Head))
                    continue;

                if (!indexBySHA.TryGetValue(branch.Head, out var headIndex))
                    continue;

                var cur = commits[headIndex];
                var collapsedCount = 0;
                while (cur.Parents.Count > 0)
                {
                    var firstParent = cur.Parents[0];
                    if (!indexBySHA.TryGetValue(firstParent, out var parentIndex))
                        break;

                    var parentCommit = commits[parentIndex];
                    if (!CanFoldAwayCommit(parentCommit, childCounts))
                        break;

                    hiddenSHAs.Add(parentCommit.SHA);
                    collapsedCount++;
                    cur = parentCommit;
                }

                if (collapsedCount > 0 && headIndex >= 0 && headIndex < commits.Count)
                    commits[headIndex].FoldedCommitsBelow = Math.Max(commits[headIndex].FoldedCommitsBelow, collapsedCount);
            }

            if (hiddenSHAs.Count > 0)
                commits.RemoveAll(x => hiddenSHAs.Contains(x.SHA));
        }

        private static bool TryGetDecoratorBranchFullName(Models.Decorator decorator, out string fullName)
        {
            fullName = string.Empty;
            if (decorator == null)
                return false;

            switch (decorator.Type)
            {
                case Models.DecoratorType.CurrentBranchHead:
                case Models.DecoratorType.LocalBranchHead:
                    fullName = $"refs/heads/{decorator.Name}";
                    return true;
                case Models.DecoratorType.RemoteBranchHead:
                    fullName = $"refs/remotes/{decorator.Name}";
                    return true;
                default:
                    return false;
            }
        }

        private static bool CanFoldAwayCommit(Models.Commit commit, Dictionary<string, int> childCounts)
        {
            if (commit == null)
                return false;

            if (commit.Decorators.Count > 0)
                return false;

            if (commit.Parents.Count != 1)
                return false;

            return !childCounts.TryGetValue(commit.SHA, out var children) || children <= 1;
        }

        private void MigrateLegacyPresetBranchFiltersIfNeeded()
        {
            if (_settings == null)
                return;

            if (!string.IsNullOrWhiteSpace(_settings.PresetBranchExactNames) ||
                !string.IsNullOrWhiteSpace(_settings.PresetBranchContainsPatterns) ||
                !string.IsNullOrWhiteSpace(_settings.PresetBranchExcludeNames) ||
                !string.IsNullOrWhiteSpace(_settings.PresetBranchExactNameColors))
                return;

            var prefs = Preferences.Instance;
            if (string.IsNullOrWhiteSpace(prefs.PresetBranchExactNames) &&
                string.IsNullOrWhiteSpace(prefs.PresetBranchContainsPatterns) &&
                string.IsNullOrWhiteSpace(prefs.PresetBranchExactNameColors))
                return;

            _settings.PresetBranchExactNames = prefs.PresetBranchExactNames ?? string.Empty;
            _settings.PresetBranchContainsPatterns = prefs.PresetBranchContainsPatterns ?? string.Empty;
            _settings.PresetBranchExactNameColors = prefs.PresetBranchExactNameColors ?? string.Empty;
            _ = _settings.SaveAsync();
        }

        private void ApplyPresetBranchFilterIfNeededOnInitialLoad()
        {
            if (!_shouldApplyPresetBranchFilterOnInitialBranchLoad || _uiStates == null)
                return;

            _shouldApplyPresetBranchFilterOnInitialBranchLoad = false;

            if (_uiStates.HistoryFilters.Count > 0)
                return;

            var exactNames = _settings?.GetPresetBranchExactNameSet() ?? [];
            var containsPatterns = _settings?.GetPresetBranchContainsRuleList() ?? [];
            var excludeNames = _settings?.GetPresetBranchExcludeNameSet() ?? [];
            if (exactNames.Count == 0 && containsPatterns.Count == 0 && excludeNames.Count == 0)
                return;

            ApplyPresetBranchFilter();
        }

        private void SavePresetBranchFilterSettingsAsync()
        {
            if (_settings != null)
                _ = _settings.SaveAsync();
        }

        private void UpdateShouldShowBranchPresetEmptyState()
        {
            ShouldShowBranchPresetEmptyState = !IsShowingAllBranches &&
                !IsPresetBranchFilterEditorExpanded &&
                string.IsNullOrEmpty(_filter) &&
                _lastVisibleBranchesCount == 0;
        }

        private void AutoFetchByTimer(object sender)
        {
            try
            {
                Dispatcher.UIThread.Invoke(AutoFetchOnUIThread);
            }
            catch
            {
                // Ignore exception.
            }
        }

        private async Task AutoFetchOnUIThread()
        {
            if (_uiStates == null)
                return;

            CommandLog log = null;

            try
            {
                if (_settings is not { } || (!_settings.EnableAutoFetch && !_settings.EnableAutoSyncAll) || !CanCreatePopup())
                {
                    _lastFetchTime = DateTime.Now;
                    return;
                }

                var lockFile = Path.Combine(GitDir, "index.lock");
                if (File.Exists(lockFile))
                    return;

                var now = DateTime.Now;
                var desire = _lastFetchTime.AddMinutes(_settings.AutoFetchInterval);
                if (desire > now)
                    return;

                var remotes = new List<string>();
                foreach (var r in _remotes)
                    remotes.Add(r.Name);

                if (remotes.Count == 0)
                    return;

                IsAutoFetching = true;
                var selectedTargets = _settings.HasConfiguredRecursiveSubmoduleUpdateTargets
                    ? _settings.GetRecursiveSubmoduleUpdateTargets()
                    : null;
                var isAutoSyncAll = _settings.EnableAutoSyncAll;
                AutoBackgroundOperationText = isAutoSyncAll ? "Auto Sync All" : App.Text("Repository.AutoFetching");
                log = CreateLog(isAutoSyncAll ? "Auto Sync All" : "Auto-Fetch");

                if (isAutoSyncAll)
                {
                    if (!CanRunAutoSyncAll(out var autoSyncSkipReason))
                    {
                        log?.AppendLine($"[skipped] {autoSyncSkipReason}");
                        _lastFetchTime = DateTime.Now;
                        return;
                    }

                    var succ = await RunPullAndUpdateSubmodulesRecursivelyAsync(log, null, selectedTargets).ConfigureAwait(false);
                    if (succ)
                    {
                        _lastFetchTime = DateTime.Now;
                        RefreshBranches();
                        RefreshCommits(true);
                        RefreshSubmodules();
                        RefreshWorkingCopyChanges();
                    }
                }
                else if (_uiStates.FetchAllRemotes)
                {
                    foreach (var remote in remotes)
                        await new Commands.Fetch(FullPath, remote, false, _settings.AutoFetchPrune).Use(log).RunAsync();
                    MarkFetched();
                }
                else
                {
                    var remote = string.IsNullOrEmpty(_settings.DefaultRemote) ?
                        remotes.Find(x => x.Equals(_settings.DefaultRemote, StringComparison.Ordinal)) :
                        remotes[0];

                    await new Commands.Fetch(FullPath, remote, false, _settings.AutoFetchPrune).Use(log).RunAsync();
                    MarkFetched();
                }
            }
            catch
            {
                // Ignore all exceptions.
            }
            finally
            {
                IsAutoFetching = false;
            }

            log?.Complete();
        }

        public void EnsureAutoFetchTimerState()
        {
            if (_settings is not { } || (!_settings.EnableAutoFetch && !_settings.EnableAutoSyncAll))
            {
                _autoFetchTimer?.Dispose();
                _autoFetchTimer = null;
                return;
            }

            if (_autoFetchTimer == null)
            {
                _lastFetchTime = DateTime.Now;
                _autoFetchTimer = new Timer(AutoFetchByTimer, null, 5000, 5000);
            }
        }

        private void QueueHistoryQuickFindApply()
        {
            if (_historyQuickFindDebounce != null)
            {
                _historyQuickFindDebounce.Cancel();
                _historyQuickFindDebounce.Dispose();
                _historyQuickFindDebounce = null;
            }

            if (string.IsNullOrEmpty(_historyQuickFindText))
            {
                ApplyHistoryQuickFind(string.Empty);
                return;
            }

            var cts = new CancellationTokenSource();
            _historyQuickFindDebounce = cts;
            _ = DebouncedApplyHistoryQuickFindAsync(cts);
        }

        private async Task DebouncedApplyHistoryQuickFindAsync(CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                await Task.Delay(200, token);
                if (token.IsCancellationRequested)
                    return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        ApplyHistoryQuickFind(_historyQuickFindText);
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                // Expected while the user is still typing.
            }
            finally
            {
                if (ReferenceEquals(_historyQuickFindDebounce, cts))
                    _historyQuickFindDebounce = null;

                cts.Dispose();
            }
        }

        private void ApplyHistoryQuickFind(string query)
        {
            query ??= string.Empty;
            if (string.Equals(_historyQuickFindAppliedText, query, StringComparison.Ordinal))
                return;

            HistoryQuickFindAppliedText = query;
            _histories?.ApplyQuickFind(query);
        }

        private bool CanRunAutoSyncAll(out string reason)
        {
            if (IsBare)
            {
                reason = "Auto Sync All skipped because pull is not available in a bare repository.";
                return false;
            }

            if (_currentBranch == null)
            {
                reason = "Auto Sync All skipped because no current branch is available for pull.";
                return false;
            }

            var pull = new Pull(this, null, false);
            if (pull.SelectedRemote == null || pull.SelectedBranch == null)
            {
                reason = "Auto Sync All skipped because no default remote branch is configured for pull.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private readonly string _gitCommonDir = null;
        private Models.RepositorySettings _settings = null;
        private Models.CommitHistoryMetadataCache _commitHistoryMetadataCache = null;
        private PresetBranchFilterMatchCache _presetBranchFilterMatchCache = null;
        private long _presetBranchFilterMatchCacheVersion = 0;
        private Models.RepositoryUIStates _uiStates = null;
        private Models.FilterMode _historyFilterMode = Models.FilterMode.None;
        private bool _hasAllowedSignersFile = false;
        private ulong _queryLocalChangesTimes = 0;

        private Models.Watcher _watcher = null;
        private Histories _histories = null;
        private WorkingCopy _workingCopy = null;
        private StashesPage _stashesPage = null;
        private int _selectedViewIndex = 0;
        private object _selectedView = null;

        private int _localBranchesCount = 0;
        private int _localChangesCount = 0;
        private int _stashesCount = 0;
        private int _lastVisibleBranchesCount = 0;
        private bool _isShowingAllBranches = false;
        private bool _shouldShowBranchPresetEmptyState = false;
        private bool _isPresetBranchFilterEditorExpanded = false;
        private string _autoBackgroundOperationText = "Auto-Fetch";
        private bool _isQuickFetching = false;
        private bool _isQuickPulling = false;
        private bool _isFetchDurationToastVisible = false;
        private double _fetchDurationToastOpacity = 1.0;
        private string _fetchDurationToastText = string.Empty;
        private CancellationTokenSource _fetchDurationToastCancellation = null;

        private bool _isSearchingCommits = false;
        private SearchCommitContext _searchCommitContext = null;
        private string _historyQuickFindText = string.Empty;
        private string _historyQuickFindAppliedText = string.Empty;
        private long _historyQuickFindFocusRequestId = 0;
        private CancellationTokenSource _historyQuickFindDebounce = null;
        private AvaloniaList<PresetBranchExactColorItem> _presetBranchExactColorItems = [];

        private string _filter = string.Empty;
        private List<Models.Remote> _remotes = [];
        private List<Models.Branch> _branches = [];
        private Models.Branch _currentBranch = null;
        private List<BranchTreeNode> _localBranchTrees = [];
        private List<BranchTreeNode> _remoteBranchTrees = [];
        private List<Models.Worktree> _worktrees = [];
        private List<Models.Tag> _tags = [];
        private object _visibleTags = null;
        private List<Models.Submodule> _submodules = [];
        private object _visibleSubmodules = null;
        private string _navigateToCommitDelayed = string.Empty;
        private string _superProjectSubmoduleSHA = string.Empty;

        private bool _isAutoFetching = false;
        private Timer _autoFetchTimer = null;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private bool _shouldApplyPresetBranchFilterOnInitialBranchLoad = false;
        private readonly HashSet<string> _foldedBranchFullNames = new(StringComparer.Ordinal);
        private int _visibleFoldableBranchesCount = 0;
        private int _visibleFoldedBranchesCount = 0;

        private Models.BisectState _bisectState = Models.BisectState.None;
        private bool _isBisectCommandRunning = false;

        private CancellationTokenSource _cancellationRefreshBranches = null;
        private CancellationTokenSource _cancellationRefreshTags = null;
        private CancellationTokenSource _cancellationRefreshWorkingCopyChanges = null;
        private CancellationTokenSource _cancellationRefreshCommits = null;
        private CancellationTokenSource _cancellationRefreshStashes = null;

        private sealed class PresetBranchFilterMatchCache
        {
            public long Version { get; set; }

            public string ExactRaw { get; set; } = string.Empty;
            public string ContainsRaw { get; set; } = string.Empty;
            public string ExcludeRaw { get; set; } = string.Empty;

            public HashSet<string> ExactNames { get; set; } = new(StringComparer.Ordinal);
            public List<string> ContainsPatterns { get; set; } = [];
            public HashSet<string> ExcludeNames { get; set; } = new(StringComparer.Ordinal);
            public HashSet<string> VisibleBranchNames { get; } = new(StringComparer.Ordinal);

            public bool ShouldShow(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return false;

                return VisibleBranchNames.Contains(name);
            }
        }

        private static readonly List<PresetBranchColorOption> PRESET_BRANCH_COLOR_OPTIONS =
        [
            new PresetBranchColorOption("Green", 0xFF10893E),
            new PresetBranchColorOption("Blue", 0xFF0078D7),
            new PresetBranchColorOption("Cyan", 0xFF0099BC),
            new PresetBranchColorOption("Purple", 0xFF744DA9),
            new PresetBranchColorOption("Magenta", 0xFFC239B3),
            new PresetBranchColorOption("Orange", 0xFFF7630C),
            new PresetBranchColorOption("Gold", 0xFFFFB900),
            new PresetBranchColorOption("Red", 0xFFD13438),
            new PresetBranchColorOption("Gray", 0xFF5D5A58),
            new PresetBranchColorOption("Teal", 0xFF008272),
            new PresetBranchColorOption("Indigo", 0xFF4F6BED),
            new PresetBranchColorOption("Pink", 0xFFE3008C),
            new PresetBranchColorOption("Crimson", 0xFFA80000),
            new PresetBranchColorOption("Coral", 0xFFFF6F61),
            new PresetBranchColorOption("Amber", 0xFFFF8C00),
            new PresetBranchColorOption("Lime", 0xFF7FBA00),
            new PresetBranchColorOption("Olive", 0xFF6B8E23),
            new PresetBranchColorOption("Mint", 0xFF00B294),
            new PresetBranchColorOption("Sky", 0xFF00B7C3),
            new PresetBranchColorOption("Navy", 0xFF003B6F),
            new PresetBranchColorOption("Violet", 0xFF8764B8),
            new PresetBranchColorOption("Brown", 0xFF8E562E),
            new PresetBranchColorOption("Slate", 0xFF607D8B),
            new PresetBranchColorOption("Charcoal", 0xFF2D2D30),
        ];

        private static readonly TimeSpan SPLIT_FETCH_TIMEOUT = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SPLIT_SUBMODULE_UPDATE_TIMEOUT = TimeSpan.FromMinutes(5);
        private static readonly uint[] s_autoHistoryFilterBranchColors =
        [
            0xFF10893E, // green
            0xFF0078D7, // blue
            0xFF744DA9, // purple
            0xFFF7630C, // orange
            0xFFC239B3, // magenta
            0xFF0099BC, // cyan
            0xFFD13438, // red
            0xFF00B294, // mint
            0xFF4F6BED, // indigo
            0xFFFFB900, // gold
            0xFF7FBA00, // lime
            0xFF8E562E, // brown
            0xFF00B7C3, // sky
            0xFF8764B8, // violet
            0xFFFF6F61, // coral
            0xFF008272, // teal
        ];
    }
}
