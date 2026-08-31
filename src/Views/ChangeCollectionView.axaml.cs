using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class ChangeTreeNodeToggleButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                DataContext is ViewModels.ChangeTreeNode { IsFolder: true } node)
            {
                var container = this.FindAncestorOfType<ChangeCollectionContainer>();
                if (container != null)
                    container.SelectedItem = node;

                var tree = this.FindAncestorOfType<ChangeCollectionView>();
                tree?.ToggleNodeIsExpanded(node);
            }

            e.Handled = true;
        }
    }

    public class ChangeCollectionContainer : ListBoxEx
    {
        protected override Type StyleKeyOverride => typeof(ListBox);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
            {
                var owner = this.FindAncestorOfType<ChangeCollectionView>();
                if (owner?.ToggleCommitIncludeForSelectedItems(this) == true)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (SelectedItems is [ViewModels.ChangeTreeNode node] && e.KeyModifiers == KeyModifiers.None)
            {
                if (e.Key == Key.Left)
                {
                    if (node.IsExpanded && node.IsFolder)
                    {
                        this.FindAncestorOfType<ChangeCollectionView>()?.ToggleNodeIsExpanded(node);
                        e.Handled = true;
                    }
                    else if (FindParent(node) is { } parent)
                    {
                        Select(parent);
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Right && node.IsFolder)
                {
                    if (!node.IsExpanded)
                    {
                        this.FindAncestorOfType<ChangeCollectionView>()?.ToggleNodeIsExpanded(node);
                        e.Handled = true;
                    }
                    else if (node.Children.Count > 0)
                    {
                        Select(node.Children[0]);
                        e.Handled = true;
                    }
                }
            }

            if (!e.Handled)
                base.OnKeyDown(e);
        }

        private ViewModels.ChangeTreeNode FindParent(ViewModels.ChangeTreeNode item)
        {
            if (item.Depth == 0)
                return null;

            var idx = Items.IndexOf(item);
            if (idx < 1)
                return null;

            for (var i = idx - 1; i >= 0; i--)
            {
                if (Items[i] is ViewModels.ChangeTreeNode node && node.Depth < item.Depth)
                    return node;
            }

            return null;
        }
    }

    public partial class ChangeCollectionView : UserControl
    {
        public static readonly DirectProperty<ChangeCollectionView, bool> IsUnstagedChangeProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, bool>(
                nameof(IsUnstagedChange),
                static o => o.IsUnstagedChange,
                static (o, v) => o.IsUnstagedChange = v);

        public bool IsUnstagedChange
        {
            get => _isUnstagedChange;
            set => SetAndRaise(IsUnstagedChangeProperty, ref _isUnstagedChange, value);
        }

        public static readonly DirectProperty<ChangeCollectionView, Models.ChangeViewMode> ViewModeProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, Models.ChangeViewMode>(
                nameof(ViewMode),
                static o => o.ViewMode,
                static (o, v) => o.ViewMode = v);

        public Models.ChangeViewMode ViewMode
        {
            get => _viewMode;
            set => SetAndRaise(ViewModeProperty, ref _viewMode, value);
        }

        public static readonly DirectProperty<ChangeCollectionView, bool> EnableCompactFoldersProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, bool>(
                nameof(EnableCompactFolders),
                static o => o.EnableCompactFolders,
                static (o, v) => o.EnableCompactFolders = v);

        public bool EnableCompactFolders
        {
            get => _enableCompactFolders;
            set => SetAndRaise(EnableCompactFoldersProperty, ref _enableCompactFolders, value);
        }

        public static readonly DirectProperty<ChangeCollectionView, bool> ShowCommitIncludeCheckBoxesProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, bool>(
                nameof(ShowCommitIncludeCheckBoxes),
                static o => o.ShowCommitIncludeCheckBoxes,
                static (o, v) => o.ShowCommitIncludeCheckBoxes = v);

        public bool ShowCommitIncludeCheckBoxes
        {
            get => _showCommitIncludeCheckBoxes;
            set => SetAndRaise(ShowCommitIncludeCheckBoxesProperty, ref _showCommitIncludeCheckBoxes, value);
        }

        public static readonly DirectProperty<ChangeCollectionView, List<Models.Change>> ChangesProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, List<Models.Change>>(
                nameof(Changes),
                static o => o.Changes,
                static (o, v) => o.Changes = v);

        public List<Models.Change> Changes
        {
            get => _changes;
            set => SetAndRaise(ChangesProperty, ref _changes, value);
        }

        public static readonly DirectProperty<ChangeCollectionView, ViewModels.ChangeSelection> SelectionProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, ViewModels.ChangeSelection>(
                nameof(Selection),
                static o => o.Selection,
                static (o, v) => o.Selection = v);

        public ViewModels.ChangeSelection Selection
        {
            get => _selection;
            set => SetAndRaise(SelectionProperty, ref _selection, value);
        }

        public static readonly DirectProperty<ChangeCollectionView, List<Models.Change>> SelectedChangesProperty =
            AvaloniaProperty.RegisterDirect<ChangeCollectionView, List<Models.Change>>(
                nameof(SelectedChanges),
                static o => o.SelectedChanges,
                static (o, v) => o.SelectedChanges = v);

        public List<Models.Change> SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetAndRaise(SelectedChangesProperty, ref _selectedChanges, value))
                    Selection = new(value);
            }
        }

        public KeyModifiers LastDoubleTappedKeyModifiers
        {
            get;
            private set;
        } = KeyModifiers.None;

        public static readonly RoutedEvent<RoutedEventArgs> ChangeDoubleTappedEvent =
            RoutedEvent.Register<ChangeCollectionView, RoutedEventArgs>(nameof(ChangeDoubleTapped), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public static readonly RoutedEvent<RoutedEventArgs> CommitIncludeToggledEvent =
            RoutedEvent.Register<ChangeCollectionView, RoutedEventArgs>(nameof(CommitIncludeToggled), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> ChangeDoubleTapped
        {
            add { AddHandler(ChangeDoubleTappedEvent, value); }
            remove { RemoveHandler(ChangeDoubleTappedEvent, value); }
        }

        public event EventHandler<RoutedEventArgs> CommitIncludeToggled
        {
            add { AddHandler(CommitIncludeToggledEvent, value); }
            remove { RemoveHandler(CommitIncludeToggledEvent, value); }
        }

        public ChangeCollectionView()
        {
            InitializeComponent();
        }

        public void ToggleNodeIsExpanded(ViewModels.ChangeTreeNode node)
        {
            if (Content is ViewModels.ChangeCollectionAsTree tree && node.IsFolder)
            {
                node.IsExpanded = !node.IsExpanded;

                var depth = node.Depth;
                var idx = tree.Rows.IndexOf(node);
                if (idx == -1)
                    return;

                if (node.IsExpanded)
                {
                    var subrows = new List<ViewModels.ChangeTreeNode>();
                    MakeTreeRows(subrows, node.Children);
                    tree.Rows.InsertRange(idx + 1, subrows);
                }
                else
                {
                    var removeCount = 0;
                    for (int i = idx + 1; i < tree.Rows.Count; i++)
                    {
                        var row = tree.Rows[i];
                        if (row.Depth <= depth)
                            break;

                        removeCount++;
                    }

                    tree.Rows.RemoveRange(idx + 1, removeCount);
                }
            }
        }

        public Models.Change GetNextChangeWithoutSelection()
        {
            var selected = _selection.Changes;
            var changes = Changes;
            if (selected == null || selected.Count == 0)
                return changes.Count > 0 ? changes[0] : null;
            if (selected.Count == changes.Count)
                return null;

            var set = new HashSet<string>();
            foreach (var c in selected)
            {
                if (!c.IsConflicted)
                    set.Add(c.Path);
            }

            if (Content is ViewModels.ChangeCollectionAsTree tree)
            {
                var lastUnselected = -1;
                for (int i = tree.Rows.Count - 1; i >= 0; i--)
                {
                    var row = tree.Rows[i];
                    if (!row.IsFolder)
                    {
                        if (set.Contains(row.FullPath))
                        {
                            if (lastUnselected == -1)
                                continue;

                            break;
                        }

                        lastUnselected = i;
                    }
                }

                if (lastUnselected != -1)
                    return tree.Rows[lastUnselected].Change;
            }
            else
            {
                var lastUnselected = -1;
                for (int i = changes.Count - 1; i >= 0; i--)
                {
                    if (set.Contains(changes[i].Path))
                    {
                        if (lastUnselected == -1)
                            continue;

                        break;
                    }

                    lastUnselected = i;
                }

                if (lastUnselected != -1)
                    return changes[lastUnselected];
            }

            return null;
        }

        public void TakeFocus()
        {
            var container = this.FindDescendantOfType<ChangeCollectionContainer>();
            if (container == null)
                return;

            if (container.SelectedItem == null && container.Items.Count > 0)
            {
                var first = container.Items[0];
                container.SelectedItem = first;
                container.ScrollIntoView(first);
            }

            if (!container.IsFocused)
                container.Focus(NavigationMethod.Tab);
        }

        public bool ToggleCommitIncludeForSelectedItems(ListBox list)
        {
            if (!ShowCommitIncludeCheckBoxes || list.SelectedItems is not { Count: > 0 } selectedItems)
                return false;

            var changes = new List<Models.Change>();
            foreach (var item in selectedItems)
            {
                if (item is Models.Change change)
                    AddChangeIfMissing(changes, change);
                else if (item is ViewModels.ChangeTreeNode node)
                    CollectChangesInNode(changes, node);
            }

            if (changes.Count == 0)
                return false;

            var include = changes.Exists(x => !x.IsCommitFlowIncluded);
            foreach (var change in changes)
                change.IsCommitFlowIncluded = include;

            RaiseEvent(new RoutedEventArgs(CommitIncludeToggledEvent));
            return true;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ViewModeProperty)
                UpdateDataSource(true);
            else if (change.Property == ChangesProperty)
                UpdateDataSource(false);
            else if (change.Property == SelectionProperty)
                UpdateSelection();

            if (change.Property == EnableCompactFoldersProperty && ViewMode == Models.ChangeViewMode.Tree)
                UpdateDataSource(true);
        }

        private void OnRowDataContextChanged(object sender, EventArgs e)
        {
            if (sender is not Control { DataContext: { } ctx } control)
                return;

            if (ctx is ViewModels.ChangeTreeNode node)
            {
                if (node.Change is { } c)
                    UpdateRowTips(control, c);
                else
                    ToolTip.SetTip(control, node.FullPath);
            }
            else if (ctx is Models.Change change)
            {
                UpdateRowTips(control, change);
            }
            else
            {
                ToolTip.SetTip(control, null);
            }
        }

        private void OnRowDoubleTapped(object sender, TappedEventArgs e)
        {
            if (sender is not Control { DataContext: { } ctx })
                return;

            LastDoubleTappedKeyModifiers = e.KeyModifiers;

            if (ctx is ViewModels.ChangeTreeNode node)
            {
                if (node.IsFolder)
                {
                    var posX = e.GetPosition(this).X;
                    if (posX < node.Depth * 16 + 16)
                        return;

                    ToggleNodeIsExpanded(node);
                }
                else
                {
                    RaiseEvent(new RoutedEventArgs(ChangeDoubleTappedEvent));
                }
            }
            else if (ctx is Models.Change)
            {
                RaiseEvent(new RoutedEventArgs(ChangeDoubleTappedEvent));
            }
        }

        private void OnCommitIncludeCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox)
            {
                var included = checkbox.IsChecked == true;
                if (checkbox.DataContext is Models.Change change)
                    change.IsCommitFlowIncluded = included;
                else if (checkbox.DataContext is ViewModels.ChangeTreeNode { Change: { } nodeChange })
                    nodeChange.IsCommitFlowIncluded = included;
            }

            RaiseEvent(new RoutedEventArgs(CommitIncludeToggledEvent));
        }

        private void OnRowSelectionChanged(object sender, SelectionChangedEventArgs _)
        {
            if (_disableSelectionChangingEvent || sender is not ListBox listBox)
                return;

            _disableSelectionChangingEvent = true;

            var selection = new ViewModels.ChangeSelection(listBox.SelectedItems);
            if (selection.IsChanged(_selection))
            {
                Selection = selection;
                SetAndRaise(SelectedChangesProperty, ref _selectedChanges, selection.Changes);
            }

            _disableSelectionChangingEvent = false;
        }

        private void MakeTreeRows(List<ViewModels.ChangeTreeNode> rows, List<ViewModels.ChangeTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node);

                if (!node.IsExpanded || !node.IsFolder)
                    continue;

                MakeTreeRows(rows, node.Children);
            }
        }

        private void UpdateDataSource(bool onlyViewModeChange)
        {
            _disableSelectionChangingEvent = !onlyViewModeChange;

            var changes = _changes;
            if (changes == null || changes.Count == 0)
            {
                Content = null;
                _disableSelectionChangingEvent = false;
                return;
            }

            var selected = _selection?.Changes ?? [];
            if (ViewMode == Models.ChangeViewMode.Tree)
            {
                var oldFolded = new HashSet<string>();
                if (Content is ViewModels.ChangeCollectionAsTree oldTree)
                {
                    foreach (var row in oldTree.Rows)
                    {
                        if (row.IsFolder && !row.IsExpanded)
                            oldFolded.Add(row.FullPath);
                    }
                }

                var tree = new ViewModels.ChangeCollectionAsTree();
                tree.Tree = ViewModels.ChangeTreeNode.Build(changes, oldFolded, EnableCompactFolders);

                var rows = new List<ViewModels.ChangeTreeNode>();
                MakeTreeRows(rows, tree.Tree);
                tree.Rows.AddRange(rows);

                if (selected.Count > 0)
                {
                    var sets = new HashSet<Models.Change>(selected);
                    var nodes = new List<ViewModels.ChangeTreeNode>();
                    foreach (var row in tree.Rows)
                    {
                        if (row.Change != null && sets.Contains(row.Change))
                            nodes.Add(row);
                    }

                    tree.SelectedRows.AddRange(nodes);
                }

                Content = tree;
            }
            else if (ViewMode == Models.ChangeViewMode.Grid)
            {
                var grid = new ViewModels.ChangeCollectionAsGrid();
                grid.Changes.AddRange(changes);
                if (selected.Count > 0)
                    grid.SelectedChanges.AddRange(selected);

                Content = grid;
            }
            else
            {
                var list = new ViewModels.ChangeCollectionAsList();
                list.Changes.AddRange(changes);
                if (selected.Count > 0)
                    list.SelectedChanges.AddRange(selected);

                Content = list;
            }

            _disableSelectionChangingEvent = false;
        }

        private void UpdateSelection()
        {
            if (_disableSelectionChangingEvent)
                return;

            _disableSelectionChangingEvent = true;

            var selected = _selection?.Changes ?? [];
            if (Content is ViewModels.ChangeCollectionAsTree tree)
            {
                tree.SelectedRows.Clear();

                if (selected.Count > 0)
                {
                    var sets = new HashSet<Models.Change>(selected);
                    var nodes = new List<ViewModels.ChangeTreeNode>();
                    foreach (var row in tree.Rows)
                    {
                        if (row.Change != null && sets.Contains(row.Change))
                            nodes.Add(row);
                    }

                    tree.SelectedRows.AddRange(nodes);
                }
            }
            else if (Content is ViewModels.ChangeCollectionAsGrid grid)
            {
                grid.SelectedChanges.Clear();
                if (selected.Count > 0)
                    grid.SelectedChanges.AddRange(selected);
            }
            else if (Content is ViewModels.ChangeCollectionAsList list)
            {
                list.SelectedChanges.Clear();
                if (selected.Count > 0)
                    list.SelectedChanges.AddRange(selected);
            }

            _disableSelectionChangingEvent = false;
        }

        private void CollectChangesInNode(List<Models.Change> outs, ViewModels.ChangeTreeNode node)
        {
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                    CollectChangesInNode(outs, child);
            }
            else if (!outs.Contains(node.Change))
            {
                outs.Add(node.Change);
            }
        }

        private static void AddChangeIfMissing(List<Models.Change> outs, Models.Change change)
        {
            if (!outs.Contains(change))
                outs.Add(change);
        }

        private void UpdateRowTips(Control control, Models.Change change)
        {
            var tip = new TextBlock() { TextWrapping = TextWrapping.Wrap };
            tip.Inlines!.Add(new Run(change.Path));
            tip.Inlines!.Add(new Run(" • ") { Foreground = Brushes.Gray });
            tip.Inlines!.Add(new Run(GetDisplayChangeStateDesc(change, IsUnstagedChange)) { Foreground = Brushes.Gray });
            if (change.IsConflicted)
            {
                tip.Inlines!.Add(new Run(" • ") { Foreground = Brushes.Gray });
                tip.Inlines!.Add(new Run(change.ConflictDesc) { Foreground = Brushes.Gray });
            }

            ToolTip.SetTip(control, tip);
        }

        private static string GetDisplayChangeStateDesc(Models.Change change, bool isUnstagedChange)
        {
            var state = isUnstagedChange ? change.WorkTree : change.Index;
            if (state == Models.ChangeState.None)
                state = change.WorkTree != Models.ChangeState.None ? change.WorkTree : change.Index;

            return state switch
            {
                Models.ChangeState.Modified => "Modified",
                Models.ChangeState.TypeChanged => "Type Changed",
                Models.ChangeState.Added => "Added",
                Models.ChangeState.Deleted => "Deleted",
                Models.ChangeState.Renamed => "Renamed",
                Models.ChangeState.Copied => "Copied",
                Models.ChangeState.Untracked => "Untracked",
                Models.ChangeState.Conflicted => "Conflict",
                _ => "Unknown",
            };
        }

        private bool _isUnstagedChange = false;
        private Models.ChangeViewMode _viewMode = Models.ChangeViewMode.Tree;
        private bool _enableCompactFolders = false;
        private bool _showCommitIncludeCheckBoxes = false;
        private List<Models.Change> _changes = null;
        private ViewModels.ChangeSelection _selection = new(null);
        private List<Models.Change> _selectedChanges = [];
        private bool _disableSelectionChangingEvent = false;
    }
}
