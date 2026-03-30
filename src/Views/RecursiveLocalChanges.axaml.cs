using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class RecursiveLocalChanges : ChromelessWindow
    {
        public RecursiveLocalChanges()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_initialized)
                return;

            _initialized = true;

            if (DataContext is ViewModels.RecursiveLocalChanges vm)
                await RefreshAsync(vm);
        }

        private async void OnRefresh(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RecursiveLocalChanges vm)
                await RefreshAsync(vm);

            e.Handled = true;
        }

        private void OnHiddenExtensionFilterKeyDown(object _, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.RecursiveLocalChanges vm)
                return;

            if (e.Key == Key.Enter)
            {
                vm.CommitHiddenExtensionFilterUsage();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.HiddenExtensionFilterText = string.Empty;
                e.Handled = true;
            }
        }

        private void OnHiddenExtensionFilterLostFocus(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RecursiveLocalChanges vm)
                vm.CommitHiddenExtensionFilterUsage();
        }

        private void OnAppendRecentHiddenExtension(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RecursiveLocalChanges vm &&
                sender is Control { DataContext: string ext })
            {
                vm.AppendHiddenExtensionFilter(ext);
                e.Handled = true;
            }
        }

        private void OnToggleRepositoryEntryExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: ViewModels.RecursiveLocalChanges.RepositoryEntry entry })
            {
                entry.IsExpanded = !entry.IsExpanded;
                e.Handled = true;
            }
        }

        private void OnOpenChangeDiff(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                sender is not Control { DataContext: Models.Change change } control)
                return;

            StyledElement current = control;
            while (current != null && current.DataContext is not ViewModels.RecursiveLocalChanges.RepositoryEntry)
                current = current.Parent as StyledElement;

            if (current?.DataContext is ViewModels.RecursiveLocalChanges.RepositoryEntry entry)
                App.ShowWindow(new ViewModels.RecursiveLocalChangeDiff(entry.RepositoryPath, change));

            e.Handled = true;
        }

        private static async Task RefreshAsync(ViewModels.RecursiveLocalChanges vm)
        {
            try
            {
                await vm.RefreshAsync();
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private bool _initialized = false;
    }
}
