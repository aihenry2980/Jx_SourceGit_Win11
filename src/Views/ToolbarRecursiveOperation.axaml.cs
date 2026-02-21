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

            e.Handled = true;
        }

        private void OnStopCountdown(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                vm.StopCountdown();

            e.Handled = true;
        }

        private async void OnCopyMessage(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                await App.CopyTextAsync(vm.Log?.Content ?? string.Empty);

            e.Handled = true;
        }

        private void OnManualClose(object sender, RoutedEventArgs e)
        {
            var launcherPage = this.FindAncestorOfType<LauncherPage>();
            if (launcherPage?.DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();

            e.Handled = true;
        }
    }
}
