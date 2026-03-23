using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public enum CommitSearchMethod
    {
        BySHA = 0,
        ByAuthor,
        ByCommitter,
        ByMessage,
        ByPath,
        ByContent,
    }

    public class Commit
    {
        public const string EmptyTreeSHA1 = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

        public string SHA { get; set; } = string.Empty;
        public User Author { get; set; } = User.Invalid;
        public ulong AuthorTime { get; set; } = 0;
        public User Committer { get; set; } = User.Invalid;
        public ulong CommitterTime { get; set; } = 0;
        public string Subject { get; set; } = string.Empty;
        public List<string> Parents { get; set; } = new();
        public List<Decorator> Decorators { get; set; } = new();

        public bool IsMerged { get; set; } = false;
        public bool HasSubmodulePointerChange { get; set; } = false;
        public bool IsQuickFindMatched { get; set; } = false;
        public int ChangedFileCount { get; set; } = -1;
        public int Color { get; set; } = 0;
        public double LeftMargin { get; set; } = 0;
        public int FoldedCommitsBelow { get; set; } = 0;

        public string AuthorTimeStr => DateTime.UnixEpoch.AddSeconds(AuthorTime).ToLocalTime().ToString(DateTimeFormat.Active.DateTime);
        public string CommitterTimeStr => DateTime.UnixEpoch.AddSeconds(CommitterTime).ToLocalTime().ToString(DateTimeFormat.Active.DateTime);
        public string AuthorTimeShortStr => DateTime.UnixEpoch.AddSeconds(AuthorTime).ToLocalTime().ToString(DateTimeFormat.Active.DateOnly);
        public string CommitterTimeShortStr => DateTime.UnixEpoch.AddSeconds(CommitterTime).ToLocalTime().ToString(DateTimeFormat.Active.DateOnly);

        public bool IsCommitterVisible => !Author.Equals(Committer) || AuthorTime != CommitterTime;
        public bool IsCurrentHead => Decorators.Find(x => x.Type is DecoratorType.CurrentBranchHead or DecoratorType.CurrentCommitHead) != null;
        public bool IsSuperProjectPointer => Decorators.Find(x => x.Type == DecoratorType.SuperProjectPointer) != null;
        public bool IsSubmoduleChangeCommit =>
            HasSubmodulePointerChange ||
            Subject.Contains("submodule", StringComparison.OrdinalIgnoreCase) ||
            Subject.Contains("spp", StringComparison.OrdinalIgnoreCase);
        public bool HasDecorators => Decorators.Count > 0;
        public string HistoryDisplaySubject => ChangedFileCount >= 0 ? $"({ChangedFileCount}) {Subject}" : Subject;

        public bool MatchesHistoryQuickFind(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            if (SHA.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                Author.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                Committer.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var decorator in Decorators)
            {
                if (decorator.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public string GetFriendlyName()
        {
            var branchDecorator = Decorators.Find(x => x.Type is DecoratorType.LocalBranchHead or DecoratorType.RemoteBranchHead);
            if (branchDecorator != null)
                return branchDecorator.Name;

            var tagDecorator = Decorators.Find(x => x.Type is DecoratorType.Tag);
            if (tagDecorator != null)
                return tagDecorator.Name;

            return SHA[..10];
        }

        public void ParseParents(string data)
        {
            if (data.Length < 8)
                return;

            Parents.AddRange(data.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        public void ParseDecorators(string data)
        {
            if (data.Length < 3)
                return;

            var subs = data.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var sub in subs)
            {
                var d = sub.Trim();
                if (d.EndsWith("/HEAD", StringComparison.Ordinal))
                    continue;

                if (d.StartsWith("tag: refs/tags/", StringComparison.Ordinal))
                {
                    Decorators.Add(new Decorator()
                    {
                        Type = DecoratorType.Tag,
                        Name = d.Substring(15),
                    });
                }
                else if (d.StartsWith("HEAD -> refs/heads/", StringComparison.Ordinal))
                {
                    IsMerged = true;
                    Decorators.Add(new Decorator()
                    {
                        Type = DecoratorType.CurrentCommitHead,
                        Name = "HEAD",
                    });

                    Decorators.Add(new Decorator()
                    {
                        Type = DecoratorType.CurrentBranchHead,
                        Name = d.Substring(19),
                    });
                }
                else if (d.Equals("HEAD"))
                {
                    IsMerged = true;
                    Decorators.Add(new Decorator()
                    {
                        Type = DecoratorType.CurrentCommitHead,
                        Name = d,
                    });
                }
                else if (d.StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    Decorators.Add(new Decorator()
                    {
                        Type = DecoratorType.LocalBranchHead,
                        Name = d.Substring(11),
                    });
                }
                else if (d.StartsWith("refs/remotes/", StringComparison.Ordinal))
                {
                    Decorators.Add(new Decorator()
                    {
                        Type = DecoratorType.RemoteBranchHead,
                        Name = d.Substring(13),
                    });
                }
            }

            SortDecorators(Decorators);
        }

        public static void SortDecorators(List<Decorator> decorators)
        {
            if (decorators == null || decorators.Count <= 1)
                return;

            decorators.Sort((l, r) =>
            {
                var delta = GetDecoratorPriority(l.Type) - GetDecoratorPriority(r.Type);
                if (delta != 0)
                    return delta;

                return NumericSort.Compare(l.Name, r.Name);
            });
        }

        private static int GetDecoratorPriority(DecoratorType type)
        {
            return type switch
            {
                DecoratorType.CurrentCommitHead => 0,
                DecoratorType.SuperProjectPointer => 1,
                DecoratorType.ParentRepository => 2,
                DecoratorType.CurrentBranchHead => 3,
                DecoratorType.LocalBranchHead => 4,
                DecoratorType.RemoteBranchHead => 5,
                DecoratorType.Tag => 6,
                _ => 100,
            };
        }
    }

    public class CommitFullMessage
    {
        public string Message { get; set; } = string.Empty;
        public InlineElementCollector Inlines { get; set; } = new();
    }
}
