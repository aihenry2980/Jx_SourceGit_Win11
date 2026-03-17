using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class ToolbarRecursiveOperation : UserControl
    {
        public ToolbarRecursiveOperation()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnAnyKeyDown, RoutingStrategies.Tunnel);
        }

        private void OnCloseImmediately(object sender, RoutedEventArgs e)
        {
            var launcherPage = this.FindAncestorOfType<LauncherPage>();
            if (launcherPage?.DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();
            else
                this.FindAncestorOfType<Window>()?.Close();

            e.Handled = true;
        }

        private void OnCancelOperation(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                vm.CancelOperation();

            e.Handled = true;
        }

        private async void OnCopyMessage(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                await App.CopyTextAsync(vm.Log?.Content ?? string.Empty);

            e.Handled = true;
        }

        private async void OnCopyCurrentCommand(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm &&
                !string.IsNullOrWhiteSpace(vm.Log?.LatestCommand))
                await App.CopyTextAsync(vm.Log.LatestCommand);

            e.Handled = true;
        }

        private void OnManualClose(object sender, RoutedEventArgs e)
        {
            var launcherPage = this.FindAncestorOfType<LauncherPage>();
            if (launcherPage?.DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();
            else
                this.FindAncestorOfType<Window>()?.Close();

            e.Handled = true;
        }

        private void OnSelectAllSubmodules(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                vm.SelectAllSubmodules();

            e.Handled = true;
        }

        private void OnClearSubmoduleSelection(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                vm.ClearSubmoduleSelection();

            e.Handled = true;
        }

        private void OnSubmoduleSelectionListKeyDown(object sender, KeyEventArgs e)
        {
            HandleSubmoduleSelectionKey(e);
        }

        private void OnAnyKeyDown(object sender, KeyEventArgs e)
        {
            HandleSubmoduleSelectionKey(e);
        }

        internal bool HandleSubmoduleSelectionKey(KeyEventArgs e)
        {
            if (e.Handled ||
                DataContext is not ViewModels.ToolbarRecursiveOperation vm ||
                !vm.IsSubmoduleSelectionVisible ||
                vm.InProgress ||
                vm.SubmoduleSelections.Count == 0)
                return false;

            var selected = vm.SelectedSubmoduleSelection ?? vm.SubmoduleSelections[0];
            var index = vm.SubmoduleSelections.IndexOf(selected);
            if (index < 0)
                index = 0;

            var nextIndex = index;
            switch (e.Key)
            {
                case Key.Left:
                    nextIndex = Math.Max(0, index - 1);
                    break;
                case Key.Right:
                    nextIndex = Math.Min(vm.SubmoduleSelections.Count - 1, index + 1);
                    break;
                case Key.Up:
                    nextIndex = Math.Max(0, index - 2);
                    break;
                case Key.Down:
                    nextIndex = Math.Min(vm.SubmoduleSelections.Count - 1, index + 2);
                    break;
                case Key.Space:
                    selected.IsSelected = !selected.IsSelected;
                    e.Handled = true;
                    SubmoduleSelectionList?.ScrollIntoView(selected);
                    return true;
                default:
                    return false;
            }

            if (nextIndex != index)
            {
                var next = vm.SubmoduleSelections[nextIndex];
                vm.SelectedSubmoduleSelection = next;
                SubmoduleSelectionList?.ScrollIntoView(next);
            }

            e.Handled = true;
            return true;
        }
    }
}
