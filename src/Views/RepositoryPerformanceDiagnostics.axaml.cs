using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class RepositoryPerformanceDiagnostics : ChromelessWindow
    {
        public RepositoryPerformanceDiagnostics()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        private async void OnRunAgain(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryPerformanceDiagnostics vm)
                await vm.RunAsync();

            e.Handled = true;
        }
    }
}
