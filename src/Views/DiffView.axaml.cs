using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class DiffView : UserControl
    {
        public DiffView()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is ViewModels.DiffContext vm)
                vm.CheckSettings();
        }

        private void OnGotoFirstChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.First);
            e.Handled = true;
        }

        private void OnGotoPrevChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Prev);
            e.Handled = true;
        }

        private void OnGotoNextChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Next);
            e.Handled = true;
        }

        private void OnGotoLastChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Last);
            e.Handled = true;
        }

        private void OnOpenSubmoduleFileChange(object sender, RoutedEventArgs e)
        {
            if (sender is not Control { DataContext: Models.Change change } control)
                return;

            StyledElement current = control;
            while (current != null && current.DataContext is not Models.SubmoduleDiff)
                current = current.Parent as StyledElement;

            if (current?.DataContext is not Models.SubmoduleDiff submodule ||
                string.IsNullOrWhiteSpace(submodule.RepositoryPath) ||
                string.IsNullOrWhiteSpace(submodule.TargetRevision))
                return;

            App.ShowWindow(new ViewModels.SubmoduleFileChange(
                submodule.RepositoryPath,
                submodule.BaseRevision,
                submodule.TargetRevision,
                change));
            e.Handled = true;
        }

        private void OnOpenSubmoduleCommitLink(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                sender is Control { Tag: string url } &&
                !string.IsNullOrWhiteSpace(url))
            {
                Native.OS.OpenBrowser(url);
            }

            e.Handled = true;
        }
    }
}
