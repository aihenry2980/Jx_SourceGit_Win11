using System;
using System.Collections.Generic;
using System.Globalization;

namespace SourceGit.Models
{
    public static class BranchLabelLayout
    {
        public const int TwoLineThreshold = 25;

        public static int GetLineBreak(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= TwoLineThreshold)
                return -1;

            var elements = StringInfo.ParseCombiningCharacters(name);
            if (elements.Length <= TwoLineThreshold)
                return -1;

            var middle = elements.Length / 2;
            var best = middle;
            var distance = int.MaxValue;
            for (var i = Math.Max(1, middle - 5); i <= Math.Min(elements.Length - 1, middle + 5); i++)
            {
                var previous = name[elements[i] - 1];
                if (previous is '/' or '-' or '_' or '.')
                {
                    var candidate = Math.Abs(i - middle);
                    if (candidate < distance)
                    {
                        best = i;
                        distance = candidate;
                    }
                }
            }

            return elements[best];
        }

        public static bool IsBranch(Decorator decorator) => decorator.Type is DecoratorType.CurrentBranchHead or
            DecoratorType.LocalBranchHead or DecoratorType.RemoteBranchHead;

        public static bool HasTwoLineNames(IReadOnlyList<Decorator> refs, bool compactTrackingBranches)
        {
            HashSet<int> consumed = null;
            if (compactTrackingBranches && refs.Count > 1)
            {
                for (var localIndex = 0; localIndex < refs.Count; localIndex++)
                {
                    var branch = refs[localIndex];
                    if (branch.Type is DecoratorType.LocalBranchHead or DecoratorType.CurrentBranchHead)
                    {
                        var match = FindCompactRemoteMatch(refs, branch.Name);
                        if (match > localIndex)
                            (consumed ??= new HashSet<int>()).Add(match);
                    }
                }
            }
            for (var i = 0; i < refs.Count; i++)
            {
                var branch = refs[i];
                if (consumed?.Contains(i) != true && IsBranch(branch) &&
                    (branch.Color == 0 || (branch.Color >> 24) > 0x40) && GetLineBreak(branch.Name) >= 0)
                    return true;
            }
            return false;
        }

        public static int FindCompactRemoteMatch(IReadOnlyList<Decorator> refs, string localName)
        {
            if (refs == null || string.IsNullOrWhiteSpace(localName))
                return -1;

            var exactOriginName = $"origin/{localName}";
            var firstMatch = -1;
            var matchCount = 0;
            for (var i = 0; i < refs.Count; i++)
            {
                var decorator = refs[i];
                if (decorator.Type != DecoratorType.RemoteBranchHead)
                    continue;
                var remoteName = decorator.Name;
                if (string.IsNullOrWhiteSpace(remoteName))
                    continue;
                var slash = remoteName.IndexOf('/');
                var leaf = slash >= 0 && slash + 1 < remoteName.Length ? remoteName.Substring(slash + 1) : remoteName;
                if (!leaf.Equals(localName, StringComparison.Ordinal))
                    continue;
                if (remoteName.Equals(exactOriginName, StringComparison.Ordinal))
                    return i;
                if (firstMatch < 0)
                    firstMatch = i;
                matchCount++;
            }
            return matchCount == 1 ? firstMatch : -1;
        }
    }
}
