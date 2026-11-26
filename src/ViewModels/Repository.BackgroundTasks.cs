using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    public partial class Repository
    {
        private sealed record BackgroundFetchTarget(string Path, string Scope, List<string> Remotes);

        public AvaloniaList<RepositoryBackgroundTask> BackgroundTasks { get; } = [];

        public RepositoryBackgroundTask RecursiveFetchTask
        {
            get => _recursiveFetchTask;
            private set
            {
                if (SetProperty(ref _recursiveFetchTask, value))
                    RecursiveFetchCloseSeconds = 0;
            }
        }

        public int RecursiveFetchCloseSeconds
        {
            get => _recursiveFetchCloseSeconds;
            private set => SetProperty(ref _recursiveFetchCloseSeconds, value);
        }

        public bool IsRecursiveFetchProgressVisible
        {
            get => _isRecursiveFetchProgressVisible;
            set => SetProperty(ref _isRecursiveFetchProgressVisible, value);
        }

        private async Task HideRecursiveFetchProgressLaterAsync(RepositoryBackgroundTask task)
        {
            for (var seconds = 5; seconds >= 0; seconds--)
            {
                if (seconds < 5)
                    await Task.Delay(TimeSpan.FromSeconds(1));

                var active = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(RecursiveFetchTask, task) || task.IsRunning)
                        return false;

                    RecursiveFetchCloseSeconds = seconds;
                    if (seconds == 0)
                        IsRecursiveFetchProgressVisible = false;
                    return true;
                });
                if (!active)
                    return;
            }
        }

        private RepositoryBackgroundTask TrackBackgroundTask(CommandLog log)
        {
            var task = new RepositoryBackgroundTask(log);
            BackgroundTasks.Add(task);
            log.PropertyChanged += OnCompleted;
            return task;

            void OnCompleted(object sender, PropertyChangedEventArgs e)
            {
                if (!log.IsComplete)
                    return;

                log.PropertyChanged -= OnCompleted;
                if (!task.IsFailed)
                    _ = DismissCompletedTaskLaterAsync(task);
            }
        }

        private async Task DismissCompletedTaskLaterAsync(RepositoryBackgroundTask task)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            await Dispatcher.UIThread.InvokeAsync(() => BackgroundTasks.Remove(task));
        }

        public void DismissBackgroundTask(RepositoryBackgroundTask task)
        {
            if (!task.IsRunning)
                BackgroundTasks.Remove(task);
        }

        public async Task RetryBackgroundTaskAsync(RepositoryBackgroundTask task)
        {
            if (IsBackgroundRecursiveFetchRunning || !task.CanRetry)
                return;

            await task.RetryAsync();
            DismissBackgroundTask(task);
        }

        private RepositoryBackgroundTask _recursiveFetchTask;
        private bool _isRecursiveFetchProgressVisible;
        private int _recursiveFetchCloseSeconds;
    }
}
