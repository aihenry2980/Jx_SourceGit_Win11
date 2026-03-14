using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public interface ICustomActionControlParameter
    {
        string GetValue();
    }

    public class CustomActionControlTextBox : ICustomActionControlParameter
    {
        public string Label { get; set; }
        public string Placeholder { get; set; }
        public string Text { get; set; }

        public CustomActionControlTextBox(string label, string placeholder, string defaultValue)
        {
            Label = label + ":";
            Placeholder = placeholder;
            Text = defaultValue;
        }

        public string GetValue() => Text;
    }

    public class CustomActionControlPathSelector : ObservableObject, ICustomActionControlParameter
    {
        public string Label { get; set; }
        public string Placeholder { get; set; }
        public bool IsFolder { get; set; }

        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }

        public CustomActionControlPathSelector(string label, string placeholder, bool isFolder, string defaultValue)
        {
            Label = label + ":";
            Placeholder = placeholder;
            IsFolder = isFolder;
            _path = defaultValue;
        }

        public string GetValue() => _path;

        private string _path;
    }

    public class CustomActionControlCheckBox : ICustomActionControlParameter
    {
        public string Label { get; set; }
        public string ToolTip { get; set; }
        public string CheckedValue { get; set; }
        public bool IsChecked { get; set; }

        public CustomActionControlCheckBox(string label, string tooltip, string checkedValue, bool isChecked)
        {
            Label = label;
            ToolTip = string.IsNullOrEmpty(tooltip) ? null : tooltip;
            CheckedValue = checkedValue;
            IsChecked = isChecked;
        }

        public string GetValue() => IsChecked ? CheckedValue : string.Empty;
    }

    public class CustomActionControlComboBox : ObservableObject, ICustomActionControlParameter
    {
        public string Label { get; set; }
        public string Description { get; set; }
        public List<string> Options { get; set; } = [];

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public CustomActionControlComboBox(string label, string description, string options)
        {
            Label = label;
            Description = description;

            var parts = options.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                Options.AddRange(parts);
                _value = parts[0];
            }
        }

        public string GetValue() => _value;

        private string _value = string.Empty;
    }

    public class ExecuteCustomAction : Popup
    {
        public Models.CustomAction CustomAction
        {
            get;
        }

        public object Target
        {
            get;
        }

        public List<ICustomActionControlParameter> ControlParameters
        {
            get;
        } = [];

        public ExecuteCustomAction(Repository repo, Models.CustomAction action, object scopeTarget)
        {
            _repo = repo;
            CustomAction = action;
            Target = scopeTarget ?? new Models.Null();
            PrepareControlParameters();
        }

        public override Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Run custom action ...";

            var cmdline = PrepareStringByTarget(CustomAction.Arguments);
            for (var i = ControlParameters.Count - 1; i >= 0; i--)
            {
                var param = ControlParameters[i];
                cmdline = cmdline.Replace($"${i + 1}", param.GetValue());
            }

            var log = _repo.CreateLog(CustomAction.Name);
            Use(log);

            log.AppendLine($"$ {CustomAction.Executable} {cmdline}\n");
            ShowOrFocusLogs(log);
            _ = Task.Run(() => RunAsync(cmdline, log));
            return Task.FromResult(true);
        }

        private void PrepareControlParameters()
        {
            foreach (var ctl in CustomAction.Controls)
            {
                switch (ctl.Type)
                {
                    case Models.CustomActionControlType.TextBox:
                        ControlParameters.Add(new CustomActionControlTextBox(ctl.Label, ctl.Description, PrepareStringByTarget(ctl.StringValue)));
                        break;
                    case Models.CustomActionControlType.PathSelector:
                        ControlParameters.Add(new CustomActionControlPathSelector(ctl.Label, ctl.Description, ctl.BoolValue, PrepareStringByTarget(ctl.StringValue)));
                        break;
                    case Models.CustomActionControlType.CheckBox:
                        ControlParameters.Add(new CustomActionControlCheckBox(ctl.Label, ctl.Description, ctl.StringValue, ctl.BoolValue));
                        break;
                    case Models.CustomActionControlType.ComboBox:
                        ControlParameters.Add(new CustomActionControlComboBox(ctl.Label, ctl.Description, PrepareStringByTarget(ctl.StringValue)));
                        break;
                }
            }
        }

        private string PrepareStringByTarget(string org)
        {
            org = org.Replace("${REPO}", GetWorkdir());

            return Target switch
            {
                Models.Branch b => org.Replace("${BRANCH_FRIENDLY_NAME}", b.FriendlyName).Replace("${BRANCH}", b.Name).Replace("${REMOTE}", b.Remote),
                Models.Commit c => org.Replace("${SHA}", c.SHA),
                Models.Tag t => org.Replace("${TAG}", t.Name),
                Models.Remote r => org.Replace("${REMOTE}", r.Name),
                Models.CustomActionTargetFile f => org.Replace("${FILE}", f.File).Replace("${SHA}", f.Revision?.SHA ?? string.Empty),
                _ => org
            };
        }

        private string GetWorkdir()
        {
            return OperatingSystem.IsWindows() ? _repo.FullPath.Replace("/", "\\") : _repo.FullPath;
        }

        private async Task RunAsync(string args, CommandLog log)
        {
            var start = new ProcessStartInfo();
            start.FileName = CustomAction.Executable;
            start.Arguments = args;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.WorkingDirectory = _repo.FullPath;

            using var proc = new Process();
            proc.StartInfo = start;

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    log?.AppendLine(e.Data);
            };

            var builder = new StringBuilder();
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    log?.AppendLine(e.Data);
                    builder.AppendLine(e.Data);
                }
            };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync().ConfigureAwait(false);

                var exitCode = proc.ExitCode;
                log?.AppendLine($"[Process exited with code {exitCode}]");
                if (exitCode != 0)
                {
                    var errMsg = builder.ToString().Trim();
                    if (!string.IsNullOrEmpty(errMsg))
                        App.RaiseException(_repo.FullPath, errMsg);
                    else
                        App.RaiseException(_repo.FullPath, $"Custom action exited with code {exitCode}.");
                }
            }
            catch (Exception e)
            {
                log?.AppendLine(e.Message);
                App.RaiseException(_repo.FullPath, e.Message);
            }
            finally
            {
                log?.Complete();
            }
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
            });
        }

        private readonly Repository _repo = null;
    }
}
