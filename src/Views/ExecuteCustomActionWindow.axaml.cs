using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class ExecuteCustomActionWindow : ChromelessWindow
    {
        public ExecuteCustomActionWindow()
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

            if (DataContext is ViewModels.ExecuteCustomAction vm && vm.ControlParameters.Count == 0)
                await ProcessAsync(vm);
        }

        private async void OnSureByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ExecuteCustomAction vm)
                await ProcessAsync(vm);

            e.Handled = true;
        }

        private async void OnSure(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ExecuteCustomAction vm)
                await ProcessAsync(vm);

            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        private async Task ProcessAsync(ViewModels.ExecuteCustomAction vm)
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

        private bool _initialized = false;
    }
}
