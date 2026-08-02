using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public partial class SubmoduleCommitFlow : UserControl
    {
        public SubmoduleCommitFlow()
        {
            InitializeComponent();

            _saveLayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveLayoutTimer.Tick += (_, _) =>
            {
                _saveLayoutTimer.Stop();
                SaveLayoutWidths();
            };

            Loaded += OnLoaded;
            DetachedFromVisualTree += (_, _) => _saveLayoutTimer.Stop();

            ModuleListColumn.PropertyChanged += OnLayoutColumnPropertyChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm)
                return;

            _applyingSavedWidths = true;
            ModuleListColumn.Width = new GridLength(vm.SavedModuleListWidth);
            _applyingSavedWidths = false;
        }

        private void OnLayoutColumnPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_applyingSavedWidths || e.Property != ColumnDefinition.WidthProperty)
                return;

            _saveLayoutTimer.Stop();
            _saveLayoutTimer.Start();
        }

        private void SaveLayoutWidths()
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm)
                return;

            var moduleListWidth = GetAbsoluteWidth(ModuleListColumn);
            if (moduleListWidth <= 0)
                return;

            vm.SaveLayoutWidths(moduleListWidth);
        }

        private static double GetAbsoluteWidth(ColumnDefinition column)
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0)
                return column.Width.Value;

            return column.ActualWidth;
        }

        private ColumnDefinition ModuleListColumn => LayoutRootGrid.ColumnDefinitions[0];

        private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.RefreshAsync();

            e.Handled = true;
        }

        private async void OnCommitClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.CommitSelectedNodeAsync();

            e.Handled = true;
        }

        private async void OnCommitAndPushClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.CommitAndPushSelectedNodeAsync();

            e.Handled = true;
        }

        private async void OnUndoCommitClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.UndoSelectedNodeCommitAsync();

            e.Handled = true;
        }

        private void OnCommitIncludeToggled(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.NotifyCommitIncludeChanged();

            e.Handled = true;
        }

        private void OnIncludeAllChangesClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.IncludeAllSelectedNodeChanges();

            e.Handled = true;
        }

        private void OnExcludeAllChangesClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.ExcludeAllSelectedNodeChanges();

            e.Handled = true;
        }

        private void OnNodeDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm &&
                sender is Control { DataContext: ViewModels.SubmoduleCommitFlowNode node })
            {
                vm.OpenGitGraphForNode(node);
            }

            e.Handled = true;
        }

        private void OnNodeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm ||
                sender is not Control { DataContext: ViewModels.SubmoduleCommitFlowNode node } control)
                return;

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Go to Git Graph" };
            open.Click += (_, ev) =>
            {
                vm.OpenGitGraphForNode(node);
                ev.Handled = true;
            };

            menu.Items.Add(open);
            menu.Open(control);
            e.Handled = true;
        }

        private void OnOpenTerminalClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow { SelectedNode: { } node })
                Native.OS.OpenTerminal(node.RepoPath);

            e.Handled = true;
        }

        private readonly DispatcherTimer _saveLayoutTimer;
        private bool _applyingSavedWidths = false;
    }
}
