using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SubmoduleCommitFlow : UserControl
    {
        public SubmoduleCommitFlow()
        {
            InitializeComponent();
        }

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

        private void OnOpenTerminalClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow { SelectedNode: { } node })
                Native.OS.OpenTerminal(node.RepoPath);

            e.Handled = true;
        }
    }
}
