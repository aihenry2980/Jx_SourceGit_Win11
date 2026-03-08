using System;
using System.Collections.Generic;
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

        public string Content
        {
            get
            {
                return IsComplete ? _content : _builder.ToString();
            }
        }

        public CommandLog(string name)
        {
            Name = name;
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
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(() => AppendLine(line));
            }
            else
            {
                if (IsComplete || _builder == null)
                    return;

                var newline = line ?? string.Empty;
                _builder.AppendLine(newline);
                var wasTruncated = TrimContentIfNeeded();

                foreach (var receiver in _receivers.ToArray())
                {
                    if (wasTruncated)
                        receiver.OnResetCommandLog(_builder.ToString());
                    else
                        receiver.OnReceiveCommandLog(newline);
                }
            }
        }

        public void Complete()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(Complete);
                return;
            }

            IsComplete = true;
            EndTime = DateTime.Now;

            _content = _builder.ToString();
            _builder.Clear();
            _receivers.Clear();
            _builder = null;

            OnPropertyChanged(nameof(IsComplete));
        }

        private string _content = string.Empty;
        private StringBuilder _builder = new StringBuilder();
        private List<Models.ICommandLogReceiver> _receivers = new List<Models.ICommandLogReceiver>();

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
    }
}
