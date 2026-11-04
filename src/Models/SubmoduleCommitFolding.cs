using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public static class SubmoduleCommitFolding
    {
        public static void Apply(List<Commit> commits, ISet<string> expandedGroups)
        {
            var childCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var commit in commits)
            {
                commit.SubmoduleCommitGroup = [];
                commit.IsSubmoduleCommitGroupExpanded = false;
                foreach (var parent in commit.Parents)
                    childCounts[parent] = childCounts.GetValueOrDefault(parent) + 1;
            }

            var visible = new List<Commit>(commits.Count);
            for (var index = 0; index < commits.Count; index++)
            {
                var head = commits[index];
                visible.Add(head);
                if (!IsPureSubmoduleUpdate(head))
                    continue;

                var paths = new HashSet<string>(head.ChangedSubmodulePaths, StringComparer.Ordinal);
                var last = head;
                var members = new List<Commit>();
                while (index + 1 < commits.Count)
                {
                    var next = commits[index + 1];
                    if (!IsPureSubmoduleUpdate(next) || next.HasDecorators ||
                        !last.Parents[0].Equals(next.SHA, StringComparison.Ordinal) ||
                        childCounts.GetValueOrDefault(next.SHA) != 1 ||
                        !paths.SetEquals(next.ChangedSubmodulePaths))
                        break;

                    members.Add(next);
                    last = next;
                    index++;
                }

                if (members.Count == 0)
                    continue;

                head.SubmoduleCommitGroup = members;
                head.IsSubmoduleCommitGroupExpanded = expandedGroups.Contains(head.SHA);
                if (head.IsSubmoduleCommitGroupExpanded)
                    visible.AddRange(members);
            }

            commits.Clear();
            commits.AddRange(visible);
        }

        private static bool IsPureSubmoduleUpdate(Commit commit) =>
            commit.Parents.Count == 1 && commit.FoldedCommitsBelow == 0 &&
            commit.HasSubmodulePointerChange && commit.RegularFileChangeCount == 0 &&
            !commit.HasRenameOrCopyChange && !commit.HasTypeChange &&
            commit.SubmodulePointerChangeCount > 0 &&
            commit.ChangedFileCount == commit.SubmodulePointerChangeCount &&
            commit.ChangedSubmodulePaths.Count == commit.SubmodulePointerChangeCount;
    }
}
