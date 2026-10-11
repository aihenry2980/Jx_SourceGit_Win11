using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SourceGit.Views
{
    public partial class ViewLogs : ChromelessWindow
    {
        public ViewLogs()
        {
            CloseOnESC = true;
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (DataContext is ViewModels.ViewLogs vm && vm.Logs.Count > 0)
            {
                vm.SelectedLog ??= vm.Logs[0];
                WatchSelectedLog(vm.SelectedLog);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _logContextMenu?.Close();
            _logContextMenu = null;
            if (DataContext is ViewModels.ViewLogs vm)
                vm.PropertyChanged -= OnViewModelPropertyChanged;

            WatchSelectedLog(null);
            _autoCloseCancellation?.Cancel();
            _autoCloseCancellation?.Dispose();
            _autoCloseCancellation = null;
            base.OnClosed(e);
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (DataContext is ViewModels.ViewLogs vm)
            {
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.PropertyChanged += OnViewModelPropertyChanged;
                WatchSelectedLog(vm.SelectedLog);
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.ViewLogs.SelectedLog) &&
                DataContext is ViewModels.ViewLogs vm)
                WatchSelectedLog(vm.SelectedLog);
        }

        private void WatchSelectedLog(ViewModels.CommandLog log)
        {
            if (ReferenceEquals(_watchedLog, log))
                return;

            if (_watchedLog != null)
                _watchedLog.PropertyChanged -= OnSelectedLogPropertyChanged;

            _autoCloseCancellation?.Cancel();
            Root.Background = Brushes.Transparent;
            AutoCloseCountdown.IsVisible = false;

            _watchedLog = log;
            if (_watchedLog != null)
            {
                _watchedLog.PropertyChanged += OnSelectedLogPropertyChanged;
                BeginAutoCloseIfNeeded(_watchedLog);
            }
        }

        private void OnSelectedLogPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModels.CommandLog.IsComplete) || sender is not ViewModels.CommandLog log)
                return;

            BeginAutoCloseIfNeeded(log);
        }

        private void BeginAutoCloseIfNeeded(ViewModels.CommandLog log)
        {
            if (!ReferenceEquals(log, _watchedLog) || !log.IsComplete || !log.IsSuccessful || !log.AutoCloseOnSuccess)
                return;

            if (DataContext is ViewModels.ViewLogs vm && vm.Logs.Any(task => !task.IsComplete))
                return;

            log.AutoCloseOnSuccess = false;
            _ = FlashSuccessAndCloseAsync();
        }

        private async Task FlashSuccessAndCloseAsync()
        {
            _autoCloseCancellation?.Cancel();
            _autoCloseCancellation?.Dispose();
            _autoCloseCancellation = new CancellationTokenSource();
            var cancellation = _autoCloseCancellation.Token;

            try
            {
                var closeAt = DateTime.UtcNow.AddSeconds(10);
                AutoCloseCountdown.IsVisible = true;

                while (true)
                {
                    if (DataContext is ViewModels.ViewLogs vm && vm.Logs.Any(task => !task.IsComplete))
                    {
                        Root.Background = Brushes.Transparent;
                        AutoCloseCountdown.IsVisible = false;
                        return;
                    }

                    var remaining = closeAt - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    AutoCloseCountdown.Text = App.Text("ViewLogs.AutoClose", Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)));
                    Root.Background = new SolidColorBrush(Color.Parse("#664CAF50"));
                    await Task.Delay(280, cancellation);
                    Root.Background = Brushes.Transparent;
                    await Task.Delay(420, cancellation);
                }

                AutoCloseCountdown.IsVisible = false;
                Close();
            }
            catch (OperationCanceledException)
            {
                Root.Background = Brushes.Transparent;
                AutoCloseCountdown.IsVisible = false;
            }
        }

        private void OnLogContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is not Grid { DataContext: ViewModels.CommandLog log } grid || DataContext is not ViewModels.ViewLogs vm)
                return;

            var copyCommand = new MenuItem();
            copyCommand.Header = App.Text("ViewLogs.CopyCommand");
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
            rm.IsEnabled = log.IsComplete;
            rm.Click += (_, ev) =>
            {
                vm.RemoveLog(log);
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(copyCommand);
            menu.Items.Add(copy);
            var cancel = new MenuItem
            {
                Header = App.Text("ViewLogs.CancelTask"),
                Icon = this.CreateMenuIcon("Icons.Clear"),
                IsEnabled = log.CanCancel,
            };
            cancel.Click += (_, ev) =>
            {
                log.Cancel();
                ev.Handled = true;
            };
            menu.Items.Add(cancel);
            menu.Items.Add(rm);
            _logContextMenu?.Close();
            _logContextMenu = menu;
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

        private void OnClose(object _, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        private void OnCancelSelectedTask(object _, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ViewLogs { SelectedLog: { } log })
                log.Cancel();

            e.Handled = true;
        }

        private void OnFollowOutput(object _, Avalonia.Interactivity.RoutedEventArgs e)
        {
            LogContent.FollowOutput();
            e.Handled = true;
        }

        private void OnLogKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key is not (Key.Delete or Key.Back))
                return;

            if (DataContext is ViewModels.ViewLogs { SelectedLog: { } log } vm)
                vm.RemoveLog(log);

            e.Handled = true;
        }

        private ViewModels.CommandLog _watchedLog;
        private ContextMenu _logContextMenu;
        private CancellationTokenSource _autoCloseCancellation;
    }
}
