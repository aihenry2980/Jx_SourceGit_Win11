using System;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RepositoryBackgroundTask : UserControl
    {
        public RepositoryBackgroundTask()
        {
            InitializeComponent();
            _elapsedTimer.Tick += (_, _) => UpdateElapsed();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            RestartElapsedTimer();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            _elapsedTimer.Stop();
            base.OnUnloaded(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (IsLoaded)
                RestartElapsedTimer();
        }

        private void RestartElapsedTimer()
        {
            _elapsedTimer.Stop();
            UpdateElapsed();
            if (DataContext is ViewModels.RepositoryBackgroundTask { IsRunning: true })
                _elapsedTimer.Start();
        }

        private void UpdateElapsed()
        {
            if (DataContext is not ViewModels.RepositoryBackgroundTask task)
                return;

            var duration = (task.Log.IsComplete ? task.Log.EndTime : DateTime.Now) - task.Log.StartTime;
            ElapsedText.Text = duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                : $"{Math.Max(0, (int)duration.TotalSeconds)}s";
            if (!task.IsRunning)
                _elapsedTimer.Stop();
        }

        private async void OnOpenLog(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryBackgroundTask task && GetRepository() is { } repo)
                await this.ShowDialogAsync(new ViewModels.ViewLogs(repo) { SelectedLog = task.Log });
            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryBackgroundTask task)
                task.Log.Cancel();
            e.Handled = true;
        }

        private async void OnRetry(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryBackgroundTask task && GetRepository() is { } repo)
            {
                try
                {
                    await repo.RetryBackgroundTaskAsync(task);
                }
                catch (Exception ex)
                {
                    App.RaiseException(repo.FullPath, ex.Message);
                }
            }
            e.Handled = true;
        }

        private void OnDismiss(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryBackgroundTask task)
                GetRepository()?.DismissBackgroundTask(task);
            e.Handled = true;
        }

        private ViewModels.Repository GetRepository() => this.FindAncestorOfType<Repository>()?.DataContext as ViewModels.Repository;

        private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    }
}
