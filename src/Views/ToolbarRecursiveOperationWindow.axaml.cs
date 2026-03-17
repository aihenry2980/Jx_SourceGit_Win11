using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class ToolbarRecursiveOperationWindow : ChromelessWindow
    {
        public ToolbarRecursiveOperationWindow()
        {
            CloseOnESC = true;
            InitializeComponent();
            AddHandler(KeyDownEvent, OnAnyKeyDown, RoutingStrategies.Tunnel);

            var layout = ViewModels.Preferences.Instance.Layout;
            Width = Math.Max(MinWidth, layout.ToolbarRecursiveOperationWindowWidth);
            Height = Math.Max(MinHeight, layout.ToolbarRecursiveOperationWindowHeight);
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_initialized)
                return;

            _initialized = true;

            if (DataContext is ViewModels.ToolbarRecursiveOperation vm && vm.CanStartDirectly())
                await ProcessAsync(vm);
        }

        protected override void OnClosed(EventArgs e)
        {
            PersistWindowSize();
            ViewModels.Preferences.Instance.Save();

            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                vm.Cleanup();

            base.OnClosed(e);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            PersistWindowSize();
        }

        private async void OnSureByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                await ProcessAsync(vm);

            e.Handled = true;
        }

        private async void OnSure(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                await ProcessAsync(vm);

            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        private void OnCancelOperation(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ToolbarRecursiveOperation vm)
                vm.CancelOperation();

            e.Handled = true;
        }

        private async Task ProcessAsync(ViewModels.ToolbarRecursiveOperation vm)
        {
            if (vm.InProgress || !vm.Check())
                return;

            vm.InProgress = true;

            try
            {
                var finished = await vm.Sure();
                if (finished)
                    Close();
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
            finally
            {
                vm.InProgress = false;
            }
        }

        private void PersistWindowSize()
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            if (Width >= MinWidth)
                layout.ToolbarRecursiveOperationWindowWidth = Width;
            if (Height >= MinHeight)
                layout.ToolbarRecursiveOperationWindowHeight = Height;
        }

        private void OnAnyKeyDown(object sender, KeyEventArgs e)
        {
            OperationView?.HandleSubmoduleSelectionKey(e);
        }

        private bool _initialized = false;
    }
}
