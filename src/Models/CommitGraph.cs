using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Media;

namespace SourceGit.Models
{
    public record CommitGraphLayout(double StartY, double ClipWidth, double RowHeight, double OffsetX = 0, double OffsetY = 0);

    public enum CommitGraphHighlighting
    {
        All = 0,
        CurrentBranchOnly,
        SelectedCommitsOnly,
        CurrentBranchAndSelectedCommits,
        SelectedCommitsOnlyFirstParent,
    }

    public class CommitGraph
    {
        public static List<Pen> Pens { get; } = [];

        public static void SetDefaultPens(double thickness = 2)
        {
            SetPens(s_defaultPenColors, thickness);
        }

        public static void SetPens(List<Color> colors, double thickness)
        {
            Pens.Clear();

            foreach (var c in colors)
                Pens.Add(new Pen(c.ToUInt32(), thickness));

            s_penColors = [.. colors];
            s_penCount = colors.Count;
        }

        public class Path(int color, bool isHighlighted)
        {
            public List<Point> Points { get; } = [];
            public int Color { get; } = color;
            public bool IsHighlighted { get; } = isHighlighted;
        }

        public class Link
        {
            public Point Start;
            public Point Control;
            public Point End;
            public int Color;
            public bool IsHighlighted;
        }

        public enum DotType
        {
            Default,
            Head,
            Merge,
        }

        public class Dot
        {
            public DotType Type;
            public Point Center;
            public int Color;
            public bool IsMerged;
            public int FoldedCommitsBelow;
            public bool IsHighlighted;
        }

        public List<Path> Paths { get; } = [];
        public List<Link> Links { get; } = [];
        public List<Dot> Dots { get; } = [];

