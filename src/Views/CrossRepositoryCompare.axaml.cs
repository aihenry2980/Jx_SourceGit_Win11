using System;
using System.Text;

using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    public partial class CrossRepositoryCompare : ChromelessWindow
    {
        public CrossRepositoryCompare()
        {
            InitializeComponent();
        }

        private void OnPressedLeftSHA(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.CrossRepositoryCompare vm)
                vm.NavigateToLeft();

            e.Handled = true;
        }

        private void OnPressedRightSHA(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is ViewModels.CrossRepositoryCompare vm)
                vm.NavigateToRight();

            e.Handled = true;
        }

        private void OnChangeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.CrossRepositoryCompare vm ||
                sender is not ChangeCollectionView view ||
                view.SelectedChanges is not { Count: > 0 } selected)
            {
                e.Handled = true;
                return;
            }

            var menu = new ContextMenu();
            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = App.CreateMenuIcon("Icons.Copy");
            copyPath.Click += async (_, ev) =>
            {
                var builder = new StringBuilder();
                foreach (var c in selected)
                    builder.AppendLine(c.Path);

                await App.CopyTextAsync(builder.ToString().TrimEnd());
                ev.Handled = true;
            };

            menu.Items.Add(copyPath);
            menu.Open(view);
            e.Handled = true;
        }

        private async void OnChangeCollectionViewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not ChangeCollectionView { SelectedChanges: { Count: > 0 } selectedChanges })
                return;

            if (e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) && e.Key == Key.C)
            {
                var builder = new StringBuilder();
                foreach (var c in selectedChanges)
                    builder.AppendLine(c.Path);

                await App.CopyTextAsync(builder.ToString().TrimEnd());
                e.Handled = true;
            }
        }
    }
}
