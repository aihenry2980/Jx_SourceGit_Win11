using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class LauncherPage : ObservableObject
    {
        public RepositoryNode Node
        {
            get => _node;
            set => SetProperty(ref _node, value);
        }

        public object Data
        {
            get => _data;
            set => SetProperty(ref _data, value);
        }

        public Models.DirtyState DirtyState
        {
            get => _dirtyState;
            private set => SetProperty(ref _dirtyState, value);
        }

        public Popup Popup
        {
            get => _popup;
            set => SetProperty(ref _popup, value);
        }

        public AvaloniaList<Models.Notification> Notifications
        {
            get;
            set;
        } = new AvaloniaList<Models.Notification>();

        public bool IsCopyToastVisible
        {
            get => _isCopyToastVisible;
            private set => SetProperty(ref _isCopyToastVisible, value);
        }

        public double CopyToastOpacity
        {
            get => _copyToastOpacity;
            private set => SetProperty(ref _copyToastOpacity, value);
        }

        public string CopyToastText
        {
            get => _copyToastText;
            private set => SetProperty(ref _copyToastText, value);
        }

        public LauncherPage()
        {
            _node = new RepositoryNode() { Id = Guid.NewGuid().ToString() };
            _data = Welcome.Instance;

            // New welcome page will clear the search filter before.
            Welcome.Instance.ClearSearchFilter();
        }

        public LauncherPage(RepositoryNode node, Repository repo)
        {
            _node = node;
            _data = repo;
        }

        public void ClearNotifications()
        {
            Notifications.Clear();
        }

        public void ChangeDirtyState(Models.DirtyState flag, bool remove)
        {
            var state = _dirtyState;
            if (remove)
            {
                if (state.HasFlag(flag))
                    state -= flag;
            }
            else
            {
                state |= flag;
            }

            DirtyState = state;
        }

        public bool CanCreatePopup()
        {
            return _popup is not { InProgress: true };
        }

        public async Task ProcessPopupAsync()
        {
            if (_popup is { InProgress: false } dump)
            {
                if (!dump.Check())
                    return;

                dump.InProgress = true;

                try
                {
                    var finished = await dump.Sure();
                    if (finished)
                    {
                        dump.Cleanup();
                        Popup = null;
                    }
                }
                catch (Exception e)
                {
                    Native.OS.LogException(e);
                }

                dump.InProgress = false;
            }
        }

        public void CancelPopup()
        {
            if (_popup == null)
                return;

            if (_popup.InProgress && !_popup.AllowCancelWhenRunning)
                return;

            _popup.Cleanup();
            Popup = null;
        }

        public async Task CopyPathAsync()
        {
            var path = Data switch
            {
                Repository repo => repo.FullPath,
                _ when !string.IsNullOrWhiteSpace(Node?.Id) && Directory.Exists(Node.Id) => Node.Id,
                _ => string.Empty,
            };

            await App.CopyTextAsync(path);
        }

        public void ShowCopyToast(string text)
        {
            _copyToastCancellation?.Cancel();
            _copyToastCancellation?.Dispose();

            var cts = new CancellationTokenSource();
            _copyToastCancellation = cts;

            CopyToastText = BuildCopyToastPreview(text);
            CopyToastOpacity = 1.0;
            IsCopyToastVisible = true;

            _ = FadeCopyToastAsync(cts.Token);
        }

        private async Task FadeCopyToastAsync(CancellationToken token)
        {
            const int holdDurationMs = 3000;
            const int fadeDurationMs = 400;
            const int fadeTickMs = 50;

            try
            {
                await Task.Delay(holdDurationMs, token);

                for (var elapsed = fadeTickMs; elapsed <= fadeDurationMs; elapsed += fadeTickMs)
                {
                    token.ThrowIfCancellationRequested();
                    CopyToastOpacity = Math.Max(0.0, 1.0 - (double)elapsed / fadeDurationMs);
                    await Task.Delay(fadeTickMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            CopyToastOpacity = 0.0;
            IsCopyToastVisible = false;
        }

        private static string BuildCopyToastPreview(string text)
        {
            const int maxLength = 140;
            if (string.IsNullOrEmpty(text))
                return "(empty)";

            var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));
            var pendingSpace = false;

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(ch);
                if (builder.Length > maxLength)
                    break;
            }

            if (builder.Length == 0)
                return "(empty)";

            return builder.Length > maxLength
                ? $"{builder.ToString(0, maxLength - 3)}..."
                : builder.ToString();
        }

        public void TerminatePopup()
        {
            if (_popup is not { CanTerminate: true, InProgress: true })
                return;

            _popup.Terminate();
        }

        private RepositoryNode _node = null;
        private object _data = null;
        private Models.DirtyState _dirtyState = Models.DirtyState.None;
        private Popup _popup = null;
        private bool _isCopyToastVisible = false;
        private double _copyToastOpacity = 0.0;
        private string _copyToastText = string.Empty;
        private CancellationTokenSource _copyToastCancellation = null;
    }
}
