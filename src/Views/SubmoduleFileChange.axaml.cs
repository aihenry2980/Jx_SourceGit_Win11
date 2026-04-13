using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SubmoduleFileChange : ChromelessWindow
    {
        public SubmoduleFileChange()
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
            layout.SubmoduleFileChangeDiffWindowPositionX = Position.X;
            layout.SubmoduleFileChangeDiffWindowPositionY = Position.Y;
        }

        private void PersistWindowSize()
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            if (Bounds.Width >= MinWidth)
                layout.SubmoduleFileChangeDiffWindowWidth = Bounds.Width;
            if (Bounds.Height >= MinHeight)
                layout.SubmoduleFileChangeDiffWindowHeight = Bounds.Height;
        }

        private void RestoreWindowPosition()
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            var x = layout.SubmoduleFileChangeDiffWindowPositionX;
            var y = layout.SubmoduleFileChangeDiffWindowPositionY;
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
