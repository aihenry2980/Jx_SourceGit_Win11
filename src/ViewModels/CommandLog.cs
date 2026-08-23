using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommandLog : ObservableObject, Models.ICommandLog
    {
        private const int MAX_CONTENT_LENGTH = 256 * 1024;
        private const int TRIMMED_CONTENT_LENGTH = 192 * 1024;
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
        public bool CanCancel => !IsComplete && _cancelAction != null;
        public string StatusText => !IsComplete
            ? IsCancellationRequested ? "Canceling..." : "Running"
            : IsSuccessful ? "Succeeded"
            : IsCancellationRequested ? "Canceled" : "Finished";

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

                _pendingLines.Add(line ?? string.Empty);
                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    shouldScheduleFlush = true;
                }
            }

            if (shouldScheduleFlush)
                Dispatcher.UIThread.Post(FlushPendingLines, DispatcherPriority.Background);
        }

        public void Complete(bool succeeded = false)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(() => Complete(succeeded));
                return;
            }

            List<string> pending;
            lock (_pendingLock)
            {
                _acceptingLines = false;
                pending = TakePendingLines();
                _flushScheduled = false;
            }

            ApplyPendingLines(pending);
            IsComplete = true;
            IsSuccessful = succeeded;
            EndTime = DateTime.Now;
            _cancelAction = null;

            _content = _builder.ToString();
            _builder.Clear();
            _receivers.Clear();
            _builder = null;

            OnPropertyChanged(nameof(IsComplete));
            OnPropertyChanged(nameof(IsSuccessful));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StatusText));
        }

        private string _content = string.Empty;
        private StringBuilder _builder = new StringBuilder();
        private List<Models.ICommandLogReceiver> _receivers = new List<Models.ICommandLogReceiver>();
        private readonly object _pendingLock = new object();
        private readonly List<string> _pendingLines = new List<string>();
        private bool _flushScheduled = false;
        private bool _acceptingLines = true;
        private Action _cancelAction = null;

        private void FlushPendingLines()
        {
            List<string> pending;
            lock (_pendingLock)
            {
                pending = TakePendingLines();
                _flushScheduled = false;
            }

            ApplyPendingLines(pending);
        }

        private List<string> TakePendingLines()
        {
            if (_pendingLines.Count == 0)
                return null;

            var pending = new List<string>(_pendingLines);
            _pendingLines.Clear();
            return pending;
        }

        private void ApplyPendingLines(List<string> lines)
        {
            if (lines == null || lines.Count == 0 || IsComplete || _builder == null)
                return;

            var latestCommand = string.Empty;
            foreach (var line in lines)
            {
                _builder.AppendLine(line);
                var command = ExtractCommandLine(line);
                if (!string.IsNullOrEmpty(command))
                    latestCommand = command;
            }

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
