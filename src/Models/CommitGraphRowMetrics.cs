using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    // Graph coordinates use row units; the grid can give each commit a different pixel height.
    public sealed class CommitGraphRowMetrics
    {
        public CommitGraphRowMetrics(IReadOnlyList<Commit> commits, double compactHeight, double expandedHeight, bool compactTrackingBranches = false,
            int branchNameWrapLength = BranchLabelLayout.DefaultWrapLength)
        {
            CompactHeight = compactHeight;
            _tops = new double[(commits?.Count ?? 0) + 1];
            for (var i = 0; i < _tops.Length - 1; i++)
                _tops[i + 1] = _tops[i] + (BranchLabelLayout.HasTwoLineNames(commits[i].Decorators, compactTrackingBranches, branchNameWrapLength) ? expandedHeight : compactHeight);
        }

        public double CompactHeight { get; }
        public int Count => _tops.Length - 1;
        public double GetTop(int index) => index < 0 ? index * CompactHeight :
            index <= Count ? _tops[index] : _tops[Count] + (index - Count) * CompactHeight;
        public double GetHeight(int index) => index >= 0 && index < Count ? _tops[index + 1] - _tops[index] : CompactHeight;
        public double GetY(double rowPosition)
        {
            var index = (int)Math.Floor(rowPosition);
            return GetTop(index) + (rowPosition - index) * GetHeight(index);
        }

        private readonly double[] _tops;
    }
}
