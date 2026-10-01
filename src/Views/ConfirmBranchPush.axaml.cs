using System;

using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class ConfirmBranchPush : ChromelessWindow
    {
        public ConfirmBranchPush()
        {
            InitializeComponent();
        }

        public void SetData(string repo, string local, string remote, string destination, bool force)
        {
            RepositoryPath.Text = repo;
            LocalBranch.Text = local;
            RemoteName.Text = remote;
            RemoteBranch.Text = destination;
            ForceWarning.IsVisible = force;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                MaxWidth = screen.WorkingArea.Width / screen.Scaling - 32;
                MaxHeight = screen.WorkingArea.Height / screen.Scaling - 32;
            }
            BtnCancel.Focus();
        }

        private void Confirm(object sender, RoutedEventArgs e) => Close(true);

        private void Cancel(object sender, RoutedEventArgs e) => Close(false);
    }
}
