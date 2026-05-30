using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Preferences : ObservableObject
    {
        public const int MIN_HISTORY_COMMITS = 1000;
        public const int MAX_HISTORY_COMMITS = 50000;
        public const int DEFAULT_HISTORY_COMMITS = 10000;
        public const int MIN_RECURSIVE_SUBMODULE_DISPLAY_DEPTH = 1;
        public const int MAX_RECURSIVE_SUBMODULE_DISPLAY_DEPTH = 20;
        public const int DEFAULT_RECURSIVE_SUBMODULE_DISPLAY_DEPTH = 5;

        [JsonIgnore]
        public static Preferences Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                _instance = Load();
                _instance._isLoading = false;

                _instance.PrepareGit();
                _instance.PrepareShellOrTerminal();
                _instance.PrepareExternalDiffMergeTool();
                _instance.PrepareWorkspaces();

                return _instance;
            }
        }

        public string Locale
        {
            get => _locale;
            set
            {
                if (SetProperty(ref _locale, value) && !_isLoading)
                    App.SetLocale(value);
            }
        }

        public string Theme
        {
            get => _theme;
            set
            {
                if (SetProperty(ref _theme, value) && !_isLoading)
                {
                    App.SetTheme(_theme, _themeOverrides);
                    App.SetAccentColor(_mainAccentColor);
                }
            }
        }

        public string ThemeOverrides
        {
            get => _themeOverrides;
            set
            {
                if (SetProperty(ref _themeOverrides, value) && !_isLoading)
                {
                    App.SetTheme(_theme, value);
                    App.SetAccentColor(_mainAccentColor);
                }
            }
        }

        public string DefaultFontFamily
        {
            get => _defaultFontFamily;
            set
            {
                if (SetProperty(ref _defaultFontFamily, value) && !_isLoading)
                    App.SetFonts(value, _monospaceFontFamily);
            }
        }

        public string MonospaceFontFamily
        {
            get => _monospaceFontFamily;
            set
            {
                if (SetProperty(ref _monospaceFontFamily, value) && !_isLoading)
                    App.SetFonts(_defaultFontFamily, value);
            }
        }

        public bool UseSystemWindowFrame
        {
            get => Native.OS.UseSystemWindowFrame;
            set => Native.OS.UseSystemWindowFrame = value;
        }

        public double DefaultFontSize
        {
            get => _defaultFontSize;
            set
            {
                if (SetProperty(ref _defaultFontSize, value))
                {
                    OnPropertyChanged(nameof(HistoriesFontSize));
                    OnPropertyChanged(nameof(HistoriesRowHeight));
                }
            }
        }

        public double EditorFontSize
        {
            get => _editorFontSize;
            set => SetProperty(ref _editorFontSize, value);
        }

        public int EditorTabWidth
        {
            get => _editorTabWidth;
            set => SetProperty(ref _editorTabWidth, value);
        }

        public double Zoom
        {
            get => _zoom;
            set => SetProperty(ref _zoom, value);
        }

        public double HistoriesZoom
        {
            get => _historiesZoom;
            set
            {
                if (SetProperty(ref _historiesZoom, value))
                {
                    OnPropertyChanged(nameof(HistoriesFontSize));
                    OnPropertyChanged(nameof(HistoriesRowHeight));
                }
            }
        }

        public double HistoriesFontSize => _defaultFontSize * _historiesZoom;
        public double HistoriesRowHeight => Math.Max(16.0, HistoriesFontSize + 10.0);

        public uint MainAccentColor
        {
            get => _mainAccentColor;
            set
            {
                if (SetProperty(ref _mainAccentColor, value))
                {
                    OnPropertyChanged(nameof(MainAccentBrush));
                    if (!_isLoading)
                        App.SetAccentColor(value);
                }
            }
        }

        [JsonIgnore]
        public Avalonia.Media.IBrush MainAccentBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromUInt32(_mainAccentColor));

        public LayoutInfo Layout
        {
            get => _layout;
            set => SetProperty(ref _layout, value);
        }

        public bool ShowLocalChangesByDefault
        {
            get;
            set;
        } = false;

        public bool ShowChangesInCommitDetailByDefault
        {
            get;
            set;
        } = false;

        public string PresetBranchExactNames
        {
            get => _presetBranchExactNames;
            set => SetProperty(ref _presetBranchExactNames, value);
        }

        public string PresetBranchContainsPatterns
        {
            get => _presetBranchContainsPatterns;
            set => SetProperty(ref _presetBranchContainsPatterns, value);
        }

        public string PresetBranchExactNameColors
        {
            get => _presetBranchExactNameColors;
            set => SetProperty(ref _presetBranchExactNameColors, value);
        }

        public string AutoRevertPullConflictExtensions
        {
            get => _autoRevertPullConflictExtensions;
            set => SetProperty(ref _autoRevertPullConflictExtensions, value?.ReplaceLineEndings("\n") ?? string.Empty);
        }

        public List<string> RecursiveLocalChangesRecentHiddenExtensions
        {
            get => _recursiveLocalChangesRecentHiddenExtensions;
            set => SetProperty(ref _recursiveLocalChangesRecentHiddenExtensions, NormalizeFileExtensionList(value));
        }

        public int MaxHistoryCommits
        {
            get => _maxHistoryCommits;
            set => SetProperty(ref _maxHistoryCommits, Math.Clamp(value, MIN_HISTORY_COMMITS, MAX_HISTORY_COMMITS));
        }

        public int RecursiveSubmoduleDisplayDepth
        {
            get => _recursiveSubmoduleDisplayDepth;
            set => SetProperty(ref _recursiveSubmoduleDisplayDepth, Math.Clamp(value, MIN_RECURSIVE_SUBMODULE_DISPLAY_DEPTH, MAX_RECURSIVE_SUBMODULE_DISPLAY_DEPTH));
        }

        public int SubjectGuideLength
        {
            get => _subjectGuideLength;
            set => SetProperty(ref _subjectGuideLength, value);
        }

        public int DateTimeFormat
        {
            get => Models.DateTimeFormat.ActiveIndex;
            set
            {
                if (value != Models.DateTimeFormat.ActiveIndex &&
                    value >= 0 &&
                    value < Models.DateTimeFormat.Supported.Count)
                {
                    Models.DateTimeFormat.ActiveIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool Use24Hours
        {
            get => Models.DateTimeFormat.Use24Hours;
            set
            {
                if (value != Models.DateTimeFormat.Use24Hours)
                {
                    Models.DateTimeFormat.Use24Hours = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool UseFixedTabWidth
        {
            get => _useFixedTabWidth;
            set => SetProperty(ref _useFixedTabWidth, value);
        }

        public bool UseAutoHideScrollBars
        {
            get => _useAutoHideScrollBars;
            set => SetProperty(ref _useAutoHideScrollBars, value);
        }

        public bool UseGitHubStyleAvatar
        {
            get => _useGitHubStyleAvatar;
            set => SetProperty(ref _useGitHubStyleAvatar, value);
        }

        public bool Check4UpdatesOnStartup
        {
            get => _check4UpdatesOnStartup;
            set => SetProperty(ref _check4UpdatesOnStartup, value);
        }

        public bool ShowAuthorTimeInGraph
        {
            get => _showAuthorTimeInGraph;
            set => SetProperty(ref _showAuthorTimeInGraph, value);
        }

        public bool ShowChildren
        {
            get => _showChildren;
            set => SetProperty(ref _showChildren, value);
        }

        public bool DisableBackgroundTasks
        {
            get => _disableBackgroundTasks;
            set => SetProperty(ref _disableBackgroundTasks, value);
        }

        public bool RefreshSubmoduleStatusByDefault
        {
            get => _refreshSubmoduleStatusByDefault;
            set => SetProperty(ref _refreshSubmoduleStatusByDefault, value);
        }

        public string IgnoreUpdateTag
        {
            get => _ignoreUpdateTag;
            set => SetProperty(ref _ignoreUpdateTag, value);
        }

        public bool ShowTagsInGraph
        {
            get => _showTagsInGraph;
            set => SetProperty(ref _showTagsInGraph, value);
        }

        public bool CompactTrackingBranches
        {
            get => _compactTrackingBranches;
            set => SetProperty(ref _compactTrackingBranches, value);
        }

        public bool UseCompactBranchNamesInGraph
        {
            get => _useCompactBranchNamesInGraph;
            set => SetProperty(ref _useCompactBranchNamesInGraph, value);
        }

        public bool UseTwoColumnsLayoutInHistories
        {
            get => _useTwoColumnsLayoutInHistories;
            set => SetProperty(ref _useTwoColumnsLayoutInHistories, value);
        }

        public bool DisplayTimeAsPeriodInHistories
        {
            get => _displayTimeAsPeriodInHistories;
            set => SetProperty(ref _displayTimeAsPeriodInHistories, value);
        }

        public bool UseSideBySideDiff
        {
            get => _useSideBySideDiff;
            set => SetProperty(ref _useSideBySideDiff, value);
        }

        public bool UseSyntaxHighlighting
        {
            get => _useSyntaxHighlighting;
            set => SetProperty(ref _useSyntaxHighlighting, value);
        }

        public bool IgnoreCRAtEOLInDiff
        {
            get => Models.DiffOption.IgnoreCRAtEOL;
            set
            {
                if (Models.DiffOption.IgnoreCRAtEOL != value)
                {
                    Models.DiffOption.IgnoreCRAtEOL = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool UseStashAndReapplyByDefault
        {
            get;
            set;
        } = false;

        public bool EnableAutoFetch
        {
            get;
            set;
        } = false;

        public int AutoFetchInterval
        {
            get;
            set;
        } = 10;

        public bool IgnoreWhitespaceChangesInDiff
        {
            get => _ignoreWhitespaceChangesInDiff;
            set => SetProperty(ref _ignoreWhitespaceChangesInDiff, value);
        }

        public bool EnableDiffViewWordWrap
        {
            get => _enableDiffViewWordWrap;
            set => SetProperty(ref _enableDiffViewWordWrap, value);
        }

        public bool ShowHiddenSymbolsInDiffView
        {
            get => _showHiddenSymbolsInDiffView;
            set => SetProperty(ref _showHiddenSymbolsInDiffView, value);
        }

        public bool UseFullTextDiff
        {
            get => _useFullTextDiff;
            set => SetProperty(ref _useFullTextDiff, value);
        }

        public int LFSImageActiveIdx
        {
            get => _lfsImageActiveIdx;
            set => SetProperty(ref _lfsImageActiveIdx, value);
        }

        public int ImageDiffActiveIdx
        {
            get => _imageDiffActiveIdx;
            set => SetProperty(ref _imageDiffActiveIdx, value);
        }

        public bool EnableCompactFoldersInChangesTree
        {
            get => _enableCompactFoldersInChangesTree;
            set => SetProperty(ref _enableCompactFoldersInChangesTree, value);
        }

        public Models.ChangeViewMode UnstagedChangeViewMode
        {
            get => _unstagedChangeViewMode;
            set => SetProperty(ref _unstagedChangeViewMode, value);
        }

        public Models.ChangeViewMode StagedChangeViewMode
        {
            get => _stagedChangeViewMode;
            set => SetProperty(ref _stagedChangeViewMode, value);
        }

        public Models.ChangeViewMode CommitChangeViewMode
        {
            get => _commitChangeViewMode;
            set => SetProperty(ref _commitChangeViewMode, value);
        }

        public Models.ChangeViewMode StashChangeViewMode
        {
            get => _stashChangeViewMode;
            set => SetProperty(ref _stashChangeViewMode, value);
        }

        public string GitInstallPath
        {
            get => Native.OS.GitExecutable;
            set
            {
                if (Native.OS.GitExecutable != value)
                {
                    Native.OS.GitExecutable = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GitDefaultCloneDir
        {
            get => _gitDefaultCloneDir;
            set => SetProperty(ref _gitDefaultCloneDir, value);
        }

        public bool UseLibsecretInsteadOfGCM
        {
            get => Native.OS.CredentialHelper.Equals("libsecret", StringComparison.Ordinal);
            set
            {
                var helper = value ? "libsecret" : "manager";
                if (OperatingSystem.IsLinux() && !Native.OS.CredentialHelper.Equals(helper, StringComparison.Ordinal))
                {
                    Native.OS.CredentialHelper = helper;
                    OnPropertyChanged();
                }
            }
        }

        public int ShellOrTerminalType
        {
            get => _shellOrTerminalType;
            set
            {
                if (SetProperty(ref _shellOrTerminalType, value) && !_isLoading)
                {
                    if (value >= 0 && value < Models.ShellOrTerminal.Supported.Count)
                        Native.OS.SetShellOrTerminal(Models.ShellOrTerminal.Supported[value]);
                    else
                        Native.OS.SetShellOrTerminal(null);

                    OnPropertyChanged(nameof(ShellOrTerminalPath));
                    OnPropertyChanged(nameof(ShellOrTerminalArgs));
                }
            }
        }

        public string ShellOrTerminalPath
        {
            get => Native.OS.ShellOrTerminal;
            set
            {
                if (value != Native.OS.ShellOrTerminal)
                {
                    Native.OS.ShellOrTerminal = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ShellOrTerminalArgs
        {
            get => Native.OS.ShellOrTerminalArgs;
            set
            {
                if (value != Native.OS.ShellOrTerminalArgs)
                {
                    Native.OS.ShellOrTerminalArgs = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ExternalMergeToolType
        {
            get => Native.OS.ExternalMergerType;
            set
            {
                if (Native.OS.ExternalMergerType != value)
                {
                    Native.OS.ExternalMergerType = value;
                    OnPropertyChanged();

                    if (!_isLoading)
                    {
                        Native.OS.AutoSelectExternalMergeToolExecFile();
                        OnPropertyChanged(nameof(ExternalMergeToolPath));
                        OnPropertyChanged(nameof(ExternalMergeToolDiffArgs));
                        OnPropertyChanged(nameof(ExternalMergeToolMergeArgs));
                    }
                }
            }
        }

        public string ExternalMergeToolPath
        {
            get => Native.OS.ExternalMergerExecFile;
            set
            {
                if (!Native.OS.ExternalMergerExecFile.Equals(value, StringComparison.Ordinal))
                {
                    Native.OS.ExternalMergerExecFile = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExternalMergeToolDiffArgs
        {
            get => Native.OS.ExternalDiffArgs;
            set
            {
                if (!Native.OS.ExternalDiffArgs.Equals(value, StringComparison.Ordinal))
                {
                    Native.OS.ExternalDiffArgs = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExternalMergeToolMergeArgs
        {
            get => Native.OS.ExternalMergeArgs;
            set
            {
                if (!Native.OS.ExternalMergeArgs.Equals(value, StringComparison.Ordinal))
                {
                    Native.OS.ExternalMergeArgs = value;
                    OnPropertyChanged();
                }
            }
        }

        public uint StatisticsSampleColor
        {
            get => _statisticsSampleColor;
            set => SetProperty(ref _statisticsSampleColor, value);
        }

        public List<RepositoryNode> RepositoryNodes
        {
            get;
            set;
        } = [];

        public List<Workspace> Workspaces
        {
            get;
            set;
        } = [];

        public AvaloniaList<Models.CustomAction> CustomActions
        {
            get;
            set;
        } = [];

        public AvaloniaList<AI.Service> OpenAIServices
        {
            get;
            set;
        } = [];

        public double LastCheckUpdateTime
        {
            get => _lastCheckUpdateTime;
            set => SetProperty(ref _lastCheckUpdateTime, value);
        }

        public void SetCanModify()
        {
            _isReadonly = false;
        }

        public bool IsGitConfigured()
        {
            var path = GitInstallPath;
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public bool ShouldCheck4UpdateOnStartup()
        {
            if (!_check4UpdatesOnStartup)
                return false;

            var lastCheck = DateTime.UnixEpoch.AddSeconds(LastCheckUpdateTime).ToLocalTime();
            var now = DateTime.Now;

            if (lastCheck.Year == now.Year && lastCheck.Month == now.Month && lastCheck.Day == now.Day)
                return false;

            LastCheckUpdateTime = now.Subtract(DateTime.UnixEpoch.ToLocalTime()).TotalSeconds;
            return true;
        }

        public Workspace GetActiveWorkspace()
        {
            foreach (var w in Workspaces)
            {
                if (w.IsActive)
                    return w;
            }

            var first = Workspaces[0];
            first.IsActive = true;
            return first;
        }

        public void AddNode(RepositoryNode node, RepositoryNode to, bool save)
        {
            var collection = to == null ? RepositoryNodes : to.SubNodes;
            collection.Add(node);
            SortNodes(collection);

            if (save)
                Save();
        }

        public void SortNodes(List<RepositoryNode> collection)
        {
            collection?.Sort((l, r) =>
            {
                if (l.IsRepository != r.IsRepository)
                    return l.IsRepository ? 1 : -1;

                return Models.NumericSort.Compare(l.Name, r.Name);
            });
        }

        public RepositoryNode FindNode(string id)
        {
            return FindNodeRecursive(id, RepositoryNodes);
        }

        public RepositoryNode FindOrAddNodeByRepositoryPath(string repo, RepositoryNode parent, bool shouldMoveNode, bool save = true)
        {
            var normalized = repo.Replace('\\', '/').TrimEnd('/');

            var node = FindNodeRecursive(normalized, RepositoryNodes);
            if (node == null)
            {
                node = new RepositoryNode()
                {
                    Id = normalized,
                    Name = Path.GetFileName(normalized),
                    Bookmark = FindUnusedBookmarkColor(),
                    IsRepository = true,
                };

                AddNode(node, parent, save);
            }
            else if (shouldMoveNode)
            {
                MoveNode(node, parent, save);
            }

            return node;
        }

        public int FindUnusedBookmarkColor()
        {
            var used = new HashSet<int>();
            CollectUsedBookmarkColors(RepositoryNodes, used);

            for (var i = 1; i < Models.Bookmarks.Brushes.Length; i++)
            {
                if (!used.Contains(i))
                    return i;
            }

            return Models.Bookmarks.Brushes.Length > 1 ? 1 : 0;
        }

        public void MoveNode(RepositoryNode node, RepositoryNode to, bool save)
        {
            if (to == null && RepositoryNodes.Contains(node))
                return;
            if (to != null && to.SubNodes.Contains(node))
                return;

            RemoveNode(node, false);
            AddNode(node, to, false);

            if (save)
                Save();
        }

        public void RemoveNode(RepositoryNode node, bool save)
        {
            RemoveNodeRecursive(node, RepositoryNodes);

            if (save)
                Save();
        }

        public void SortByRenamedNode(RepositoryNode node)
        {
            var container = FindNodeContainer(node, RepositoryNodes);
            SortNodes(container);
            Save();
        }

        public void AutoRemoveInvalidNode()
        {
            RemoveInvalidRepositoriesRecursive(RepositoryNodes);
        }

        public void UpdateAvailableAIModels()
        {
            Task.Run(() =>
            {
                foreach (var service in OpenAIServices)
                {
                    try
                    {
                        service.FetchAvailableModels();
                    }
                    catch
                    {
                        // Ignore errors.
                    }
                }
            });
        }

        public void Save()
        {
            if (_isLoading || _isReadonly)
                return;

            var tmpfile = Path.Combine(Native.OS.DataDir, "preference_tmp.json");
            var content = JsonSerializer.Serialize(this, JsonCodeGen.Default.Preferences);
            File.WriteAllText(tmpfile, content);

            var finalFile = Path.Combine(Native.OS.DataDir, "preference.json");
            File.Move(tmpfile, finalFile, true);
        }

        public HashSet<string> GetPresetBranchExactNameSet()
        {
            return new HashSet<string>(ParsePresetBranchRules(_presetBranchExactNames), StringComparer.Ordinal);
        }

        public List<string> GetPresetBranchExactNameList()
        {
            return ParsePresetBranchRules(_presetBranchExactNames);
        }

        public List<string> GetPresetBranchContainsRuleList()
        {
            return ParsePresetBranchRules(_presetBranchContainsPatterns);
        }

        public Dictionary<string, uint> GetPresetBranchExactNameColorMap()
        {
            var exactNames = GetPresetBranchExactNameSet();
            var colors = ParsePresetBranchRuleColors(_presetBranchExactNameColors);
            if (colors.Count == 0)
                return colors;

            var filtered = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var kv in colors)
            {
                if (exactNames.Contains(kv.Key))
                    filtered[kv.Key] = kv.Value;
            }

            return filtered;
        }

        public uint GetPresetBranchExactNameColor(string exactName)
        {
            if (string.IsNullOrEmpty(exactName))
                return PRESET_BRANCH_EXACT_DEFAULT_COLOR;

            var colors = GetPresetBranchExactNameColorMap();
            return colors.GetValueOrDefault(exactName, PRESET_BRANCH_EXACT_DEFAULT_COLOR);
        }

        public List<string> GetAutoRevertPullConflictExtensions()
        {
            var parsed = ParsePresetBranchRules(_autoRevertPullConflictExtensions);
            var outs = new List<string>();
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in parsed)
            {
                var normalized = NormalizeFileExtension(rule);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                if (dedupe.Add(normalized))
                    outs.Add(normalized);
            }

            return outs;
        }

        public bool ShouldAutoRevertPullConflictFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var ext = NormalizeFileExtension(Path.GetExtension(path));
            if (string.IsNullOrEmpty(ext))
                return false;

            var configured = GetAutoRevertPullConflictExtensions();
            return configured.Exists(one => one.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRecursiveLocalChangesRecentHiddenExtensions()
        {
            return NormalizeFileExtensionList(_recursiveLocalChangesRecentHiddenExtensions);
        }

        public bool RecordRecursiveLocalChangesHiddenExtensions(IEnumerable<string> extensions)
        {
            var next = new List<string>();
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (extensions != null)
            {
                foreach (var raw in extensions)
                {
                    var normalized = NormalizeFileExtension(raw);
                    if (!string.IsNullOrEmpty(normalized) && dedupe.Add(normalized))
                        next.Add(normalized);
                }
            }

            foreach (var existing in GetRecursiveLocalChangesRecentHiddenExtensions())
            {
                if (next.Count >= 10)
                    break;

                if (dedupe.Add(existing))
                    next.Add(existing);
            }

            if (_recursiveLocalChangesRecentHiddenExtensions.Count == next.Count)
            {
                var same = true;
                for (var i = 0; i < next.Count; i++)
                {
                    if (!_recursiveLocalChangesRecentHiddenExtensions[i].Equals(next[i], StringComparison.OrdinalIgnoreCase))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                    return false;
            }

            _recursiveLocalChangesRecentHiddenExtensions = next;
            OnPropertyChanged(nameof(RecursiveLocalChangesRecentHiddenExtensions));
            return true;
        }

        public bool RemoveRecursiveLocalChangesHiddenExtension(string extension)
        {
            var normalized = NormalizeFileExtension(extension);
            if (string.IsNullOrEmpty(normalized))
                return false;

            var next = GetRecursiveLocalChangesRecentHiddenExtensions();
            var removed = next.RemoveAll(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
                return false;

            _recursiveLocalChangesRecentHiddenExtensions = next;
            OnPropertyChanged(nameof(RecursiveLocalChangesRecentHiddenExtensions));
            return true;
        }

        public bool SetPresetBranchExactNameColor(string exactName, uint color)
        {
            if (string.IsNullOrWhiteSpace(exactName))
                return false;

            var colors = ParsePresetBranchRuleColors(_presetBranchExactNameColors);
            if (color == PRESET_BRANCH_EXACT_DEFAULT_COLOR)
                colors.Remove(exactName);
            else
                colors[exactName] = color;

            var next = SerializePresetBranchRuleColors(colors);
            if (next.Equals(_presetBranchExactNameColors, StringComparison.Ordinal))
                return false;

            _presetBranchExactNameColors = next;
            OnPropertyChanged(nameof(PresetBranchExactNameColors));
            return true;
        }

        private static Preferences Load()
        {
            var path = Path.Combine(Native.OS.DataDir, "preference.json");
            if (!File.Exists(path))
                return new Preferences();

            try
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.Preferences) ?? new Preferences();
                loaded.Normalize();
                return loaded;
            }
            catch
            {
                return new Preferences();
            }
        }

        private void PrepareGit()
        {
            var path = Native.OS.GitExecutable;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                GitInstallPath = Native.OS.FindGitExecutable();
        }

        private void PrepareShellOrTerminal()
        {
            if (_shellOrTerminalType >= 0)
                return;

            for (int i = 0; i < Models.ShellOrTerminal.Supported.Count; i++)
            {
                var shell = Models.ShellOrTerminal.Supported[i];
                if (Native.OS.TestShellOrTerminal(shell))
                {
                    ShellOrTerminalType = i;
                    break;
                }
            }
        }

        private void PrepareExternalDiffMergeTool()
        {
            var mergerType = Native.OS.ExternalMergerType;
            if (mergerType > 0 && mergerType < Models.ExternalMerger.Supported.Count)
            {
                var merger = Models.ExternalMerger.Supported[mergerType];
                if (string.IsNullOrEmpty(Native.OS.ExternalDiffArgs))
                    Native.OS.ExternalDiffArgs = merger.DiffCmd;
                if (string.IsNullOrEmpty(Native.OS.ExternalMergeArgs))
                    Native.OS.ExternalMergeArgs = merger.MergeCmd;
            }
        }

        private void PrepareWorkspaces()
        {
            if (Workspaces.Count == 0)
            {
                Workspaces.Add(new Workspace() { Name = "Default" });
                return;
            }

            foreach (var workspace in Workspaces)
            {
                if (!workspace.RestoreOnStartup)
                {
                    workspace.Repositories.Clear();
                    workspace.ActiveIdx = 0;
                }
            }
        }

        private void Normalize()
        {
            _maxHistoryCommits = Math.Clamp(_maxHistoryCommits, MIN_HISTORY_COMMITS, MAX_HISTORY_COMMITS);
            _recursiveSubmoduleDisplayDepth = Math.Clamp(_recursiveSubmoduleDisplayDepth, MIN_RECURSIVE_SUBMODULE_DISPLAY_DEPTH, MAX_RECURSIVE_SUBMODULE_DISPLAY_DEPTH);
            _autoRevertPullConflictExtensions ??= DEFAULT_AUTO_REVERT_PULL_CONFLICT_EXTENSIONS;
            _recursiveLocalChangesRecentHiddenExtensions = NormalizeFileExtensionList(_recursiveLocalChangesRecentHiddenExtensions);
        }

        private static List<string> ParsePresetBranchRules(string raw)
        {
            var parsed = new List<string>();
            if (string.IsNullOrEmpty(raw))
                return parsed;

            var dedupe = new HashSet<string>(StringComparer.Ordinal);
            var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var rule = line.Trim();
                if (string.IsNullOrEmpty(rule))
                    continue;

                if (dedupe.Add(rule))
                    parsed.Add(rule);
            }

            return parsed;
        }

        private static Dictionary<string, uint> ParsePresetBranchRuleColors(string raw)
        {
            var parsed = new Dictionary<string, uint>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw))
                return parsed;

            var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var sepIdx = line.LastIndexOf('\t');
                if (sepIdx <= 0 || sepIdx >= line.Length - 1)
                    continue;

                var name = line.Substring(0, sepIdx).Trim();
                var colorText = line.Substring(sepIdx + 1).Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(colorText))
                    continue;

                if (colorText.StartsWith("#", StringComparison.Ordinal))
                    colorText = colorText.Substring(1);
                else if (colorText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    colorText = colorText.Substring(2);

                if (uint.TryParse(colorText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
                    parsed[name] = color;
            }

            return parsed;
        }

        private static string NormalizeFileExtension(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var ext = raw.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".", StringComparison.Ordinal))
                ext = "." + ext;

            return ext.Length > 1 ? ext : string.Empty;
        }

        private static List<string> NormalizeFileExtensionList(IEnumerable<string> raw)
        {
            var outs = new List<string>();
            if (raw == null)
                return outs;

            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var one in raw)
            {
                var normalized = NormalizeFileExtension(one);
                if (!string.IsNullOrEmpty(normalized) && dedupe.Add(normalized))
                    outs.Add(normalized);

                if (outs.Count >= 10)
                    break;
            }

            return outs;
        }

        private static string SerializePresetBranchRuleColors(Dictionary<string, uint> colors)
        {
            if (colors == null || colors.Count == 0)
                return string.Empty;

            var names = new List<string>(colors.Keys);
            names.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            foreach (var name in names)
            {
                if (builder.Length > 0)
                    builder.Append('\n');

                builder.Append(name);
                builder.Append('\t');
                builder.Append(colors[name].ToString("X8", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private RepositoryNode FindNodeRecursive(string id, List<RepositoryNode> collection)
        {
            foreach (var node in collection)
            {
                if (node.Id == id)
                    return node;

                var sub = FindNodeRecursive(id, node.SubNodes);
                if (sub != null)
                    return sub;
            }

            return null;
        }

        private List<RepositoryNode> FindNodeContainer(RepositoryNode node, List<RepositoryNode> collection)
        {
            foreach (var sub in collection)
            {
                if (node == sub)
                    return collection;

                var subCollection = FindNodeContainer(node, sub.SubNodes);
                if (subCollection != null)
                    return subCollection;
            }

            return null;
        }

        private void CollectUsedBookmarkColors(List<RepositoryNode> nodes, HashSet<int> used)
        {
            if (nodes == null || used == null)
                return;

            foreach (var node in nodes)
            {
                if (node.IsRepository &&
                    node.Bookmark > 0 &&
                    node.Bookmark < Models.Bookmarks.Brushes.Length)
                {
                    used.Add(node.Bookmark);
                }

                CollectUsedBookmarkColors(node.SubNodes, used);
            }
        }

        private bool RemoveNodeRecursive(RepositoryNode node, List<RepositoryNode> collection)
        {
            if (collection.Contains(node))
            {
                collection.Remove(node);
                return true;
            }

            foreach (var one in collection)
            {
                if (RemoveNodeRecursive(node, one.SubNodes))
                    return true;
            }

            return false;
        }

        private bool RemoveInvalidRepositoriesRecursive(List<RepositoryNode> collection)
        {
            bool changed = false;

            for (int i = collection.Count - 1; i >= 0; i--)
            {
                var node = collection[i];
                if (node.IsInvalid)
                {
                    collection.RemoveAt(i);
                    changed = true;
                }
                else if (!node.IsRepository)
                {
                    changed |= RemoveInvalidRepositoriesRecursive(node.SubNodes);
                }
            }

            return changed;
        }

        private static Preferences _instance = null;

        private bool _isLoading = true;
        private bool _isReadonly = true;
        private string _locale = "en_US";
        private string _theme = "Default";
        private string _themeOverrides = string.Empty;
        private string _defaultFontFamily = string.Empty;
        private string _monospaceFontFamily = string.Empty;
        private double _defaultFontSize = 13;
        private double _editorFontSize = 13;
        private int _editorTabWidth = 4;
        private double _zoom = 1.0;
        private double _historiesZoom = 1.0;
        private uint _mainAccentColor = 0xFF0078D7;
        private LayoutInfo _layout = new();

        private int _maxHistoryCommits = DEFAULT_HISTORY_COMMITS;
        private int _recursiveSubmoduleDisplayDepth = DEFAULT_RECURSIVE_SUBMODULE_DISPLAY_DEPTH;
        private int _subjectGuideLength = 50;
        private bool _useFixedTabWidth = true;
        private bool _useAutoHideScrollBars = true;
        private bool _useGitHubStyleAvatar = true;
        private bool _showAuthorTimeInGraph = false;
        private bool _showChildren = false;
        private bool _disableBackgroundTasks = false;
        private bool _refreshSubmoduleStatusByDefault = false;
        private string _presetBranchExactNames = string.Empty;
        private string _presetBranchContainsPatterns = string.Empty;
        private string _presetBranchExactNameColors = string.Empty;
        private string _autoRevertPullConflictExtensions = DEFAULT_AUTO_REVERT_PULL_CONFLICT_EXTENSIONS;
        private List<string> _recursiveLocalChangesRecentHiddenExtensions = [];
        private bool _useCompactBranchNamesInGraph = true;

        private bool _check4UpdatesOnStartup = true;
        private double _lastCheckUpdateTime = 0;
        private string _ignoreUpdateTag = string.Empty;

        private bool _showTagsInGraph = true;
        private bool _compactTrackingBranches = false;
        private bool _useTwoColumnsLayoutInHistories = false;
        private bool _displayTimeAsPeriodInHistories = false;
        private bool _useSideBySideDiff = false;
        private bool _ignoreWhitespaceChangesInDiff = false;
        private bool _useSyntaxHighlighting = false;
        private bool _enableDiffViewWordWrap = false;
        private bool _showHiddenSymbolsInDiffView = false;
        private bool _useFullTextDiff = false;
        private int _lfsImageActiveIdx = 0;
        private int _imageDiffActiveIdx = 0;
        private bool _enableCompactFoldersInChangesTree = false;

        private Models.ChangeViewMode _unstagedChangeViewMode = Models.ChangeViewMode.List;
        private Models.ChangeViewMode _stagedChangeViewMode = Models.ChangeViewMode.List;
        private Models.ChangeViewMode _commitChangeViewMode = Models.ChangeViewMode.List;
        private Models.ChangeViewMode _stashChangeViewMode = Models.ChangeViewMode.List;

        private string _gitDefaultCloneDir = string.Empty;
        private int _shellOrTerminalType = -1;
        private uint _statisticsSampleColor = 0xFF00FF00;

        public const uint PRESET_BRANCH_EXACT_DEFAULT_COLOR = 0xFF10893E;
        public const string DEFAULT_AUTO_REVERT_PULL_CONFLICT_EXTENSIONS =
            ".accdb\n" +
            ".accde\n" +
            ".accdr\n" +
            ".accdt\n" +
            ".doc\n" +
            ".docm\n" +
            ".docx\n" +
            ".dot\n" +
            ".dotm\n" +
            ".dotx\n" +
            ".mda\n" +
            ".mdb\n" +
            ".mde\n" +
            ".mdw\n" +
            ".mpp\n" +
            ".mpt\n" +
            ".mpx\n" +
            ".msg\n" +
            ".one\n" +
            ".oft\n" +
            ".ost\n" +
            ".ppam\n" +
            ".pot\n" +
            ".potm\n" +
            ".potx\n" +
            ".pps\n" +
            ".ppsm\n" +
            ".ppsx\n" +
            ".ppt\n" +
            ".pptm\n" +
            ".pptx\n" +
            ".pst\n" +
            ".pub\n" +
            ".sldm\n" +
            ".sldx\n" +
            ".thmx\n" +
            ".vsd\n" +
            ".vsdm\n" +
            ".vsdx\n" +
            ".vss\n" +
            ".vssm\n" +
            ".vssx\n" +
            ".vst\n" +
            ".vstm\n" +
            ".vstx\n" +
            ".wbk\n" +
            ".xla\n" +
            ".xlam\n" +
            ".xls\n" +
            ".xlsb\n" +
            ".xlsm\n" +
            ".xlsx\n" +
            ".xlt\n" +
            ".xltm\n" +
            ".xltx";
    }
}
