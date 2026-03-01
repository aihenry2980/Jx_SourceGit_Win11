using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class HistoriesLayout : Grid
    {
        public static readonly StyledProperty<bool> UseHorizontalProperty =
            AvaloniaProperty.Register<HistoriesLayout, bool>(nameof(UseHorizontal));

        public bool UseHorizontal
        {
            get => GetValue(UseHorizontalProperty);
            set => SetValue(UseHorizontalProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(Grid);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == UseHorizontalProperty && IsLoaded)
                RefreshLayout();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            RefreshLayout();
        }

        private void RefreshLayout()
        {
            if (UseHorizontal)
            {
                var rowSpan = RowDefinitions.Count;
                for (int i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    child.SetValue(RowProperty, 0);
                    child.SetValue(RowSpanProperty, rowSpan);
                    child.SetValue(ColumnProperty, i);
                    child.SetValue(ColumnSpanProperty, 1);

                    if (child is GridSplitter splitter)
                        splitter.BorderThickness = new Thickness(1, 0, 0, 0);
                }
            }
            else
            {
                var colSpan = ColumnDefinitions.Count;
                for (int i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    child.SetValue(RowProperty, i);
                    child.SetValue(RowSpanProperty, 1);
                    child.SetValue(ColumnProperty, 0);
                    child.SetValue(ColumnSpanProperty, colSpan);

                    if (child is GridSplitter splitter)
                        splitter.BorderThickness = new Thickness(0, 1, 0, 0);
                }
            }
        }
    }

    public partial class Histories : UserControl
    {
        public static readonly StyledProperty<Models.Branch> CurrentBranchProperty =
            AvaloniaProperty.Register<Histories, Models.Branch>(nameof(CurrentBranch));

        public Models.Branch CurrentBranch
        {
            get => GetValue(CurrentBranchProperty);
            set => SetValue(CurrentBranchProperty, value);
        }

        public static readonly StyledProperty<Models.Bisect> BisectProperty =
            AvaloniaProperty.Register<Histories, Models.Bisect>(nameof(Bisect));

        public Models.Bisect Bisect
        {
            get => GetValue(BisectProperty);
            set => SetValue(BisectProperty, value);
        }

        public static readonly StyledProperty<AvaloniaList<Models.IssueTracker>> IssueTrackersProperty =
            AvaloniaProperty.Register<Histories, AvaloniaList<Models.IssueTracker>>(nameof(IssueTrackers));

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get => GetValue(IssueTrackersProperty);
            set => SetValue(IssueTrackersProperty, value);
        }

        public static readonly StyledProperty<bool> OnlyHighlightCurrentBranchProperty =
            AvaloniaProperty.Register<Histories, bool>(nameof(OnlyHighlightCurrentBranch), true);

        public bool OnlyHighlightCurrentBranch
        {
            get => GetValue(OnlyHighlightCurrentBranchProperty);
            set => SetValue(OnlyHighlightCurrentBranchProperty, value);
        }

        public static readonly StyledProperty<long> NavigationIdProperty =
            AvaloniaProperty.Register<Histories, long>(nameof(NavigationId));

        public long NavigationId
        {
            get => GetValue(NavigationIdProperty);
            set => SetValue(NavigationIdProperty, value);
        }

        public static readonly StyledProperty<bool> IsScrollToTopVisibleProperty =
            AvaloniaProperty.Register<Histories, bool>(nameof(IsScrollToTopVisible));

        public bool IsScrollToTopVisible
        {
            get => GetValue(IsScrollToTopVisibleProperty);
            set => SetValue(IsScrollToTopVisibleProperty, value);
        }

        public Histories()
        {
            InitializeComponent();
            CommitListContainer.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnCommitListPointerWheelChanged,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                true);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == NavigationIdProperty)
            {
                if (CommitListContainer is { SelectedItems.Count: 1, IsLoaded: true } dataGrid)
                    CenterCommitInViewport(dataGrid, dataGrid.SelectedItem);
            }
        }

        private void OnCommitListLoaded(object sender, RoutedEventArgs e)
        {
            var dataGrid = CommitListContainer;
            var rowsPresenter = dataGrid.FindDescendantOfType<DataGridRowsPresenter>();
            if (rowsPresenter is { Children: { Count: > 0 } rows } &&
                TryGetGraphColumnLayout(dataGrid, out var graphOffsetX, out var graphClipWidth))
            {
                var rowHeight = dataGrid.RowHeight;
                if (rowHeight <= 0 || double.IsNaN(rowHeight))
                    rowHeight = rows[0].Bounds.Height;

                CommitGraph.Layout = new(0, graphClipWidth, rowHeight, graphOffsetX);
            }

            if (dataGrid.SelectedItems.Count == 1)
                dataGrid.ScrollIntoView(dataGrid.SelectedItem, null);

            _pendingEnsureHeadVisibleRetries = 6;
            TryEnsureHeadVisibleInViewport();
        }

        private async void OnGotoParent(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Histories vm)
                return;

            if (!CommitListContainer.IsKeyboardFocusWithin)
                return;

            if (CommitListContainer.SelectedItems is not { Count: 1 } selected)
                return;

            if (selected[0] is not Models.Commit { Parents.Count: > 0 } commit)
                return;

            if (commit.Parents.Count == 1)
            {
                vm.NavigateTo(commit.Parents[0]);
                e.Handled = true;
                return;
            }

            var parents = new List<Models.Commit>();
            foreach (var sha in commit.Parents)
            {
                var c = await vm.GetCommitAsync(sha);
                if (c != null)
                    parents.Add(c);
            }

            if (parents.Count == 1)
            {
                vm.NavigateTo(parents[0].SHA);
            }
            else if (parents.Count > 1 && TopLevel.GetTopLevel(this) is Window owner)
            {
                var dialog = new GotoParentSelector();
                dialog.ParentList.ItemsSource = parents;

                var c = await dialog.ShowDialog<Models.Commit>(owner);
                if (c != null)
                    vm.NavigateTo(c.SHA);
            }

            e.Handled = true;
        }

        private void OnCommitListLayoutUpdated(object _1, EventArgs _2)
        {
            if (!IsLoaded)
                return;

            if (DataContext is ViewModels.Histories histories)
            {
                if (_lastHistoriesIsLoading && !histories.IsLoading)
                    _pendingEnsureHeadVisibleRetries = 6;

                _lastHistoriesIsLoading = histories.IsLoading;
            }

            var dataGrid = CommitListContainer;
            var rowsPresenter = dataGrid.FindDescendantOfType<DataGridRowsPresenter>();
            if (rowsPresenter == null)
                return;

            var rowHeight = dataGrid.RowHeight;
            if (rowHeight <= 0 || double.IsNaN(rowHeight))
                rowHeight = 24;

            double startY = 0;
            foreach (var child in rowsPresenter.Children)
            {
                if (child is DataGridRow { IsVisible: true } row)
                {
                    if (row.Bounds.Top <= 0 && row.Bounds.Top > -rowHeight)
                    {
                        var test = rowHeight * row.Index - row.Bounds.Top;
                        if (startY < test)
                            startY = test;
                    }
                }
            }

            SetCurrentValue(IsScrollToTopVisibleProperty, startY >= rowHeight);

            if (!TryGetGraphColumnLayout(dataGrid, out var graphOffsetX, out var clipWidth))
                return;

            if (Math.Abs(_lastGraphStartY - startY) > 0.01 ||
                Math.Abs(_lastGraphClipWidth - clipWidth) > 0.01 ||
                Math.Abs(_lastGraphRowHeight - rowHeight) > 0.01 ||
                Math.Abs(_lastGraphOffsetX - graphOffsetX) > 0.01)
            {
                _lastGraphStartY = startY;
                _lastGraphClipWidth = clipWidth;
                _lastGraphRowHeight = rowHeight;
                _lastGraphOffsetX = graphOffsetX;

                CommitGraph.Layout = new(startY, clipWidth, rowHeight, graphOffsetX);
            }

            if (_pendingEnsureHeadVisibleRetries > 0)
            {
                if (TryEnsureHeadVisibleInViewport())
                    _pendingEnsureHeadVisibleRetries = 0;
                else
                    _pendingEnsureHeadVisibleRetries--;
            }
        }

        private static bool TryGetGraphColumnLayout(DataGrid dataGrid, out double offsetX, out double clipWidth)
        {
            offsetX = 0;
            clipWidth = 0;
            if (dataGrid == null || dataGrid.Columns.Count == 0)
                return false;

            var graphColumnIndex = -1;
            for (var i = 0; i < dataGrid.Columns.Count; i++)
            {
                var col = dataGrid.Columns[i];
                if (!col.IsVisible)
                    continue;

                // Graph&Subject is the only visible star-sized column.
                if (col.Width.UnitType == DataGridLengthUnitType.Star)
                {
                    graphColumnIndex = i;
                    break;
                }
            }

            if (graphColumnIndex < 0)
                return false;

            for (var i = 0; i < graphColumnIndex; i++)
            {
                var col = dataGrid.Columns[i];
                if (col.IsVisible)
                    offsetX += GetEffectiveColumnWidth(col);
            }

            clipWidth = Math.Max(0, GetEffectiveColumnWidth(dataGrid.Columns[graphColumnIndex]) - 4);
            return clipWidth > 0;
        }

        private static double GetEffectiveColumnWidth(DataGridColumn col)
        {
            var width = col.ActualWidth;
            if (width > 0.01)
                return width;

            width = col.Width.DisplayValue;
            if (width > 0.01)
                return width;

            return Math.Max(0, col.MinWidth);
        }

        private void OnScrollToTopPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.Histories histories)
                CenterCommitInViewport(CommitListContainer, histories.Commits[0]);
        }

        private bool CenterCommitInViewport(DataGrid dataGrid, object target)
        {
            if (dataGrid == null || target == null)
                return false;

            dataGrid.ScrollIntoView(target, null);

            var scrollViewer = dataGrid.FindDescendantOfType<ScrollViewer>();
            var rowsPresenter = dataGrid.FindDescendantOfType<DataGridRowsPresenter>();
            if (scrollViewer == null || rowsPresenter == null)
                return false;

            DataGridRow row = null;
            foreach (var child in rowsPresenter.Children)
            {
                if (child is DataGridRow c && ReferenceEquals(c.DataContext, target))
                {
                    row = c;
                    break;
                }
            }

            if (row == null || !row.IsVisible)
                return false;

            var rowHeight = dataGrid.RowHeight;
            if (rowHeight <= 0 || double.IsNaN(rowHeight))
                rowHeight = row.Bounds.Height;
            if (rowHeight <= 0 || double.IsNaN(rowHeight))
                return false;

            var viewportHeight = rowsPresenter.Bounds.Height;
            if (viewportHeight <= 0 || double.IsNaN(viewportHeight))
                viewportHeight = scrollViewer.Viewport.Height;
            var extentHeight = scrollViewer.Extent.Height;
            if (viewportHeight <= 0 || extentHeight <= 0)
                return false;

            // Center inside the commit rows viewport (history graph area), not the whole window.
            var centerY = row.Index * rowHeight + rowHeight * 0.5;
            var targetOffsetY = centerY - viewportHeight * 0.5;
            var maxOffsetY = Math.Max(0, extentHeight - viewportHeight);
            var clampedOffsetY = Math.Clamp(targetOffsetY, 0, maxOffsetY);

            if (Math.Abs(scrollViewer.Offset.Y - clampedOffsetY) > 0.5)
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, clampedOffsetY);

            return true;
        }

        private bool TryEnsureHeadVisibleInViewport()
        {
            if (_isCenteringHeadCommit || DataContext is not ViewModels.Histories histories || histories.IsLoading)
                return false;

            Models.Commit head = null;
            foreach (var commit in histories.Commits)
            {
                if (commit.IsCurrentHead)
                {
                    head = commit;
                    break;
                }
            }

            if (head == null)
            {
                return true;
            }

            _isCenteringHeadCommit = true;
            try
            {
                var dataGrid = CommitListContainer;
                dataGrid.ScrollIntoView(head, null);

                var rowsPresenter = dataGrid.FindDescendantOfType<DataGridRowsPresenter>();
                if (rowsPresenter == null)
                    return false;

                foreach (var child in rowsPresenter.Children)
                {
                    if (child is DataGridRow row && ReferenceEquals(row.DataContext, head) && row.IsVisible)
                    {
                        var viewportHeight = rowsPresenter.Bounds.Height;
                        return row.Bounds.Bottom > 0 && row.Bounds.Top < viewportHeight;
                    }
                }

                return false;
            }
            finally
            {
                _isCenteringHeadCommit = false;
            }
        }

        private void OnCommitListPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            var zoomKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (!e.KeyModifiers.HasFlag(zoomKey))
                return;

            // Some mice/touchpads report very small values on one axis.
            // Use whichever axis has larger absolute value, and accept any non-zero delta.
            var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
            if (Math.Abs(delta) <= double.Epsilon)
                return;

            var pref = ViewModels.Preferences.Instance;
            var step = delta > 0 ? 0.05 : -0.05;
            var next = pref.HistoriesZoom + step;
            pref.HistoriesZoom = Math.Clamp(next, 0.75, 2.50);
            e.Handled = true;
        }

        private void OnCommitRefsPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not CommitRefsPresenter presenter)
                return;

            var point = e.GetCurrentPoint(presenter);
            if (point.Properties.IsLeftButtonPressed &&
                presenter.TryGetFoldableDecoratorAt(e.GetPosition(presenter), out var foldDecorator) &&
                TryGetBranchByDecorator(foldDecorator, out var foldRepo, out var foldBranch))
            {
                foldRepo.ToggleFoldBranch(foldBranch);
                _pressedCommitRef = false;
                _startDragCommitRef = false;
                _pressedCommitRefBranchName = string.Empty;
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed || !TryGetBranchNameAtPoint(presenter, e.GetPosition(presenter), out var name))
            {
                _pressedCommitRef = false;
                _startDragCommitRef = false;
                _pressedCommitRefBranchName = string.Empty;
                return;
            }

            _pressedCommitRef = true;
            _startDragCommitRef = false;
            _pressedCommitRefPosition = e.GetPosition(presenter);
            _pressedCommitRefBranchName = name;
        }

        private async void OnCommitRefsPointerMoved(object sender, PointerEventArgs e)
        {
            if (!_pressedCommitRef || _startDragCommitRef || string.IsNullOrEmpty(_pressedCommitRefBranchName))
                return;

            if (sender is not CommitRefsPresenter presenter)
                return;

            var delta = e.GetPosition(presenter) - _pressedCommitRefPosition;
            var sizeSquared = delta.X * delta.X + delta.Y * delta.Y;
            if (sizeSquared < 64)
                return;

            _startDragCommitRef = true;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(_dndPresetBranchNameFormat, _pressedCommitRefBranchName));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);

            _pressedCommitRef = false;
            _startDragCommitRef = false;
            _pressedCommitRefBranchName = string.Empty;
        }

        private void OnCommitRefsPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            _pressedCommitRef = false;
            _startDragCommitRef = false;
            _pressedCommitRefBranchName = string.Empty;
        }

        private void OnCommitRefsDragOver(object sender, DragEventArgs e)
        {
            if (sender is not CommitRefsPresenter presenter ||
                !TryGetRebaseDragSource(e, out _, out _) ||
                !TryGetBranchAtPoint(presenter, e.GetPosition(presenter), out _, out _))
                e.DragEffects = DragDropEffects.None;
            else
                e.DragEffects = DragDropEffects.Copy;

            e.Handled = true;
        }

        private async void OnCommitRefsDrop(object sender, DragEventArgs e)
        {
            if (sender is not CommitRefsPresenter presenter ||
                !TryGetRebaseBranchDropTargets(presenter, e, out var repo, out var source, out var target))
            {
                e.Handled = true;
                return;
            }

            if (repo.CanCreatePopup())
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    var to = await new Commands.QuerySingleCommit(repo.FullPath, target.Head).GetResultAsync();
                    if (to != null)
                    {
                        var reset = new ViewModels.Reset(repo, source, to)
                        {
                            SelectedMode = Models.ResetMode.Supported[^1], // hard
                        };
                        repo.ShowPopup(reset);
                    }
                }
                else
                {
                    repo.ShowPopup(new ViewModels.Rebase(repo, source, target));
                }
            }

            e.Handled = true;
        }

        private void OnCommitSubjectDragOver(object sender, DragEventArgs e)
        {
            if (sender is not Control { DataContext: Models.Commit commit } ||
                string.IsNullOrWhiteSpace(commit.SHA) ||
                !TryGetRebaseDragSource(e, out _, out _))
                e.DragEffects = DragDropEffects.None;
            else
                e.DragEffects = DragDropEffects.Copy;

            e.Handled = true;
        }

        private void OnCommitSubjectDrop(object sender, DragEventArgs e)
        {
            if (sender is not Control { DataContext: Models.Commit commit } ||
                string.IsNullOrWhiteSpace(commit.SHA) ||
                !TryGetRebaseDragSource(e, out var repo, out var source))
            {
                e.Handled = true;
                return;
            }

            if (repo.CanCreatePopup())
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    var reset = new ViewModels.Reset(repo, source, commit)
                    {
                        SelectedMode = Models.ResetMode.Supported[^1], // hard
                    };
                    repo.ShowPopup(reset);
                }
                else
                {
                    repo.ShowPopup(new ViewModels.Rebase(repo, source, commit));
                }
            }

            e.Handled = true;
        }

        private void OnCommitListSelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (DataContext is ViewModels.Histories histories)
                histories.Select(CommitListContainer.SelectedItems);

            e.Handled = true;
        }

        private void OnCommitListContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (e.Source is Control { DataContext: Models.Commit })
            {
                var repoView = this.FindAncestorOfType<Repository>();
                if (repoView is not { DataContext: ViewModels.Repository repo })
                    return;

                var selected = CommitListContainer.SelectedItems;
                if (selected is not { Count: > 0 })
                    return;

                var commits = new List<Models.Commit>();
                for (var i = selected.Count - 1; i >= 0; i--)
                {
                    if (selected[i] is Models.Commit c)
                        commits.Add(c);
                }

                if (selected.Count > 1)
                {
                    var menu = CreateContextMenuForMultipleCommits(repo, commits);
                    menu.Open(CommitListContainer);
                }
                else if (selected.Count == 1)
                {
                    var menu = CreateContextMenuForSingleCommit(repo, commits[0]);
                    menu.Open(CommitListContainer);
                }
            }
            else if (e.Source is Control elem)
            {
                var headersPresenter = CommitListContainer.FindDescendantOfType<DataGridColumnHeadersPresenter>();
                if (!headersPresenter.IsVisualAncestorOf(elem))
                    return;

                if (DataContext is not ViewModels.Histories vm)
                    return;

                var columnsHeader = new MenuItem();
                columnsHeader.Header = new TextBlock() { Text = App.Text("Histories.ShowColumns"), FontWeight = FontWeight.Bold };
                columnsHeader.IsEnabled = false;

                var authorColumn = new MenuItem();
                authorColumn.Header = App.Text("Histories.Header.Author");
                if (vm.IsAuthorColumnVisible)
                    authorColumn.Icon = App.CreateMenuIcon("Icons.Check");
                authorColumn.Click += (_, ev) =>
                {
                    vm.IsAuthorColumnVisible = !vm.IsAuthorColumnVisible;
                    ev.Handled = true;
                };

                var shaColumn = new MenuItem();
                shaColumn.Header = App.Text("Histories.Header.SHA");
                if (vm.IsSHAColumnVisible)
                    shaColumn.Icon = App.CreateMenuIcon("Icons.Check");
                shaColumn.Click += (_, ev) =>
                {
                    vm.IsSHAColumnVisible = !vm.IsSHAColumnVisible;
                    ev.Handled = true;
                };

                var timeColumn = new MenuItem();
                timeColumn.Header = App.Text("Histories.Header.DateTime");
                if (vm.IsDateTimeColumnVisible)
                    timeColumn.Icon = App.CreateMenuIcon("Icons.Check");
                timeColumn.Click += (_, ev) =>
                {
                    vm.IsDateTimeColumnVisible = !vm.IsDateTimeColumnVisible;
                    ev.Handled = true;
                };

                var menu = new ContextMenu();
                menu.Items.Add(columnsHeader);
                menu.Items.Add(authorColumn);
                menu.Items.Add(shaColumn);
                menu.Items.Add(timeColumn);
                menu.Open(CommitListContainer);
            }

            e.Handled = true;
        }

        private async void OnCommitListKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
                return;

            if (sender is DataGrid { SelectedItems: { Count: > 0 } selected })
            {
                if (e.Key == Key.C)
                {
                    var builder = new StringBuilder();
                    foreach (var item in selected)
                    {
                        if (item is Models.Commit commit)
                            builder.Append(commit.SHA.AsSpan(0, 10)).Append(" - ").AppendLine(commit.Subject);
                    }

                    await App.CopyTextAsync(builder.ToString());
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    var repoView = this.FindAncestorOfType<Repository>();
                    if (repoView?.DataContext is not ViewModels.Repository repo || !repo.CanCreatePopup())
                        return;

                    if (selected.Count == 1 && selected[0] is Models.Commit commit)
                    {
                        repo.ShowPopup(new ViewModels.CreateBranch(repo, commit));
                        e.Handled = true;
                    }

                    return;
                }

                if (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    var repoView = this.FindAncestorOfType<Repository>();
                    if (repoView?.DataContext is not ViewModels.Repository repo || !repo.CanCreatePopup())
                        return;

                    if (selected.Count == 1 && selected[0] is Models.Commit commit)
                    {
                        repo.ShowPopup(new ViewModels.CreateTag(repo, commit));
                        e.Handled = true;
                    }
                }
            }
        }

        private async void OnCommitListDoubleTapped(object sender, TappedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is ViewModels.Histories histories &&
                CommitListContainer.SelectedItems is { Count: 1 } &&
                sender is DataGrid grid &&
                !Equals(e.Source, grid))
            {
                if (e.Source is CommitRefsPresenter crp)
                {
                    var decorator = crp.DecoratorAt(e.GetPosition(crp));
                    var succ = await histories.CheckoutBranchByDecoratorAsync(decorator);
                    if (succ)
                        return;
                }

                if (e.Source is Control { DataContext: Models.Commit c })
                    await histories.CheckoutBranchByCommitAsync(c);
            }
        }

        private ContextMenu CreateContextMenuForMultipleCommits(ViewModels.Repository repo, List<Models.Commit> selected)
        {
            var canCherryPick = true;
            var canMerge = true;

            foreach (var c in selected)
            {
                if (c.IsMerged)
                {
                    canMerge = false;
                    canCherryPick = false;
                }
                else if (c.Parents.Count > 1)
                {
                    canCherryPick = false;
                }
            }

            var menu = new ContextMenu();

            if (!repo.IsBare)
            {
                if (canCherryPick)
                {
                    var cherryPick = new MenuItem();
                    cherryPick.Header = App.Text("CommitCM.CherryPickMultiple");
                    cherryPick.Icon = App.CreateMenuIcon("Icons.CherryPick");
                    cherryPick.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.CherryPick(repo, selected));
                        e.Handled = true;
                    };
                    menu.Items.Add(cherryPick);
                }

                if (canMerge)
                {
                    var merge = new MenuItem();
                    merge.Header = App.Text("CommitCM.MergeMultiple");
                    merge.Icon = App.CreateMenuIcon("Icons.Merge");
                    merge.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.MergeMultiple(repo, selected));
                        e.Handled = true;
                    };
                    menu.Items.Add(merge);
                }

                if (canCherryPick || canMerge)
                    menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var saveToPatch = new MenuItem();
            saveToPatch.Icon = App.CreateMenuIcon("Icons.Save");
            saveToPatch.Header = App.Text("CommitCM.SaveAsPatch");
            saveToPatch.Click += async (_, e) =>
            {
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null)
                    return;

                var options = new FolderPickerOpenOptions() { AllowMultiple = false };
                try
                {
                    var picker = await storageProvider.OpenFolderPickerAsync(options);
                    if (picker.Count == 1)
                    {
                        var folder = picker[0];
                        var folderPath = folder is { Path: { IsAbsoluteUri: true } path } ? path.LocalPath : folder.Path.ToString();
                        var succ = false;
                        for (var i = 0; i < selected.Count; i++)
                        {
                            succ = await repo.SaveCommitAsPatchAsync(selected[i], folderPath, i);
                            if (!succ)
                                break;
                        }

                        if (succ)
                            App.SendNotification(repo.FullPath, App.Text("SaveAsPatchSuccess"));
                    }
                }
                catch (Exception exception)
                {
                    App.RaiseException(repo.FullPath, $"Failed to save as patch: {exception.Message}");
                }

                e.Handled = true;
            };
            menu.Items.Add(saveToPatch);
            menu.Items.Add(new MenuItem() { Header = "-" });

            var copyInfos = new MenuItem();
            copyInfos.Header = App.Text("CommitCM.CopySHA") + " - " + App.Text("CommitCM.CopySubject");
            copyInfos.Tag = OperatingSystem.IsMacOS() ? "⌘+C" : "Ctrl+C";
            copyInfos.Click += async (_, e) =>
            {
                var builder = new StringBuilder();
                foreach (var c in selected)
                    builder.Append(c.SHA.AsSpan(0, 10)).Append(" - ").AppendLine(c.Subject);

                await App.CopyTextAsync(builder.ToString());
                e.Handled = true;
            };

            var copyShas = new MenuItem();
            copyShas.Header = App.Text("CommitCM.CopySHA");
            copyShas.Icon = App.CreateMenuIcon("Icons.Hash");
            copyShas.Click += async (_, e) =>
            {
                var builder = new StringBuilder();
                foreach (var c in selected)
                    builder.AppendLine(c.SHA);

                await App.CopyTextAsync(builder.ToString());
                e.Handled = true;
            };

            var copySubjects = new MenuItem();
            copySubjects.Header = App.Text("CommitCM.CopySubject");
            copySubjects.Icon = App.CreateMenuIcon("Icons.Subject");
            copySubjects.Click += async (_, e) =>
            {
                var builder = new StringBuilder();
                foreach (var c in selected)
                    builder.AppendLine(c.Subject);

                await App.CopyTextAsync(builder.ToString());
                e.Handled = true;
            };

            var copyMessage = new MenuItem();
            copyMessage.Header = App.Text("CommitCM.CopyCommitMessage");
            copyMessage.Icon = App.CreateMenuIcon("Icons.Message");
            copyMessage.Click += async (_, e) =>
            {
                var vm = DataContext as ViewModels.Histories;
                var messages = new List<string>();
                foreach (var c in selected)
                {
                    var message = await vm!.GetCommitFullMessageAsync(c);
                    messages.Add(message);
                }

                await App.CopyTextAsync(string.Join("\n-----\n", messages));
                e.Handled = true;
            };

            var copy = new MenuItem();
            copy.Header = App.Text("Copy");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Items.Add(copyInfos);
            copy.Items.Add(new MenuItem() { Header = "-" });
            copy.Items.Add(copyShas);
            copy.Items.Add(copySubjects);
            copy.Items.Add(copyMessage);
            menu.Items.Add(copy);
            return menu;
        }

        private ContextMenu CreateContextMenuForSingleCommit(ViewModels.Repository repo, Models.Commit commit)
        {
            var current = repo.CurrentBranch;
            var vm = DataContext as ViewModels.Histories;
            if (current == null || vm == null)
                return null;

            var menu = new ContextMenu();
            var tags = new List<Models.Tag>();
            var isHead = commit.IsCurrentHead;

            if (commit.HasDecorators)
            {
                foreach (var d in commit.Decorators)
                {
                    switch (d.Type)
                    {
                        case Models.DecoratorType.CurrentBranchHead:
                            FillCurrentBranchMenu(menu, repo, current, d.Color, commit.Color);
                            break;
                        case Models.DecoratorType.LocalBranchHead:
                            var lb = repo.Branches.Find(x => x.IsLocal && d.Name == x.Name);
                            FillOtherLocalBranchMenu(menu, repo, lb, current, commit.IsMerged, d.Color, commit.Color);
                            break;
                        case Models.DecoratorType.RemoteBranchHead:
                            var rb = repo.Branches.Find(x => !x.IsLocal && d.Name == x.FriendlyName);
                            FillRemoteBranchMenu(menu, repo, rb, current, commit.IsMerged, d.Color, commit.Color);
                            break;
                        case Models.DecoratorType.Tag:
                            var t = repo.Tags.Find(x => x.Name == d.Name);
                            if (t != null)
                                tags.Add(t);
                            break;
                    }
                }

                if (menu.Items.Count > 0)
                    menu.Items.Add(new MenuItem() { Header = "-" });
            }

            if (tags.Count > 0)
            {
                foreach (var tag in tags)
                    FillTagMenu(menu, repo, tag, current, commit.IsMerged);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var createBranch = new MenuItem();
            createBranch.Icon = App.CreateMenuIcon("Icons.Branch.Add");
            createBranch.Header = App.Text("CreateBranch");
            createBranch.Tag = OperatingSystem.IsMacOS() ? "⌘+⇧+B" : "Ctrl+Shift+B";
            createBranch.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.CreateBranch(repo, commit));
                e.Handled = true;
            };
            menu.Items.Add(createBranch);

            var createTag = new MenuItem();
            createTag.Icon = App.CreateMenuIcon("Icons.Tag.Add");
            createTag.Header = App.Text("CreateTag");
            createTag.Tag = OperatingSystem.IsMacOS() ? "⌘+⇧+T" : "Ctrl+Shift+T";
            createTag.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.CreateTag(repo, commit));
                e.Handled = true;
            };
            menu.Items.Add(createTag);
            menu.Items.Add(new MenuItem() { Header = "-" });

            if (!repo.IsBare)
            {
                var target = commit.GetFriendlyName();
                if (target.Length > 32)
                    target = commit.SHA.Substring(0, 10);

                if (isHead)
                {
                    var undoLastRebase = new MenuItem();
                    undoLastRebase.Header = "Undo Last Rebase (ORIG_HEAD)...";
                    undoLastRebase.Icon = App.CreateMenuIcon("Icons.Undo");
                    undoLastRebase.Click += async (_, e) =>
                    {
                        await TryOpenUndoLastRebasePopupAsync(repo, current);
                        e.Handled = true;
                    };
                    menu.Items.Add(undoLastRebase);

                    var reword = new MenuItem();
                    reword.Header = App.Text("CommitCM.Reword");
                    reword.Icon = App.CreateMenuIcon("Icons.Edit");
                    reword.Click += async (_, e) =>
                    {
                        await vm.RewordHeadAsync(commit);
                        e.Handled = true;
                    };
                    menu.Items.Add(reword);

                    var squash = new MenuItem();
                    squash.Header = App.Text("CommitCM.Squash");
                    squash.Icon = App.CreateMenuIcon("Icons.SquashIntoParent");
                    squash.IsEnabled = commit.Parents.Count == 1;
                    squash.Click += async (_, e) =>
                    {
                        await vm.SquashOrFixupHeadAsync(commit, false);
                        e.Handled = true;
                    };
                    menu.Items.Add(squash);

                    var fixup = new MenuItem();
                    fixup.Header = App.Text("CommitCM.Fixup");
                    fixup.Icon = App.CreateMenuIcon("Icons.Fix");
                    fixup.IsEnabled = commit.Parents.Count == 1;
                    fixup.Click += async (_, e) =>
                    {
                        await vm.SquashOrFixupHeadAsync(commit, true);
                        e.Handled = true;
                    };
                    menu.Items.Add(fixup);
                }
                else
                {
                    var reset = new MenuItem();
                    reset.Header = App.Text("CommitCM.Reset", current.Name, target);
                    reset.Icon = App.CreateMenuIcon("Icons.Reset");
                    reset.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.Reset(repo, current, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(reset);
                }

                if (!commit.IsMerged)
                {
                    var rebase = new MenuItem();
                    rebase.Header = App.Text("CommitCM.Rebase", current.Name, target);
                    rebase.Icon = App.CreateMenuIcon("Icons.Rebase");
                    rebase.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.Rebase(repo, current, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(rebase);

                    if (!commit.HasDecorators)
                    {
                        var merge = new MenuItem();
                        merge.Header = App.Text("CommitCM.Merge", current.Name);
                        merge.Icon = App.CreateMenuIcon("Icons.Merge");
                        merge.Click += (_, e) =>
                        {
                            if (repo.CanCreatePopup())
                                repo.ShowPopup(new ViewModels.Merge(repo, commit, current.Name));

                            e.Handled = true;
                        };
                        menu.Items.Add(merge);
                    }

                    var cherryPick = new MenuItem();
                    cherryPick.Header = App.Text("CommitCM.CherryPick");
                    cherryPick.Icon = App.CreateMenuIcon("Icons.CherryPick");
                    cherryPick.Click += async (_, e) =>
                    {
                        await vm.CherryPickAsync(commit);
                        e.Handled = true;
                    };
                    menu.Items.Add(cherryPick);
                }

                var revert = new MenuItem();
                revert.Header = App.Text("CommitCM.Revert");
                revert.Icon = App.CreateMenuIcon("Icons.Undo");
                revert.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.Revert(repo, commit));
                    e.Handled = true;
                };
                menu.Items.Add(revert);

                if (isHead)
                {
                    var dropHead = new MenuItem();
                    dropHead.Header = App.Text("CommitCM.Drop");
                    dropHead.Icon = App.CreateMenuIcon("Icons.Clear");
                    dropHead.Click += async (_, e) =>
                    {
                        await vm.DropHeadAsync(commit);
                        e.Handled = true;
                    };
                    menu.Items.Add(dropHead);
                }
                else
                {
                    var checkoutCommit = new MenuItem();
                    checkoutCommit.Header = App.Text("CommitCM.Checkout");
                    checkoutCommit.Icon = App.CreateMenuIcon("Icons.Detached");
                    checkoutCommit.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.CheckoutCommit(repo, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(checkoutCommit);

                    if (commit.IsMerged && commit.Parents.Count > 0)
                    {
                        var manually = new MenuItem();
                        manually.Header = App.Text("CommitCM.InteractiveRebase.Manually", current.Name, target);
                        manually.Icon = App.CreateMenuIcon("Icons.InteractiveRebase");
                        manually.Click += async (_, e) =>
                        {
                            await App.ShowDialog(new ViewModels.InteractiveRebase(repo, commit));
                            e.Handled = true;
                        };

                        var reword = new MenuItem();
                        reword.Header = App.Text("CommitCM.InteractiveRebase.Reword");
                        reword.Icon = App.CreateMenuIcon("Icons.Rename");
                        reword.Click += async (_, e) =>
                        {
                            await vm.InteractiveRebaseAsync(commit, Models.InteractiveRebaseAction.Reword);
                            e.Handled = true;
                        };

                        var edit = new MenuItem();
                        edit.Header = App.Text("CommitCM.InteractiveRebase.Edit");
                        edit.Icon = App.CreateMenuIcon("Icons.Edit");
                        edit.Click += async (_, e) =>
                        {
                            await vm.InteractiveRebaseAsync(commit, Models.InteractiveRebaseAction.Edit);
                            e.Handled = true;
                        };

                        var squash = new MenuItem();
                        squash.Header = App.Text("CommitCM.InteractiveRebase.Squash");
                        squash.Icon = App.CreateMenuIcon("Icons.SquashIntoParent");
                        squash.Click += async (_, e) =>
                        {
                            await vm.InteractiveRebaseAsync(commit, Models.InteractiveRebaseAction.Squash);
                            e.Handled = true;
                        };

                        var fixup = new MenuItem();
                        fixup.Header = App.Text("CommitCM.InteractiveRebase.Fixup");
                        fixup.Icon = App.CreateMenuIcon("Icons.Fix");
                        fixup.Click += async (_, e) =>
                        {
                            await vm.InteractiveRebaseAsync(commit, Models.InteractiveRebaseAction.Fixup);
                            e.Handled = true;
                        };

                        var drop = new MenuItem();
                        drop.Header = App.Text("CommitCM.InteractiveRebase.Drop");
                        drop.Icon = App.CreateMenuIcon("Icons.Clear");
                        drop.Click += async (_, e) =>
                        {
                            await vm.InteractiveRebaseAsync(commit, Models.InteractiveRebaseAction.Drop);
                            e.Handled = true;
                        };

                        var interactiveRebase = new MenuItem();
                        interactiveRebase.Header = App.Text("CommitCM.InteractiveRebase");
                        interactiveRebase.Icon = App.CreateMenuIcon("Icons.InteractiveRebase");
                        interactiveRebase.Items.Add(manually);
                        interactiveRebase.Items.Add(new MenuItem() { Header = "-" });
                        interactiveRebase.Items.Add(reword);
                        interactiveRebase.Items.Add(edit);
                        interactiveRebase.Items.Add(squash);
                        interactiveRebase.Items.Add(fixup);
                        interactiveRebase.Items.Add(drop);

                        menu.Items.Add(new MenuItem() { Header = "-" });
                        menu.Items.Add(interactiveRebase);
                    }
                    else
                    {
                        var interactiveRebase = new MenuItem();
                        interactiveRebase.Header = App.Text("CommitCM.InteractiveRebase.Manually", current.Name, target);
                        interactiveRebase.Icon = App.CreateMenuIcon("Icons.InteractiveRebase");
                        interactiveRebase.Click += async (_, e) =>
                        {
                            await App.ShowDialog(new ViewModels.InteractiveRebase(repo, commit));
                            e.Handled = true;
                        };

                        menu.Items.Add(new MenuItem() { Header = "-" });
                        menu.Items.Add(interactiveRebase);
                    }
                }

                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            if (!isHead)
            {
                if (current.Ahead.Contains(commit.SHA))
                {
                    var upstream = repo.Branches.Find(x => x.FullName.Equals(current.Upstream, StringComparison.Ordinal));
                    var pushRevision = new MenuItem();
                    pushRevision.Header = App.Text("CommitCM.PushRevision", commit.SHA.Substring(0, 10), upstream.FriendlyName);
                    pushRevision.Icon = App.CreateMenuIcon("Icons.Push");
                    pushRevision.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.PushRevision(repo, commit, upstream));
                        e.Handled = true;
                    };
                    menu.Items.Add(pushRevision);
                    menu.Items.Add(new MenuItem() { Header = "-" });
                }

                var compareWithHead = new MenuItem();
                compareWithHead.Header = App.Text("CommitCM.CompareWithHead");
                compareWithHead.Icon = App.CreateMenuIcon("Icons.Compare");
                compareWithHead.Click += async (_, e) =>
                {
                    var head = await vm.CompareWithHeadAsync(commit);
                    if (head != null)
                        CommitListContainer.SelectedItems.Add(head);

                    e.Handled = true;
                };
                menu.Items.Add(compareWithHead);

                if (repo.LocalChangesCount > 0)
                {
                    var compareWithWorktree = new MenuItem();
                    compareWithWorktree.Header = App.Text("CommitCM.CompareWithWorktree");
                    compareWithWorktree.Icon = App.CreateMenuIcon("Icons.Compare");
                    compareWithWorktree.Click += (_, e) =>
                    {
                        vm.CompareWithWorktree(commit);
                        e.Handled = true;
                    };
                    menu.Items.Add(compareWithWorktree);
                }

                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var saveToPatch = new MenuItem();
            saveToPatch.Icon = App.CreateMenuIcon("Icons.Save");
            saveToPatch.Header = App.Text("CommitCM.SaveAsPatch");
            saveToPatch.Click += async (_, e) =>
            {
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null)
                    return;

                var options = new FolderPickerOpenOptions() { AllowMultiple = false };
                try
                {
                    var selected = await storageProvider.OpenFolderPickerAsync(options);
                    if (selected.Count == 1)
                    {
                        var folder = selected[0];
                        var folderPath = folder is { Path: { IsAbsoluteUri: true } path } ? path.LocalPath : folder.Path.ToString();
                        await repo.SaveCommitAsPatchAsync(commit, folderPath);
                    }
                }
                catch (Exception exception)
                {
                    App.RaiseException(repo.FullPath, $"Failed to save as patch: {exception.Message}");
                }

                e.Handled = true;
            };
            menu.Items.Add(saveToPatch);

            var archive = new MenuItem();
            archive.Icon = App.CreateMenuIcon("Icons.Archive");
            archive.Header = App.Text("Archive");
            archive.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.Archive(repo, commit));
                e.Handled = true;
            };
            menu.Items.Add(archive);
            menu.Items.Add(new MenuItem() { Header = "-" });

            var actions = repo.GetCustomActions(Models.CustomActionScope.Commit);
            if (actions.Count > 0)
            {
                var custom = new MenuItem();
                custom.Header = App.Text("CommitCM.CustomAction");
                custom.Icon = App.CreateMenuIcon("Icons.Action");

                foreach (var action in actions)
                {
                    var (dup, label) = action;
                    var item = new MenuItem();
                    item.Icon = App.CreateMenuIcon("Icons.Action");
                    item.Header = label;
                    item.Click += async (_, e) =>
                    {
                        await repo.ExecCustomActionAsync(dup, commit);
                        e.Handled = true;
                    };

                    custom.Items.Add(item);
                }

                menu.Items.Add(custom);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var copyInfo = new MenuItem();
            copyInfo.Header = App.Text("CommitCM.CopySHA") + " - " + App.Text("CommitCM.CopySubject");
            copyInfo.Tag = OperatingSystem.IsMacOS() ? "⌘+C" : "Ctrl+C";
            copyInfo.Click += async (_, e) =>
            {
                await App.CopyTextAsync($"{commit.SHA.AsSpan(0, 10)} - {commit.Subject}");
                e.Handled = true;
            };

            var copySHA = new MenuItem();
            copySHA.Header = App.Text("CommitCM.CopySHA");
            copySHA.Icon = App.CreateMenuIcon("Icons.Hash");
            copySHA.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.SHA);
                e.Handled = true;
            };

            var copySubject = new MenuItem();
            copySubject.Header = App.Text("CommitCM.CopySubject");
            copySubject.Icon = App.CreateMenuIcon("Icons.Subject");
            copySubject.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.Subject);
                e.Handled = true;
            };

            var copyMessage = new MenuItem();
            copyMessage.Header = App.Text("CommitCM.CopyCommitMessage");
            copyMessage.Icon = App.CreateMenuIcon("Icons.Message");
            copyMessage.Click += async (_, e) =>
            {
                var message = await vm.GetCommitFullMessageAsync(commit);
                await App.CopyTextAsync(message);
                e.Handled = true;
            };

            var copyAuthor = new MenuItem();
            copyAuthor.Header = App.Text("CommitCM.CopyAuthor");
            copyAuthor.Icon = App.CreateMenuIcon("Icons.User");
            copyAuthor.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.Author.ToString());
                e.Handled = true;
            };

            var copyCommitter = new MenuItem();
            copyCommitter.Header = App.Text("CommitCM.CopyCommitter");
            copyCommitter.Icon = App.CreateMenuIcon("Icons.User");
            copyCommitter.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.Committer.ToString());
                e.Handled = true;
            };

            var copy = new MenuItem();
            copy.Header = App.Text("Copy");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Items.Add(copyInfo);
            copy.Items.Add(new MenuItem() { Header = "-" });
            copy.Items.Add(copySHA);
            copy.Items.Add(copySubject);
            copy.Items.Add(copyMessage);
            copy.Items.Add(copyAuthor);
            copy.Items.Add(copyCommitter);
            menu.Items.Add(copy);

            return menu;
        }

        private void FillCurrentBranchMenu(ContextMenu menu, ViewModels.Repository repo, Models.Branch current, uint decoratorColor, int commitColorIndex)
        {
            var submenu = new MenuItem();
            submenu.Icon = App.CreateMenuIcon("Icons.Branch");
            submenu.Header = current.Name;
            var graphColor = GetCommitGraphColor(commitColorIndex);
            var color = decoratorColor != 0 ? decoratorColor : (graphColor != 0 ? graphColor : repo.GetBranchFilterColor(current));
            submenu.Background = CreateBranchNameBackground(color, true);
            var actionBackground = CreateBranchActionBackground(color, true);

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new ViewModels.FilterModeInGraph(repo, current);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            if (!string.IsNullOrEmpty(current.Upstream))
            {
                var upstream = current.Upstream.Substring(13);

                var fastForward = new MenuItem();
                fastForward.Header = App.Text("BranchCM.FastForward", upstream);
                fastForward.Icon = App.CreateMenuIcon("Icons.FastForward");
                fastForward.IsEnabled = current.Ahead.Count == 0 && current.Behind.Count > 0;
                fastForward.Click += async (_, e) =>
                {
                    var b = repo.Branches.Find(x => x.FriendlyName == upstream);
                    if (b == null)
                        return;

                    if (repo.CanCreatePopup())
                        await repo.ShowAndStartPopupAsync(new ViewModels.Merge(repo, b, current.Name, true));

                    e.Handled = true;
                };
                submenu.Items.Add(fastForward);

                var pull = new MenuItem();
                pull.Header = App.Text("BranchCM.Pull", upstream);
                pull.Icon = App.CreateMenuIcon("Icons.Pull");
                pull.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.Pull(repo, null));
                    e.Handled = true;
                };
                submenu.Items.Add(pull);
            }

            var rename = new MenuItem();
            rename.Header = App.Text("BranchCM.Rename", current.Name);
            rename.Icon = App.CreateMenuIcon("Icons.Rename");
            rename.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.RenameBranch(repo, current));
                e.Handled = true;
            };
            submenu.Items.Add(rename);

            var undoLastRebase = new MenuItem();
            undoLastRebase.Header = "Undo Last Rebase (ORIG_HEAD)...";
            undoLastRebase.Icon = App.CreateMenuIcon("Icons.Undo");
            undoLastRebase.Click += async (_, e) =>
            {
                await TryOpenUndoLastRebasePopupAsync(repo, current);
                e.Handled = true;
            };
            submenu.Items.Add(undoLastRebase);
            menu.Items.Add(submenu);
            AddLevel1ExcludeBranchMenuItem(menu, repo, current.Name, actionBackground);
            AddLevel1PushBranchMenuItem(menu, repo, current, actionBackground);
            AddLevel1CopyBranchNameMenuItem(menu, current.Name, actionBackground);
        }

        private void FillOtherLocalBranchMenu(ContextMenu menu, ViewModels.Repository repo, Models.Branch branch, Models.Branch current, bool merged, uint decoratorColor, int commitColorIndex)
        {
            if (branch == null)
                return;

            var submenu = new MenuItem();
            submenu.Icon = App.CreateMenuIcon("Icons.Branch");
            submenu.Header = branch.Name;
            var graphColor = GetCommitGraphColor(commitColorIndex);
            var color = decoratorColor != 0 ? decoratorColor : (graphColor != 0 ? graphColor : repo.GetBranchFilterColor(branch));
            submenu.Background = CreateBranchNameBackground(color, true);
            var actionBackground = CreateBranchActionBackground(color, true);

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new ViewModels.FilterModeInGraph(repo, branch);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            if (!repo.IsBare)
            {
                var merge = new MenuItem();
                merge.Header = App.Text("BranchCM.Merge", branch.Name, current.Name);
                merge.Icon = App.CreateMenuIcon("Icons.Merge");
                merge.IsEnabled = !merged;
                merge.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.Merge(repo, branch, current.Name, false));
                    e.Handled = true;
                };
                submenu.Items.Add(merge);
            }

            var rename = new MenuItem();
            rename.Header = App.Text("BranchCM.Rename", branch.Name);
            rename.Icon = App.CreateMenuIcon("Icons.Rename");
            rename.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.RenameBranch(repo, branch));
                e.Handled = true;
            };
            submenu.Items.Add(rename);

            var delete = new MenuItem();
            delete.Header = App.Text("BranchCM.Delete", branch.Name);
            delete.Icon = App.CreateMenuIcon("Icons.Clear");
            delete.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.DeleteBranch(repo, branch));
                e.Handled = true;
            };
            submenu.Items.Add(delete);
            menu.Items.Add(submenu);
            AddLevel1CheckoutBranchMenuItem(menu, repo, branch, branch.Name, actionBackground);
            AddLevel1ExcludeBranchMenuItem(menu, repo, branch.Name, actionBackground);
            AddLevel1PushBranchMenuItem(menu, repo, branch, actionBackground);
            AddLevel1CopyBranchNameMenuItem(menu, branch.Name, actionBackground);
        }

        private void FillRemoteBranchMenu(ContextMenu menu, ViewModels.Repository repo, Models.Branch branch, Models.Branch current, bool merged, uint decoratorColor, int commitColorIndex)
        {
            if (branch == null)
                return;

            var name = branch.FriendlyName;

            var submenu = new MenuItem();
            submenu.Icon = App.CreateMenuIcon("Icons.Branch");
            submenu.Header = name;
            var graphColor = GetCommitGraphColor(commitColorIndex);
            var color = decoratorColor != 0 ? decoratorColor : (graphColor != 0 ? graphColor : repo.GetBranchFilterColor(branch));
            submenu.Background = CreateBranchNameBackground(color, false);
            var actionBackground = CreateBranchActionBackground(color, false);

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new ViewModels.FilterModeInGraph(repo, branch);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var merge = new MenuItem();
            merge.Header = App.Text("BranchCM.Merge", name, current.Name);
            merge.Icon = App.CreateMenuIcon("Icons.Merge");
            merge.IsEnabled = !merged;
            merge.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.Merge(repo, branch, current.Name, false));
                e.Handled = true;
            };

            submenu.Items.Add(merge);

            var delete = new MenuItem();
            delete.Header = App.Text("BranchCM.Delete", name);
            delete.Icon = App.CreateMenuIcon("Icons.Clear");
            delete.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.DeleteBranch(repo, branch));
                e.Handled = true;
            };
            submenu.Items.Add(delete);
            menu.Items.Add(submenu);
            AddLevel1CheckoutBranchMenuItem(menu, repo, branch, name, actionBackground);
            AddLevel1ExcludeBranchMenuItem(menu, repo, branch.Name, actionBackground);
            AddLevel1CopyBranchNameMenuItem(menu, name, actionBackground);
        }

        private static void AddLevel1CheckoutBranchMenuItem(ContextMenu menu, ViewModels.Repository repo, Models.Branch branch, string displayName, IBrush background)
        {
            var checkout = new MenuItem();
            checkout.Header = App.Text("BranchCM.Checkout", displayName);
            checkout.Icon = App.CreateMenuIcon("Icons.Check");
            checkout.IsEnabled = !repo.IsBare;
            checkout.Background = background;
            checkout.Click += async (_, e) =>
            {
                await repo.CheckoutBranchAsync(branch);
                e.Handled = true;
            };
            menu.Items.Add(checkout);
        }

        private static void AddLevel1ExcludeBranchMenuItem(ContextMenu menu, ViewModels.Repository repo, string branchName, IBrush background)
        {
            var exclude = new MenuItem();
            exclude.Header = App.Text("Repository.BranchesVisibility.ExcludeThisBranch");
            exclude.Icon = App.CreateMenuIcon("Icons.Filter");
            exclude.Background = background;
            exclude.Click += (_, e) =>
            {
                repo.ExcludeBranchInPresetFilter(branchName);
                e.Handled = true;
            };
            menu.Items.Add(exclude);
        }

        private static void AddLevel1PushBranchMenuItem(ContextMenu menu, ViewModels.Repository repo, Models.Branch branch, IBrush background)
        {
            var push = new MenuItem();
            push.Header = App.Text("BranchCM.Push", branch.Name);
            push.Icon = App.CreateMenuIcon("Icons.Push");
            push.IsEnabled = repo.Remotes.Count > 0;
            push.Background = background;
            push.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.Push(repo, branch));
                e.Handled = true;
            };
            menu.Items.Add(push);
        }

        private static void AddLevel1CopyBranchNameMenuItem(ContextMenu menu, string branchName, IBrush background)
        {
            var copy = new MenuItem();
            copy.Header = App.Text("BranchCM.CopyName");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Background = background;
            copy.Click += async (_, e) =>
            {
                await App.CopyTextAsync(branchName);
                e.Handled = true;
            };
            menu.Items.Add(copy);
        }

        private static IBrush CreateBranchActionBackground(uint branchColor, bool isLocal)
        {
            var color = Color.FromUInt32(branchColor == 0 ? Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR : branchColor);
            var alpha = isLocal ? (byte)0x80 : (byte)0x20;
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private static IBrush CreateBranchNameBackground(uint branchColor, bool isLocal)
        {
            var color = Color.FromUInt32(branchColor == 0 ? Models.RepositorySettings.PRESET_BRANCH_EXACT_DEFAULT_COLOR : branchColor);
            var alpha = isLocal ? (byte)0xA0 : (byte)0x32;
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private static uint GetCommitGraphColor(int colorIndex)
        {
            if (colorIndex < 0 || colorIndex >= Models.CommitGraph.Pens.Count)
                return 0;

            if (Models.CommitGraph.Pens[colorIndex].Brush is ISolidColorBrush solid)
                return solid.Color.ToUInt32();

            return 0;
        }

        private bool TryGetBranchNameAtPoint(CommitRefsPresenter presenter, Point point, out string branchName)
        {
            branchName = string.Empty;
            var decorator = presenter.DecoratorAt(point);
            if (decorator == null)
                return false;

            switch (decorator.Type)
            {
                case Models.DecoratorType.CurrentCommitHead:
                    branchName = ResolveCurrentLocalBranchName();
                    break;
                case Models.DecoratorType.CurrentBranchHead:
                case Models.DecoratorType.LocalBranchHead:
                    branchName = decorator.Name;
                    break;
                case Models.DecoratorType.RemoteBranchHead:
                    branchName = ResolveRemoteDecoratorNameToBranchName(decorator.Name);
                    break;
                default:
                    return false;
            }

            if (string.IsNullOrWhiteSpace(branchName))
                return false;

            branchName = branchName.Trim();
            return true;
        }

        private bool TryGetRebaseBranchDropTargets(CommitRefsPresenter presenter, DragEventArgs e, out ViewModels.Repository repo, out Models.Branch source, out Models.Branch target)
        {
            repo = null;
            source = null;
            target = null;

            if (!TryGetRebaseDragSource(e, out repo, out source))
                return false;

            if (!TryGetBranchAtPoint(presenter, e.GetPosition(presenter), out _, out target))
                return false;

            if (IsSameBranch(source, target))
                return false;

            return true;
        }

        private bool TryGetRebaseDragSource(DragEventArgs e, out ViewModels.Repository repo, out Models.Branch source)
        {
            repo = null;
            source = null;

            var repoView = this.FindAncestorOfType<Repository>();
            if (repoView?.DataContext is not ViewModels.Repository r || r.IsBare)
                return false;

            repo = r;

            var hasPayload = e.DataTransfer.Contains(_dndPresetBranchNameFormat);
            var raw = hasPayload ? e.DataTransfer.TryGetValue(_dndPresetBranchNameFormat) : null;
            var sourceName = raw?.Trim();
            if (string.IsNullOrEmpty(sourceName))
            {
                // Avalonia drag payload may be unavailable during DragOver.
                // For history-originated drags, keep using the in-memory pressed ref.
                if (_startDragCommitRef && !string.IsNullOrWhiteSpace(_pressedCommitRefBranchName))
                    sourceName = _pressedCommitRefBranchName.Trim();
            }
            if (string.IsNullOrEmpty(sourceName) || sourceName.Equals("HEAD", StringComparison.Ordinal))
                sourceName = ResolveCurrentLocalBranchName();
            if (string.IsNullOrEmpty(sourceName))
                return false;

            if (repo.CurrentBranch is { IsLocal: true, IsCurrent: true } current &&
                current.Name.Equals(sourceName, StringComparison.Ordinal))
            {
                source = current;
                return true;
            }

            source = repo.Branches.Find(x => x.IsLocal && x.IsCurrent && x.Name.Equals(sourceName, StringComparison.Ordinal));
            return source != null;
        }

        private bool TryGetBranchAtPoint(CommitRefsPresenter presenter, Point point, out ViewModels.Repository repo, out Models.Branch branch)
        {
            repo = null;
            branch = null;

            var repoView = this.FindAncestorOfType<Repository>();
            if (repoView?.DataContext is not ViewModels.Repository r || r.IsBare)
                return false;

            repo = r;
            var decorator = presenter.DecoratorAt(point);
            return TryResolveBranchFromDecorator(repo, decorator, out branch);
        }

        private bool TryGetBranchByDecorator(Models.Decorator decorator, out ViewModels.Repository repo, out Models.Branch branch)
        {
            repo = null;
            branch = null;

            var repoView = this.FindAncestorOfType<Repository>();
            if (repoView?.DataContext is not ViewModels.Repository r || r.IsBare)
                return false;

            repo = r;
            return TryResolveBranchFromDecorator(repo, decorator, out branch);
        }

        private static bool TryResolveBranchFromDecorator(ViewModels.Repository repo, Models.Decorator decorator, out Models.Branch branch)
        {
            branch = null;
            if (repo == null || decorator == null)
                return false;

            switch (decorator.Type)
            {
                case Models.DecoratorType.CurrentCommitHead:
                    branch = repo.CurrentBranch?.IsLocal == true
                        ? repo.CurrentBranch
                        : repo.Branches.Find(x => x.IsLocal && x.IsCurrent);
                    break;
                case Models.DecoratorType.CurrentBranchHead:
                case Models.DecoratorType.LocalBranchHead:
                    branch = repo.Branches.Find(x => x.IsLocal && x.Name.Equals(decorator.Name, StringComparison.Ordinal));
                    break;
                case Models.DecoratorType.RemoteBranchHead:
                    branch = repo.Branches.Find(x => !x.IsLocal && x.FriendlyName.Equals(decorator.Name, StringComparison.Ordinal));
                    break;
            }

            return branch != null;
        }

        private static bool IsSameBranch(Models.Branch left, Models.Branch right)
        {
            if (left == null || right == null)
                return false;

            if (!string.IsNullOrWhiteSpace(left.FullName) && !string.IsNullOrWhiteSpace(right.FullName))
                return left.FullName.Equals(right.FullName, StringComparison.Ordinal);

            return left.IsLocal == right.IsLocal &&
                   left.Name.Equals(right.Name, StringComparison.Ordinal) &&
                   string.Equals(left.Remote, right.Remote, StringComparison.Ordinal);
        }

        private static Models.Branch FindBranchByName(ViewModels.Repository repo, string name)
        {
            if (repo == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var branch in repo.Branches)
            {
                if (branch.Name.Equals(name, StringComparison.Ordinal))
                    return branch;
            }

            return null;
        }

        private string ResolveCurrentLocalBranchName()
        {
            var repoView = this.FindAncestorOfType<Repository>();
            if (repoView?.DataContext is not ViewModels.Repository repo)
                return string.Empty;

            if (repo.CurrentBranch?.IsLocal == true && !string.IsNullOrWhiteSpace(repo.CurrentBranch.Name))
                return repo.CurrentBranch.Name.Trim();

            foreach (var branch in repo.Branches)
            {
                if (branch.IsLocal && branch.IsCurrent && !string.IsNullOrWhiteSpace(branch.Name))
                    return branch.Name.Trim();
            }

            return string.Empty;
        }

        private string ResolveRemoteDecoratorNameToBranchName(string remoteFriendlyName)
        {
            if (string.IsNullOrEmpty(remoteFriendlyName))
                return string.Empty;

            var repoView = this.FindAncestorOfType<Repository>();
            if (repoView?.DataContext is not ViewModels.Repository repo)
                return remoteFriendlyName;

            foreach (var branch in repo.Branches)
            {
                if (!branch.IsLocal && branch.FriendlyName.Equals(remoteFriendlyName, StringComparison.Ordinal))
                    return branch.Name;
            }

            return remoteFriendlyName;
        }

        private async Task TryOpenUndoLastRebasePopupAsync(ViewModels.Repository repo, Models.Branch current)
        {
            if (repo == null || current == null || !repo.CanCreatePopup())
                return;

            var confirmed = await App.AskConfirmAsync(
                $"Undo last rebase on '{current.Name}' by resetting to ORIG_HEAD?");
            if (!confirmed)
                return;

            var target = await new Commands.QuerySingleCommit(repo.FullPath, "ORIG_HEAD").GetResultAsync();
            if (target == null)
            {
                App.SendNotification("Undo Last Rebase", "ORIG_HEAD not found. Nothing to undo.");
                return;
            }

            repo.ShowPopup(new ViewModels.Reset(repo, current, target));
        }

        private void FillTagMenu(ContextMenu menu, ViewModels.Repository repo, Models.Tag tag, Models.Branch current, bool merged)
        {
            var submenu = new MenuItem();
            submenu.Header = tag.Name;
            submenu.Icon = App.CreateMenuIcon("Icons.Tag");
            submenu.MinWidth = 200;

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new ViewModels.FilterModeInGraph(repo, tag);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var push = new MenuItem();
            push.Header = App.Text("TagCM.Push", tag.Name);
            push.Icon = App.CreateMenuIcon("Icons.Push");
            push.IsEnabled = repo.Remotes.Count > 0;
            push.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.PushTag(repo, tag));
                e.Handled = true;
            };
            submenu.Items.Add(push);

            if (!repo.IsBare && !merged)
            {
                var merge = new MenuItem();
                merge.Header = App.Text("TagCM.Merge", tag.Name, current.Name);
                merge.Icon = App.CreateMenuIcon("Icons.Merge");
                merge.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.Merge(repo, tag, current.Name));
                    e.Handled = true;
                };
                submenu.Items.Add(merge);
            }

            var delete = new MenuItem();
            delete.Header = App.Text("TagCM.Delete", tag.Name);
            delete.Icon = App.CreateMenuIcon("Icons.Clear");
            delete.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.DeleteTag(repo, tag));
                e.Handled = true;
            };
            submenu.Items.Add(delete);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var copy = new MenuItem();
            copy.Header = App.Text("TagCM.CopyName");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, e) =>
            {
                await App.CopyTextAsync(tag.Name);
                e.Handled = true;
            };
            submenu.Items.Add(copy);

            menu.Items.Add(submenu);
        }

        private double _lastGraphStartY = 0;
        private double _lastGraphClipWidth = 0;
        private double _lastGraphRowHeight = 0;
        private double _lastGraphOffsetX = 0;
        private int _pendingEnsureHeadVisibleRetries = 0;
        private bool _lastHistoriesIsLoading = false;
        private bool _isCenteringHeadCommit = false;
        private bool _pressedCommitRef = false;
        private bool _startDragCommitRef = false;
        private Point _pressedCommitRefPosition = default;
        private string _pressedCommitRefBranchName = string.Empty;
        private readonly DataFormat<string> _dndPresetBranchNameFormat = DataFormat.CreateStringApplicationFormat("sourcegit-dnd-branch-filter-name");
    }
}
