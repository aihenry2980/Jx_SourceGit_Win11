using System;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class ToolbarRecursiveOperation : UserControl
    {
        public ToolbarRecursiveOperation()
        {
            InitializeComponent();
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

    }
}
