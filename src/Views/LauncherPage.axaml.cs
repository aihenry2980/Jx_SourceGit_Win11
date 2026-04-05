using Avalonia.Controls;
using System;
using System.ComponentModel;

using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class LauncherPage : UserControl
    {
        public LauncherPage()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property.Name == nameof(ActualThemeVariant))
                RefreshToolbarBackground();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_ownerPage != null)
                _ownerPage.PropertyChanged -= OnOwnerPagePropertyChanged;
            if (_toolbarRepo != null)
                _toolbarRepo.PropertyChanged -= OnToolbarRepoPropertyChanged;

            _ownerPage = DataContext as ViewModels.LauncherPage;
            if (_ownerPage != null)
                _ownerPage.PropertyChanged += OnOwnerPagePropertyChanged;

            AttachToolbarRepo(_ownerPage?.Data as ViewModels.Repository);
            RefreshToolbarBackground();
        }

        private async void OnPopupSureByHotKey(object sender, RoutedEventArgs e)
        {
            var children = PopupPanel.GetLogicalDescendants();
            foreach (var child in children)
            {
                if (child is Control { IsKeyboardFocusWithin: true, Tag: StealHotKey steal } control &&
                    steal is { Key: Key.Enter, KeyModifiers: KeyModifiers.None })
                {
                    var fake = new KeyEventArgs()
                    {
                        RoutedEvent = KeyDownEvent,
                        Route = RoutingStrategies.Direct,
                        Source = control,
                        Key = Key.Enter,
                        KeyModifiers = KeyModifiers.None,
                        PhysicalKey = PhysicalKey.Enter,
                    };

                    if (control is AvaloniaEdit.TextEditor editor)
                        editor.TextArea.TextView.RaiseEvent(fake);
                    else
                        control.RaiseEvent(fake);

                    e.Handled = false;
                    return;
                }
            }

            if (DataContext is ViewModels.LauncherPage page)
                await page.ProcessPopupAsync();

            e.Handled = true;
        }

        private async void OnPopupSure(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                await page.ProcessPopupAsync();

            e.Handled = true;
        }

        private void OnPopupCancel(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();

            e.Handled = true;
        }

        private void OnMaskClicked(object sender, PointerPressedEventArgs e)
        {
            OnPopupCancel(sender, e);
        }

        private async void OnCopyNotification(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.Notification notice })
                await this.CopyTextAsync(notice.Message);

            e.Handled = true;
        }

        private void OnDismissNotification(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.Notification notice } &&
                DataContext is ViewModels.LauncherPage page)
                page.Notifications.Remove(notice);

            e.Handled = true;
        }

        private void OnToolBarPointerPressed(object sender, PointerPressedEventArgs e)
        {
            this.FindAncestorOfType<ChromelessWindow>()?.BeginMoveWindow(sender, e);
        }

        private void OnOwnerPagePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.LauncherPage.Data))
            {
                AttachToolbarRepo(_ownerPage?.Data as ViewModels.Repository);
                RefreshToolbarBackground();
            }
        }

        private void AttachToolbarRepo(ViewModels.Repository repo)
        {
            if (_toolbarRepo != null)
                _toolbarRepo.PropertyChanged -= OnToolbarRepoPropertyChanged;

            _toolbarRepo = repo;
            if (_toolbarRepo != null)
                _toolbarRepo.PropertyChanged += OnToolbarRepoPropertyChanged;
        }

        private void OnToolbarRepoPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.Repository.AccentToolbarBackground))
                RefreshToolbarBackground();
        }

        private void RefreshToolbarBackground()
        {
            if (_toolbarRepo != null)
            {
                ToolBarBorder.Background = _toolbarRepo.AccentToolbarBackground;
                return;
            }

            var fallback = this.FindResource("Brush.ToolBar") as IBrush;
            ToolBarBorder.Background = fallback;
        }

        private ViewModels.LauncherPage _ownerPage = null;
        private ViewModels.Repository _toolbarRepo = null;
    }
}
