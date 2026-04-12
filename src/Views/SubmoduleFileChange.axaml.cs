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

        protected override void OnClosed(System.EventArgs e)
        {
            PersistWindowSize();
            ViewModels.Preferences.Instance.Save();

            base.OnClosed(e);
        }

        protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            PersistWindowSize();
        }

        private void PersistWindowSize()
        {
            var layout = ViewModels.Preferences.Instance.Layout;
            if (Bounds.Width >= MinWidth)
                layout.SubmoduleFileChangeDiffWindowWidth = Bounds.Width;
            if (Bounds.Height >= MinHeight)
                layout.SubmoduleFileChangeDiffWindowHeight = Bounds.Height;
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
