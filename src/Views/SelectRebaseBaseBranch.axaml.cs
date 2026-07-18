using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SelectRebaseBaseBranch : UserControl
    {
        public SelectRebaseBaseBranch()
        {
            InitializeComponent();
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.SelectRebaseBaseBranch vm)
                return;

            if (e.Key == Key.Down && vm.VisibleBranches.Count > 0)
            {
                BranchList.SelectedIndex = 0;
                BranchList.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                vm.ConfirmSearchText();
                e.Handled = true;
            }
        }

        private void OnBranchListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                DataContext is ViewModels.SelectRebaseBaseBranch vm &&
                BranchList.SelectedItem is ViewModels.RebaseBaseBranchChoice choice)
            {
                vm.Select(choice);
                e.Handled = true;
            }
        }

        private void OnBranchPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.SelectRebaseBaseBranch vm &&
                sender is Control { DataContext: ViewModels.RebaseBaseBranchChoice choice })
            {
                vm.Select(choice);
                e.Handled = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelectRebaseBaseBranch vm)
                vm.Cancel();

            e.Handled = true;
        }
    }
}
