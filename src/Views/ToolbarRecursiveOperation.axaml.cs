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

        private void OnManualClose(object sender, RoutedEventArgs e)
        {
            var launcherPage = this.FindAncestorOfType<LauncherPage>();
            if (launcherPage?.DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();

            e.Handled = true;
        }
    }
}
