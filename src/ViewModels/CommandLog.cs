using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommandLog : ObservableObject, Models.ICommandLog
    {
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
                if (_isTruncated)
                    return;

                var append = newline + Environment.NewLine;
                var remain = MAX_CONTENT_LENGTH - _builder.Length;
                if (remain <= 0)
                {
                    AppendTruncatedMarker();
                }
                else if (append.Length <= remain)
                {
                    _builder.Append(append);
                }
                else
                {
                    _builder.Append(append.AsSpan(0, remain));
                    AppendTruncatedMarker();
                }

                foreach (var receiver in _receivers.ToArray())
                    receiver.OnReceiveCommandLog(newline);
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

        private void AppendTruncatedMarker()
        {
            if (_isTruncated || _builder == null)
                return;

            _isTruncated = true;
            var remain = MAX_CONTENT_LENGTH - _builder.Length;
            if (remain <= 0)
                return;

            if (TRUNCATED_MARKER.Length <= remain)
                _builder.Append(TRUNCATED_MARKER);
            else
                _builder.Append(TRUNCATED_MARKER.AsSpan(0, remain));
        }

        private string _content = string.Empty;
        private StringBuilder _builder = new StringBuilder();
        private List<Models.ICommandLogReceiver> _receivers = new List<Models.ICommandLogReceiver>();
        private bool _isTruncated = false;

        private const int MAX_CONTENT_LENGTH = 512 * 1024;
        private const string TRUNCATED_MARKER = "\n... (log output truncated)\n";
    }
}
