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
