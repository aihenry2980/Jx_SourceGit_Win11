using System;
using System.ComponentModel;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class RepositoryBackgroundTask : ObservableObject
    {
        public CommandLog Log { get; }
        public string Detail => _detail;
        public int CompletedCount => _completedCount;
        public int TotalCount => Math.Max(1, _totalCount);
        public bool IsIndeterminate => _totalCount == 0;
        public bool IsRunning => !Log.IsComplete;
        public bool IsFailed => Log.IsComplete && !Log.IsSuccessful && !Log.IsCancellationRequested;
        public bool CanCancel => Log.CanCancel && !Log.IsCancellationRequested;
        public bool CanRetry => IsFailed && _retry != null && !_isRetrying;
        public string Summary => $"{Log.Name} | " + (IsRunning
            ? Log.IsCancellationRequested ? "Canceling..." : _totalCount > 0 ? $"{_completedCount}/{_totalCount}" : "Running"
            : IsFailed ? "Failed" : Log.IsCancellationRequested ? "Canceled" : "Completed");

        public RepositoryBackgroundTask(CommandLog log)
        {
            Log = log;
            log.PropertyChanged += OnLogPropertyChanged;
        }

        public void Update(string detail, int completed = 0, int total = 0)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(() => Update(detail, completed, total));
                return;
            }

            _detail = detail;
            _completedCount = Math.Max(0, completed);
            _totalCount = total > 0 ? Math.Max(_completedCount, total) : 0;
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(Summary));
        }

        public void SetRetryAction(Func<Task> retry)
        {
            _retry = retry;
            OnPropertyChanged(nameof(CanRetry));
        }

        public async Task RetryAsync()
        {
            if (!CanRetry)
                return;

            _isRetrying = true;
            OnPropertyChanged(nameof(CanRetry));
            try
            {
                await _retry();
            }
            finally
            {
                _isRetrying = false;
                OnPropertyChanged(nameof(CanRetry));
            }
        }

        private void OnLogPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(Summary));
            if (Log.IsComplete)
                Log.PropertyChanged -= OnLogPropertyChanged;
        }

        private string _detail = string.Empty;
        private int _completedCount;
        private int _totalCount;
        private Func<Task> _retry;
        private bool _isRetrying;
    }
}
