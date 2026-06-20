using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    public partial class ViewLogs : ChromelessWindow
    {
        public ViewLogs()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (DataContext is ViewModels.ViewLogs vm && vm.Logs.Count > 0)
                vm.SelectedLog = vm.Logs[0];
        }

        private void OnLogContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is not Grid { DataContext: ViewModels.CommandLog log } grid || DataContext is not ViewModels.ViewLogs vm)
                return;

            var copyCommand = new MenuItem();
            copyCommand.Header = "Copy current command";
            copyCommand.Icon = App.CreateMenuIcon("Icons.Copy");
            copyCommand.IsEnabled = !string.IsNullOrWhiteSpace(log.LatestCommand);
            copyCommand.Click += async (_, ev) =>
            {
                await App.CopyTextAsync(log.LatestCommand ?? string.Empty);
                ev.Handled = true;
            };

            var copy = new MenuItem();
            copy.Header = App.Text("ViewLogs.CopyLog");
            copy.Icon = this.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, ev) =>
            {
                await this.CopyTextAsync(log.Content);
                ev.Handled = true;
            };

            var rm = new MenuItem();
            rm.Header = App.Text("ViewLogs.Delete");
            rm.Icon = this.CreateMenuIcon("Icons.Clear");
            rm.Click += (_, ev) =>
            {
                vm.Logs.Remove(log);
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(copyCommand);
            menu.Items.Add(copy);
            menu.Items.Add(rm);
            menu.Open(grid);

            e.Handled = true;
        }

        private async void OnCopyCurrentCommand(object _, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ViewLogs { SelectedLog: { } log } &&
                !string.IsNullOrWhiteSpace(log.LatestCommand))
                await App.CopyTextAsync(log.LatestCommand);

            e.Handled = true;
        }

        private void OnLogKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key is not (Key.Delete or Key.Back))
                return;

            if (DataContext is ViewModels.ViewLogs { SelectedLog: { } log } vm)
                vm.Logs.Remove(log);

            e.Handled = true;
        }
    }
}
