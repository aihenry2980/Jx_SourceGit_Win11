using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class RepositoryConfigure : ObservableObject
    {
        public string UserName
        {
            get;
            set;
        }

        public string UserEmail
        {
            get;
            set;
        }

        public List<string> Remotes
        {
            get;
        }

        public string DefaultRemote
        {
            get => _repo.Settings.DefaultRemote;
            set
            {
                if (_repo.Settings.DefaultRemote != value)
                {
                    _repo.Settings.DefaultRemote = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RebaseBaseBranch
        {
            get => _repo.Settings.RebaseBaseBranch;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_repo.Settings.RebaseBaseBranch != normalized)
                {
                    _repo.Settings.RebaseBaseBranch = normalized;
                    OnPropertyChanged();
                }
            }
        }

        public int PreferredMergeMode
        {
            get => _repo.Settings.PreferredMergeMode;
            set
            {
                if (_repo.Settings.PreferredMergeMode != value)
                {
                    _repo.Settings.PreferredMergeMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool GPGCommitSigningEnabled
        {
            get;
            set;
        }

        public bool GPGTagSigningEnabled
        {
            get;
            set;
        }

        public string GPGUserSigningKey
        {
            get;
            set;
        }

        public string HttpProxy
        {
            get => _httpProxy;
            set => SetProperty(ref _httpProxy, value);
        }

        public string ConventionalTypesOverride
        {
            get => _repo.Settings.ConventionalTypesOverride;
            set
            {
                if (_repo.Settings.ConventionalTypesOverride != value)
                {
                    _repo.Settings.ConventionalTypesOverride = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnablePruneOnFetch
        {
            get;
            set;
        }

        public bool AskBeforeAutoUpdatingSubmodules
        {
            get => _repo.Settings.AskBeforeAutoUpdatingSubmodules;
            set => _repo.Settings.AskBeforeAutoUpdatingSubmodules = value;
        }

        public bool EnableRecursiveWhenAutoUpdatingSubmodules
        {
            get => _repo.Settings.EnableRecursiveWhenAutoUpdatingSubmodules;
            set => _repo.Settings.EnableRecursiveWhenAutoUpdatingSubmodules = value;
        }

        public bool EnableAutoFetch
        {
            get => _repo.Settings.EnableAutoFetch;
            set
            {
                if (_repo.Settings.EnableAutoFetch == value)
                    return;

                _repo.Settings.EnableAutoFetch = value;
                if (value)
                    _repo.Settings.EnableAutoSyncAll = false;

                OnPropertyChanged();
                OnPropertyChanged(nameof(EnableAutoSyncAll));
                OnPropertyChanged(nameof(AutoBackgroundOperationEnabled));
            }
        }

        public bool EnableAutoSyncAll
        {
            get => _repo.Settings.EnableAutoSyncAll;
            set
            {
                if (_repo.Settings.EnableAutoSyncAll == value)
                    return;

                _repo.Settings.EnableAutoSyncAll = value;
                if (value)
                    _repo.Settings.EnableAutoFetch = false;

                OnPropertyChanged();
                OnPropertyChanged(nameof(EnableAutoFetch));
                OnPropertyChanged(nameof(AutoBackgroundOperationEnabled));
            }
        }

        public int? AutoFetchInterval
        {
            get => _repo.Settings.AutoFetchInterval;
            set
            {
                if (value is null || value < 1)
                    return;

                var interval = (int)value;
                if (_repo.Settings.AutoFetchInterval != interval)
                    _repo.Settings.AutoFetchInterval = interval;
            }
        }

        public bool AutoBackgroundOperationEnabled => EnableAutoFetch || EnableAutoSyncAll;

        public bool AutoFetchPrune
        {
            get => _repo.Settings.AutoFetchPrune;
            set => _repo.Settings.AutoFetchPrune = value;
        }

        public int? SuccessfulOperationAutoCloseSeconds
        {
            get => _repo.Settings.SuccessfulOperationAutoCloseSeconds;
            set
            {
                if (value is null || value < 1)
                    return;

                var seconds = (int)value;
                if (_repo.Settings.SuccessfulOperationAutoCloseSeconds != seconds)
                    _repo.Settings.SuccessfulOperationAutoCloseSeconds = seconds;
            }
        }

        public AvaloniaList<Models.CommitTemplate> CommitTemplates
        {
            get => _repo.Settings.CommitTemplates;
        }

        public Models.CommitTemplate SelectedCommitTemplate
        {
            get => _selectedCommitTemplate;
            set => SetProperty(ref _selectedCommitTemplate, value);
        }

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get;
        } = [];

        public Models.IssueTracker SelectedIssueTracker
        {
            get => _selectedIssueTracker;
            set => SetProperty(ref _selectedIssueTracker, value);
        }

        public List<string> AvailableOpenAIServices
        {
            get;
            private set;
        }

        public string PreferredOpenAIService
        {
            get => _repo.Settings.PreferredOpenAIService;
            set => _repo.Settings.PreferredOpenAIService = value;
        }

        public AvaloniaList<Models.CustomAction> CustomActions
        {
            get => _repo.Settings.CustomActions;
        }

        public string RepoLocalIgnoreRules
        {
            get => _repoLocalIgnoreRules;
            set => SetProperty(ref _repoLocalIgnoreRules, value);
        }

        public Models.CustomAction SelectedCustomAction
        {
            get => _selectedCustomAction;
            set => SetProperty(ref _selectedCustomAction, value);
        }

        public RepositoryConfigure(Repository repo)
        {
            _repo = repo;
            _originalRebaseBaseBranch = _repo.Settings.RebaseBaseBranch;

            Remotes = new List<string>();
            foreach (var remote in _repo.Remotes)
                Remotes.Add(remote.Name);

            AvailableOpenAIServices = new List<string>() { "---" };
            foreach (var service in Preferences.Instance.OpenAIServices)
                AvailableOpenAIServices.Add(service.Name);

            if (!AvailableOpenAIServices.Contains(PreferredOpenAIService))
                PreferredOpenAIService = "---";

            _cached = new Commands.Config(repo.FullPath).ReadAll();
            if (_cached.TryGetValue("user.name", out var name))
                UserName = name;
            if (_cached.TryGetValue("user.email", out var email))
                UserEmail = email;
            if (_cached.TryGetValue("commit.gpgsign", out var gpgCommitSign))
                GPGCommitSigningEnabled = gpgCommitSign == "true";
            if (_cached.TryGetValue("tag.gpgsign", out var gpgTagSign))
                GPGTagSigningEnabled = gpgTagSign == "true";
            if (_cached.TryGetValue("user.signingkey", out var signingKey))
                GPGUserSigningKey = signingKey;
            if (_cached.TryGetValue("http.proxy", out var proxy))
                HttpProxy = proxy;
            if (_cached.TryGetValue("fetch.prune", out var prune))
                EnablePruneOnFetch = (prune == "true");

            foreach (var rule in _repo.IssueTrackers)
            {
                IssueTrackers.Add(new()
                {
                    IsShared = rule.IsShared,
                    Name = rule.Name,
                    RegexString = rule.RegexString,
                    URLTemplate = rule.URLTemplate,
                });
            }

            _repoLocalIgnoreFile = Path.Combine(_repo.GitDir, "info", "exclude");
            if (File.Exists(_repoLocalIgnoreFile))
            {
                try
                {
                    _repoLocalIgnoreRules = File.ReadAllText(_repoLocalIgnoreFile).ReplaceLineEndings("\n");
                    _repoLocalIgnoreRulesOrg = _repoLocalIgnoreRules;
                }
                catch
                {
                    _repoLocalIgnoreRules = string.Empty;
                    _repoLocalIgnoreRulesOrg = string.Empty;
                }
            }
        }

        public void ClearHttpProxy()
        {
            HttpProxy = string.Empty;
        }

        public void AddCommitTemplate()
        {
            var template = new Models.CommitTemplate() { Name = "New Template" };
            _repo.Settings.CommitTemplates.Add(template);
            SelectedCommitTemplate = template;
        }

        public void RemoveSelectedCommitTemplate()
        {
            if (_selectedCommitTemplate != null)
                _repo.Settings.CommitTemplates.Remove(_selectedCommitTemplate);
            SelectedCommitTemplate = null;
        }

        public List<string> GetRemoteVisitUrls()
        {
            var outs = new List<string>();
            foreach (var remote in _repo.Remotes)
            {
                if (remote.TryGetVisitURL(out var url))
                    outs.Add(url);
            }
            return outs;
        }

        public void AddIssueTracker(string name, string regex, string url)
        {
            var rule = new Models.IssueTracker()
            {
                IsShared = false,
                Name = name,
                RegexString = regex,
                URLTemplate = url,
            };

            IssueTrackers.Add(rule);
            SelectedIssueTracker = rule;
        }

        public void RemoveIssueTracker()
        {
            if (_selectedIssueTracker is { } rule)
                IssueTrackers.Remove(rule);

            SelectedIssueTracker = null;
        }

        public void AddNewCustomAction()
        {
            SelectedCustomAction = _repo.Settings.AddNewCustomAction();
        }

        public void RemoveSelectedCustomAction()
        {
            _repo.Settings.RemoveCustomAction(_selectedCustomAction);
            SelectedCustomAction = null;
        }

        public void MoveSelectedCustomActionUp()
        {
            if (_selectedCustomAction != null)
                _repo.Settings.MoveCustomActionUp(_selectedCustomAction);
        }

        public void MoveSelectedCustomActionDown()
        {
            if (_selectedCustomAction != null)
                _repo.Settings.MoveCustomActionDown(_selectedCustomAction);
        }

        public async Task SaveAsync()
        {
            var rebaseBaseBranchChanged = !string.Equals(
                _originalRebaseBaseBranch,
                _repo.Settings.RebaseBaseBranch,
                StringComparison.Ordinal);
            _repo.Settings.Save();

            await SetIfChangedAsync("user.name", UserName, "");
            await SetIfChangedAsync("user.email", UserEmail, "");
            await SetIfChangedAsync("commit.gpgsign", GPGCommitSigningEnabled ? "true" : "false", "false");
            await SetIfChangedAsync("tag.gpgsign", GPGTagSigningEnabled ? "true" : "false", "false");
            await SetIfChangedAsync("user.signingkey", GPGUserSigningKey, "");
            await SetIfChangedAsync("http.proxy", HttpProxy, "");
            await SetIfChangedAsync("fetch.prune", EnablePruneOnFetch ? "true" : "false", "false");

            await ApplyIssueTrackerChangesAsync();
            await ApplyRepoLocalIgnoreRulesAsync();
            await _repo.Settings.SaveAsync();
            _repo.EnsureAutoFetchTimerState();
            if (rebaseBaseBranchChanged)
            {
                _repo.RefreshBranchSidebarByCurrentFilters();
                _repo.RefreshCommits();
            }
        }

        public async Task ApplyRepoLocalIgnoreRulesAsync()
        {
            await SaveRepoLocalIgnoreRulesAsync();
            await ApplyAssumeUnchangedForTrackedLocalIgnoreRulesAsync();
            _repo.RefreshWorkingCopyChanges(true);
        }

        private async Task SetIfChangedAsync(string key, string value, string defValue)
        {
            if (value != _cached.GetValueOrDefault(key, defValue))
                await new Commands.Config(_repo.FullPath).SetAsync(key, value);
        }

        private async Task ApplyIssueTrackerChangesAsync()
        {
            var changed = false;
            var oldRules = new Dictionary<string, Models.IssueTracker>();
            foreach (var rule in _repo.IssueTrackers)
                oldRules.Add(rule.Name, rule);

            foreach (var rule in IssueTrackers)
            {
                if (oldRules.TryGetValue(rule.Name, out var old))
                {
                    if (old.IsShared != rule.IsShared)
                    {
                        changed = true;
                        await new Commands.IssueTracker(_repo.FullPath, old.IsShared).RemoveAsync(old.Name);
                        await new Commands.IssueTracker(_repo.FullPath, rule.IsShared).AddAsync(rule);
                    }
                    else
                    {
                        if (!old.RegexString.Equals(rule.RegexString, StringComparison.Ordinal))
                        {
                            changed = true;
                            await new Commands.IssueTracker(_repo.FullPath, old.IsShared).UpdateRegexAsync(rule);
                        }

                        if (!old.URLTemplate.Equals(rule.URLTemplate, StringComparison.Ordinal))
                        {
                            changed = true;
                            await new Commands.IssueTracker(_repo.FullPath, old.IsShared).UpdateURLTemplateAsync(rule);
                        }
                    }

                    oldRules.Remove(rule.Name);
                }
                else
                {
                    changed = true;
                    await new Commands.IssueTracker(_repo.FullPath, rule.IsShared).AddAsync(rule);
                }
            }

            if (oldRules.Count > 0)
            {
                changed = true;

                foreach (var kv in oldRules)
                    await new Commands.IssueTracker(_repo.FullPath, kv.Value.IsShared).RemoveAsync(kv.Key);
            }

            if (changed)
            {
                _repo.IssueTrackers.Clear();
                _repo.IssueTrackers.AddRange(IssueTrackers);
            }
        }

        private async Task<bool> SaveRepoLocalIgnoreRulesAsync()
        {
            var next = (_repoLocalIgnoreRules ?? string.Empty).ReplaceLineEndings("\n");
            if (next.Equals(_repoLocalIgnoreRulesOrg, StringComparison.Ordinal))
                return false;

            try
            {
                var dir = Path.GetDirectoryName(_repoLocalIgnoreFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(_repoLocalIgnoreFile, next);
                _repoLocalIgnoreRulesOrg = next;
                return true;
            }
            catch
            {
                // Ignore save errors
                return false;
            }
        }

        private async Task ApplyAssumeUnchangedForTrackedLocalIgnoreRulesAsync()
        {
            var rules = new List<string>();
            var dedupeRules = new HashSet<string>(StringComparer.Ordinal);

            var lines = (_repoLocalIgnoreRules ?? string.Empty).ReplaceLineEndings("\n").Split('\n');
            foreach (var raw in lines)
            {
                var rule = raw.Trim();
                if (!IsLocalIgnoreRuleApplicableToTrackedFiles(rule))
                    continue;

                rule = rule.Replace('\\', '/');
                if (!dedupeRules.Add(rule))
                    continue;

                rules.Add(rule);
            }

            if (rules.Count == 0)
                return;

            var trackedTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                var found = await new Commands.QueryTrackedFiles(_repo.FullPath, rule).GetResultAsync();
                foreach (var one in found)
                    trackedTargets.Add(one);
            }

            if (trackedTargets.Count == 0)
                return;

            using var lockWatcher = _repo.LockWatcher();
            var log = _repo.CreateLog("Apply Local Ignore Rules");
            var failed = new List<string>();

            foreach (var file in trackedTargets)
            {
                var success = false;
                for (var i = 0; i < 5; i++)
                {
                    success = await new Commands.AssumeUnchanged(_repo.FullPath, file, true) { RaiseError = false }.Use(log).ExecAsync();
                    if (success)
                        break;

                    await Task.Delay(120);
                }

                if (!success)
                    failed.Add(file);
            }

            log.Complete();

            if (failed.Count > 0)
                App.RaiseException(_repo.FullPath, $"Failed to mark ignore rules as assume-unchanged:\n{string.Join("\n", failed)}");
        }

        private static bool IsLocalIgnoreRuleApplicableToTrackedFiles(string rule)
        {
            if (string.IsNullOrWhiteSpace(rule))
                return false;

            if (rule.StartsWith('#') || rule.StartsWith('!'))
                return false;
            return true;
        }

        private readonly Repository _repo;
        private readonly Dictionary<string, string> _cached;
        private readonly string _repoLocalIgnoreFile;
        private readonly string _originalRebaseBaseBranch;
        private string _httpProxy;
        private string _repoLocalIgnoreRules = string.Empty;
        private string _repoLocalIgnoreRulesOrg = string.Empty;
        private Models.CommitTemplate _selectedCommitTemplate = null;
        private Models.IssueTracker _selectedIssueTracker = null;
        private Models.CustomAction _selectedCustomAction = null;
    }
}
