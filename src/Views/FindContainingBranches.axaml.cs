using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class FindContainingBranches : ChromelessWindow
    {
        public FindContainingBranches()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (DataContext is ViewModels.FindContainingBranches vm)
                await vm.SearchAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _branchContextMenu?.Close();
            _branchContextMenu = null;
            if (DataContext is ViewModels.FindContainingBranches vm)
                vm.Dispose();
            base.OnClosed(e);
        }

        private void OnBranchContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is not Control { DataContext: ViewModels.ContainingBranch branch } control ||
                DataContext is not ViewModels.FindContainingBranches vm)
                return;

            var add = new MenuItem
            {
                Header = App.Text("FindContainingBranches.AddToFilter"),
                Icon = App.CreateMenuIcon("Icons.Filter"),
            };
            add.Click += (_, ev) =>
            {
                vm.AddToFilter(branch);
                ev.Handled = true;
            };
            var menu = new ContextMenu();
            menu.Items.Add(add);
            _branchContextMenu?.Close();
            _branchContextMenu = menu;
            menu.Open(control);
            e.Handled = true;
        }

        private async void OnSearch(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FindContainingBranches vm)
                await vm.SearchAsync();
            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FindContainingBranches vm)
                vm.Cancel();
            e.Handled = true;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        private ContextMenu _branchContextMenu;
    }
}
