using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommandLog : ObservableObject, Models.ICommandLog
    {
        private const int MAX_CONTENT_LENGTH = 256 * 1024;
        private const int TRIMMED_CONTENT_LENGTH = 192 * 1024;
        private const int MAX_PENDING_LENGTH = 64 * 1024;
        private const int MAX_LINE_LENGTH = 8 * 1024;
        private const string TRUNCATED_NOTICE = "[... older log output truncated ...]";

        public string Name
        {
            get;
            private set;
        }

        public DateTime StartTime
        {
            get;
        } = DateTime.Now;

        public DateTime EndTime
        {
            get;
            private set;
        } = DateTime.Now;

        public bool IsComplete
        {
            get;
            private set;
        } = false;

        public bool IsSuccessful
        {
            get;
            private set;
        } = false;

        public bool AutoCloseOnSuccess
        {
            get;
            set;
        } = false;

        public string LatestCommand
        {
            get;
            private set;
        } = string.Empty;

        public string RepositoryPath { get; set; } = string.Empty;
        public string RepositoryName => string.IsNullOrWhiteSpace(RepositoryPath) ? string.Empty : Path.GetFileName(RepositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        public bool IsCancellationRequested { get; private set; } = false;
        public bool CanCancel => !IsComplete && !IsCancellationRequested && _cancelAction != null;
        public bool IsFailed => IsComplete && _succeeded == false && !IsCancellationRequested;
        public string StatusText => !IsComplete
            ? App.Text(IsCancellationRequested ? "ViewLogs.Status.Canceling" : "ViewLogs.Status.Running")
            : App.Text(IsSuccessful ? "ViewLogs.Status.Succeeded"
                : IsCancellationRequested ? "ViewLogs.Status.Canceled"
                : IsFailed ? "ViewLogs.Status.Failed" : "ViewLogs.Status.Unknown");

        public string Content
        {
            get
            {
                return IsComplete ? _content : _builder.ToString();
            }
        }

        public int EstimatedContentLength => IsComplete ? _content?.Length ?? 0 : _builder?.Length ?? 0;

        public CommandLog(string name)
        {
            Name = name;
        }

        public void SetCancelAction(Action cancelAction)
        {
            _cancelAction = cancelAction;
            OnPropertyChanged(nameof(CanCancel));
        }

        public void Cancel()
        {
            if (!CanCancel || IsCancellationRequested)
                return;

            IsCancellationRequested = true;
            OnPropertyChanged(nameof(IsCancellationRequested));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StatusText));
            _cancelAction?.Invoke();
        }

        public void Subscribe(Models.ICommandLogReceiver receiver)
        {
            _receivers.Add(receiver);
        }

        public void Unsubscribe(Models.ICommandLogReceiver receiver)
        {
            _receivers.Remove(receiver);
        }

        public void AppendLine(string line = null)
        {
            var shouldScheduleFlush = false;
            lock (_pendingLock)
            {
                if (!_acceptingLines)
                    return;

                line ??= string.Empty;
                if (line.Length > MAX_LINE_LENGTH)
                    line = "[... long line truncated ...] " + line.Substring(line.Length - MAX_LINE_LENGTH);

                var command = ExtractCommandLine(line);
                if (!string.IsNullOrEmpty(command))
                    _pendingCommand = command;

                _pendingLines.Enqueue(line);
                _pendingLength += line.Length + Environment.NewLine.Length;
                while (_pendingLength > MAX_PENDING_LENGTH && _pendingLines.Count > 1)
                {
                    _pendingLength -= _pendingLines.Dequeue().Length + Environment.NewLine.Length;
                    _pendingTruncated = true;
                }
                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    shouldScheduleFlush = true;
                }
            }

            if (shouldScheduleFlush)
                _ = ScheduleFlushAsync();
        }

        public void Complete(bool? succeeded = null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                try
                {
                    Dispatcher.UIThread.Invoke(() => Complete(succeeded));
                }
                catch (TaskCanceledException)
                {
                    // Pending UI updates are canceled when the application exits.
                }

                return;
            }

            if (IsComplete)
                return;

            List<string> pending;
            string latestCommand;
            lock (_pendingLock)
            {
                _acceptingLines = false;
                pending = TakePendingLines(out latestCommand);
                _flushScheduled = false;
            }

            ApplyPendingLines(pending, latestCommand);
            IsComplete = true;
            _succeeded = succeeded;
            IsSuccessful = succeeded == true;
            EndTime = DateTime.Now;
            _cancelAction = null;

            _content = _builder.ToString();
            _builder.Clear();
            _receivers.Clear();
            _builder = null;

            OnPropertyChanged(nameof(IsComplete));
            OnPropertyChanged(nameof(IsSuccessful));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StatusText));
        }

        private string _content = string.Empty;
        private StringBuilder _builder = new StringBuilder();
        private List<Models.ICommandLogReceiver> _receivers = new List<Models.ICommandLogReceiver>();
        private readonly object _pendingLock = new object();
        private readonly Queue<string> _pendingLines = new Queue<string>();
        private int _pendingLength = 0;
        private bool _pendingTruncated = false;
        private string _pendingCommand = string.Empty;
        private bool _flushScheduled = false;
        private bool _acceptingLines = true;
        private Action _cancelAction = null;
        private bool? _succeeded;

        private async Task ScheduleFlushAsync()
        {
            // Bound rendering frequency as well as memory when a script floods both pipes.
            await Task.Delay(100).ConfigureAwait(false);
            Dispatcher.UIThread.Post(FlushPendingLines, DispatcherPriority.Background);
        }

        private void FlushPendingLines()
        {
            List<string> pending;
            string latestCommand;
            lock (_pendingLock)
            {
                pending = TakePendingLines(out latestCommand);
                _flushScheduled = false;
            }

            ApplyPendingLines(pending, latestCommand);
        }

        private List<string> TakePendingLines(out string latestCommand)
        {
            latestCommand = _pendingCommand;
            _pendingCommand = string.Empty;
            if (_pendingLines.Count == 0)
                return null;

            var pending = new List<string>(_pendingLines.Count + 1);
            if (_pendingTruncated)
                pending.Add(TRUNCATED_NOTICE);
            pending.AddRange(_pendingLines);
            _pendingLines.Clear();
            _pendingLength = 0;
            _pendingTruncated = false;
            return pending;
        }

        private void ApplyPendingLines(List<string> lines, string latestCommand)
        {
            if (lines == null || lines.Count == 0 || IsComplete || _builder == null)
                return;

            foreach (var line in lines)
                _builder.AppendLine(line);

            var wasTruncated = TrimContentIfNeeded();
            if (!string.IsNullOrEmpty(latestCommand))
            {
                LatestCommand = latestCommand;
                OnPropertyChanged(nameof(LatestCommand));
            }

            var batch = string.Join(Environment.NewLine, lines);
            foreach (var receiver in _receivers.ToArray())
            {
                if (wasTruncated)
                    receiver.OnResetCommandLog(_builder.ToString());
                else
                    receiver.OnReceiveCommandLog(batch);
            }
        }

        private bool TrimContentIfNeeded()
        {
            if (_builder.Length <= MAX_CONTENT_LENGTH)
                return false;

            var content = _builder.ToString();
            var keepFrom = Math.Max(0, content.Length - TRIMMED_CONTENT_LENGTH);
            if (keepFrom > 0)
            {
                var nextLine = content.IndexOf('\n', keepFrom);
                keepFrom = nextLine >= 0 ? nextLine + 1 : keepFrom;
            }

            var trimmed = keepFrom > 0 ? content.Substring(keepFrom) : content;
            _builder.Clear();
            _builder.AppendLine(TRUNCATED_NOTICE);
            _builder.Append(trimmed.TrimStart('\r', '\n'));
            return true;
        }

        private static string ExtractCommandLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var trimmed = line.Trim();
            return trimmed.StartsWith("$ ", StringComparison.Ordinal) ? trimmed.Substring(2) : string.Empty;
        }
    }
}
