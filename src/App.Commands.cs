using System;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

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

        public static readonly Command OpenAppDataDirCommand = new Command(_ =>
        {
            Native.OS.OpenInFileManager(Native.OS.DataDir);
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

        public static readonly Command QuitCommand = new Command(_ =>
        {
            Quit(0);
        });

        public static readonly Command HideAppCommand = new Command(_ =>
        {
            if (Current is App app && app.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
                lifetime.TryEnterBackground();
        });

        public static readonly Command ShowAppCommand = new Command(_ =>
        {
            if (Current is App app && app.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
                lifetime.TryLeaveBackground();
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
                await clipboard.SetTextAsync(text ?? string.Empty);
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
    }
}
