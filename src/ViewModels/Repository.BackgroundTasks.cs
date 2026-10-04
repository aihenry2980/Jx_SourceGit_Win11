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
    }
}