        public static CommitGraph Generate(List<Commit> commits, bool firstParentOnlyEnabled, CommitGraphHighlighting highlighting, HashSet<string> highlightExtraCommits)
        {
            const double unitWidth = 12;
            const double halfWidth = 6;
            const double unitHeight = 1;
            const double halfHeight = 0.5;

            var temp = new CommitGraph();
            var unsolved = new List<PathHelper>();
            var ended = new List<PathHelper>();
            var offsetY = -halfHeight;
            var colorPicker = new ColorPicker();
            var defHighlighting = highlighting == CommitGraphHighlighting.All;

            foreach (var commit in commits)
            {
                PathHelper major = null;

                // Update current y offset
                offsetY += unitHeight;

                // Find first curves that links to this commit and marks others that links to this commit ended.
                var offsetX = 4 - halfWidth;
                var maxOffsetOld = unsolved.Count > 0 ? unsolved[^1].LastX : offsetX + unitWidth;
                var isHighlighted = defHighlighting;
                foreach (var l in unsolved)
                {
                    if (l.Next.Equals(commit.SHA, StringComparison.Ordinal))
                    {
                        if (major == null)
                        {
                            offsetX += unitWidth;
                            major = l;
                            isHighlighted = major.IsHighlighted;

                            if (commit.Parents.Count > 0)
                            {
                                major.Next = commit.Parents[0];
                                major.Goto(offsetX, offsetY, halfHeight);
                            }
                            else
                            {
                                major.End(offsetX, offsetY, halfHeight);
                                ended.Add(l);
                            }
                        }
                        else
                        {
                            l.End(major.LastX, offsetY, halfHeight);
                            ended.Add(l);

                            if (!isHighlighted && l.IsHighlighted)
                                isHighlighted = true;
                        }
                    }
                    else
                    {
                        offsetX += unitWidth;
                        l.Pass(offsetX, offsetY, halfHeight);
                    }
                }

                // Remove ended curves from unsolved
                foreach (var l in ended)
                {
                    colorPicker.Recycle(l.Path.Color);
                    unsolved.Remove(l);
                }
                ended.Clear();

                // Calculate highlighted state
                if (!isHighlighted)
                {
                    switch (highlighting)
                    {
                        case CommitGraphHighlighting.CurrentBranchOnly:
                            isHighlighted = commit.IsMerged;
                            break;
                        case CommitGraphHighlighting.SelectedCommitsOnly:
                        case CommitGraphHighlighting.SelectedCommitsOnlyFirstParent:
                            if (highlightExtraCommits.Remove(commit.SHA))
                            {
                                isHighlighted = true;
                                // Highlight first parent, other parents are dealt with later
                                if (commit.Parents.Count > 0)
                                    highlightExtraCommits.Add(commit.Parents[0]);
                            }
                            break;
                        default: // CommitGraphHighlighting.CurrentBranchAndSelectedCommits
                            if (commit.IsMerged)
                            {
                                isHighlighted = true;
                            }
                            else if (highlightExtraCommits.Remove(commit.SHA))
                            {
                                isHighlighted = true;
                                // Highlight first parent, other parents are dealt with later
                                if (commit.Parents.Count > 0)
                                    highlightExtraCommits.Add(commit.Parents[0]);
                            }
                            break;
                    }
                }
                commit.IsHighlightedInGraph = isHighlighted;

                var preferredColor = FindPreferredPenIndex(commit);

                // If no path found, create new curve for branch head
                // Otherwise, create new curve for new merged commit
                if (major == null)
                {
                    offsetX += unitWidth;

                    if (commit.Parents.Count > 0)
                    {
                        major = new PathHelper(commit.Parents[0], isHighlighted, colorPicker.Next(preferredColor), new Point(offsetX, offsetY));
                        unsolved.Add(major);
                        temp.Paths.Add(major.Path);
                    }
                }
                else
                {
                    // A branch can point into an existing lane. Split it at the branch head
                    // so the selected branch color applies from this commit onward.
                    if (preferredColor >= 0 && major.Path.Color != preferredColor && commit.Parents.Count > 0)
                    {
                        var majorIndex = unsolved.IndexOf(major);
                        var splitPoint = new Point(major.LastX, offsetY);
                        major.FinishAtCurrentPosition();
                        colorPicker.Recycle(major.Path.Color);
                        major = new PathHelper(
                            commit.Parents[0],
                            isHighlighted,
                            colorPicker.Next(preferredColor),
                            splitPoint);
                        unsolved[majorIndex] = major;
                        temp.Paths.Add(major.Path);
                    }
                    else if (isHighlighted && !major.IsHighlighted && commit.Parents.Count > 0)
                    {
                        major.Highlight();
                        temp.Paths.Add(major.Path);
                    }
                }

                // Calculate link position of this commit.
                var position = new Point(major?.LastX ?? offsetX, offsetY);
                var dotColor = preferredColor >= 0 ? preferredColor : (major?.Path.Color ?? 0);
                var anchor = new Dot() { Center = position, Color = dotColor, IsMerged = commit.IsMerged, IsHighlighted = isHighlighted };
                if (commit.IsCurrentHead)
                    anchor.Type = DotType.Head;
                else if (commit.Parents.Count > 1)
                    anchor.Type = DotType.Merge;
                else
                    anchor.Type = DotType.Default;
                anchor.FoldedCommitsBelow = commit.FoldedCommitsBelow;
                temp.Dots.Add(anchor);

                // Deal with other parents (the first parent has been processed)
                if (!firstParentOnlyEnabled)
                {
                    if (highlighting == CommitGraphHighlighting.SelectedCommitsOnlyFirstParent)
                        isHighlighted = false;

                    for (int j = 1; j < commit.Parents.Count; j++)
                    {
                        var parentHash = commit.Parents[j];
                        var parent = unsolved.Find(x => x.Next.Equals(parentHash, StringComparison.Ordinal));
                        if (parent != null)
                        {
                            if (isHighlighted && !parent.IsHighlighted)
                            {
                                parent.Goto(parent.LastX, offsetY + halfHeight, halfHeight);
                                parent.Highlight();
                                temp.Paths.Add(parent.Path);
                            }

                            temp.Links.Add(new Link
                            {
                                Start = position,
                                End = new Point(parent.LastX, offsetY + halfHeight),
                                Control = new Point(parent.LastX, position.Y),
                                Color = parent.Path.Color,
                                IsHighlighted = isHighlighted,
                            });
                        }
                        else
                        {
                            offsetX += unitWidth;

                            // Create new curve for parent commit that not includes before
                            var l = new PathHelper(parentHash, isHighlighted, colorPicker.Next(), position, new Point(offsetX, position.Y + halfHeight));
                            unsolved.Add(l);
                            temp.Paths.Add(l.Path);
                        }
                    }
                }

                // Margins & colors (used by Views.Histories).
                commit.Color = dotColor;
                commit.LeftMargin = Math.Max(offsetX, maxOffsetOld) + halfWidth + 2;
            }

            // Deal with curves haven't ended yet.
            for (var i = 0; i < unsolved.Count; i++)
            {
                var path = unsolved[i];
                var endY = (commits.Count - 0.5) * unitHeight;

                if (path.Path.Points.Count == 1 && Math.Abs(path.Path.Points[0].Y - endY) < 0.0001)
                    continue;

                path.End((i + 0.5) * unitWidth + 4, endY + halfHeight, halfHeight);
            }
            unsolved.Clear();

            return temp;
        }

