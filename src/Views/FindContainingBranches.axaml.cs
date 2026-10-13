using System;

using Avalonia.Controls;
using Avalonia.Input;
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
            AddCopyNameItem(menu, branch.Name);
            OpenResultContextMenu(menu, control);
            e.Handled = true;
        }

        private void OnTagContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is not Control { DataContext: ViewModels.ContainingTag tag } control)
                return;

            var menu = new ContextMenu();
            AddCopyNameItem(menu, tag.Name);
            OpenResultContextMenu(menu, control);
            e.Handled = true;
        }

        private void AddCopyNameItem(ContextMenu menu, string name)
        {
            var copy = new MenuItem
            {
                Header = App.Text("FindContainingBranches.CopyName"),
                Icon = App.CreateMenuIcon("Icons.Copy"),
            };
            copy.Click += async (_, ev) =>
            {
                await this.CopyTextAsync(name);
                ev.Handled = true;
            };
            menu.Items.Add(copy);
        }

        private void OpenResultContextMenu(ContextMenu menu, Control control)
        {
            _branchContextMenu?.Close();
            _branchContextMenu = menu;
            menu.Open(control);
        }

        private async void OnResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || !e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) ||
                sender is not ListBox list)
                return;

            var name = list.SelectedItem switch
            {
                ViewModels.ContainingBranch branch => branch.Name,
                ViewModels.ContainingTag tag => tag.Name,
                _ => null,
            };
            if (name == null)
                return;

            e.Handled = true;
            await this.CopyTextAsync(name);
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
