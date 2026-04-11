using System.Collections.Generic;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class SubmoduleTreeNode : ObservableObject
    {
        public string FullPath { get; private set; } = string.Empty;
        public int Depth { get; private set; } = 0;
        public Models.Submodule Module { get; private set; } = null;
        public List<SubmoduleTreeNode> Children { get; private set; } = [];
        public int Counter = 0;

        public bool IsFolder
        {
            get => Module == null;
        }

        public bool HasChildren
        {
            get => Children.Count > 0;
        }

        public bool HasModule
        {
            get => Module != null;
        }

        public bool CanShowStatusBadge
        {
            get => Module != null;
        }

        public bool IsInitializedClean
        {
            get => Module?.IsInitializedClean ?? false;
        }

        public bool HasWarningStatusBadge
        {
            get => Module?.HasWarningStatusBadge ?? false;
        }

        public bool HasUnavailableStatusBadge
        {
            get => Module?.HasUnavailableStatusBadge ?? false;
        }

        public bool HasFileChangeStatusBadge
        {
            get => Module?.HasFileChangeStatusBadge ?? false;
        }

        public bool HasSubmoduleChangeStatusBadge
        {
            get => Module?.HasSubmoduleChangeStatusBadge ?? false;
        }

        public bool HasErrorStatusBadge
        {
            get => Module?.HasErrorStatusBadge ?? false;
        }

        public string StatusBadgeText
        {
            get => Module?.StatusBadgeText ?? string.Empty;
        }

        public string StatusBadgeToolTip
        {
            get => Module?.StatusBadgeToolTip ?? string.Empty;
        }

        public string FileChangeStatusBadgeText
        {
            get => Module?.FileChangeStatusBadgeText ?? string.Empty;
        }

        public string SubmoduleChangeStatusBadgeText
        {
            get => Module?.SubmoduleChangeStatusBadgeText ?? string.Empty;
        }

        public string FileChangeStatusBadgeToolTip
        {
            get => Module?.FileChangeStatusBadgeToolTip ?? string.Empty;
        }

        public string SubmoduleChangeStatusBadgeToolTip
        {
            get => Module?.SubmoduleChangeStatusBadgeToolTip ?? string.Empty;
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public string ChildCounter
        {
            get => Counter > 0 ? $"({Counter})" : string.Empty;
        }

        public bool IsDirty
        {
            get => Module?.IsDirty ?? false;
        }

        public SubmoduleTreeNode(Models.Submodule module, int depth)
        {
            FullPath = module.Path;
            Depth = depth;
            Module = module;
            IsExpanded = false;
        }

        public SubmoduleTreeNode(string path, int depth)
        {
            FullPath = path;
            Depth = depth;
            IsExpanded = false;
            Counter = 0;
        }

        public static List<SubmoduleTreeNode> Build(
            IList<Models.Submodule> submodules,
            HashSet<string> oldExpanded,
            HashSet<string> oldExpandable)
        {
            var nodes = new List<SubmoduleTreeNode>();
            var nodeMap = new Dictionary<string, SubmoduleTreeNode>();

            foreach (var module in submodules)
            {
                var parts = module.Path.Split('/');
                SubmoduleTreeNode parent = null;
                var fullPath = string.Empty;

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (string.IsNullOrEmpty(part))
                        continue;

                    fullPath = string.IsNullOrEmpty(fullPath) ? part : $"{fullPath}/{part}";
                    var isLeaf = i == parts.Length - 1;

                    if (!nodeMap.TryGetValue(fullPath, out var node))
                    {
                        node = isLeaf ? new SubmoduleTreeNode(module, i) : new SubmoduleTreeNode(fullPath, i);
                        nodeMap.Add(fullPath, node);

                        if (parent == null)
                            InsertNode(nodes, node);
                        else
                            InsertNode(parent.Children, node);
                    }
                    else
                    {
                        if (isLeaf && node.Module == null)
                            node.Module = module;
                    }

                    if (!isLeaf)
                        node.Counter++;

                    parent = node;
                }
            }

            ApplyExpansionDefaults(nodes, oldExpanded, oldExpandable);
            return nodes;
        }

        public static void CollectExpandableState(
            IEnumerable<SubmoduleTreeNode> nodes,
            HashSet<string> expanded,
            HashSet<string> expandable)
        {
            foreach (var node in nodes)
            {
                if (node.HasChildren)
                {
                    expandable.Add(node.FullPath);
                    if (node.IsExpanded)
                        expanded.Add(node.FullPath);
                }

                CollectExpandableState(node.Children, expanded, expandable);
            }
        }

        private static void ApplyExpansionDefaults(
            IEnumerable<SubmoduleTreeNode> nodes,
            HashSet<string> oldExpanded,
            HashSet<string> oldExpandable)
        {
            foreach (var node in nodes)
            {
                if (node.HasChildren)
                {
                    node.IsExpanded = oldExpandable.Contains(node.FullPath) ?
                        oldExpanded.Contains(node.FullPath) :
                        true;
                }

                ApplyExpansionDefaults(node.Children, oldExpanded, oldExpandable);
            }
        }

        private static void InsertNode(List<SubmoduleTreeNode> collection, SubmoduleTreeNode node)
        {
            if (!node.IsFolder)
            {
                collection.Add(node);
                return;
            }

            for (int i = 0; i < collection.Count; i++)
            {
                if (!collection[i].IsFolder)
                {
                    collection.Insert(i, node);
                    return;
                }
            }

            collection.Add(node);
        }

        private bool _isExpanded = false;
    }

    public class SubmoduleCollectionAsTree
    {
        public List<SubmoduleTreeNode> Tree
        {
            get;
            set;
        } = [];

        public AvaloniaList<SubmoduleTreeNode> Rows
        {
            get;
            set;
        } = [];

        public static SubmoduleCollectionAsTree Build(List<Models.Submodule> submodules, SubmoduleCollectionAsTree old)
        {
            var oldExpanded = new HashSet<string>();
            var oldExpandable = new HashSet<string>();
            if (old != null)
                SubmoduleTreeNode.CollectExpandableState(old.Tree, oldExpanded, oldExpandable);

            var collection = new SubmoduleCollectionAsTree();
            collection.Tree = SubmoduleTreeNode.Build(submodules, oldExpanded, oldExpandable);

            var rows = new List<SubmoduleTreeNode>();
            MakeTreeRows(rows, collection.Tree);
            collection.Rows.AddRange(rows);

            return collection;
        }

        public void ToggleExpand(SubmoduleTreeNode node)
        {
            if (!node.HasChildren)
                return;

            node.IsExpanded = !node.IsExpanded;

            var rows = Rows;
            var depth = node.Depth;
            var idx = rows.IndexOf(node);
            if (idx == -1)
                return;

            if (node.IsExpanded)
            {
                var subrows = new List<SubmoduleTreeNode>();
                MakeTreeRows(subrows, node.Children);
                rows.InsertRange(idx + 1, subrows);
            }
            else
            {
                var removeCount = 0;
                for (int i = idx + 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.Depth <= depth)
                        break;

                    removeCount++;
                }
                rows.RemoveRange(idx + 1, removeCount);
            }
        }

        private static void MakeTreeRows(List<SubmoduleTreeNode> rows, List<SubmoduleTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node);

                if (!node.IsExpanded || !node.HasChildren)
                    continue;

                MakeTreeRows(rows, node.Children);
            }
        }
    }

    public class SubmoduleCollectionAsList
    {
        public List<Models.Submodule> Submodules
        {
            get;
            set;
        } = [];
    }
}
