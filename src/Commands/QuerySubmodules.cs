using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class QuerySubmodules : Command
    {
        [GeneratedRegex(@"^([U\-\+ ])([0-9a-f]+)\s(.*?)(\s\(.*\))?$")]
        private static partial Regex REG_FORMAT_STATUS();
        [GeneratedRegex(@"^submodule\.(\S*)\.(\w+)=(.*)$")]
        private static partial Regex REG_FORMAT_MODULE_INFO();

        public QuerySubmodules(string repo, int maxDepth = 1, bool queryStatus = true)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = queryStatus ? "submodule status" : "config --file .gitmodules --list";
            _maxDepth = Math.Max(1, maxDepth);
            _queryStatus = queryStatus;
        }

        public async Task<List<Models.Submodule>> GetResultAsync()
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var visited = new HashSet<string>(comparer);
            var root = NormalizeRepositoryPath(WorkingDirectory);
            return await QueryRecursivelyAsync(root, string.Empty, _maxDepth, visited, _queryStatus).ConfigureAwait(false);
        }

        private static async Task<List<Models.Submodule>> QueryRecursivelyAsync(
            string repo,
            string displayPrefix,
            int remainingDepth,
            HashSet<string> visited,
            bool queryStatus)
        {
            var normalizedRepo = NormalizeRepositoryPath(repo);
            if (string.IsNullOrEmpty(normalizedRepo) || !Directory.Exists(normalizedRepo) || !visited.Add(normalizedRepo))
                return [];

            var current = await new QuerySubmodules(normalizedRepo, 1, queryStatus).GetCurrentLevelAsync().ConfigureAwait(false);
            var outs = new List<Models.Submodule>();
            foreach (var module in current)
            {
                var localPath = module.Path;
                if (!string.IsNullOrEmpty(displayPrefix))
                    module.Path = CombineGitPath(displayPrefix, localPath);

                outs.Add(module);

                if (remainingDepth <= 1 || module.Status == Models.SubmoduleStatus.NotInited)
                    continue;

                var submoduleRepo = NormalizeRepositoryPath(Native.OS.GetAbsPath(normalizedRepo, localPath));
                if (!Directory.Exists(submoduleRepo))
                    continue;

                var children = await QueryRecursivelyAsync(submoduleRepo, module.Path, remainingDepth - 1, visited, queryStatus).ConfigureAwait(false);
                if (children.Exists(x => x.IsDirty))
                {
                    module.HasSubmoduleChanges = true;
                    UpdateDirtyStatus(module);
                }

                outs.AddRange(children);
            }

            return outs;
        }

        private async Task<List<Models.Submodule>> GetCurrentLevelAsync()
        {
            if (!_queryStatus)
                return await GetCurrentLevelDefinitionsAsync().ConfigureAwait(false);

            var submodules = new List<Models.Submodule>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var map = new Dictionary<string, Models.Submodule>();
            var needCheckLocalChanges = false;
            foreach (var line in lines)
            {
                var match = REG_FORMAT_STATUS().Match(line);
                if (match.Success)
                {
                    var stat = match.Groups[1].Value;
                    var sha = match.Groups[2].Value;
                    var path = match.Groups[3].Value;

                    var module = new Models.Submodule() { Path = path, SHA = sha };
                    switch (stat[0])
                    {
                        case '-':
                            module.Status = Models.SubmoduleStatus.NotInited;
                            break;
                        case '+':
                            module.Status = Models.SubmoduleStatus.RevisionChanged;
                            module.HasSubmoduleChanges = true;
                            needCheckLocalChanges = true;
                            break;
                        case 'U':
                            module.Status = Models.SubmoduleStatus.Unmerged;
                            break;
                        default:
                            module.Status = Models.SubmoduleStatus.Normal;
                            needCheckLocalChanges = true;
                            break;
                    }

                    map.Add(path, module);
                    submodules.Add(module);
                }
            }

            if (submodules.Count > 0)
            {
                Args = "config --file .gitmodules --list";
                rs = await ReadToEndAsync().ConfigureAwait(false);
                if (rs.IsSuccess)
                {
                    var modules = new Dictionary<string, ModuleInfo>();
                    lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var match = REG_FORMAT_MODULE_INFO().Match(line);
                        if (match.Success)
                        {
                            var name = match.Groups[1].Value;
                            var key = match.Groups[2].Value;
                            var val = match.Groups[3].Value;

                            if (!modules.TryGetValue(name, out var m))
                            {
                                // Find name alias.
                                foreach (var kv in modules)
                                {
                                    if (kv.Value.Path.Equals(name, StringComparison.Ordinal))
                                    {
                                        m = kv.Value;
                                        break;
                                    }
                                }

                                if (m == null)
                                {
                                    m = new ModuleInfo();
                                    modules.Add(name, m);
                                }
                            }

                            if (key.Equals("path", StringComparison.Ordinal))
                                m.Path = val;
                            else if (key.Equals("url", StringComparison.Ordinal))
                                m.URL = val;
                            else if (key.Equals("branch", StringComparison.Ordinal))
                                m.Branch = val;
                        }
                    }

                    foreach (var kv in modules)
                    {
                        if (map.TryGetValue(kv.Value.Path, out var m))
                        {
                            m.URL = kv.Value.URL;
                            m.Branch = kv.Value.Branch;
                        }
                    }
                }
            }

            if (needCheckLocalChanges)
            {
                var builder = new StringBuilder();
                foreach (var kv in map)
                {
                    if (kv.Value.Status is not Models.SubmoduleStatus.NotInited and not Models.SubmoduleStatus.Unmerged)
                        builder.Append(kv.Key.Quoted()).Append(' ');
                }

                Args = $"--no-optional-locks status --porcelain=v2 -- {builder}";
                rs = await ReadToEndAsync().ConfigureAwait(false);
                if (!rs.IsSuccess)
                    return submodules;

                lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (TryApplyPorcelainV2Status(line, map, out var module))
                        UpdateDirtyStatus(module);
                }
            }

            return submodules;
        }

        private async Task<List<Models.Submodule>> GetCurrentLevelDefinitionsAsync()
        {
            var submodules = new List<Models.Submodule>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return submodules;

            var modules = new Dictionary<string, ModuleInfo>();
            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT_MODULE_INFO().Match(line);
                if (!match.Success)
                    continue;

                var name = match.Groups[1].Value;
                var key = match.Groups[2].Value;
                var val = match.Groups[3].Value;

                if (!modules.TryGetValue(name, out var module))
                {
                    module = new ModuleInfo();
                    modules.Add(name, module);
                }

                if (key.Equals("path", StringComparison.Ordinal))
                    module.Path = val;
                else if (key.Equals("url", StringComparison.Ordinal))
                    module.URL = val;
                else if (key.Equals("branch", StringComparison.Ordinal))
                    module.Branch = val;
            }

            foreach (var module in modules.Values)
            {
                if (!string.IsNullOrWhiteSpace(module.Path))
                {
                    submodules.Add(new Models.Submodule
                    {
                        Path = module.Path,
                        URL = module.URL,
                        Branch = module.Branch,
                        Status = Models.SubmoduleStatus.Unknown,
                    });
                }
            }

            return submodules;
        }

        private static string NormalizeRepositoryPath(string repo)
        {
            if (string.IsNullOrWhiteSpace(repo))
                return string.Empty;

            try
            {
                return Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return repo.Replace('\\', '/').TrimEnd('/');
            }
        }

        private static string CombineGitPath(string prefix, string path)
        {
            if (string.IsNullOrEmpty(prefix))
                return path.Replace('\\', '/');

            if (string.IsNullOrEmpty(path))
                return prefix.Replace('\\', '/');

            return $"{prefix.TrimEnd('/', '\\')}/{path.TrimStart('/', '\\')}".Replace('\\', '/');
        }

        private static bool TryApplyPorcelainV2Status(string line, Dictionary<string, Models.Submodule> map, out Models.Submodule module)
        {
            module = null;
            if (string.IsNullOrEmpty(line))
                return false;

            if (line.StartsWith("1 ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', 9);
                if (parts.Length < 9 || !map.TryGetValue(parts[8], out module))
                    return false;

                var xy = parts[1];
                var submodule = parts[2];
                if (submodule.Length == 4 && submodule[0] == 'S')
                {
                    module.HasSubmoduleChanges |= submodule[1] == 'C';
                    module.HasFileChanges |= submodule[2] == 'M' || submodule[3] == 'U';
                }
                else
                {
                    module.HasFileChanges |= HasOrdinaryFileChange(xy);
                }

                return true;
            }

            if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                var path = line.Substring(2);
                if (!map.TryGetValue(path, out module))
                    return false;

                module.HasFileChanges = true;
                return true;
            }

            return false;
        }

        private static bool HasOrdinaryFileChange(string xy)
        {
            return !string.IsNullOrEmpty(xy) && !xy.Equals("..", StringComparison.Ordinal);
        }

        private static void UpdateDirtyStatus(Models.Submodule module)
        {
            if (module.Status is Models.SubmoduleStatus.NotInited or Models.SubmoduleStatus.Unmerged)
                return;

            if (module.HasSubmoduleChanges)
                module.Status = Models.SubmoduleStatus.SubmoduleChanged;
            else if (module.HasFileChanges)
                module.Status = Models.SubmoduleStatus.Modified;
            else
                module.Status = Models.SubmoduleStatus.Normal;
        }

        private class ModuleInfo
        {
            public string Path { get; set; } = string.Empty;
            public string URL { get; set; } = string.Empty;
            public string Branch { get; set; } = "HEAD";
        }

        private readonly int _maxDepth = 1;
        private readonly bool _queryStatus = true;
    }
}
