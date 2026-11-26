using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class RecursiveFetchProgress : UserControl
    {
        public RecursiveFetchProgress()
        {
            InitializeComponent();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.IsRecursiveFetchProgressVisible = false;
            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.RecursiveFetchTask?.Log.Cancel();
            e.Handled = true;
        }

        private async void OnOpenLog(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository { RecursiveFetchTask: { } task } repo)
            {
                var dialog = this.ShowDialogAsync(new ViewModels.ViewLogs(repo) { SelectedLog = task.Log });
                if (dialog != null)
                    await dialog;
            }
            e.Handled = true;
        }
    }
}
