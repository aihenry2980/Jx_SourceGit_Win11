namespace SourceGit.Views
{
    public partial class CpuProfiler : ChromelessWindow
    {
        public CpuProfiler()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (DataContext is System.IDisposable disposable)
                disposable.Dispose();

            base.OnClosed(e);
        }
    }
}
