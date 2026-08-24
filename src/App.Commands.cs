using System;
using System.Diagnostics;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;

namespace SourceGit
{
    public partial class App
    {
        public class Command : ICommand
        {
            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            public Command(Action<object> action)
            {
                _action = action;
            }

            public bool CanExecute(object parameter) => _action != null;
            public void Execute(object parameter) => _action?.Invoke(parameter);

            private Action<object> _action = null;
        }

        public static bool IsCheckForUpdateCommandVisible
        {
            get
            {
#if DISABLE_UPDATE_DETECTION
                return false;
#else
                return true;
#endif
            }
        }

        public static bool IsInstallLatestVersionCommandVisible => OperatingSystem.IsWindows();

        public static readonly Command OpenPreferencesCommand = new Command(async _ =>
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                var dialog = new Views.Preferences();
                await dialog.ShowDialog(owner);
            }
        });

        public static readonly Command OpenHotkeysCommand = new Command(async _ =>
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                var dialog = new Views.Hotkeys();
                await dialog.ShowDialog(owner);
            }
        });

        public static readonly Command OpenAppConfigDirCommand = new Command(_ =>
        {
            Native.OS.OpenInFileManager(Native.OS.BasicDirectories.ConfigDir);
        });

        public static readonly Command OpenAppDataDirCommand = new Command(_ =>
        {
            Native.OS.OpenInFileManager(Native.OS.BasicDirectories.CacheDir);
        });

        public static readonly Command OpenAboutCommand = new Command(async _ =>
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                var dialog = new Views.About();
                await dialog.ShowDialog(owner);
            }
        });

        public static readonly Command CheckForUpdateCommand = new Command(_ =>
        {
            (Current as App)?.Check4Update(true);
        });

        public static readonly Command InstallLatestVersionCommand = new Command(_ =>
        {
            if (!OperatingSystem.IsWindows())
                return;

            var installDir = AppContext.BaseDirectory;
            var updater = System.IO.Path.Combine(installDir, "update-sourcegit.win.ps1");
            if (!System.IO.File.Exists(updater))
            {
                RaiseException(null, Text("SelfUpdate.InstallLatest.Missing"));
                return;
            }

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = true,
                    WorkingDirectory = installDir,
                };

                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-ExecutionPolicy");
                start.ArgumentList.Add("Bypass");
                start.ArgumentList.Add("-File");
                start.ArgumentList.Add(updater);
                start.ArgumentList.Add("-InstallDir");
                start.ArgumentList.Add(installDir);

                Process.Start(start);
            }
            catch (Exception ex)
            {
                LogException(ex);
                RaiseException(null, Text("SelfUpdate.InstallLatest.Failed"));
            }
        });

        public static readonly Command QuitCommand = new Command(_ =>
        {
            Quit(0);
        });

        public static readonly Command HideAppCommand = new Command(_ =>
        {
            if (OperatingSystem.IsMacOS())
                Native.MacOSUtilities.HideSelf();
        });

        public static readonly Command HideOtherApplicationsCommand = new Command(_ =>
        {
            if (OperatingSystem.IsMacOS())
                Native.MacOSUtilities.HideOtherApplications();
        });

        public static readonly Command ShowAllApplicationsCommand = new Command(_ =>
        {
            if (OperatingSystem.IsMacOS())
                Native.MacOSUtilities.ShowAllApplications();
        });

        public static Path CreateMenuIcon(string iconKey)
        {
            if (Current?.TryGetResource(iconKey, Current.ActualThemeVariant, out var resource) == true &&
                resource is StreamGeometry geo)
            {
                return new Path()
                {
                    Data = geo,
                    Width = 12,
                    Height = 12,
                    Stretch = Stretch.Uniform,
                };
            }

            return null;
        }

        public static async Task CopyTextAsync(string text)
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { Clipboard: { } clipboard } })
            {
                await clipboard.SetTextAsync(text ?? string.Empty);
                ShowCopyToast(text);
            }
        }

        public static void ShowCopyToast(string text)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ShowCopyToast(text));
                return;
            }

            GetLauncher()?.ActivePage?.ShowCopyToast(text);
        }

        public static void SendNotification(string group, string message, bool isError = false)
        {
            Models.Notification.Send(group, message, isError);
        }

        public static void RaiseException(string group, string message)
        {
            Models.Notification.Send(string.IsNullOrEmpty(group) ? null : group, message, true);
        }

        public static void LogException(Exception ex)
        {
            Native.OS.LogException(ex);
        }

        public static readonly Command OpenSSHKeyHelperCommand = new Command(async _ =>
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                var vm = new ViewModels.SSHKeyHelper();
                var dialog = new Views.SSHKeyHelper() { DataContext = vm };
                await dialog.ShowDialog(owner);
            }
        });
    }
}
