using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Avalonia.Collections;

namespace SourceGit.Models
{
    public class RepositorySettings
    {
        public string DefaultRemote
        {
            get;
            set;
        } = string.Empty;

        public int PreferredMergeMode
        {
            get;
            set;
        } = 0;

        public string ConventionalTypesOverride
        {
            get;
            set;
        } = string.Empty;

        public bool EnableAutoFetch
        {
            get;
            set;
        } = false;

        public bool EnableAutoSyncAll
        {
            get;
            set;
        } = false;

        public int AutoFetchInterval
        {
            get;
            set;
        } = 10;

        public bool AutoFetchPrune
        {
            get;
            set;
        } = true;

        public int SuccessfulOperationAutoCloseSeconds
        {
            get;
            set;
        } = 5;

        public bool EnableRecursiveWhenAutoUpdatingSubmodules
        {
            get;
            set;
        } = true;

        public bool AskBeforeAutoUpdatingSubmodules
        {
            get;
            set;
        } = false;

        public string PreferredOpenAIService
        {
            get;
            set;
        } = "---";

        public string PresetBranchExactNames
        {
            get;
            set;
        } = string.Empty;

        public string PresetBranchContainsPatterns
        {
            get;
            set;
        } = string.Empty;

        public string PresetBranchExcludeNames
        {
            get;
            set;
        } = string.Empty;

        public string PresetBranchExactNameColors
        {
            get;
            set;
        } = string.Empty;

        public string SubmoduleUpdateBadgeColors
        {
            get;
            set;
        } = string.Empty;

        public int PreferredGitIgnoreStorageKind
        {
            get;
            set;
        } = 0;

        public string CustomGitIgnoreStorageFile
        {
            get;
            set;
        } = string.Empty;

        public string RecursiveSubmoduleUpdateTargets
        {
            get;
            set;
        } = string.Empty;

        public bool HasConfiguredRecursiveSubmoduleUpdateTargets
        {
            get;
            set;
        } = false;

        public AvaloniaList<CommitTemplate> CommitTemplates
        {
            get;
            set;
        } = [];

        public AvaloniaList<CustomAction> CustomActions
        {
            get;
            set;
        } = [];

        public static RepositorySettings Get(string gitCommonDir)
        {
            var fileInfo = new FileInfo(Path.Combine(gitCommonDir, "sourcegit.settings"));
            var fullpath = fileInfo.FullName;
            if (_cache.TryGetValue(fullpath, out var setting))
                return setting;

            if (!File.Exists(fullpath))
            {
                setting = new();
            }
            else
            {
                try
                {
                    using var stream = File.OpenRead(fullpath);
                    setting = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.RepositorySettings);
                }
                catch
                {
                    setting = new();
                }
            }

            // Serialize setting again to make sure there are no unnecessary whitespaces.
            Task.Run(() =>
            {
                var formatted = JsonSerializer.Serialize(setting, JsonCodeGen.Default.RepositorySettings);
                setting._orgHash = HashContent(formatted);
            });

            setting._file = fullpath;
            _cache.Add(fullpath, setting);
            return setting;
        }

        public void Save()
        {
            try
            {
                var content = JsonSerializer.Serialize(this, JsonCodeGen.Default.RepositorySettings);
                var hash = HashContent(content);
                if (!hash.Equals(_orgHash, StringComparison.Ordinal))
                {
                    var tmpfile = $"{_file}.tmp";
                    File.WriteAllText(tmpfile, content);
                    File.Move(tmpfile, _file, true);
                    _orgHash = hash;
                }
            }
            catch
            {
                // Ignore save errors
            }
        }

        public Task SaveAsync()
        {
            Save();
            return Task.CompletedTask;
        }

        public HashSet<string> GetPresetBranchExactNameSet()
        {
            return new HashSet<string>(ParsePresetBranchRules(PresetBranchExactNames), StringComparer.Ordinal);
        }

        public List<string> GetPresetBranchExactNameList()
        {
            return ParsePresetBranchRules(PresetBranchExactNames);
        }

        public List<string> GetPresetBranchContainsRuleList()
        {
            return ParsePresetBranchRules(PresetBranchContainsPatterns);
        }

        public HashSet<string> GetPresetBranchExcludeNameSet()
        {
            return new HashSet<string>(ParsePresetBranchRules(PresetBranchExcludeNames), StringComparer.Ordinal);
        }

