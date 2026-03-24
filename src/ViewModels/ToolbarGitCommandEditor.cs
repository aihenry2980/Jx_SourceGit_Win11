using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class ToolbarGitCommandEditor : ObservableObject
    {
        public string Title
        {
            get;
        }

        public string Description
        {
            get;
        }

        public string CommandText
        {
            get => _commandText;
            set => SetProperty(ref _commandText, value);
        }

        public ToolbarGitCommandEditor(Repository repo, string title, string description, string commandText, Action onSuccess = null)
        {
            _repo = repo;
            Title = title;
            Description = description;
            _commandText = commandText;
            _onSuccess = onSuccess;
        }

        public async Task<bool> StartAsync()
        {
            var commands = ParseCommands();
            if (commands.Count == 0)
            {
                App.SendNotification(Title, "No git commands to run.");
                return false;
            }

            var log = _repo.CreateLog(Title);
            ShowOrFocusLogs(log);
            _ = Task.Run(async () =>
            {
                var success = true;
                try
                {
                    foreach (var args in commands)
                    {
                        var cmd = new Commands.Command()
                        {
                            WorkingDirectory = _repo.FullPath,
                            Context = _repo.FullPath,
                            Args = args,
                            Log = log,
                        };

                        success = await cmd.ExecAsync().ConfigureAwait(false);
                        if (!success)
                            break;
                    }

                    if (success && _onSuccess != null)
                        await Dispatcher.UIThread.InvokeAsync(_onSuccess);
                }
                finally
                {
                    log.Complete();
                }
            });

            return true;
        }

        private List<string> ParseCommands()
        {
            var outs = new List<string>();
            var lines = (_commandText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
                    line = line.Substring(4).TrimStart();

                if (!string.IsNullOrWhiteSpace(line))
                    outs.Add(line);
            }

            return outs;
        }

        private void ShowOrFocusLogs(CommandLog log)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { Windows: { } windows })
                {
                    foreach (var window in windows)
                    {
                        if (window is Views.ViewLogs &&
                            window.DataContext is ViewModels.ViewLogs vm &&
                            ReferenceEquals(vm.Logs, _repo.Logs))
                        {
                            vm.SelectedLog = log;

                            if (window.WindowState == WindowState.Minimized)
                                window.WindowState = WindowState.Normal;

                            window.Activate();
                            return;
                        }
                    }
                }

                App.ShowWindow(new ViewModels.ViewLogs(_repo));
            }, DispatcherPriority.Background);
        }

        private readonly Repository _repo;
        private readonly Action _onSuccess;
        private string _commandText = string.Empty;
    }
}
