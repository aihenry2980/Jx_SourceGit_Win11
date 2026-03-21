using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class MemoryProfiler : ChromelessWindow
    {
        public MemoryProfiler()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        private void OnRefresh(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MemoryProfiler vm)
                vm.Refresh();

            e.Handled = true;
        }

        private void OnCollectAndRefresh(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MemoryProfiler vm)
                vm.CollectGarbageAndRefresh();

            e.Handled = true;
        }
    }
}