        public List<string> GetPresetBranchExcludeNameList()
        {
            return ParsePresetBranchRules(PresetBranchExcludeNames);
        }

        public bool AddPresetBranchExcludeName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return false;

            var target = branchName.Trim();
            var list = ParsePresetBranchRules(PresetBranchExcludeNames);
            foreach (var existing in list)
            {
                if (existing.Equals(target, StringComparison.Ordinal))
                    return false;
            }

            list.Add(target);
            PresetBranchExcludeNames = string.Join('\n', list);
            return true;
        }

        public Dictionary<string, uint> GetPresetBranchExactNameColorMap()
        {
            var exactNames = GetPresetBranchExactNameSet();
            var colors = ParsePresetBranchRuleColors(PresetBranchExactNameColors);
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

        public Dictionary<string, uint> GetPresetBranchConfiguredColorMap()
        {
            return ParsePresetBranchRuleColors(PresetBranchExactNameColors);
        }

        public bool SetPresetBranchExactNameColor(string exactName, uint color)
        {
            if (string.IsNullOrWhiteSpace(exactName))
                return false;

            var colors = ParsePresetBranchRuleColors(PresetBranchExactNameColors);
            if (color == PRESET_BRANCH_EXACT_DEFAULT_COLOR)
                colors.Remove(exactName);
            else
                colors[exactName] = color;

            var next = SerializePresetBranchRuleColors(colors);
            if (next.Equals(PresetBranchExactNameColors, StringComparison.Ordinal))
                return false;

            PresetBranchExactNameColors = next;
            return true;
        }

        public Dictionary<string, uint> GetSubmoduleUpdateBadgeColorMap()
        {
            return ParsePresetBranchRuleColors(SubmoduleUpdateBadgeColors);
        }

        public bool SetSubmoduleUpdateBadgeColor(string path, uint? color)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normalized))
                return false;

            var colors = ParsePresetBranchRuleColors(SubmoduleUpdateBadgeColors);
            if (color.HasValue)
                colors[normalized] = color.Value;
            else
                colors.Remove(normalized);

            var next = SerializePresetBranchRuleColors(colors);
            if (next.Equals(SubmoduleUpdateBadgeColors, StringComparison.Ordinal))
                return false;

            SubmoduleUpdateBadgeColors = next;
            return true;
        }

        public List<string> GetRecursiveSubmoduleUpdateTargets()
        {
            return ParsePresetBranchRules(RecursiveSubmoduleUpdateTargets);
        }

        public bool NeedsRecursiveSubmoduleUpdateTargetsConfiguration()
        {
            return !HasConfiguredRecursiveSubmoduleUpdateTargets;
        }

        public void SetRecursiveSubmoduleUpdateTargets(IEnumerable<string> targets)
        {
            HasConfiguredRecursiveSubmoduleUpdateTargets = true;

            if (targets == null)
            {
                RecursiveSubmoduleUpdateTargets = string.Empty;
                return;
            }

            var list = new List<string>();
            var dedupe = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in targets)
            {
                var target = raw?.Trim();
                if (string.IsNullOrEmpty(target))
                    continue;

                if (dedupe.Add(target))
                    list.Add(target);
            }

            RecursiveSubmoduleUpdateTargets = string.Join('\n', list);
        }

        public CustomAction AddNewCustomAction()
        {
            var act = new CustomAction() { Name = "Unnamed Action" };
            CustomActions.Add(act);
            return act;
        }

        public void RemoveCustomAction(CustomAction act)
        {
            if (act != null)
                CustomActions.Remove(act);
        }

        public void MoveCustomActionUp(CustomAction act)
        {
            var idx = CustomActions.IndexOf(act);
            if (idx > 0)
                CustomActions.Move(idx - 1, idx);
        }

        public void MoveCustomActionDown(CustomAction act)
        {
            var idx = CustomActions.IndexOf(act);
            if (idx < CustomActions.Count - 1)
                CustomActions.Move(idx + 1, idx);
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

        private static string HashContent(string source)
        {
            var hash = MD5.HashData(Encoding.Default.GetBytes(source));
            return Convert.ToHexStringLower(hash);
        }

        private static Dictionary<string, RepositorySettings> _cache = new();
        private string _file = string.Empty;
        private string _orgHash = string.Empty;

        public const uint PRESET_BRANCH_EXACT_DEFAULT_COLOR = 0xFF10893E;
    }
}