        private static int FindPreferredPenIndex(Commit commit)
        {
            uint preferredColor = 0;
            foreach (var decorator in commit.Decorators)
            {
                if (decorator.Type is not (DecoratorType.CurrentBranchHead or DecoratorType.LocalBranchHead or DecoratorType.RemoteBranchHead))
                    continue;

                var color = Color.FromUInt32(decorator.Color);
                if (color.A < 0x80)
                    continue;

                preferredColor = decorator.Color;
                break;
            }

            var penColors = s_penColors;
            if (preferredColor == 0 || penColors.Length == 0)
                return -1;

            var target = Color.FromUInt32(preferredColor);
            var nearest = -1;
            var nearestDistance = long.MaxValue;
            for (var i = 0; i < penColors.Length; i++)
            {
                var candidate = penColors[i];
                var red = target.R - candidate.R;
                var green = target.G - candidate.G;
                var blue = target.B - candidate.B;
                var distance = (long)red * red + (long)green * green + (long)blue * blue;
                if (distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private class ColorPicker
        {
            public int Next(int preferred = -1)
            {
                FillQueueIfEmpty();

                if (preferred >= 0 && preferred < s_penCount)
                {
                    RemoveFromQueue(preferred);
                    _usages[preferred]++;
                    return preferred;
                }

                var color = _colorsQueue.Dequeue();
                _usages[color]++;
                return color;
            }

            public void Recycle(int idx)
            {
                if (idx < 0 || idx >= _usages.Length || _usages[idx] == 0)
                    return;

                _usages[idx]--;
                if (_usages[idx] == 0 && !_colorsQueue.Contains(idx))
                    _colorsQueue.Enqueue(idx);
            }

            private void FillQueueIfEmpty()
            {
                if (_colorsQueue.Count > 0)
                    return;

                for (var i = 0; i < s_penCount; i++)
                    _colorsQueue.Enqueue(i);
            }

            private void RemoveFromQueue(int color)
            {
                var count = _colorsQueue.Count;
                for (var i = 0; i < count; i++)
                {
                    var candidate = _colorsQueue.Dequeue();
                    if (candidate != color)
                        _colorsQueue.Enqueue(candidate);
                }
            }

            private Queue<int> _colorsQueue = new Queue<int>();
            private int[] _usages = new int[s_penCount];
        }

        private class PathHelper
        {
            public Path Path { get; private set; }
            public string Next { get; set; }
            public double LastX { get; private set; }
            public bool IsHighlighted { get => Path.IsHighlighted; }

            public PathHelper(string next, bool IsHighlighted, int color, Point start)
            {
                Next = next;
                LastX = start.X;
                _lastY = start.Y;

                Path = new Path(color, IsHighlighted);
                Path.Points.Add(start);
            }

            public PathHelper(string next, bool IsHighlighted, int color, Point start, Point to)
            {
                Next = next;
                LastX = to.X;
                _lastY = to.Y;

                Path = new Path(color, IsHighlighted);
                Path.Points.Add(start);
                Path.Points.Add(to);
            }

            /// <summary>
            ///     A path that just passed this row.
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <param name="halfHeight"></param>
            public void Pass(double x, double y, double halfHeight)
            {
                if (x > LastX)
                {
                    Add(LastX, _lastY);
                    Add(x, y - halfHeight);
                }
                else if (x < LastX)
                {
                    Add(LastX, y - halfHeight);
                    y += halfHeight;
                    Add(x, y);
                }

                LastX = x;
                _lastY = y;
            }

            /// <summary>
            ///     A path that has commit in this row but not ended
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <param name="halfHeight"></param>
            public void Goto(double x, double y, double halfHeight)
            {
                if (x > LastX)
                {
                    Add(LastX, _lastY);
                    Add(x, y - halfHeight);
                }
                else if (x < LastX)
                {
                    var minY = y - halfHeight;
                    if (minY > _lastY)
                        minY -= halfHeight;

                    Add(LastX, minY);
                    Add(x, y);
                }

                LastX = x;
                _lastY = y;
            }

            /// <summary>
            ///     A path that has commit in this row and end.
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <param name="halfHeight"></param>
            public void End(double x, double y, double halfHeight)
            {
                if (x > LastX)
                {
                    Add(LastX, _lastY);
                    Add(x, y - halfHeight);
                }
                else if (x < LastX)
                {
                    Add(LastX, y - halfHeight);
                }

                Add(x, y);

                LastX = x;
                _lastY = y;
            }

            /// <summary>
            ///     End the current path and create a new highlighted from the end.
            /// </summary>
            public void Highlight()
            {
                var color = Path.Color;
                Add(LastX, _lastY);

                Path = new Path(color, true);
                Path.Points.Add(new Point(LastX, _lastY));
                _endY = 0;
            }

            public void FinishAtCurrentPosition()
            {
                Add(LastX, _lastY);
            }

            private void Add(double x, double y)
            {
                if (_endY < y)
                {
                    Path.Points.Add(new Point(x, y));
                    _endY = y;
                }
            }

            private double _lastY = 0;
            private double _endY = 0;
        }

        private static int s_penCount = 0;
        private static Color[] s_penColors = [];
        private static readonly List<Color> s_defaultPenColors = [
            Colors.Orange,
            Colors.ForestGreen,
            Colors.Turquoise,
            Colors.Olive,
            Colors.Magenta,
            Colors.Red,
            Colors.Khaki,
            Colors.Lime,
            Colors.RoyalBlue,
            Colors.Teal,
        ];
    }
}
