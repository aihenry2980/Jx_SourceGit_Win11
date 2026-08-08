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
                vm.HiddenExtensionInputText = string.Empty;
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
                sender is Control { DataContext: ViewModels.RecursiveLocalChanges.HiddenExtensionTag tag })
            {
                vm.AppendHiddenExtensionFilter(tag.Extension);
                e.Handled = true;
            }
        }

        private void OnRemoveRecentHiddenExtension(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RecursiveLocalChanges vm &&
                sender is Control { DataContext: ViewModels.RecursiveLocalChanges.HiddenExtensionTag tag })
            {
                vm.ForgetRecentHiddenExtension(tag.Extension);
                e.Handled = true;
            }
        }

        private void OnRemoveHiddenExtensionFilter(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RecursiveLocalChanges vm &&
                sender is Control { DataContext: ViewModels.RecursiveLocalChanges.HiddenExtensionTag tag })
            {
                vm.RemoveHiddenExtensionFilter(tag.Extension);
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

        private async void OnRevertRepositoryEntryChanges(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.RecursiveLocalChanges vm ||
                sender is not Control { DataContext: ViewModels.RecursiveLocalChanges.RepositoryEntry entry } ||
                entry.Changes.Count == 0)
                return;

            var confirmed = await App.AskConfirmAsync(
                this,
                $"Revert all {entry.Changes.Count} listed change{(entry.Changes.Count == 1 ? string.Empty : "s")} in '{entry.DisplayName}'?\n\nThis cannot be undone.",
                Models.ConfirmButtonType.YesNo);
            if (!confirmed)
                return;

            await RevertChangesAsync(vm, () => vm.RevertRepositoryChangesAsync(entry));
            e.Handled = true;
        }

        private async void OnRevertAllChangesRecursively(object _, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.RecursiveLocalChanges vm || !vm.CanRevertAllChanges)
                return;

            var repositoryText = vm.AllRepositoryCount == 1 ? "repository" : "repositories";
            var confirmed = await App.AskConfirmAsync(
                this,
                $"Revert all {vm.AllChangeCount} change{(vm.AllChangeCount == 1 ? string.Empty : "s")} across {vm.AllRepositoryCount} {repositoryText} recursively?\n\nThis includes changes hidden by extension filters and cannot be undone.",
                Models.ConfirmButtonType.YesNo);
            if (!confirmed)
                return;

            await RevertChangesAsync(vm, vm.RevertAllChangesRecursivelyAsync);
            e.Handled = true;
        }

        private void OnOpenChangeDiff(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                sender is not Control { DataContext: Models.Change change } control)
                return;

            if (FindRepositoryEntry(control) is { } entry)
                App.ShowWindow(new ViewModels.RecursiveLocalChangeDiff(entry.RepositoryPath, change));

            e.Handled = true;
        }

        private void OnChangeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.RecursiveLocalChanges vm ||
                sender is not Control { DataContext: Models.Change change } control ||
                FindRepositoryEntry(control) is not { } entry)
                return;

            var menu = new ContextMenu();

            var revert = new MenuItem();
            revert.Header = "Revert this file";
            revert.Icon = this.CreateMenuIcon("Icons.Undo");
            revert.Click += async (_, ev) =>
            {
                var confirmed = await App.AskConfirmAsync(
                    this,
                    $"Revert '{change.Path}' in '{entry.DisplayName}'?\n\nThis cannot be undone.",
                    Models.ConfirmButtonType.YesNo);
                if (confirmed)
                    await RevertChangesAsync(vm, () => vm.RevertSingleChangeAsync(entry, change));

                ev.Handled = true;
            };
            menu.Items.Add(revert);

            menu.Open(control);
            e.Handled = true;
        }

        private static ViewModels.RecursiveLocalChanges.RepositoryEntry FindRepositoryEntry(StyledElement control)
        {
            StyledElement current = control;
            while (current != null && current.DataContext is not ViewModels.RecursiveLocalChanges.RepositoryEntry)
                current = current.Parent as StyledElement;

            return current?.DataContext as ViewModels.RecursiveLocalChanges.RepositoryEntry;
        }

        private async Task RevertChangesAsync(ViewModels.RecursiveLocalChanges vm, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                App.LogException(ex);
                await RefreshAsync(vm);
            }
            finally
            {
                // Reverting refreshes the owning repository and may activate its main window.
                // Restore this utility window after the refresh has settled.
                if (IsVisible)
                    Activate();
            }
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
