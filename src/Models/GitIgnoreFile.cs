using System.Collections.Generic;
using System.IO;
using Avalonia.Media;

namespace SourceGit.Models
{
    public enum GitIgnoreFileKind
    {
        Shared = 0,
        Private = 1,
        Custom = 2,
    }

    public class GitIgnoreFile
    {
        public static readonly List<GitIgnoreFile> Supported = [new(true), new(false)];

        public bool IsShared => Kind == GitIgnoreFileKind.Shared;
        public bool IsCustom => Kind == GitIgnoreFileKind.Custom;

        public GitIgnoreFileKind Kind { get; set; }
        public string CustomPath { get; set; } = string.Empty;

        public string File
        {
            get
            {
                return Kind switch
                {
                    GitIgnoreFileKind.Shared => ".gitignore",
                    GitIgnoreFileKind.Private => "<git_dir>/info/exclude",
                    _ => string.IsNullOrWhiteSpace(CustomPath) ? "(custom file path)" : CustomPath,
                };
            }
        }

        public string Desc => Kind switch
        {
            GitIgnoreFileKind.Shared => "Shared",
            GitIgnoreFileKind.Private => "Private",
            _ => "Custom",
        };

        public IBrush Brush => Kind switch
        {
            GitIgnoreFileKind.Shared => Brushes.Green,
            GitIgnoreFileKind.Private => Brushes.Gray,
            _ => Brushes.DarkOrange,
        };

        public GitIgnoreFile(bool isShared)
        {
            Kind = isShared ? GitIgnoreFileKind.Shared : GitIgnoreFileKind.Private;
        }

        public GitIgnoreFile(string customPath)
        {
            Kind = GitIgnoreFileKind.Custom;
            CustomPath = customPath ?? string.Empty;
        }

        public string GetFullPath(string repoPath, string gitDir)
        {
            if (Kind == GitIgnoreFileKind.Shared)
                return Path.Combine(repoPath, ".gitignore");

            if (Kind == GitIgnoreFileKind.Private)
                return Path.Combine(gitDir, "info", "exclude");

            var raw = CustomPath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var normalized = raw.Replace('\\', '/');
            const string gitDirPrefix = "<git_dir>/";
            if (normalized.StartsWith(gitDirPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var suffix = normalized.Substring(gitDirPrefix.Length);
                return Path.GetFullPath(Path.Combine(gitDir, suffix));
            }

            if (Path.IsPathRooted(raw))
                return Path.GetFullPath(raw);

            return Path.GetFullPath(Path.Combine(repoPath, raw));
        }
    }
}
