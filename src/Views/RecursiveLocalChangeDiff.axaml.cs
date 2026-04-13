using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class RecursiveLocalChangeDiff : ChromelessWindow
    {
        public RecursiveLocalChangeDiff()
        {
            CloseOnESC = true;
            InitializeComponent();
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            RestoreWindowPosition();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            PersistWindowLayout();
            ViewModels.Preferences.Instance.Save();

            base.OnClosed(e);
        }

        protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            PersistWindowSize();
        }

        private void PersistWindowLayout()
        {
            PersistWindowSize();

            var layout = ViewModels.Preferences.Instance.Layout;
            layout.RecursiveLocalChangeDiffWindowPositionX = Position.X;
            layout.RecursiveLocalChangeDiffWindowPositionY = Position.Y;
        }

        private void PersistWindowSize()
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            if (Bounds.Width >= MinWidth)
                layout.RecursiveLocalChangeDiffWindowWidth = Bounds.Width;
            if (Bounds.Height >= MinHeight)
                layout.RecursiveLocalChangeDiffWindowHeight = Bounds.Height;
        }

        private void RestoreWindowPosition()
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            var x = layout.RecursiveLocalChangeDiffWindowPositionX;
            var y = layout.RecursiveLocalChangeDiffWindowPositionY;
            if (x == int.MinValue || y == int.MinValue || Screens == null)
                return;

            var position = new PixelPoint(x, y);
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            var desiredRect = new PixelRect(position, size);
            for (var i = 0; i < Screens.ScreenCount; i++)
            {
                if (Screens.All[i].WorkingArea.Contains(desiredRect))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Position = position;
                    return;
                }
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e is not { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
                return;

            Close();
            e.Handled = true;
        }
    }
}
