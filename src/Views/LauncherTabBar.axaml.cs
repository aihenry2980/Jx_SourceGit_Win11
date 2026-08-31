using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class LauncherTabSizeBox : Border
    {
        public static readonly DirectProperty<LauncherTabSizeBox, bool> UseFixedWidthProperty =
            AvaloniaProperty.RegisterDirect<LauncherTabSizeBox, bool>(
                nameof(UseFixedWidth),
                static o => o.UseFixedWidth,
                static (o, v) => o.UseFixedWidth = v);

        public bool UseFixedWidth
        {
            get => _useFixedWidth;
            set => SetAndRaise(UseFixedWidthProperty, ref _useFixedWidth, value);
        }

        public LauncherTabSizeBox()
        {
            Width = double.NaN;
        }

        protected override Type StyleKeyOverride => typeof(Border);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == UseFixedWidthProperty)
            {
                if (_useFixedWidth)
                    Width = 200;
                else
                    Width = double.NaN;
            }
        }

        private bool _useFixedWidth = false;
    }

    public partial class LauncherTabBar : UserControl
    {
        public static readonly DirectProperty<LauncherTabBar, bool> IsScrollButtonVisibleProperty =
            AvaloniaProperty.RegisterDirect<LauncherTabBar, bool>(
                nameof(IsScrollButtonVisible),
                static o => o.IsScrollButtonVisible);

        public bool IsScrollButtonVisible
        {
            get => _isScrollButtonVisible;
            set => SetAndRaise(IsScrollButtonVisibleProperty, ref _isScrollButtonVisible, value);
        }

        public LauncherTabBar()
        {
            InitializeComponent();
            LauncherTabsList.AddHandler(InputElement.PointerPressedEvent, OnTabsPointerPressed, RoutingStrategies.Tunnel, true);
            LauncherTabsList.AddHandler(ContextRequestedEvent, OnTabsContextRequested, RoutingStrategies.Tunnel, true);
            AddHandler(InputElement.PointerPressedEvent, OnTabBarPointerPressed, RoutingStrategies.Tunnel, true);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel, true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _topLevel?.RemoveHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed);
            _topLevel = null;
            base.OnDetachedFromVisualTree(e);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (LauncherTabsList == null || LauncherTabsList.SelectedIndex == -1)
                return;

            var startX = LauncherTabsScroller.Offset.X;
            var endX = startX + LauncherTabsScroller.Viewport.Width;
            var height = LauncherTabsScroller.Viewport.Height;

            var selectedIdx = LauncherTabsList.SelectedIndex;
            var count = LauncherTabsList.ItemCount;
            var inactiveBorder = new SolidColorBrush(
                ActualThemeVariant == ThemeVariant.Dark ? Colors.White : Colors.Black,
                ActualThemeVariant == ThemeVariant.Dark ? 0.32 : 0.25);
            var inactiveFill = new SolidColorBrush(
                ActualThemeVariant == ThemeVariant.Dark ? Colors.White : Colors.Black,
                ActualThemeVariant == ThemeVariant.Dark ? 0.05 : 0.035);
            var inactivePen = new Pen(inactiveBorder);

            using (context.PushClip(new Rect(
                LauncherTabsScroller.Bounds.X,
                0,
                LauncherTabsScroller.Viewport.Width,
                height)))
            {
                for (var i = 0; i < count; i++)
                {
                    if (i == selectedIdx)
                        continue;

                    var container = LauncherTabsList.ContainerFromIndex(i);
                    if (container == null)
                        continue;

                    var containerStartX = container.Bounds.Left;
                    var containerEndX = container.Bounds.Right;
                    if (containerEndX < startX || containerStartX > endX)
                        continue;

                    var drawLeftX = containerStartX - startX + LauncherTabsScroller.Bounds.X + 0.5;
                    var drawRightX = containerEndX - startX + LauncherTabsScroller.Bounds.X - 0.5;
                    var tabRect = new Rect(drawLeftX, 0.5, drawRightX - drawLeftX, height - 1);
                    context.DrawRectangle(
                        inactiveFill,
                        inactivePen,
                        new RoundedRect(tabRect, new CornerRadius(5)));
                }
            }

            var selected = LauncherTabsList.ContainerFromIndex(selectedIdx);
            if (selected == null)
                return;

            var activeStartX = selected.Bounds.X;
            var activeEndX = activeStartX + selected.Bounds.Width;
            if (activeStartX > endX + 5 || activeEndX < startX - 5)
                return;

            var geo = new StreamGeometry();
            const double angle = Math.PI / 2;
            var bottom = height + 0.5;
            var cornerSize = new Size(5, 5);

            using (var ctx = geo.Open())
            {
                var drawLeftX = activeStartX - startX + LauncherTabsScroller.Bounds.X;
                if (drawLeftX < LauncherTabsScroller.Bounds.X)
                {
                    ctx.BeginFigure(new Point(LauncherTabsScroller.Bounds.X - 0.5, bottom), true);
                    ctx.LineTo(new Point(LauncherTabsScroller.Bounds.X - 0.5, 0.5));
                }
                else
                {
                    ctx.BeginFigure(new Point(drawLeftX - 5.5, bottom), true);
                    ctx.ArcTo(new Point(drawLeftX - 0.5, bottom - 5), cornerSize, angle, false, SweepDirection.CounterClockwise);
                    ctx.LineTo(new Point(drawLeftX - 0.5, 5.5));
                    ctx.ArcTo(new Point(drawLeftX + 4.5, 0.5), cornerSize, angle, false, SweepDirection.Clockwise);
                }

                var drawRightX = activeEndX - startX + LauncherTabsScroller.Bounds.X;
                if (drawRightX <= LauncherTabsScroller.Bounds.Right)
                {
                    ctx.LineTo(new Point(drawRightX - 5.5, 0.5));
                    ctx.ArcTo(new Point(drawRightX - 0.5, 5.5), cornerSize, angle, false, SweepDirection.Clockwise);
                    ctx.LineTo(new Point(drawRightX - 0.5, bottom - 5));
                    ctx.ArcTo(new Point(drawRightX + 4.5, bottom), cornerSize, angle, false, SweepDirection.CounterClockwise);
                }
                else
                {
                    ctx.LineTo(new Point(LauncherTabsScroller.Bounds.Right - 0.5, 0.5));
                    ctx.LineTo(new Point(LauncherTabsScroller.Bounds.Right - 0.5, bottom));
                }
            }

            IBrush fill = this.FindResource("Brush.ToolBar") as IBrush;
            if (this.FindResource("SystemAccentColor") is Color accent)
            {
                var opacity = ActualThemeVariant == ThemeVariant.Dark ? 0.28 : 0.20;
                fill = new SolidColorBrush(accent, opacity);
            }

            var stroke = new Pen(this.FindResource("Brush.Border1") as IBrush, 1.25);
            context.DrawGeometry(fill, stroke, geo);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property.Name == nameof(ActualThemeVariant) && change.NewValue != null)
                InvalidateVisual();
        }

        private void ScrollTabs(object _, PointerWheelEventArgs e)
        {
            if (Math.Abs(e.Delta.X) < Math.Abs(e.Delta.Y))
            {
                var x = LauncherTabsScroller.Offset.X;
                var extent = LauncherTabsScroller.Extent.Width;
                var viewport = LauncherTabsScroller.Viewport.Width;
                var delta = e.Delta.Y;

                if (extent > viewport)
                {
                    x += -delta * 64; // Use the same logic with vertical scrolling in `ScrollContentPresenter`
                    x = Math.Min(Math.Max(x, 0), extent - viewport);
                }

                LauncherTabsScroller.Offset = new Vector(x, 0);
                e.Handled = true;
            }
        }

        private void ScrollTabsLeft(object _, RoutedEventArgs e)
        {
            LauncherTabsScroller.Offset -= _scrollStep;
            e.Handled = true;
        }

        private void ScrollTabsRight(object _, RoutedEventArgs e)
        {
            LauncherTabsScroller.Offset += _scrollStep;
            e.Handled = true;
        }

        private void OnTabsLayoutUpdated(object _1, EventArgs _2)
        {
            IsScrollButtonVisible = LauncherTabsScroller.Extent.Width > LauncherTabsScroller.Viewport.Width;

            var selectedIndex = LauncherTabsList.SelectedIndex;
            var selectedBounds = LauncherTabsList.ContainerFromIndex(selectedIndex)?.Bounds ?? default;
            var state = new TabRenderState(
                LauncherTabsScroller.Offset,
                LauncherTabsScroller.Viewport,
                LauncherTabsScroller.Extent,
                selectedBounds,
                selectedIndex,
                LauncherTabsList.ItemCount);

            if (!_lastTabRenderState.ApproximatelyEquals(state))
            {
                _lastTabRenderState = state;
                InvalidateVisual();
            }
        }

        private void OnTabsSelectionChanged(object _1, SelectionChangedEventArgs _2)
        {
            InvalidateVisual();
        }

        private void OnTabsPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(LauncherTabsList).Properties.IsRightButtonPressed)
                return;

            var tab = FindTabAt(e.GetPosition(LauncherTabsList));
            if (tab == null)
                return;

            OpenTabContextMenu(tab);
            e.Handled = true;
        }

        private void OnTabsContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var source = e.Source as Visual;
            var tab = source as LauncherTabSizeBox ?? source?.FindAncestorOfType<LauncherTabSizeBox>();
            if (tab == null)
                return;

            OpenTabContextMenu(tab);
            e.Handled = true;
        }

        private void OnTabBarPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_tabContextMenu?.IsOpen == true)
                _tabContextMenu.Close();
        }

        private void OnTopLevelPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_tabContextMenu?.IsOpen == true)
                _tabContextMenu.Close();
        }

        private LauncherTabSizeBox FindTabAt(Point position)
        {
            for (var i = 0; i < LauncherTabsList.ItemCount; i++)
            {
                var container = LauncherTabsList.ContainerFromIndex(i);
                if (container == null)
                    continue;

                var topLeft = container.TranslatePoint(default, LauncherTabsList);
                if (topLeft.HasValue && new Rect(topLeft.Value, container.Bounds.Size).Contains(position))
                    return container.FindDescendantOfType<LauncherTabSizeBox>();
            }

            return null;
        }

        private void OnPointerPressedTab(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                var point = e.GetCurrentPoint(border);
                if (point.Properties.IsMiddleButtonPressed && border.DataContext is ViewModels.LauncherPage page)
                {
                    (DataContext as ViewModels.Launcher)?.CloseTab(page);
                    e.Handled = true;
                }
                else if (point.Properties.IsLeftButtonPressed)
                {
                    _pressedTabEvent = e;
                    _startDragTab = false;
                    _pressedTabPosition = e.GetPosition(border);
                }
                else if (point.Properties.IsRightButtonPressed)
                {
                    OpenTabContextMenu(border);
                    e.Handled = true;
                }
                else
                {
                    _pressedTabEvent = null;
                    _startDragTab = false;
                }
            }
        }

        private void OnPointerReleasedTab(object _1, PointerReleasedEventArgs _2)
        {
            _pressedTabEvent = null;
            _startDragTab = false;
        }

        private async void OnPointerMovedOverTab(object sender, PointerEventArgs e)
        {
            if (_pressedTabEvent != null && !_startDragTab && sender is Border { DataContext: ViewModels.LauncherPage page } border)
            {
                var delta = e.GetPosition(border) - _pressedTabPosition;
                var sizeSquired = delta.X * delta.X + delta.Y * delta.Y;
                if (sizeSquired < 64)
                    return;

                _startDragTab = true;

                var data = new DataTransfer();
                data.Add(DataTransferItem.Create(_dndMainTabFormat, page.Node.Id));
                await DragDrop.DoDragDropAsync(_pressedTabEvent, data, DragDropEffects.Move);
            }
            e.Handled = true;
        }

        private void DropTab(object sender, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetValue(_dndMainTabFormat) is not { Length: > 0 } id)
                return;

            if (DataContext is not ViewModels.Launcher launcher)
                return;

            ViewModels.LauncherPage target = null;
            foreach (var page in launcher.Pages)
            {
                if (page.Node.Id.Equals(id, StringComparison.Ordinal))
                {
                    target = page;
                    break;
                }
            }

            if (target == null)
                return;

            if (sender is not Border { DataContext: ViewModels.LauncherPage to })
                return;

            if (target == to)
                return;

            launcher.MoveTab(target, to);

            _pressedTabEvent = null;
            _startDragTab = false;
            e.Handled = true;
        }

        private void OnTabContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is Border border)
                OpenTabContextMenu(border);

            e.Handled = true;
        }

        private void OpenTabContextMenu(Border border)
        {
            if (border.DataContext is ViewModels.LauncherPage page &&
                DataContext is ViewModels.Launcher vm)
            {
                var menu = new ContextMenu();

                if (page.Data is ViewModels.Repository repo)
                {
                    var refresh = new MenuItem();
                    refresh.Header = App.Text("PageTabBar.Tab.Refresh");
                    refresh.Icon = App.CreateMenuIcon("Icons.Loading");
                    refresh.Tag = "F5";
                    refresh.Click += (_, ev) =>
                    {
                        repo.RefreshAll();
                        ev.Handled = true;
                    };
                    menu.Items.Add(refresh);

                    var copyPath = new MenuItem();
                    copyPath.Header = App.Text("PageTabBar.Tab.CopyPath");
                    copyPath.Icon = App.CreateMenuIcon("Icons.Copy");
                    copyPath.Click += async (_, ev) =>
                    {
                        await page.CopyPathAsync();
                        ev.Handled = true;
                    };
                    menu.Items.Add(copyPath);
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    var edit = new MenuItem();
                    edit.Header = App.Text("PageTabBar.Tab.Edit");
                    edit.Icon = App.CreateMenuIcon("Icons.Edit");
                    edit.Click += (_, ev) =>
                    {
                        page.Node.Edit();
                        ev.Handled = true;
                    };
                    menu.Items.Add(edit);

                    var bookmark = new MenuItem();
                    bookmark.Header = App.Text("PageTabBar.Tab.Bookmark");
                    bookmark.Icon = App.CreateMenuIcon("Icons.Bookmark");
                    bookmark.Classes.Add("bookmark_palette");

                    for (int i = 0; i < Models.Bookmarks.Brushes.Length; i++)
                    {
                        var brush = Models.Bookmarks.Brushes[i];
                        var icon = App.CreateMenuIcon("Icons.Bookmark");
                        if (brush != null)
                            icon.Fill = brush;

                        var dupIdx = i;
                        var setter = new MenuItem();
                        setter.Header = icon;
                        setter.Click += (_, ev) =>
                        {
                            page.Node.Bookmark = dupIdx;
                            if (page.Data is ViewModels.Repository pageRepo)
                                pageRepo.NotifyAccentColorChanged();
                            ev.Handled = true;
                        };
                        bookmark.Items.Add(setter);
                    }
                    menu.Items.Add(bookmark);

                    var workspaces = ViewModels.Preferences.Instance.Workspaces;
                    if (workspaces.Count > 1)
                    {
                        var moveTo = new MenuItem();
                        moveTo.Header = App.Text("PageTabBar.Tab.MoveToWorkspace");
                        moveTo.Icon = App.CreateMenuIcon("Icons.MoveTo");

                        foreach (var ws in workspaces)
                        {
                            var dupWs = ws;
                            var isCurrent = dupWs == vm.ActiveWorkspace;
                            var icon = App.CreateMenuIcon(isCurrent ? "Icons.Check" : "Icons.Workspace");
                            icon.Fill = dupWs.Brush;

                            var target = new MenuItem();
                            target.Header = ws.Name;
                            target.Icon = icon;
                            target.Click += (_, ev) =>
                            {
                                if (!isCurrent)
                                {
                                    vm.CloseTab(page);
                                    dupWs.Repositories.Add(repo.FullPath);
                                }

                                ev.Handled = true;
                            };
                            moveTo.Items.Add(target);
                        }

                        menu.Items.Add(moveTo);
                    }

                    menu.Items.Add(new MenuItem() { Header = "-" });
                }

                var close = new MenuItem();
                close.Header = App.Text("PageTabBar.Tab.Close");
                close.Tag = OperatingSystem.IsMacOS() ? "⌘+W" : "Ctrl+W";
                close.Click += (_, ev) =>
                {
                    vm.CloseTab(page);
                    ev.Handled = true;
                };
                menu.Items.Add(close);

                var closeOthers = new MenuItem();
                closeOthers.Header = App.Text("PageTabBar.Tab.CloseOther");
                closeOthers.Click += (_, ev) =>
                {
                    vm.CloseOtherTabs();
                    ev.Handled = true;
                };
                menu.Items.Add(closeOthers);

                var closeRight = new MenuItem();
                closeRight.Header = App.Text("PageTabBar.Tab.CloseRight");
                closeRight.Click += (_, ev) =>
                {
                    vm.CloseRightTabs();
                    ev.Handled = true;
                };
                menu.Items.Add(closeRight);
                _tabContextMenu?.Close();
                _tabContextMenu = menu;
                menu.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_tabContextMenu, menu))
                        _tabContextMenu = null;
                };
                menu.Open(border);
            }
        }

        private void OnCloseTab(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && DataContext is ViewModels.Launcher vm)
                vm.CloseTab(btn.DataContext as ViewModels.LauncherPage);

            e.Handled = true;
        }

        private async void OpenLocalRepository(object _1, RoutedEventArgs e)
        {
            var activePage = App.GetLauncher().ActivePage;
            if (activePage == null || !activePage.CanCreatePopup())
                return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return;

            var preference = ViewModels.Preferences.Instance;
            var workspace = preference.GetActiveWorkspace();
            var initDir = workspace.DefaultCloneDir;
            if (string.IsNullOrEmpty(initDir) || !global::System.IO.Directory.Exists(initDir))
                initDir = preference.GitDefaultCloneDir;

            var options = new FolderPickerOpenOptions() { AllowMultiple = false };
            if (global::System.IO.Directory.Exists(initDir))
            {
                var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initDir);
                options.SuggestedStartLocation = folder;
            }

            try
            {
                var selected = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
                if (selected.Count == 1)
                {
                    var folder = selected[0];
                    var folderPath = folder is { Path: { IsAbsoluteUri: true } path } ? path.LocalPath : folder?.Path.ToString();
                    var repoPath = await ViewModels.Welcome.Instance.GetRepositoryRootAsync(folderPath);
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        await ViewModels.Welcome.Instance.AddRepositoryAsync(repoPath, null, false, true);
                        ViewModels.Welcome.Instance.Refresh();
                    }
                    else if (global::System.IO.Directory.Exists(folderPath))
                    {
                        var test = await new Commands.QueryRepositoryRootPath(folderPath).GetResultAsync();
                        activePage.Popup = new ViewModels.Init(activePage.Node.Id, folderPath, null, 0, test.StdErr);
                    }
                }
            }
            catch (Exception exception)
            {
                App.RaiseException(string.Empty, $"Failed to open repository: {exception.Message}");
            }

            e.Handled = true;
        }

        private bool _isScrollButtonVisible = false;
        private readonly Vector _scrollStep = new(64, 0);
        private PointerPressedEventArgs _pressedTabEvent = null;
        private Point _pressedTabPosition = new();
        private bool _startDragTab = false;
        private ContextMenu _tabContextMenu = null;
        private TopLevel _topLevel = null;
        private readonly DataFormat<string> _dndMainTabFormat = DataFormat.CreateStringApplicationFormat("sourcegit-dnd-main-tab");
        private TabRenderState _lastTabRenderState = TabRenderState.Empty;

        private readonly record struct TabRenderState(
            Vector Offset,
            Size Viewport,
            Size Extent,
            Rect SelectedBounds,
            int SelectedIndex,
            int ItemCount)
        {
            public static TabRenderState Empty { get; } = new(
                new Vector(double.NaN, double.NaN),
                new Size(double.NaN, double.NaN),
                new Size(double.NaN, double.NaN),
                new Rect(double.NaN, double.NaN, double.NaN, double.NaN),
                -2,
                -1);

            public bool ApproximatelyEquals(TabRenderState other)
            {
                return AreClose(Offset.X, other.Offset.X) &&
                    AreClose(Offset.Y, other.Offset.Y) &&
                    AreClose(Viewport.Width, other.Viewport.Width) &&
                    AreClose(Viewport.Height, other.Viewport.Height) &&
                    AreClose(Extent.Width, other.Extent.Width) &&
                    AreClose(Extent.Height, other.Extent.Height) &&
                    AreClose(SelectedBounds.X, other.SelectedBounds.X) &&
                    AreClose(SelectedBounds.Y, other.SelectedBounds.Y) &&
                    AreClose(SelectedBounds.Width, other.SelectedBounds.Width) &&
                    AreClose(SelectedBounds.Height, other.SelectedBounds.Height) &&
                    SelectedIndex == other.SelectedIndex &&
                    ItemCount == other.ItemCount;
            }

            private static bool AreClose(double left, double right)
            {
                return double.IsNaN(left) && double.IsNaN(right) || Math.Abs(left - right) < 0.01;
            }
        }
    }
}
