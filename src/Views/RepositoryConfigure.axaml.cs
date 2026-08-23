using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace SourceGit.Views
{
    public class GitIgnoreRulesEditor : TextEditor
    {
        private class CommentLineTransformer : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                var content = CurrentContext.Document.GetText(line);
                if (string.IsNullOrEmpty(content) || !content.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    return;

                ChangeLinePart(line.Offset, line.EndOffset, v =>
                {
                    v.TextRunProperties.SetForegroundBrush(Brushes.Gray);
                });
            }
        }

        public static readonly StyledProperty<string> RulesProperty =
            AvaloniaProperty.Register<GitIgnoreRulesEditor, string>(nameof(Rules), string.Empty, defaultBindingMode: BindingMode.TwoWay);

        public string Rules
        {
            get => GetValue(RulesProperty);
            set => SetValue(RulesProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public GitIgnoreRulesEditor() : base(new TextArea(), new TextDocument())
        {
            ShowLineNumbers = false;
            WordWrap = false;
            Background = Brushes.Transparent;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.Options.EnableHyperlinks = false;
            TextArea.TextView.Options.EnableEmailHyperlinks = false;
            TextArea.TextView.Options.AllowScrollBelowDocument = false;
            TextArea.TextView.LineTransformers.Add(new CommentLineTransformer());
            TextChanged += OnTextChanged;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != RulesProperty || _isUpdating)
                return;

            var next = change.GetNewValue<string>() ?? string.Empty;
            if (string.Equals(Text, next, StringComparison.Ordinal))
                return;

            _isUpdating = true;
            Text = next;
            _isUpdating = false;
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            SetCurrentValue(RulesProperty, Text);
            _isUpdating = false;
        }

        private bool _isUpdating = false;
    }

    public partial class RepositoryConfigure : ChromelessWindow
    {
        public RepositoryConfigure()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        public void OpenLocalIgnoreTab()
        {
            Tabs.SelectedIndex = 1;
        }

        public void OpenCustomActionTab()
        {
            Tabs.SelectedIndex = 4;
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (!Design.IsDesignMode && DataContext is ViewModels.RepositoryConfigure configure)
                await configure.SaveAsync();
        }

        private async void SelectConventionalTypesFile(object sender, RoutedEventArgs e)
        {
            var options = new FilePickerOpenOptions()
            {
                FileTypeFilter = [new FilePickerFileType("Conventional Commit Types") { Patterns = ["*.json"] }],
                AllowMultiple = false,
            };

            var selected = await StorageProvider.OpenFilePickerAsync(options);
            if (selected.Count == 1 && DataContext is ViewModels.RepositoryConfigure vm)
                vm.ConventionalTypesOverride = selected[0].Path.LocalPath;

            e.Handled = true;
        }

        private async void SelectExecutableForCustomAction(object sender, RoutedEventArgs e)
        {
            var suggestedStartLocation = DataContext is ViewModels.RepositoryConfigure vm ?
                await StorageProvider.TryGetFolderFromPathAsync(vm.RepoPath) :
                null;

            var options = new FilePickerOpenOptions()
            {
                AllowMultiple = false,
                FileTypeFilter = [new("Executable file(script)") { Patterns = ["*"] }],
                SuggestedStartLocation = suggestedStartLocation,
            };

            var selected = await StorageProvider.OpenFilePickerAsync(options);
            if (selected.Count == 1 && sender is Button { DataContext: Models.CustomAction action })
            {
                var executable = selected[0].Path.LocalPath;
                action.Executable = executable;
                RenameDefaultCustomAction(action, executable);
            }

            e.Handled = true;
        }

        private static void RenameDefaultCustomAction(Models.CustomAction action, string executable)
        {
            if (!string.IsNullOrWhiteSpace(action.Name) &&
                !action.Name.Equals("Unnamed Action", StringComparison.Ordinal))
                return;

            var filename = Path.GetFileName(executable);
            if (!string.IsNullOrWhiteSpace(filename))
                action.Name = filename;
        }

        private async void EditCustomActionControls(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: Models.CustomAction act })
                return;

            await this.ShowDialogAsync(new ViewModels.ConfigureCustomActionControls(act.Controls));
            e.Handled = true;
        }

        private async void ApplyLocalIgnoreRules(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
                await vm.ApplyRepoLocalIgnoreRulesAsync();

            e.Handled = true;
        }

        private async void ManageAssumeUnchangedFiles(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
                await this.ShowDialogAsync(new ViewModels.AssumeUnchangedManager(vm.Repository));

            e.Handled = true;
        }

        private async void ClearLocalIgnoreRules(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                var confirmed = await App.AskConfirmAsync(
                    this,
                    "Clear repository-local ignore rules and restore the matching tracked files?\n\nThis does not modify .gitignore.",
                    Models.ConfirmButtonType.OkCancel);
                if (confirmed)
                    await vm.ClearRepoLocalIgnoreRulesAsync();
            }

            e.Handled = true;
        }

        private void OnNewCustomIssueTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
                vm.AddIssueTracker("New Issue Tracker", @"#(\d+)", "https://xxx/$1");

            e.Handled = true;
        }

        private void OnAddGitHubIssueTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                var link = "https://github.com/username/repository/issues/$1";
                var remotes = vm.GetRemoteVisitUrls();
                foreach (var remote in remotes)
                {
                    if (remote.Contains("github.com", StringComparison.Ordinal))
                    {
                        link = $"{remote}/issues/$1";
                        break;
                    }
                }

                vm.AddIssueTracker("GitHub Issue", @"#(\d+)", link);
            }

            e.Handled = true;
        }

        private void OnAddJiraIssueTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                vm.AddIssueTracker(
                    "Jira Tracker",
                    @"PROJ-(\d+)",
                    "https://jira.yourcompany.com/browse/PROJ-$1");
            }

            e.Handled = true;
        }

        private void OnAddAzureWorkItemTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                vm.AddIssueTracker(
                    "Azure DevOps Tracker",
                    @"#(\d+)",
                    "https://dev.azure.com/yourcompany/workspace/_workitems/edit/$1");
            }

            e.Handled = true;
        }

        private void OnAddGitLabIssueTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                var link = "https://gitlab.com/username/repository/-/issues/$1";
                var remotes = vm.GetRemoteVisitUrls();
                foreach (var remote in remotes)
                {
                    link = $"{remote}/-/issues/$1";
                    break;
                }

                vm.AddIssueTracker("GitLab Issue", @"#(\d+)", link);
            }

            e.Handled = true;
        }

        private void OnAddGitLabMergeRequestTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                var link = "https://gitlab.com/username/repository/-/merge_requests/$1";
                var remotes = vm.GetRemoteVisitUrls();
                foreach (var remote in remotes)
                {
                    link = $"{remote}/-/merge_requests/$1";
                    break;
                }

                vm.AddIssueTracker("GitLab MR", @"!(\d+)", link);
            }

            e.Handled = true;
        }

        private void OnAddGiteeIssueTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                var link = "https://gitee.com/username/repository/issues/$1";
                var remotes = vm.GetRemoteVisitUrls();
                foreach (var remote in remotes)
                {
                    if (remote.Contains("gitee.com", StringComparison.Ordinal))
                    {
                        link = $"{remote}/issues/$1";
                        break;
                    }
                }

                vm.AddIssueTracker("Gitee Issue", @"#([0-9A-Z]{6,10})", link);
            }

            e.Handled = true;
        }

        private void OnAddGiteePullRequestTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                var link = "https://gitee.com/username/repository/pulls/$1";
                var remotes = vm.GetRemoteVisitUrls();
                foreach (var remote in remotes)
                {
                    if (remote.Contains("gitee.com", StringComparison.Ordinal))
                    {
                        link = $"{remote}/pulls/$1";
                        break;
                    }
                }

                vm.AddIssueTracker("Gitee Pull Request", @"!(\d+)", link);
            }

            e.Handled = true;
        }

        private void OnAddGerritChangeIdTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
            {
                vm.AddIssueTracker(
                    "Gerrit Change-Id",
                    @"(I[A-Za-z0-9]{40})",
                    "https://gerrit.yourcompany.com/q/$1");
            }

            e.Handled = true;
        }

        private void OnRemoveIssueTracker(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RepositoryConfigure vm)
                vm.RemoveIssueTracker();

            e.Handled = true;
        }
    }
}
