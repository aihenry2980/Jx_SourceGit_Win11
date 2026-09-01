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
        private const int AUTO_COLLAPSE_HISTORY_FILTER_COUNT = 6;

        private class CommitHistorySnapshot
        {
            public List<Models.Commit> Commits { get; set; } = [];
            public Models.CommitGraph Graph { get; set; } = null;
            public bool ShouldNotifyFoldControlChange { get; set; } = false;
            public long QueryCommitsMilliseconds { get; set; } = 0;
            public long MetadataMilliseconds { get; set; } = 0;
            public long PrepareMilliseconds { get; set; } = 0;
            public long GraphMilliseconds { get; set; } = 0;
            public long TotalMilliseconds { get; set; } = 0;
            public int MetadataCacheHits { get; set; } = 0;
            public int QueriedCommitCount { get; set; } = 0;
        }

        private class PrunedRemoteBranch(string scope, string remoteRef)
        {
            public string Scope { get; } = scope;
            public string RemoteRef { get; } = remoteRef;
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

        public Models.Branch GetRebaseBaseBranch()
        {
            var configured = _settings?.RebaseBaseBranch?.Trim();
            if (string.IsNullOrEmpty(configured))
                return null;

            var branch = _branches.Find(x => x.FullName.Equals(configured, StringComparison.Ordinal));
            branch ??= _branches.Find(x => x.IsLocal && x.Name.Equals(configured, StringComparison.Ordinal));
            branch ??= _branches.Find(x => x.FriendlyName.Equals(configured, StringComparison.Ordinal));
            branch ??= _branches.Find(x =>
                !x.IsLocal &&
                x.Name.Equals(configured, StringComparison.Ordinal) &&
                string.Equals(x.Remote, _settings.DefaultRemote, StringComparison.Ordinal));
            branch ??= _branches.Find(x =>
                !x.IsLocal &&
                x.Name.Equals(configured, StringComparison.Ordinal) &&
                string.Equals(x.Remote, "origin", StringComparison.Ordinal));
            branch ??= _branches.Find(x => !x.IsLocal && x.Name.Equals(configured, StringComparison.Ordinal));
            return branch;
        }

        public bool IsRebaseBaseBranch(Models.Branch branch)
        {
            var configured = GetRebaseBaseBranch();
            return branch != null && configured != null &&
                branch.FullName.Equals(configured.FullName, StringComparison.Ordinal);
        }

        public string RebaseBaseBranchDisplayName
        {
            get
            {
                var resolved = GetRebaseBaseBranch();
                if (resolved != null)
                    return resolved.FriendlyName;

                return _settings?.RebaseBaseBranch?.Trim() ?? string.Empty;
            }
        }

        public bool IsRebaseBaseBranchMissing
        {
            get
            {
                var configured = _settings?.RebaseBaseBranch?.Trim();
                return !string.IsNullOrEmpty(configured) && GetRebaseBaseBranch() == null;
            }
        }

        public void SetRebaseBaseBranch(Models.Branch branch)
        {
            if (_settings == null || branch == null || IsRebaseBaseBranch(branch))
                return;

            _settings.RebaseBaseBranch = branch.FriendlyName;
            _ = _settings.SaveAsync();
            RefreshBranchSidebarByCurrentFilters();
            RefreshCommits();
            SendNotification($"`{branch.FriendlyName}` is now the rebase base branch.");
        }

        public void ClearRebaseBaseBranch()
        {
            if (_settings == null || string.IsNullOrWhiteSpace(_settings.RebaseBaseBranch))
                return;

            _settings.RebaseBaseBranch = string.Empty;
            _ = _settings.SaveAsync();
            RefreshBranchSidebarByCurrentFilters();
            RefreshCommits();
            SendNotification("Rebase base branch disabled.");
        }

        public void NavigateToRebaseBaseBranchCommit()
        {
            var configured = _settings?.RebaseBaseBranch?.Trim();
            if (string.IsNullOrEmpty(configured))
                return;

            var branch = GetRebaseBaseBranch();
            if (branch == null || string.IsNullOrEmpty(branch.Head))
            {
                SendNotification($"Rebase base branch '{configured}' doesn't exist.", true);
                OpenRebaseBaseBranchPicker();
                return;
            }

            NavigateToCommit(branch.Head);
        }

        public void OpenRebaseBaseBranchPicker()
        {
            if (!CanCreatePopup())
                return;

            var configured = _settings?.RebaseBaseBranch?.Trim() ?? string.Empty;
            ShowPopup(new SelectRebaseBaseBranch(this, configured));
        }

        public Models.GitFlow GitFlow
        {
            get;
            set;
        } = new();

        public string MachineName { get; } = Environment.MachineName;

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

        public int HistoryPathFilterCount
        {
            get
            {
                if (_uiStates == null)
                    return 0;

                var count = 0;
                foreach (var filter in _uiStates.HistoryFilters)
                {
                    if (filter.Type == Models.FilterType.Path && filter.Mode == Models.FilterMode.Included)
                        count++;
                }

                return count;
            }
        }

        public bool CanFoldVisibleBranchesInGraph => _visibleFoldableBranchesCount > _visibleFoldedBranchesCount;

        public bool CanUnfoldBranchesInGraph => _foldedBranchFullNames.Count > 0;

        public bool IsHistoryFiltersCollapsed
        {
            get => _uiStates.IsHistoryFiltersCollapsed;
            set
            {
                if (value != _uiStates.IsHistoryFiltersCollapsed)
                {
                    _uiStates.IsHistoryFiltersCollapsed = value;
                    OnPropertyChanged();
                }
            }
        }

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
                    OnPropertyChanged(nameof(IsHistoriesVisible));
                    OnPropertyChanged(nameof(IsWorkingCopyVisible));
                    OnPropertyChanged(nameof(IsStashesVisible));
                    OnPropertyChanged(nameof(IsSubmoduleCommitFlowVisible));

                    if (value == 3)
                        _submoduleCommitFlow?.Activate();
                }
            }
        }

        public Histories Histories
        {
            get => _histories;
        }

        public WorkingCopy WorkingCopy
        {
            get => _workingCopy;
        }

        public StashesPage StashesPage
        {
            get => _stashesPage;
        }

        public SubmoduleCommitFlow SubmoduleCommitFlow
        {
            get => _submoduleCommitFlow;
        }

        public bool IsHistoriesVisible
        {
            get => SelectedViewIndex == 0;
        }

        public bool IsWorkingCopyVisible
        {
            get => SelectedViewIndex == 1;
        }

        public bool IsStashesVisible
        {
            get => SelectedViewIndex == 2;
        }

        public bool IsSubmoduleCommitFlowVisible
        {
            get => SelectedViewIndex == 3;
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

        public bool HighlightCurrentBranchOnlyInHistory
        {
            get => _uiStates.GraphHighlighting == Models.CommitGraphHighlighting.CurrentBranchOnly;
            set
            {
                var mode = value ? Models.CommitGraphHighlighting.CurrentBranchOnly : Models.CommitGraphHighlighting.All;
                if (_uiStates.GraphHighlighting != mode)
                {
                    _uiStates.GraphHighlighting = mode;
                    OnPropertyChanged();
                    RefreshCommits();
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
            private set
            {
                if (SetProperty(ref _remotes, value))
                {
                    if (_histories != null)
                        _histories.HasSingleRemote = value != null && value.Count == 1;
                }
            }
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
                    _histories?.NotifyCurrentBranchChanged();
                    if (value != null && !string.Equals(value.Head, oldHead, StringComparison.Ordinal) && _workingCopy is { UseAmend: true })
                        _workingCopy.UseAmend = false;

                    NotifyCurrentBranchVisualChanged();
                }
            }
        }

        public string CurrentBranchDisplayName => CurrentBranch?.FriendlyName ?? "--";

        public string CurrentBranchDisplayLabel => FormatCurrentBranchDisplayLabel(CurrentBranchDisplayName);

        public bool HasSuperProjectPointer => !string.IsNullOrEmpty(_superProjectSubmoduleSHA);

        public bool IsParentRepository => string.IsNullOrEmpty(_superProjectSubmoduleSHA) && _submodules.Count > 0;

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
                if (CurrentBranch is not { IsDetachedHead: false })
                    return Brushes.Black;

                var raw = Color.FromUInt32(ResolveCurrentBranchDisplayColor());
                var alpha = CurrentBranch.IsLocal ? (byte)0xA0 : (byte)0x32;
                return new SolidColorBrush(Color.FromArgb(alpha, raw.R, raw.G, raw.B));
            }
        }

        public IBrush CurrentBranchDisplayForeground
        {
            get
            {
                if (CurrentBranch is not { IsDetachedHead: false })
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66));

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

        public List<Worktree> Worktrees
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
            private set
            {
                if (SetProperty(ref _submodules, value))
                {
                    var paths = value.ConvertAll(x => x.Path);
                    var colors = BuildSubmoduleUpdateBadgeColorMap(paths);
                    var colorsChanged = !AreSubmoduleColorMapsEqual(_submoduleUpdateBadgeColors, colors);
                    _submoduleUpdateBadgeColors = colors;
                    OnPropertyChanged(nameof(SubmodulesHeaderCountText));
                    OnPropertyChanged(nameof(IsParentRepository));

                    if (colorsChanged && _histories?.Commits.Count > 0)
                        RefreshCommits();
                }
            }
        }

        public uint ResolveSubmoduleUpdateBadgeColor(string path)
        {
            return Models.SubmoduleUpdateBadge.ResolveAccentColor(path, _submoduleUpdateBadgeColors);
        }

        public uint? GetConfiguredSubmoduleUpdateBadgeColor(string path)
        {
            var normalized = NormalizeSubmodulePath(path);
            var configured = _settings?.GetSubmoduleUpdateBadgeColorMap();
            return configured != null && configured.TryGetValue(normalized, out var color) ? color : null;
        }

        public void SetSubmoduleUpdateBadgeColor(string path, uint? color)
        {
            if (_settings == null || !_settings.SetSubmoduleUpdateBadgeColor(path, color))
                return;

            var paths = _submodules.ConvertAll(x => x.Path);
            var colors = new Dictionary<string, uint>(BuildSubmoduleUpdateBadgeColorMap(paths), StringComparer.Ordinal);
            var normalized = NormalizeSubmodulePath(path);
            if (color.HasValue && !colors.ContainsKey(normalized))
                colors[normalized] = color.Value;

            _submoduleUpdateBadgeColors = colors;
            if (_histories != null)
            {
                foreach (var commit in _histories.Commits)
                {
                    if (commit.SubmoduleUpdateBadges.Count == 0)
                        continue;

                    commit.SubmoduleUpdateBadges = commit.SubmoduleUpdateBadges.ConvertAll(
                        badge => new Models.SubmoduleUpdateBadge(badge.Path, ResolveSubmoduleUpdateBadgeColor(badge.Path)));
                }
            }

            _ = _settings.SaveAsync();
        }

        public bool IsSubmodulesLoading
        {
            get => _isSubmodulesLoading;
            private set
            {
                if (SetProperty(ref _isSubmodulesLoading, value))
                    OnPropertyChanged(nameof(SubmodulesHeaderCountText));
            }
        }

        public string SubmodulesHeaderCountText
        {
            get => IsSubmodulesLoading ? "(loading...)" : $"({_submodules.Count})";
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

        public bool HasInProgressStatus => InProgressContext != null;

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

        public bool NavigateHistoryQuickFind(bool forward)
        {
            if (string.IsNullOrWhiteSpace(_historyQuickFindText) || _histories == null)
                return false;

            if (!string.Equals(_historyQuickFindAppliedText, _historyQuickFindText, StringComparison.Ordinal))
            {
                ApplyHistoryQuickFind(_historyQuickFindText);
                return true;
            }

            return _histories.NavigateQuickFind(forward);
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

        public bool IsInfrequentGroupExpanded
        {
            get => _uiStates.IsInfrequentExpandedInSideBar;
            set
            {
                if (value != _uiStates.IsInfrequentExpandedInSideBar)
                {
                    _uiStates.IsInfrequentExpandedInSideBar = value;
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
                if (Path.IsPathRooted(commonDir))
                    commonDir = new DirectoryInfo(commonDir).FullName;
                else
                    commonDir = new DirectoryInfo(Path.Combine(GitDir, commonDir)).FullName;

                _gitCommonDir = commonDir.Replace('\\', '/').TrimEnd('/');
            }
            else
            {
                _gitCommonDir = GitDir;
            }

            _settings = Models.RepositorySettings.Get(_gitCommonDir);
            _uiStates = Models.RepositoryUIStates.Load(GitDir);
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

            EnsureWatcherState();

            _historyFilterMode = _uiStates.GetHistoryFilterMode();
            _histories = new Histories(this);
            _workingCopy = new WorkingCopy(this) { CommitMessage = _uiStates.LastCommitMessage };
            _stashesPage = new StashesPage(this);
            _submoduleCommitFlow = new SubmoduleCommitFlow(this);
            _searchCommitContext = new SearchCommitContext(this);
            _selectedViewIndex = Preferences.Instance.ShowLocalChangesByDefault ? 1 : 0;
            _lastFetchTime = DateTime.Now;
            EnsureBackgroundTaskState();
            RefreshAll();
            RefreshSuperProjectSubmodulePointer();
        }

        public void Close()
        {
            _historyQuickFindDebounce?.Cancel();
            _historyQuickFindDebounce?.Dispose();
            _historyQuickFindDebounce = null;
            var commitMessage = _workingCopy.CommitMessage;
            if (!string.IsNullOrEmpty(commitMessage) && _workingCopy.InProgressContext != null)
                File.WriteAllText(Path.Combine(GitDir, "MERGE_MSG"), commitMessage);

            _uiStates.LastCommitMessage = commitMessage;
            _uiStates.Save();

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

            _watcher = null;
            _histories = null;
            _workingCopy = null;
            _stashesPage = null;
            _submoduleCommitFlow = null;

            _localChangesCount = 0;
            _stashesCount = 0;

            _remotes.Clear();
            _branches.Clear();
            _localBranchTrees.Clear();
            _remoteBranchTrees.Clear();
            _tags.Clear();
            _visibleTags = null;
            _submodules.Clear();
            _submoduleUpdateBadgeColors = new Dictionary<string, uint>(StringComparer.Ordinal);
            _visibleSubmodules = null;
            _presetBranchExactColorItems.Clear();
        }

        public void SendNotification(string message, bool isError = false)
        {
            Models.Notification.Send(FullPath, message, isError);
        }

        public void OpenSubmoduleCommitFlow()
        {
            SelectedViewIndex = 3;
            _submoduleCommitFlow?.Activate();
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

        public void ClosePopup()
        {
            GetOwnerPage()?.CancelPopup();
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
                _branches.Find(x => x.IsLocal && x.Name.Equals(GitFlow.ProductionBranch, StringComparison.Ordinal)) != null &&
                _branches.Find(x => x.IsLocal && x.Name.Equals(GitFlow.DevelopmentBranch, StringComparison.Ordinal)) != null;
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
            var path = Path.Combine(GitDir, "hooks", "pre-push");
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
                SendNotification("LFS enabled successfully!");

            log.Complete();
        }

        public async Task<bool> TrackLFSFileAsync(string pattern, bool isFilenameMode)
        {
            var log = CreateLog("Track LFS");
            var succ = await new Commands.LFS(FullPath)
                .Use(log)
                .TrackAsync(pattern, isFilenameMode);

            if (succ)
                SendNotification($"Tracking successfully! Pattern: {pattern}");

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
                SendNotification($"Lock file successfully! File: {path}");

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
                SendNotification($"Unlock file successfully! File: {path}");

            log.Complete();
            return succ;
        }

        public CommandLog CreateLog(string name)
        {
            var log = new CommandLog(name) { RepositoryPath = FullPath };
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
                GitFlow.Parse(config);
            });
        }

        public async Task FetchAsync(bool autoStart)
        {
            if (!CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                SendNotification("No remotes added to this repository!!!", true);
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

            var refspecs = onlyFilteredBranches ? await BuildQuickFetchFilteredRefSpecsAsync(remote).ConfigureAwait(false) : null;
            if (onlyFilteredBranches && (refspecs == null || refspecs.Count == 0))
            {
                App.SendNotification(FullPath, $"Quick Fetch (Filtered) skipped because no included branch filters match remote '{remote}'.");
                return;
            }

            var operationName = onlyFilteredBranches ? "Quick Fetch (Filtered)" : "Quick Fetch";
            var log = CreateLog(operationName);
            var succ = false;
            using var cancellation = new CancellationTokenSource();
            _quickFetchCancellation = cancellation;
            log.SetCancelAction(cancellation.Cancel);
            AutoBackgroundOperationText = operationName;
            IsQuickFetching = true;
            var gitStopwatch = Stopwatch.StartNew();
            using var lockWatcher = LockWatcher();
            var sawRefStatus = 0;
            var refsChanged = 0;

            try
            {
                var fetch = onlyFilteredBranches
                    ? new Commands.Fetch(FullPath, remote, true, false, false, false, refspecs)
                    : new Commands.Fetch(FullPath, remote, true, false);
                fetch.OnOutputLine = line =>
                {
                    if (!IsFetchRefStatusLine(line, out var changed))
                        return;

                    Interlocked.Exchange(ref sawRefStatus, 1);
                    if (changed)
                        Interlocked.Exchange(ref refsChanged, 1);
                };
                succ = await fetch.WithCancellation(cancellation.Token).Use(log).RunAsync();
            }
            finally
            {
                gitStopwatch.Stop();
                IsQuickFetching = false;
                _quickFetchCancellation = null;
                log.Complete(succ && !cancellation.IsCancellationRequested);
            }

            if (succ)
            {
                TimeSpan refreshDuration;
                if (sawRefStatus != 0 && refsChanged == 0)
                {
                    _lastFetchTime = DateTime.Now;
                    _watcher?.MarkBranchUpdated();
                    refreshDuration = TimeSpan.Zero;
                }
                else
                {
                    refreshDuration = await MarkFetchedAndMeasureRefreshAsync();
                }

                ShowFetchDurationToast(gitStopwatch.Elapsed, refreshDuration);
            }
            else
                App.SendNotification(FullPath, $"{operationName} failed. Review the repository log for details.");
        }

        public async Task FastFetchCurrentUpstreamAsync()
        {
            const string originPrefix = "refs/remotes/origin/";
            var current = CurrentBranch;
            if (current is not { IsLocal: true } || string.IsNullOrWhiteSpace(current.Upstream))
            {
                App.SendNotification(FullPath, "Fast Fetch requires the current local branch to track origin/<branch>.");
                return;
            }

            if (!current.Upstream.StartsWith(originPrefix, StringComparison.Ordinal) || current.Upstream.Length == originPrefix.Length)
            {
                App.SendNotification(FullPath, "Fast Fetch supports branches tracking origin/<branch> only.");
                return;
            }

            var remoteBranch = current.Upstream[originPrefix.Length..];
            var log = CreateLog("Fast Fetch");
            var succeeded = false;
            using var cancellation = new CancellationTokenSource();
            _quickFetchCancellation = cancellation;
            log.SetCancelAction(cancellation.Cancel);
            var sawRefStatus = 0;
            var refsChanged = 0;
            var gitStopwatch = Stopwatch.StartNew();
            AutoBackgroundOperationText = "Fast Fetch";
            IsQuickFetching = true;
            using var lockWatcher = LockWatcher();

            try
            {
                var fetch = new Commands.Fetch(
                    FullPath,
                    "origin",
                    true,
                    false,
                    false,
                    false,
                    [$"refs/heads/{remoteBranch}:{current.Upstream}"]);
                fetch.OnOutputLine = line =>
                {
                    if (!IsFetchRefStatusLine(line, out var changed))
                        return;

                    Interlocked.Exchange(ref sawRefStatus, 1);
                    if (changed)
                        Interlocked.Exchange(ref refsChanged, 1);
                };
                succeeded = await fetch.WithCancellation(cancellation.Token).Use(log).RunAsync();
            }
            finally
            {
                gitStopwatch.Stop();
                IsQuickFetching = false;
                _quickFetchCancellation = null;
                log.Complete(succeeded && !cancellation.IsCancellationRequested);
            }

            if (!succeeded)
            {
                App.SendNotification(FullPath, "Fast Fetch failed. Review the repository log for details.");
                return;
            }

            TimeSpan refreshDuration;
            if (sawRefStatus != 0 && refsChanged == 0)
            {
                _lastFetchTime = DateTime.Now;
                _watcher?.MarkBranchUpdated();
                refreshDuration = TimeSpan.Zero;
            }
            else
            {
                refreshDuration = await MarkFetchedAndMeasureRefreshAsync();
            }

            ShowFetchDurationToast(gitStopwatch.Elapsed, refreshDuration);
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
            using var cancellation = new CancellationTokenSource();
            log.SetCancelAction(cancellation.Cancel);
            var succeeded = await RunFetchRecursivelyAsync(prune, log, cancellationToken: cancellation.Token);
            log.Complete(succeeded && !cancellation.IsCancellationRequested);
        }

        public async Task PullAsync(bool autoStart)
        {
            if (IsBare || !CanCreatePopup())
                return;

            if (_remotes.Count == 0)
            {
                SendNotification("No remotes added to this repository!!!", true);
                return;
            }

            if (_currentBranch == null)
            {
                SendNotification("Can NOT find current branch!!!", true);
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
                SendNotification("No remotes added to this repository!!!", true);
                return;
            }

            if (_currentBranch == null)
            {
                SendNotification("Can NOT find current branch!!!", true);
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
            InvalidatePresetBranchFilterMatchCache();

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
            OnPropertyChanged(nameof(RebaseBaseBranchDisplayName));
            OnPropertyChanged(nameof(IsRebaseBaseBranchMissing));
        }

        public IDisposable LockWatcher()
        {
            return _watcher?.Lock();
        }

        public void RefreshAfterCreateBranch(Models.Branch created, bool checkout)
        {
            _watcher?.MarkBranchUpdated();
            _watcher?.MarkWorkingCopyUpdated();

            _branches.RemoveAll(b => b.IsLocal && b.Name.Equals(created.Name, StringComparison.Ordinal));
            _branches.Add(created);
            InvalidatePresetBranchFilterMatchCache();

            if (checkout)
            {
                if (_currentBranch.IsDetachedHead)
                {
                    _branches.Remove(_currentBranch);
                }
                else
                {
                    _currentBranch.IsCurrent = false;
                    _currentBranch.WorktreePath = null;
                }

                created.IsCurrent = true;
                created.WorktreePath = FullPath;

                var folderEndIdx = created.FullName.LastIndexOf('/');
                if (folderEndIdx > 10)
                    _uiStates.ExpandedBranchNodesInSideBar.Add(created.FullName.Substring(0, folderEndIdx));

                CurrentBranch = created;
            }

            var locals = new List<Models.Branch>();
            var count = 0;
            foreach (var b in _branches)
            {
                if (b.IsLocal)
                {
                    locals.Add(b);
                    if (!b.IsDetachedHead)
                        count++;
                }
            }

            var builder = BuildBranchTree(locals, [], false);
            LocalBranchTrees = builder.Locals;
            LocalBranchesCount = count;

            if (_historyFilterMode == Models.FilterMode.Included)
                IncludeBranchInHistoryFilter(created, true);
            else
                RefreshCommits();

            RefreshWorkingCopyChanges();
            RefreshWorktrees();
        }

        public void RefreshAfterCheckoutBranch(Models.Branch checkouted)
        {
            _watcher?.MarkBranchUpdated();
            _watcher?.MarkWorkingCopyUpdated();

            if (_currentBranch.IsDetachedHead)
            {
                _branches.Remove(_currentBranch);
            }
            else
            {
                _currentBranch.IsCurrent = false;
                _currentBranch.WorktreePath = null;
            }

            checkouted.IsCurrent = true;
            checkouted.WorktreePath = FullPath;

            List<Models.Branch> locals = [];
            foreach (var b in _branches)
            {
                if (b.IsLocal)
                    locals.Add(b);
            }

            var builder = BuildBranchTree(locals, [], false);
            LocalBranchTrees = builder.Locals;
            CurrentBranch = checkouted;

            if (_historyFilterMode == Models.FilterMode.Included)
                IncludeBranchInHistoryFilter(checkouted, true);
            else
                RefreshCommits();

            RefreshWorkingCopyChanges();
            RefreshWorktrees();
        }

        public void RefreshAfterRenameBranch(Models.Branch b, string newName)
        {
            _watcher?.MarkBranchUpdated();

            var newFullName = $"refs/heads/{newName}";
            _uiStates.RenameBranchFilter(b.FullName, newFullName);

            var renamed = new Models.Branch
            {
                Name = newName,
                FullName = newFullName,
                CommitterDate = b.CommitterDate,
                Head = b.Head,
                IsLocal = b.IsLocal,
                IsCurrent = b.IsCurrent,
                IsDetachedHead = b.IsDetachedHead,
                Upstream = b.Upstream,
                Ahead = b.Ahead,
                Behind = b.Behind,
                Remote = b.Remote,
                IsUpstreamGone = b.IsUpstreamGone,
                WorktreePath = b.WorktreePath,
            };

            _branches.Remove(b);
            _branches.Add(renamed);

            if (b.IsCurrent)
                CurrentBranch = renamed;

            List<Models.Branch> locals = [];
            foreach (var branch in _branches)
            {
                if (branch.IsLocal)
                    locals.Add(branch);
            }

            var builder = BuildBranchTree(locals, [], false);
            LocalBranchTrees = builder.Locals;

            RefreshCommits();
            RefreshWorktrees();
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
            RefreshSubmodules(true);
        }

        public void MarkFetched(bool refsMayBeDeleted = false)
        {
            _lastFetchTime = DateTime.Now;
            _ = RefreshAfterFetchAsync(refsMayBeDeleted);
        }

        public async Task<TimeSpan> MarkFetchedAndMeasureRefreshAsync(bool refsMayBeDeleted = false)
        {
            _lastFetchTime = DateTime.Now;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await RefreshAfterFetchAsync(refsMayBeDeleted).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Another refresh request won the race; fetch still completed successfully.
            }

            stopwatch.Stop();

            return stopwatch.Elapsed;
        }

        private async Task RefreshAfterFetchAsync(bool refsMayBeDeleted)
        {
            if (refsMayBeDeleted)
            {
                // A prune can invalidate refs used by active history filters. Reconcile
                // branches before starting any command that consumes those refs.
                await RefreshBranchesAsync().ConfigureAwait(false);
                await RefreshCommitsAsync(true).ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAll(
                    RefreshBranchesAsync(),
                    RefreshCommitsAsync(true)).ConfigureAwait(false);
            }

            _watcher?.MarkBranchUpdated();
        }

        public async Task RefreshAfterPullAsync()
        {
            await Task.WhenAll(
                RefreshBranchesAsync(),
                RefreshCommitsAsync(true),
                RefreshWorkingCopyChangesAsync(false)).ConfigureAwait(false);
            _watcher?.MarkBranchUpdated();
            _watcher?.MarkWorkingCopyUpdated();
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

        public void SetHistoryPathFilter(string path, bool clearExists = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            SetHistoryPathFilters([path], clearExists);
        }

        public void SetHistoryPathFilters(IEnumerable<string> paths, bool clearExists = true)
        {
            if (clearExists)
                _uiStates.HistoryFilters.Clear();
            else
            {
                for (var i = _uiStates.HistoryFilters.Count - 1; i >= 0; i--)
                {
                    if (_uiStates.HistoryFilters[i].Type == Models.FilterType.Path)
                        _uiStates.HistoryFilters.RemoveAt(i);
                }
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && unique.Add(path))
                    _uiStates.UpdateHistoryFilters(path, Models.FilterType.Path, Models.FilterMode.Included);
            }

            HistoryFilterMode = _uiStates.GetHistoryFilterMode();
            SelectedViewIndex = 0;
            RefreshHistoryFilters(true);
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

        private bool IncludeBranchInHistoryFilter(Models.Branch branch, bool refresh)
        {
            if (_uiStates == null || branch == null)
                return false;

            var changed = false;
            if (branch.IsLocal)
            {
                changed |= _uiStates.UpdateHistoryFilters(branch.FullName, Models.FilterType.LocalBranch, Models.FilterMode.Included);
                if (!string.IsNullOrEmpty(branch.Upstream) && !branch.IsUpstreamGone)
                    changed |= _uiStates.UpdateHistoryFilters(branch.Upstream, Models.FilterType.RemoteBranch, Models.FilterMode.Included);
            }
            else
            {
                changed |= _uiStates.UpdateHistoryFilters(branch.FullName, Models.FilterType.RemoteBranch, Models.FilterMode.Included);
            }

            if (changed || refresh)
                RefreshHistoryFilters(refresh);

            return changed;
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

        public uint GetEffectiveBranchDisplayColor(Models.Branch branch)
        {
            if (branch == null)
                return Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR;

            // A color explicitly chosen from "Visibility in Graph" must win over
            // the automatic conflict-avoidance palette used for branch filters.
            if (_settings != null)
            {
                var configuredColors = _settings.GetPresetBranchConfiguredColorMap();
                if (configuredColors.TryGetValue(branch.Name, out var configuredColor) && configuredColor != 0)
                    return configuredColor;
            }

            EnsureIncludedBranchFiltersHaveColors();

            var branchColors = new Dictionary<string, uint>(StringComparer.Ordinal);
            var branchesByFullName = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);
            var localBranchesByUpstream = new Dictionary<string, Models.Branch>(StringComparer.Ordinal);

            foreach (var one in _branches)
            {
                if (!string.IsNullOrWhiteSpace(one.FullName))
                    branchesByFullName[one.FullName] = one;

                if (one.IsLocal &&
                    !string.IsNullOrWhiteSpace(one.Upstream) &&
                    !localBranchesByUpstream.ContainsKey(one.Upstream))
                {
                    localBranchesByUpstream[one.Upstream] = one;
                }
            }

            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Mode == Models.FilterMode.Included &&
                    filter.Type is Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch)
                {
                    branchColors[filter.Pattern] = filter.Color == 0
                        ? Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR
                        : filter.Color;
                }
            }

            if (TryResolveBranchDisplayColor(branch.FullName, branch.IsLocal, branchColors, branchesByFullName, localBranchesByUpstream, out var color))
                return color == 0 ? Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR : color;

            return GetBranchFilterColor(branch);
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
                SendNotification(log.Content.Substring(log.Content.IndexOf('\n')).Trim(), true);
            else if (log.Content.Contains("is the first bad commit"))
                SendNotification(log.Content.Substring(log.Content.IndexOf('\n')).Trim());

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
            _ = RefreshBranchesAsync();
        }

        private Task RefreshBranchesAsync()
        {
            if (_cancellationRefreshBranches is { IsCancellationRequested: false })
                _cancellationRefreshBranches.Cancel();

            _cancellationRefreshBranches = new CancellationTokenSource();
            var token = _cancellationRefreshBranches.Token;

            return Task.Run(async () =>
            {
                var branches = await new Commands.QueryBranches(FullPath).GetResultAsync().ConfigureAwait(false);
                var remotes = await new Commands.QueryRemotes(FullPath).GetResultAsync().ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    Remotes = remotes;
                    Branches = branches;
                    InvalidatePresetBranchFilterMatchCache();
                    CurrentBranch = branches.Find(x => x.IsCurrent);
                    RemoveInvalidBranchHistoryFilters(branches);
                    RefreshBranchSidebarByCurrentFilters();
                    ApplyPresetBranchFilterIfNeededOnInitialLoad();
                    ValidateHistoryFilters(true);

                    if (_workingCopy != null)
                        _workingCopy.HasRemotes = remotes.Count > 0;

                    var hasPendingPullOrPush = CurrentBranch?.IsTrackStatusVisible ?? false;
                    GetOwnerPage()?.ChangeDirtyState(Models.DirtyState.HasPendingPullOrPush, !hasPendingPullOrPush);
                });
            }, token);
        }

        private void RemoveInvalidBranchHistoryFilters(List<Models.Branch> branches)
        {
            if (_uiStates == null || _uiStates.HistoryFilters.Count == 0)
                return;

            var localBranches = new HashSet<string>(StringComparer.Ordinal);
            var remoteBranches = new HashSet<string>(StringComparer.Ordinal);
            foreach (var branch in branches)
            {
                if (string.IsNullOrEmpty(branch.FullName))
                    continue;

                if (branch.IsLocal)
                    localBranches.Add(branch.FullName);
                else
                    remoteBranches.Add(branch.FullName);
            }

            var changed = false;
            for (var i = _uiStates.HistoryFilters.Count - 1; i >= 0; i--)
            {
                var filter = _uiStates.HistoryFilters[i];
                var valid = filter.Type switch
                {
                    Models.FilterType.LocalBranch => localBranches.Contains(filter.Pattern),
                    Models.FilterType.RemoteBranch => remoteBranches.Contains(filter.Pattern),
                    Models.FilterType.LocalBranchFolder => HasBranchWithPrefix(localBranches, filter.Pattern),
                    Models.FilterType.RemoteBranchFolder => HasBranchWithPrefix(remoteBranches, filter.Pattern),
                    _ => true,
                };

                if (valid)
                    continue;

                _uiStates.HistoryFilters.RemoveAt(i);
                changed = true;
            }

            if (!changed)
                return;

            HistoryFilterMode = _uiStates.GetHistoryFilterMode();
            IsHistoryFiltersCollapsed = _uiStates.HistoryFilters.Count > AUTO_COLLAPSE_HISTORY_FILTER_COUNT;
            NotifyHistoryFilterIndicatorsChanged();
            NotifyCurrentBranchVisualChanged();
        }

        private static bool HasBranchWithPrefix(HashSet<string> branches, string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return false;

            var prefixWithSeparator = $"{prefix}/";
            foreach (var branch in branches)
            {
                if (branch.StartsWith(prefixWithSeparator, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public void RefreshWorktrees()
        {
            Task.Run(async () =>
            {
                var worktrees = await new Commands.Worktree(FullPath).ReadAllAsync().ConfigureAwait(false);
                var cleaned = Worktree.Build(FullPath, worktrees);
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
                    ValidateHistoryFilters(false);
                });
            }, token);
        }

        public void RefreshCommits()
        {
            RefreshCommits(false);
        }

        public void RefreshCommits(bool fastAfterFetch)
        {
            _ = RefreshCommitsAsync(fastAfterFetch);
        }

        private Task RefreshCommitsAsync(bool fastAfterFetch)
        {
            lock (_refreshCommitsLock)
            {
                if (_cancellationRefreshCommits is { IsCancellationRequested: false })
                    _cancellationRefreshCommits.Cancel();

                _cancellationRefreshCommits = new CancellationTokenSource();
                var token = _cancellationRefreshCommits.Token;
                return Task.Run(async () =>
                {
                    var enteredRefreshGate = false;
                    try
                    {
                        await _refreshCommitsGate.WaitAsync(token).ConfigureAwait(false);
                        enteredRefreshGate = true;
                        token.ThrowIfCancellationRequested();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (_histories != null)
                            {
                                _histories.IsLoading = true;
                                _histories.IsBackfilling = false;
                            }
                        });

                        var fullLimits = BuildHistoryLimits(Preferences.Instance.MaxHistoryCommits);
                        var quickLimits = fastAfterFetch ? string.Empty : BuildQuickHistoryLimits();

                        if (!string.IsNullOrEmpty(quickLimits) && !quickLimits.Equals(fullLimits, StringComparison.Ordinal))
                        {
                            var quickSnapshot = await QueryCommitHistorySnapshotAsync(quickLimits, false, token).ConfigureAwait(false);
                            if (!token.IsCancellationRequested && quickSnapshot != null && quickSnapshot.Commits.Count > 0)
                                await ApplyCommitHistorySnapshotAsync(quickSnapshot, token, false, true, false).ConfigureAwait(false);
                        }

                        if (token.IsCancellationRequested)
                            return;

                        var fullSnapshot = await QueryCommitHistorySnapshotAsync(fullLimits, true, token).ConfigureAwait(false);
                        if (fullSnapshot != null)
                            await ApplyCommitHistorySnapshotAsync(fullSnapshot, token, false, false, true).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // A newer refresh owns the history view now.
                    }
                    catch (Exception e)
                    {
                        App.RaiseException(FullPath, $"Failed to load commit history. Reason: {e.GetBaseException().Message}");
                    }
                    finally
                    {
                        try
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (_histories == null || _cancellationRefreshCommits?.Token != token)
                                    return;

                                _histories.IsLoading = false;
                                _histories.IsBackfilling = false;
                            });
                        }
                        finally
                        {
                            if (enteredRefreshGate)
                                _refreshCommitsGate.Release();
                        }
                    }
                }, CancellationToken.None);
            }
        }

        private string BuildHistoryLimits(int maxCommits)
        {
            var builder = new StringBuilder();
            builder
                .Append('-').Append(maxCommits).Append(' ')
                .Append(_uiStates.BuildHistoryParams(GitDir));

            var hasIncludedHistoryFilters = false;
            foreach (var filter in _uiStates.HistoryFilters)
            {
                if (filter.Type != Models.FilterType.Path && filter.Mode == Models.FilterMode.Included)
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

            var pathspecs = _uiStates.BuildHistoryPathspecs();
            if (!string.IsNullOrEmpty(pathspecs))
                builder.Append(' ').Append(pathspecs);

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

        private async Task<CommitHistorySnapshot> QueryCommitHistorySnapshotAsync(
            string limits,
            bool pruneFoldState,
            CancellationToken cancellationToken)
        {
            var totalStopwatch = Stopwatch.StartNew();
            var stageStopwatch = Stopwatch.StartNew();
            var queryCommits = new Commands.QueryCommits(FullPath, limits)
            {
                CancellationToken = cancellationToken,
            };
            var commits = await queryCommits.GetResultAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var queriedCommitCount = commits.Count;
            var queryCommitsMilliseconds = stageStopwatch.ElapsedMilliseconds;
            stageStopwatch.Restart();
            var commitDiffStats = new Dictionary<string, Commands.CommitHistoryDiffStat>(StringComparer.Ordinal);
            var commitsMissingMetadata = new List<Models.Commit>();
            var metadataCacheHits = 0;

            foreach (var commit in commits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_commitHistoryMetadataCache != null && _commitHistoryMetadataCache.TryGet(commit.SHA, out var cached))
                {
                    metadataCacheHits++;
                    commitDiffStats[commit.SHA] = new Commands.CommitHistoryDiffStat()
                    {
                        ChangedFileCount = cached.ChangedFileCount,
                        HasSubmodulePointerChange = cached.HasSubmodulePointerChange,
                        RegularFileChangeCount = cached.RegularFileChangeCount,
                        AddedFileChangeCount = cached.AddedFileChangeCount,
                        ModifiedFileChangeCount = cached.ModifiedFileChangeCount,
                        SubmodulePointerChangeCount = cached.SubmodulePointerChangeCount,
                        SubmodulePaths = cached.SubmodulePaths ?? [],
                        HasRenameOrCopyChange = cached.HasRenameOrCopyChange,
                        HasTypeChange = cached.HasTypeChange,
                    };
                }
                else
                {
                    commitsMissingMetadata.Add(commit);
                }
            }

            if (commitsMissingMetadata.Count > 0)
            {
                var queryOnlyMissingCommits =
                    _commitHistoryMetadataCache != null &&
                    commitsMissingMetadata.Count <= MAX_INCREMENTAL_HISTORY_METADATA_COMMITS;
                var queryMetadata = queryOnlyMissingCommits
                    ? new Commands.QueryCommitSubmodulePointerFlags(
                        FullPath,
                        commitsMissingMetadata.ConvertAll(x => x.SHA))
                    : new Commands.QueryCommitSubmodulePointerFlags(FullPath, limits);
                queryMetadata.CancellationToken = cancellationToken;
                var queriedDiffStats = await queryMetadata.GetResultAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var pair in queriedDiffStats)
                    commitDiffStats[pair.Key] = pair.Value;

                if (_commitHistoryMetadataCache != null)
                {
                    var commitsToCache = queryOnlyMissingCommits ? commitsMissingMetadata : commits;
                    var cacheUpdates = new Dictionary<string, Models.CommitHistoryMetadata>(StringComparer.Ordinal);
                    foreach (var commit in commitsToCache)
                    {
                        if (commitDiffStats.TryGetValue(commit.SHA, out var stat))
                        {
                            cacheUpdates[commit.SHA] = new Models.CommitHistoryMetadata()
                            {
                                ChangedFileCount = stat.ChangedFileCount,
                                HasSubmodulePointerChange = stat.HasSubmodulePointerChange,
                                RegularFileChangeCount = stat.RegularFileChangeCount,
                                AddedFileChangeCount = stat.AddedFileChangeCount,
                                ModifiedFileChangeCount = stat.ModifiedFileChangeCount,
                                SubmodulePointerChangeCount = stat.SubmodulePointerChangeCount,
                                SubmodulePaths = [.. stat.SubmodulePaths],
                                HasRenameOrCopyChange = stat.HasRenameOrCopyChange,
                                HasTypeChange = stat.HasTypeChange,
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
                cancellationToken.ThrowIfCancellationRequested();

                if (commitDiffStats.TryGetValue(commit.SHA, out var stat))
                {
                    var submodulePointerChangeCount = stat.SubmodulePointerChangeCount;
                    if (stat.HasSubmodulePointerChange && submodulePointerChangeCount == 0)
                        submodulePointerChangeCount = 1;

                    var regularFileChangeCount = stat.RegularFileChangeCount;
                    if (stat.ChangedFileCount > 0 && regularFileChangeCount == 0 && stat.SubmodulePointerChangeCount == 0)
                        regularFileChangeCount = Math.Max(0, stat.ChangedFileCount - submodulePointerChangeCount);

                    commit.HasSubmodulePointerChange = stat.HasSubmodulePointerChange;
                    commit.ChangedFileCount = stat.ChangedFileCount;
                    commit.RegularFileChangeCount = regularFileChangeCount;
                    commit.AddedFileChangeCount = stat.AddedFileChangeCount;
                    commit.ModifiedFileChangeCount = stat.ModifiedFileChangeCount;
                    commit.SubmodulePointerChangeCount = submodulePointerChangeCount;
                    commit.SubmoduleUpdateBadges = stat.SubmodulePaths.Count > 0
                        ? stat.SubmodulePaths.ConvertAll(path => new Models.SubmoduleUpdateBadge(path, ResolveSubmoduleUpdateBadgeColor(path)))
                        : stat.HasSubmodulePointerChange
                            ? [new Models.SubmoduleUpdateBadge("submodule", ResolveSubmoduleUpdateBadgeColor("submodule"))]
                            : [];
                    commit.HasRenameOrCopyChange = stat.HasRenameOrCopyChange;
                    commit.HasTypeChange = stat.HasTypeChange;
                }
                else
                {
                    commit.HasSubmodulePointerChange = false;
                    commit.ChangedFileCount = 0;
                    commit.RegularFileChangeCount = 0;
                    commit.AddedFileChangeCount = 0;
                    commit.ModifiedFileChangeCount = 0;
                    commit.SubmodulePointerChangeCount = 0;
                    commit.SubmoduleUpdateBadges = [];
                    commit.HasRenameOrCopyChange = false;
                    commit.HasTypeChange = false;
                }
            }

            var metadataMilliseconds = stageStopwatch.ElapsedMilliseconds;
            stageStopwatch.Restart();

            if (_uiStates.OnlyShowSPPCommitsInHistory)
                commits.RemoveAll(x => !x.HasSubmodulePointerChange);

            AttachSuperProjectPointerDecorator(commits);
            ApplyHistoryFilterColorsToDecorators(commits);
            var foldableBranchFullNames = BuildFoldableBranchFullNameSet(commits);
            var notifyFoldControlChange = false;
            if (pruneFoldState)
                notifyFoldControlChange = _foldedBranchFullNames.RemoveWhere(name => !foldableBranchFullNames.Contains(name)) > 0;

            ApplyFoldStateToDecorators(commits, foldableBranchFullNames);
            ApplyFoldedBranchRuns(commits, foldableBranchFullNames);

            var prepareMilliseconds = stageStopwatch.ElapsedMilliseconds;
            stageStopwatch.Restart();
            cancellationToken.ThrowIfCancellationRequested();
            var graph = Models.CommitGraph.Generate(
                commits,
                _uiStates.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly),
                _uiStates.GraphHighlighting,
                []);
            var graphMilliseconds = stageStopwatch.ElapsedMilliseconds;
            totalStopwatch.Stop();

            return new CommitHistorySnapshot()
            {
                Commits = commits,
                Graph = graph,
                ShouldNotifyFoldControlChange = notifyFoldControlChange,
                QueryCommitsMilliseconds = queryCommitsMilliseconds,
                MetadataMilliseconds = metadataMilliseconds,
                PrepareMilliseconds = prepareMilliseconds,
                GraphMilliseconds = graphMilliseconds,
                TotalMilliseconds = totalStopwatch.ElapsedMilliseconds,
                MetadataCacheHits = metadataCacheHits,
                QueriedCommitCount = queriedCommitCount,
            };
        }

        private async Task ApplyCommitHistorySnapshotAsync(
            CommitHistorySnapshot snapshot,
            CancellationToken token,
            bool isLoading,
            bool isBackfilling,
            bool finalizeNavigation)
        {
            var uiQueueStopwatch = Stopwatch.StartNew();
            Histories appliedHistories = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _histories == null)
                    return;

                uiQueueStopwatch.Stop();
                var applyStopwatch = Stopwatch.StartNew();

                if (snapshot.ShouldNotifyFoldControlChange)
                    NotifyFoldControlsChanged();

                _histories.IsLoading = isLoading;
                _histories.IsBackfilling = isBackfilling;
                _histories.ApplySnapshot(snapshot.Commits, snapshot.Graph);
                UpdateVisibleFoldBranchStatesFromCurrentGraph();
                NotifyCurrentBranchVisualChanged();

                appliedHistories = _histories;

                if (finalizeNavigation)
                {
                    if (!string.IsNullOrEmpty(_navigateToCommitDelayed))
                        NavigateToCommit(_navigateToCommitDelayed);

                    _navigateToCommitDelayed = string.Empty;
                }

                applyStopwatch.Stop();
                Debug.WriteLine(
                    $"[HistoryPerformance] mode={(isBackfilling ? "quick" : "full")}, " +
                    $"commits={snapshot.Commits.Count}/{snapshot.QueriedCommitCount}, " +
                    $"query={snapshot.QueryCommitsMilliseconds}ms, " +
                    $"metadata={snapshot.MetadataMilliseconds}ms " +
                    $"(cache={snapshot.MetadataCacheHits}/{snapshot.QueriedCommitCount}), " +
                    $"prepare={snapshot.PrepareMilliseconds}ms, " +
                    $"graph={snapshot.GraphMilliseconds}ms, " +
                    $"queue={uiQueueStopwatch.ElapsedMilliseconds}ms, " +
                    $"ui={applyStopwatch.ElapsedMilliseconds}ms, " +
                    $"total={snapshot.TotalMilliseconds + uiQueueStopwatch.ElapsedMilliseconds + applyStopwatch.ElapsedMilliseconds}ms");
            });

            if (appliedHistories != null && !token.IsCancellationRequested)
            {
                var state = await appliedHistories.UpdateBisectInfoAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested && _histories == appliedHistories)
                        BisectState = state;
                });
            }
        }

        public void RefreshSubmodules(bool force = false)
        {
            var refreshVersion = Interlocked.Increment(ref _refreshSubmodulesVersion);
            var queryStatus = force || Preferences.Instance.RefreshSubmoduleStatusByDefault;

            if (!MayHaveSubmodules())
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    IsSubmodulesLoading = false;

                    if (_submodules.Count > 0)
                    {
                        Submodules = [];
                        VisibleSubmodules = BuildVisibleSubmodules();
                    }
                });

                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (refreshVersion == _refreshSubmodulesVersion)
                    IsSubmodulesLoading = true;
            });

            Task.Run(async () =>
            {
                try
                {
                    var depth = Preferences.Instance.RecursiveSubmoduleDisplayDepth;
                    var submodules = await new Commands.QuerySubmodules(FullPath, depth, queryStatus).GetResultAsync().ConfigureAwait(false);

                    Dispatcher.UIThread.Invoke(() =>
                    {
                        if (refreshVersion != _refreshSubmodulesVersion)
                            return;

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
                                         exist.Status != module.Status ||
                                         exist.HasFileChanges != module.HasFileChanges ||
                                         exist.HasSubmoduleChanges != module.HasSubmoduleChanges;

                                if (hasChanged)
                                    break;
                            }
                        }

                        if (hasChanged)
                        {
                            Submodules = submodules;
                            VisibleSubmodules = BuildVisibleSubmodules();
                        }
                    });
                }
                finally
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (refreshVersion == _refreshSubmodulesVersion)
                            IsSubmodulesLoading = false;
                    });
                }
            });
        }

        public void RefreshWorkingCopyChanges()
        {
            RefreshWorkingCopyChanges(false);
        }

        public void RefreshWorkingCopyChanges(bool bypassUntrackedCache)
        {
            _ = RefreshWorkingCopyChangesAsync(bypassUntrackedCache);
        }

        private Task RefreshWorkingCopyChangesAsync(bool bypassUntrackedCache)
        {
            if (IsBare)
                return Task.CompletedTask;

            if (_cancellationRefreshWorkingCopyChanges is { IsCancellationRequested: false })
                _cancellationRefreshWorkingCopyChanges.Cancel();

            _cancellationRefreshWorkingCopyChanges = new CancellationTokenSource();
            var token = _cancellationRefreshWorkingCopyChanges.Token;
            var noOptionalLocks = Interlocked.Add(ref _queryLocalChangesTimes, 1) > 1;

            return Task.Run(async () =>
            {
                var changes = await new Commands.QueryLocalChanges(
                    FullPath,
                    _uiStates.IncludeUntrackedInLocalChanges,
                    noOptionalLocks,
                    !bypassUntrackedCache)
                    .WithCancellation(token)
                    .GetResultAsync()
                    .ConfigureAwait(false);

                if (_workingCopy == null || token.IsCancellationRequested)
                    return;

                await MarkSubmodulePointerChangesAsync(changes).ConfigureAwait(false);
                changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));

                Dispatcher.UIThread.Invoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    _workingCopy.SetData(changes);
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
                SendNotification("Git cannot create a branch before your first commit.", true);
                return;
            }

            if (CanCreatePopup())
                ShowPopup(new CreateBranch(this, _currentBranch));
        }

        public async Task CheckoutBranchAsync(Models.Branch branch)
        {
            if (branch.IsLocal)
            {
                var worktree = _worktrees.Find(x => x.IsAttachedTo(branch));
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
                if (_workingCopy is { CanSwitchBranchDirectly: true })
                    await ShowAndStartPopupAsync(new Checkout(this, branch));
                else
                    ShowPopup(new Checkout(this, branch));
            }
            else
            {
                foreach (var b in _branches)
                {
                    if (b.IsLocal &&
                        !string.IsNullOrEmpty(b.Upstream) &&
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
                SendNotification("Git cannot create a tag before your first commit.", true);
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

        public async Task ToggleAutoFetchOnRemoteAsync(Models.Remote remote)
        {
            var val = remote.DisableAutoFetch ? "false" : "true";
            var succ = await new Commands.Config(FullPath).SetAsync($"remote.{remote.Name.Quoted()}.disableautofetch", val);
            if (succ)
                remote.DisableAutoFetch = !remote.DisableAutoFetch;
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
            var progressLock = new object();
            var prunedBranches = prune ? new List<PrunedRemoteBranch>() : null;
            var prunedBranchKeys = prune ? new HashSet<string>(StringComparer.Ordinal) : null;
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

            Models.RecursiveOperationProgress CreateProgress(
                string target,
                Models.RecursiveOperationTargetState state)
            {
                lock (progressLock)
                {
                    return new Models.RecursiveOperationProgress
                    {
                        Total = totalTargets,
                        Succeeded = succeededTargets,
                        SkippedByUser = skippedByUserTargets,
                        SkippedAutomatically = skippedAutomaticallyTargets,
                        SkippedNotInitialized = skippedNotInitializedTargets,
                        Failed = failedTargets,
                        CurrentTarget = target,
                        CurrentState = state,
                    };
                }
            }

            void ApplyResult(Models.RecursiveOperationTargetState state, bool notInitialized)
            {
                lock (progressLock)
                {
                    switch (state)
                    {
                        case Models.RecursiveOperationTargetState.Succeeded:
                            succeededTargets++;
                            break;
                        case Models.RecursiveOperationTargetState.Skipped:
                            skippedAutomaticallyTargets++;
                            if (notInitialized)
                                skippedNotInitializedTargets++;
                            break;
                        case Models.RecursiveOperationTargetState.Failed:
                            failedTargets++;
                            succ = false;
                            break;
                    }
                }
            }

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
                    cancellationToken,
                    prunedBranches,
                    prunedBranchKeys);
                if (!one)
                {
                    succ = false;
                    if (stopOnError)
                        return false;
                }
            }

            async Task<Models.RecursiveOperationTargetState> RunSubmoduleFetchAsync(string submodulePath)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Models.RecursiveOperationTargetState.Failed;

                onProgressChanged?.Invoke(CreateProgress(submodulePath, Models.RecursiveOperationTargetState.Running));

                var submoduleRoot = Native.OS.GetAbsPath(FullPath, submodulePath).Replace('\\', '/');
                var gitDir = Path.Combine(submoduleRoot, ".git");
                if (!Directory.Exists(submoduleRoot) || (!Directory.Exists(gitDir) && !File.Exists(gitDir)))
                {
                    log?.AppendLine($"Skip submodule `{submodulePath}` (not initialized).");
                    ApplyResult(Models.RecursiveOperationTargetState.Skipped, true);
                    onProgressChanged?.Invoke(CreateProgress(submodulePath, Models.RecursiveOperationTargetState.Skipped));
                    return Models.RecursiveOperationTargetState.Skipped;
                }

                var submoduleRemotes = await GetFetchRemoteNamesForRepositoryAsync(submoduleRoot);
                if (submoduleRemotes.Count == 0)
                {
                    log?.AppendLine($"Skip submodule `{submodulePath}` (no remotes).");
                    ApplyResult(Models.RecursiveOperationTargetState.Skipped, false);
                    onProgressChanged?.Invoke(CreateProgress(submodulePath, Models.RecursiveOperationTargetState.Skipped));
                    return Models.RecursiveOperationTargetState.Skipped;
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
                        cancellationToken,
                        prunedBranches,
                        prunedBranchKeys);
                    if (!one)
                    {
                        submoduleSucceeded = false;
                        if (stopOnError)
                            break;
                    }
                }

                var state = submoduleSucceeded
                    ? Models.RecursiveOperationTargetState.Succeeded
                    : Models.RecursiveOperationTargetState.Failed;
                ApplyResult(state, false);
                onProgressChanged?.Invoke(CreateProgress(submodulePath, state));
                return state;
            }

            foreach (var target in targets)
            {
                var state = await RunSubmoduleFetchAsync(target).ConfigureAwait(false);
                if (state == Models.RecursiveOperationTargetState.Failed && stopOnError)
                    return false;
            }

            if (prune)
                AppendPrunedRemoteBranchesSummary(log, prunedBranches);

            if (succ)
                MarkFetched(prune);

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
            CancellationToken cancellationToken,
            List<PrunedRemoteBranch> prunedBranches = null,
            HashSet<string> prunedBranchKeys = null)
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
            if (prune && prunedBranches != null && prunedBranchKeys != null)
            {
                cmd.OnOutputLine = line =>
                {
                    var remoteRef = TryParsePrunedRemoteBranch(line);
                    if (string.IsNullOrEmpty(remoteRef))
                        return;

                    var key = $"{scope}\n{remoteRef}";
                    lock (prunedBranchKeys)
                    {
                        if (prunedBranchKeys.Add(key))
                            prunedBranches.Add(new PrunedRemoteBranch(scope, remoteRef));
                    }
                };
            }

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

        private static string TryParsePrunedRemoteBranch(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var deletedIdx = line.IndexOf("[deleted]", StringComparison.Ordinal);
            if (deletedIdx < 0)
                return null;

            var arrowIdx = line.IndexOf("->", deletedIdx, StringComparison.Ordinal);
            if (arrowIdx < 0)
                return null;

            var remoteRef = line.Substring(arrowIdx + 2).Trim();
            if (string.IsNullOrEmpty(remoteRef) ||
                remoteRef.StartsWith("tag ", StringComparison.OrdinalIgnoreCase) ||
                remoteRef.StartsWith("refs/tags/", StringComparison.Ordinal))
                return null;

            return remoteRef;
        }

        private static bool IsFetchRefStatusLine(string line, out bool changed)
        {
            changed = false;
            if (string.IsNullOrWhiteSpace(line) || !line.Contains(" -> ", StringComparison.Ordinal))
                return false;

            changed = !line.Contains("[up to date]", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private static void AppendPrunedRemoteBranchesSummary(Models.ICommandLog log, List<PrunedRemoteBranch> prunedBranches)
        {
            log?.AppendLine("=== Pruned remote branches ===");
            if (prunedBranches == null || prunedBranches.Count == 0)
            {
                log?.AppendLine("No remote branches were pruned.");
                return;
            }

            foreach (var branch in prunedBranches)
                log?.AppendLine($"- {FormatFetchScope(branch.Scope)}: {branch.RemoteRef}");
        }

        private static string FormatFetchScope(string scope)
        {
            const string submodulePrefix = "submodule:";
            if (string.IsNullOrEmpty(scope) || scope.Equals("root", StringComparison.Ordinal))
                return "root";

            return scope.StartsWith(submodulePrefix, StringComparison.Ordinal)
                ? scope.Substring(submodulePrefix.Length)
                : scope;
        }

        public async Task<bool> RunUpdateSubmodulesRecursivelyAsync(
            Models.ICommandLog log,
            List<string> selectedTargets = null,
            bool stopOnError = false,
            CancellationToken cancellationToken = default,
            Action<Models.RecursiveOperationProgress> onProgressChanged = null,
            bool runInParallel = true)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var sourceSubmodules = _submodules;
            if (sourceSubmodules.Count == 0 && MayHaveSubmodules())
            {
                try
                {
                    var depth = Preferences.Instance.RecursiveSubmoduleDisplayDepth;
                    sourceSubmodules = await new Commands.QuerySubmodules(FullPath, depth).GetResultAsync().ConfigureAwait(false);
                }
                catch
                {
                    sourceSubmodules = _submodules;
                }
            }

            var targets = new List<string>();
            var skippedByUserTargets = 0;
            if (selectedTargets == null)
            {
                foreach (var submodule in sourceSubmodules)
                    targets.Add(submodule.Path);
            }
            else
            {
                var available = new HashSet<string>(StringComparer.Ordinal);
                foreach (var submodule in sourceSubmodules)
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

            var requestedTargetCount = targets.Count;
            targets = BuildRecursiveSubmoduleUpdateRoots(targets, sourceSubmodules);
            if (targets.Count < requestedTargetCount)
                log?.AppendLine($"Collapsed {requestedTargetCount - targets.Count} nested submodule target(s) into their recursive parent update.");

            using var lockWatcher = _watcher?.Lock();
            var succ = true;
            var anyUpdated = false;
            var totalTargets = targets.Count;
            var succeededTargets = 0;
            var skippedAutomaticallyTargets = 0;
            var failedTargets = 0;
            var progressLock = new object();

            Models.RecursiveOperationProgress CreateProgress(
                string target,
                Models.RecursiveOperationTargetState state,
                string repositoryPath = null,
                string beforeRevision = null,
                string afterRevision = null)
            {
                lock (progressLock)
                {
                    return new Models.RecursiveOperationProgress
                    {
                        Total = totalTargets,
                        Succeeded = succeededTargets,
                        SkippedByUser = skippedByUserTargets,
                        SkippedAutomatically = skippedAutomaticallyTargets,
                        Failed = failedTargets,
                        CurrentTarget = target,
                        CurrentRepositoryPath = repositoryPath ?? string.Empty,
                        CurrentBeforeRevision = beforeRevision ?? string.Empty,
                        CurrentAfterRevision = afterRevision ?? string.Empty,
                        CurrentState = state,
                    };
                }
            }

            void ApplyResult(Models.RecursiveOperationTargetState state, bool updated)
            {
                lock (progressLock)
                {
                    switch (state)
                    {
                        case Models.RecursiveOperationTargetState.Succeeded:
                            succeededTargets++;
                            if (updated)
                                anyUpdated = true;
                            break;
                        case Models.RecursiveOperationTargetState.Skipped:
                            skippedAutomaticallyTargets++;
                            break;
                        case Models.RecursiveOperationTargetState.Failed:
                            failedTargets++;
                            succ = false;
                            break;
                    }
                }
            }

            async Task<Models.RecursiveOperationTargetState> RunOneAsync(string target)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Models.RecursiveOperationTargetState.Failed;

                using var timeout = new CancellationTokenSource(SPLIT_SUBMODULE_UPDATE_TIMEOUT);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
                var cmd = new Commands.Submodule(FullPath)
                {
                    RaiseError = stopOnError,
                    CancellationToken = linked.Token,
                };

                onProgressChanged?.Invoke(CreateProgress(target, Models.RecursiveOperationTargetState.Running));

                log?.AppendLine($"=== Update submodule `{target}` ===");
                var submoduleRoot = Native.OS.GetAbsPath(FullPath, target).Replace('\\', '/');
                var beforeHead = await new Commands.QueryRevisionByRefName(submoduleRoot, "HEAD").GetResultAsync().ConfigureAwait(false);
                var superProjectPointer = await new Commands.QuerySubmoduleSuperProjectPointer(FullPath, target).GetResultAsync().ConfigureAwait(false);
                var beforeMatchesPointer = IsSameRevision(beforeHead, superProjectPointer);
                var afterHead = string.Empty;
                var targetState = Models.RecursiveOperationTargetState.Running;
                var one = await cmd.Use(log).UpdateAsync([target], true, true, false).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    log?.AppendLine($"[canceled] Update `{target}` was canceled.");
                    return Models.RecursiveOperationTargetState.Failed;
                }

                if (timeout.IsCancellationRequested)
                {
                    log?.AppendLine($"[timeout] Update `{target}` exceeded {SPLIT_SUBMODULE_UPDATE_TIMEOUT.TotalMinutes:0} min and was terminated.");
                    ApplyResult(Models.RecursiveOperationTargetState.Failed, false);
                    onProgressChanged?.Invoke(CreateProgress(target, Models.RecursiveOperationTargetState.Failed));
                    if (stopOnError)
                        App.RaiseException(FullPath, $"Update `{target}` timed out.");

                    return Models.RecursiveOperationTargetState.Failed;
                }

                if (one)
                {
                    afterHead = await new Commands.QueryRevisionByRefName(submoduleRoot, "HEAD").GetResultAsync().ConfigureAwait(false);
                    var afterMatchesPointer = IsSameRevision(afterHead, superProjectPointer);
                    var becameInitialized = string.IsNullOrEmpty(beforeHead) && !string.IsNullOrEmpty(afterHead);
                    var movedToSuperProjectPointer = !beforeMatchesPointer && afterMatchesPointer;
                    var headChanged = !string.IsNullOrEmpty(afterHead) && !string.Equals(beforeHead, afterHead, StringComparison.Ordinal);

                    if (becameInitialized || movedToSuperProjectPointer || headChanged)
                    {
                        targetState = Models.RecursiveOperationTargetState.Succeeded;
                    }
                    else
                    {
                        log?.AppendLine($"[skip] Submodule `{target}` already matches the super-project pointer.");
                        targetState = Models.RecursiveOperationTargetState.Skipped;
                    }
                }
                else
                {
                    log?.AppendLine($"[failed] Update `{target}` failed.");
                    targetState = Models.RecursiveOperationTargetState.Failed;
                }

                ApplyResult(targetState, targetState == Models.RecursiveOperationTargetState.Succeeded);
                onProgressChanged?.Invoke(CreateProgress(target, targetState, submoduleRoot, beforeHead, afterHead));
                return targetState;
            }

            var batches = BuildOrderedSubmoduleTargetBatches(targets);
            var maxParallelism = runInParallel && !stopOnError
                ? Math.Min(MAX_RECURSIVE_SUBMODULE_UPDATE_PARALLELISM, Math.Max(1, totalTargets))
                : 1;
            if (maxParallelism == 1)
            {
                log?.AppendLine("Running submodule updates sequentially with one Git job at a time.");
                log?.AppendLine("Execution order is parent-first. Each selected submodule must finish successfully before the next one starts.");
            }
            else
            {
                log?.AppendLine($"Running up to {maxParallelism} top-level submodule updates in parallel.");
                log?.AppendLine("Each Git command updates nested submodules with one job at a time.");
            }

            for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                var batch = batches[batchIndex];
                if (cancellationToken.IsCancellationRequested)
                    return false;

                if (batch.Count > 0)
                    log?.AppendLine($"--- Submodule update wave {batchIndex + 1}/{batches.Count}: {string.Join(", ", batch)} ---");

                if (maxParallelism == 1)
                {
                    foreach (var target in batch)
                    {
                        if (string.IsNullOrWhiteSpace(target))
                            continue;

                        var state = await RunOneAsync(target).ConfigureAwait(false);
                        if (state == Models.RecursiveOperationTargetState.Failed)
                            return false;
                    }
                }
                else
                {
                    using var limiter = new SemaphoreSlim(maxParallelism);
                    var tasks = new List<Task>();
                    foreach (var target in batch)
                    {
                        if (string.IsNullOrWhiteSpace(target))
                            continue;

                        tasks.Add(Task.Run(async () =>
                        {
                            await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                            try
                            {
                                await RunOneAsync(target).ConfigureAwait(false);
                            }
                            finally
                            {
                                limiter.Release();
                            }
                        }, cancellationToken));
                    }

                    try
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                }
            }

            if (anyUpdated)
                MarkSubmodulesDirtyManually();

            return succ;
        }

        private static List<List<string>> BuildOrderedSubmoduleTargetBatches(List<string> targets)
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var seen = new HashSet<string>(comparer);
            var uniqueTargets = new List<string>();
            foreach (var target in targets)
            {
                if (!string.IsNullOrWhiteSpace(target) && seen.Add(target))
                    uniqueTargets.Add(target);
            }

            uniqueTargets.Sort((left, right) => comparer.Compare(left, right));

            var levels = new SortedDictionary<int, List<string>>();
            foreach (var target in uniqueTargets)
            {
                var selectedAncestorDepth = 0;
                foreach (var other in uniqueTargets)
                {
                    if (IsSubmodulePathAncestor(other, target, comparison))
                        selectedAncestorDepth++;
                }

                if (!levels.TryGetValue(selectedAncestorDepth, out var batch))
                {
                    batch = [];
                    levels[selectedAncestorDepth] = batch;
                }

                batch.Add(target);
            }

            var ordered = new List<List<string>>(levels.Count);
            foreach (var (_, batch) in levels)
                ordered.Add(batch);

            return ordered;
        }

        private static List<string> BuildRecursiveSubmoduleUpdateRoots(
            List<string> targets,
            List<Models.Submodule> knownSubmodules)
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var knownPaths = new HashSet<string>(comparer);
            foreach (var module in knownSubmodules)
            {
                if (!string.IsNullOrWhiteSpace(module.Path))
                    knownPaths.Add(module.Path);
            }

            var roots = new HashSet<string>(comparer);
            foreach (var target in targets)
            {
                var root = target;
                var parent = FindDirectSubmoduleAncestor(root, knownPaths, comparison);
                while (parent != null)
                {
                    root = parent;
                    parent = FindDirectSubmoduleAncestor(root, knownPaths, comparison);
                }

                roots.Add(root);
            }

            var ordered = new List<string>(roots);
            ordered.Sort(comparer);
            return ordered;
        }

        private static string FindDirectSubmoduleAncestor(
            string path,
            HashSet<string> knownPaths,
            StringComparison comparison)
        {
            string best = null;
            foreach (var candidate in knownPaths)
            {
                if (!IsSubmodulePathAncestor(candidate, path, comparison))
                    continue;

                if (best == null || candidate.Length > best.Length)
                    best = candidate;
            }

            return best;
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

        private static bool IsSameRevision(string left, string right)
        {
            return !string.IsNullOrEmpty(left) &&
                !string.IsNullOrEmpty(right) &&
                string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> RunRestoreCleanStateRecursivelyAsync(
            Models.ICommandLog log,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            using var lockWatcher = _watcher?.Lock();

            log?.AppendLine("=== Step 1/3: Discard root repository tracked changes ===");
            var resetRoot = await RunRecursiveGitCommandAsync(
                "reset --hard",
                log,
                cancellationToken).ConfigureAwait(false);
            if (!resetRoot)
                return false;
            ClearCommitMessage();

            if (cancellationToken.IsCancellationRequested)
                return false;

            log?.AppendLine("=== Step 2/3: Discard initialized submodule tracked changes recursively ===");
            var resetSubmodules = await RunRecursiveGitCommandAsync(
                "submodule foreach --recursive \"git reset --hard\"",
                log,
                cancellationToken).ConfigureAwait(false);
            if (!resetSubmodules)
                return false;

            if (cancellationToken.IsCancellationRequested)
                return false;

            log?.AppendLine("=== Step 3/3: Restore submodules to parent-recorded commits ===");
            var updateSubmodules = await new Commands.Submodule(FullPath)
            {
                RaiseError = true,
                CancellationToken = cancellationToken,
            }.Use(log).UpdateAsync([], true, true, false).ConfigureAwait(false);
            if (!updateSubmodules)
                return false;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshAll();
                RefreshSuperProjectSubmodulePointer();
            });
            return true;
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
                    .UpdateAsync(submodules, false, _settings.EnableRecursiveWhenAutoUpdatingSubmodules, false);
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
            OnPropertyChanged(nameof(IsParentRepository));
            RefreshCommits();
        }

        private async Task<bool> RunRecursiveGitCommandAsync(
            string args,
            Models.ICommandLog log,
            CancellationToken cancellationToken)
        {
            return await new Commands.Command()
            {
                WorkingDirectory = FullPath,
                Context = FullPath,
                Args = args,
                Log = log,
                CancellationToken = cancellationToken,
                RaiseError = true,
            }.ExecAsync().ConfigureAwait(false);
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

        public void OpenWorktree(Worktree worktree)
        {
            if (worktree.IsCurrent)
                return;

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

        public async Task LockWorktreeAsync(Worktree worktree)
        {
            using var lockWatcher = _watcher?.Lock();
            var log = CreateLog("Lock Worktree");
            var succ = await new Commands.Worktree(FullPath).Use(log).LockAsync(worktree.FullPath);
            if (succ)
                worktree.IsLocked = true;
            log.Complete();
        }

        public async Task UnlockWorktreeAsync(Worktree worktree)
        {
            using var lockWatcher = _watcher?.Lock();
            var log = CreateLog("Unlock Worktree");
            var succ = await new Commands.Worktree(FullPath).Use(log).UnlockAsync(worktree.FullPath);
            if (succ)
                worktree.IsLocked = false;
            log.Complete();
        }

        public List<AI.Service> GetPreferredOpenAIServices()
        {
            var services = Preferences.Instance.OpenAIServices;
            if (services == null || services.Count == 0)
                return [];

            if (services.Count == 1)
                return [services[0]];

            var preferred = _settings.PreferredOpenAIService;
            var all = new List<AI.Service>();
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

        private BranchTreeNode.Builder BuildBranchTree(List<Models.Branch> branches, List<Models.Remote> remotes, bool shouldCleanupExpandedNodes = false)
        {
            var builder = new BranchTreeNode.Builder(
                _uiStates.LocalBranchSortMode,
                _uiStates.RemoteBranchSortMode,
                GetRebaseBaseBranch()?.FullName);
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
            IsHistoryFiltersCollapsed = _uiStates.HistoryFilters.Count > AUTO_COLLAPSE_HISTORY_FILTER_COUNT;
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
            OnPropertyChanged(nameof(HistoryPathFilterCount));
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
            OnPropertyChanged(nameof(HasInProgressStatus));
            OnPropertyChanged(nameof(InProgressStatusText));
        }

        private static bool AreSubmoduleColorMapsEqual(
            IReadOnlyDictionary<string, uint> left,
            IReadOnlyDictionary<string, uint> right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null || left.Count != right.Count)
                return false;

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var color) || color != pair.Value)
                    return false;
            }

            return true;
        }

        private IReadOnlyDictionary<string, uint> BuildSubmoduleUpdateBadgeColorMap(IEnumerable<string> paths)
        {
            var colors = new Dictionary<string, uint>(Models.SubmoduleUpdateBadge.BuildDirectSubmoduleColorMap(paths), StringComparer.Ordinal);
            var configured = _settings?.GetSubmoduleUpdateBadgeColorMap();
            if (configured != null)
            {
                foreach (var pair in configured)
                {
                    if (colors.ContainsKey(pair.Key))
                        colors[pair.Key] = pair.Value;
                }
            }

            return colors;
        }

        private static string NormalizeSubmodulePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim('/');
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

        private void ValidateHistoryFilters(bool forBranch)
        {
            if (_historyFilterMode == Models.FilterMode.None)
                return;

            var set = new HashSet<string>();

            if (forBranch)
            {
                foreach (var b in _branches)
                    set.Add(b.FullName);

                foreach (var f in _uiStates.HistoryFilters)
                {
                    if (f.Type is Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch)
                        f.IsValid = set.Contains(f.Pattern);
                }
            }
            else
            {
                foreach (var t in _tags)
                    set.Add(t.Name);

                foreach (var f in _uiStates.HistoryFilters)
                {
                    if (f.Type is Models.FilterType.Tag)
                        f.IsValid = set.Contains(f.Pattern);
                }
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
            else if (e.PropertyName == nameof(Preferences.DisableBackgroundTasks))
                EnsureBackgroundTaskState();
            else if (e.PropertyName == nameof(Preferences.RecursiveSubmoduleDisplayDepth))
                RefreshSubmodules();
            else if (e.PropertyName == nameof(Preferences.RefreshSubmoduleStatusByDefault))
                RefreshSubmodules();
        }

        private void EnsureBackgroundTaskState()
        {
            EnsureWatcherState();
            EnsureAutoFetchTimerState();
        }

        private void EnsureWatcherState()
        {
            if (Preferences.Instance.DisableBackgroundTasks)
            {
                _watcher?.Dispose();
                _watcher = null;
                return;
            }

            if (_watcher != null)
                return;

            try
            {
                _watcher = new Models.Watcher(this, FullPath, _gitCommonDir);
            }
            catch (Exception ex)
            {
                App.RaiseException(string.Empty, $"Failed to start watcher for repository: '{FullPath}'. You may need to press 'F5' to refresh repository manually!\n\nReason: {ex.Message}");
            }
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

        public void ShowFetchDurationToast(TimeSpan gitDuration, TimeSpan guiRefreshDuration)
        {
            _fetchDurationToastCancellation?.Cancel();
            _fetchDurationToastCancellation?.Dispose();

            var cts = new CancellationTokenSource();
            _fetchDurationToastCancellation = cts;

            FetchDurationToastText = $"Fetch finished: Git {gitDuration.TotalSeconds:0.0}s, GUI refresh {guiRefreshDuration.TotalSeconds:0.0}s";
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
                        var submodules = await new Commands.QuerySubmodules(normalizedSuperProjectRoot, 1, false).GetResultAsync().ConfigureAwait(false);
                        foreach (var submodule in submodules)
                        {
                            if (string.IsNullOrWhiteSpace(submodule.Path))
                                continue;

                            var submoduleRoot = Path.GetFullPath(Path.Combine(normalizedSuperProjectRoot, submodule.Path)).Replace('\\', '/').TrimEnd('/');
                            if (!submoduleRoot.Equals(FullPath, pathComparison))
                                continue;

                            // `submodule status` reports the checked-out SHA; SPP must come from the parent HEAD's gitlink.
                            var pointer = await new Commands.QuerySubmoduleSuperProjectPointer(normalizedSuperProjectRoot, submodule.Path)
                                .GetResultAsync()
                                .ConfigureAwait(false);
                            resolved = NormalizeSubmodulePointerSHA(pointer);
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

        private void ApplyHistoryFilterColorsToDecorators(List<Models.Commit> commits)
        {
            if (commits == null || commits.Count == 0)
                return;

            EnsureIncludedBranchFiltersHaveColors(commits);

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
            var rebaseBaseBranchFullName = GetRebaseBaseBranch()?.FullName;
            const uint incidentalBranchColor = 0x18808080;
            ApplyAutoColorsToVisibleBranchDecorators(commits, branchColors, includedBranches, hasIncludedBranches, branchesByFullName, localBranchesByUpstream);

            foreach (var commit in commits)
            {
                foreach (var decorator in commit.Decorators)
                {
                    decorator.Color = 0;
                    decorator.IsRebaseBaseBranch = false;
                    switch (decorator.Type)
                    {
                        case Models.DecoratorType.CurrentBranchHead:
                        case Models.DecoratorType.LocalBranchHead:
                            var localRefName = $"refs/heads/{decorator.Name}";
                            decorator.IsRebaseBaseBranch = localRefName.Equals(rebaseBaseBranchFullName, StringComparison.Ordinal);
                            if (TryResolveBranchDisplayColor(localRefName, true, branchColors, branchesByFullName, localBranchesByUpstream, out var localColor))
                                decorator.Color = localColor;
                            else if (hasIncludedBranches && !ShouldKeepBranchVisibleColor(localRefName, true, includedBranches, branchesByFullName, localBranchesByUpstream))
                                decorator.Color = incidentalBranchColor;
                            break;
                        case Models.DecoratorType.RemoteBranchHead:
                            var remoteRefName = $"refs/remotes/{decorator.Name}";
                            decorator.IsRebaseBaseBranch = remoteRefName.Equals(rebaseBaseBranchFullName, StringComparison.Ordinal);
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

        private void ApplyAutoColorsToVisibleBranchDecorators(
            List<Models.Commit> commits,
            Dictionary<string, uint> branchColors,
            HashSet<string> includedBranches,
            bool hasIncludedBranches,
            Dictionary<string, Models.Branch> branchesByFullName,
            Dictionary<string, Models.Branch> localBranchesByUpstream)
        {
            var conflictsByLogicalBranch = BuildBranchColorConflictMap(commits, branchesByFullName, localBranchesByUpstream);
            var assignedByLogicalBranch = new Dictionary<string, uint>(StringComparer.Ordinal);
            var usedColors = new HashSet<uint>();

            foreach (var pair in branchColors)
            {
                var logicalKey = ResolveBranchColorGroupKey(pair.Key, branchesByFullName, localBranchesByUpstream);
                if (string.IsNullOrWhiteSpace(logicalKey))
                    logicalKey = pair.Key;

                assignedByLogicalBranch[logicalKey] = pair.Value;
                usedColors.Add(pair.Value);
            }

            foreach (var commit in commits)
            {
                foreach (var decorator in commit.Decorators)
                {
                    var fullRefName = GetFullRefNameFromDecorator(decorator);
                    if (string.IsNullOrWhiteSpace(fullRefName) ||
                        branchColors.ContainsKey(fullRefName) ||
                        (hasIncludedBranches && !ShouldKeepBranchVisibleColor(fullRefName, decorator.Type != Models.DecoratorType.RemoteBranchHead, includedBranches, branchesByFullName, localBranchesByUpstream)))
                    {
                        continue;
                    }

                    var logicalKey = ResolveBranchColorGroupKey(fullRefName, branchesByFullName, localBranchesByUpstream);
                    if (string.IsNullOrWhiteSpace(logicalKey))
                        logicalKey = fullRefName;

                    if (!assignedByLogicalBranch.TryGetValue(logicalKey, out var color))
                    {
                        color = ChooseAutoHistoryFilterBranchColor(logicalKey, assignedByLogicalBranch, usedColors, conflictsByLogicalBranch, 0);
                        assignedByLogicalBranch[logicalKey] = color;
                        usedColors.Add(color);
                    }

                    branchColors[fullRefName] = color;
                }
            }
        }

        private void EnsureIncludedBranchFiltersHaveColors(List<Models.Commit> commits = null)
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

            var conflictsByLogicalBranch = BuildBranchColorConflictMap(commits, branchesByFullName, localBranchesByUpstream);
            var assignedByLogicalBranch = new Dictionary<string, uint>(StringComparer.Ordinal);
            var usedColors = new HashSet<uint>();
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
                    usedColors.Add(filter.Color);
                    continue;
                }
            }

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
                    continue;

                if (!assignedByLogicalBranch.TryGetValue(logicalBranchKey, out var color))
                {
                    color = ChooseAutoHistoryFilterBranchColor(logicalBranchKey, assignedByLogicalBranch, usedColors, conflictsByLogicalBranch, filter.Color);
                    assignedByLogicalBranch[logicalBranchKey] = color;
                    usedColors.Add(color);
                }

                filter.Color = color;
            }
        }

        private Dictionary<string, HashSet<string>> BuildBranchColorConflictMap(
            List<Models.Commit> commits,
            Dictionary<string, Models.Branch> branchesByFullName,
            Dictionary<string, Models.Branch> localBranchesByUpstream)
        {
            var conflicts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (commits == null || commits.Count == 0)
                return conflicts;

            foreach (var commit in commits)
            {
                List<string> logicalKeys = null;
                foreach (var decorator in commit.Decorators)
                {
                    var fullRefName = GetFullRefNameFromDecorator(decorator);

                    if (string.IsNullOrWhiteSpace(fullRefName))
                        continue;

                    var logicalKey = ResolveBranchColorGroupKey(fullRefName, branchesByFullName, localBranchesByUpstream);
                    if (string.IsNullOrWhiteSpace(logicalKey))
                        continue;

                    logicalKeys ??= [];
                    if (!logicalKeys.Exists(x => x.Equals(logicalKey, StringComparison.Ordinal)))
                        logicalKeys.Add(logicalKey);
                }

                if (logicalKeys is not { Count: > 1 })
                    continue;

                for (var i = 0; i < logicalKeys.Count - 1; i++)
                {
                    for (var j = i + 1; j < logicalKeys.Count; j++)
                    {
                        AddBranchColorConflict(conflicts, logicalKeys[i], logicalKeys[j]);
                        AddBranchColorConflict(conflicts, logicalKeys[j], logicalKeys[i]);
                    }
                }
            }

            return conflicts;
        }

        private static void AddBranchColorConflict(Dictionary<string, HashSet<string>> conflicts, string left, string right)
        {
            if (left.Equals(right, StringComparison.Ordinal))
                return;

            if (!conflicts.TryGetValue(left, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                conflicts[left] = set;
            }

            set.Add(right);
        }

        private static string GetFullRefNameFromDecorator(Models.Decorator decorator)
        {
            return decorator.Type switch
            {
                Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.LocalBranchHead => $"refs/heads/{decorator.Name}",
                Models.DecoratorType.RemoteBranchHead => $"refs/remotes/{decorator.Name}",
                _ => string.Empty,
            };
        }

        private static uint ChooseAutoHistoryFilterBranchColor(
            string logicalBranchKey,
            Dictionary<string, uint> assignedByLogicalBranch,
            HashSet<uint> usedColors,
            Dictionary<string, HashSet<string>> conflictsByLogicalBranch,
            uint fallback)
        {
            var bestColor = GetAutoHistoryFilterBranchColorAt(0);
            var bestScore = int.MaxValue;
            for (var i = 0; i < AUTO_HISTORY_FILTER_BRANCH_COLOR_COUNT; i++)
            {
                var color = GetAutoHistoryFilterBranchColorAt(i);
                var score = usedColors.Contains(color) ? 100 : 0;

                if (conflictsByLogicalBranch.TryGetValue(logicalBranchKey, out var conflicts))
                {
                    foreach (var conflict in conflicts)
                    {
                        if (assignedByLogicalBranch.TryGetValue(conflict, out var conflictColor) && conflictColor == color)
                            score += 10000;
                    }
                }

                score += i;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestColor = color;
                }
            }

            return bestColor;
        }

        private static uint GetAutoHistoryFilterBranchColor(int index, uint fallback)
        {
            return GetAutoHistoryFilterBranchColorAt(index);
        }

        private static uint GetAutoHistoryFilterBranchColorAt(int index)
        {
            return (index % AUTO_HISTORY_FILTER_BRANCH_COLOR_COUNT) switch
            {
                0 => 0xFF10893E, // green
                1 => 0xFF0078D7, // blue
                2 => 0xFF744DA9, // purple
                3 => 0xFFF7630C, // orange
                4 => 0xFFC239B3, // magenta
                5 => 0xFF0099BC, // cyan
                6 => 0xFFD13438, // red
                7 => 0xFF00B294, // mint
                8 => 0xFF4F6BED, // indigo
                9 => 0xFFFFB900, // gold
                10 => 0xFF7FBA00, // lime
                11 => 0xFF8E562E, // brown
                12 => 0xFF00B7C3, // sky
                13 => 0xFF8764B8, // violet
                14 => 0xFFFF6F61, // coral
                _ => 0xFF008272, // teal
            };
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
            if (IsAutoFetching)
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
                {
                    if (!r.DisableAutoFetch)
                        remotes.Add(r.Name);
                }

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
                    var succ = true;
                    foreach (var remote in remotes)
                        succ &= await new Commands.Fetch(FullPath, remote, false, _settings.AutoFetchPrune).Use(log).RunAsync();

                    if (succ)
                        MarkFetched();
                }
                else
                {
                    var remote = GetPreferredRemoteName();
                    if (string.IsNullOrEmpty(remote))
                        return;

                    var succ = await new Commands.Fetch(FullPath, remote, false, _settings.AutoFetchPrune).Use(log).RunAsync();
                    if (succ)
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
            if (Preferences.Instance.DisableBackgroundTasks ||
                _settings is not { } ||
                (!_settings.EnableAutoFetch && !_settings.EnableAutoSyncAll))
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
                await Task.Delay(500, token);
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
        private SubmoduleCommitFlow _submoduleCommitFlow = null;
        private int _selectedViewIndex = 0;

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
        private List<Worktree> _worktrees = [];
        private List<Models.Tag> _tags = [];
        private object _visibleTags = null;
        private List<Models.Submodule> _submodules = [];
        private IReadOnlyDictionary<string, uint> _submoduleUpdateBadgeColors = new Dictionary<string, uint>(StringComparer.Ordinal);
        private object _visibleSubmodules = null;
        private bool _isSubmodulesLoading = false;
        private int _refreshSubmodulesVersion = 0;
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
        private readonly object _refreshCommitsLock = new();
        private readonly SemaphoreSlim _refreshCommitsGate = new(1, 1);
        private CancellationTokenSource _cancellationRefreshStashes = null;
        private CancellationTokenSource _quickFetchCancellation = null;

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
        private static readonly int MAX_RECURSIVE_SUBMODULE_UPDATE_PARALLELISM = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        private const int MAX_INCREMENTAL_HISTORY_METADATA_COMMITS = 256;
        private const int AUTO_HISTORY_FILTER_BRANCH_COLOR_COUNT = 16;
    }
}
