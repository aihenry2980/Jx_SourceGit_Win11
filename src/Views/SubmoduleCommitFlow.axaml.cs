using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class SubmoduleCommitFlow : UserControl
    {
        public SubmoduleCommitFlow()
        {
            InitializeComponent();

            _saveLayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveLayoutTimer.Tick += (_, _) =>
            {
                _saveLayoutTimer.Stop();
                SaveLayoutWidths();
            };

            Loaded += OnLoaded;
            DetachedFromVisualTree += (_, _) => _saveLayoutTimer.Stop();

            ModuleListColumn.PropertyChanged += OnLayoutColumnPropertyChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm)
                return;

            _applyingSavedWidths = true;
            ModuleListColumn.Width = new GridLength(vm.SavedModuleListWidth);
            _applyingSavedWidths = false;
        }

        private void OnLayoutColumnPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_applyingSavedWidths || e.Property != ColumnDefinition.WidthProperty)
                return;

            _saveLayoutTimer.Stop();
            _saveLayoutTimer.Start();
        }

        private void SaveLayoutWidths()
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm)
                return;

            var moduleListWidth = GetAbsoluteWidth(ModuleListColumn);
            if (moduleListWidth <= 0)
                return;

            vm.SaveLayoutWidths(moduleListWidth);
        }

        private static double GetAbsoluteWidth(ColumnDefinition column)
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0)
                return column.Width.Value;

            return column.ActualWidth;
        }

        private ColumnDefinition ModuleListColumn => LayoutRootGrid.ColumnDefinitions[0];

        private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.RefreshAsync();

            e.Handled = true;
        }

        private async void OnCommitClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.CommitSelectedNodeAsync();

            e.Handled = true;
        }

        private void OnSelectRecommendedNodeClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.SelectRecommendedNode();

            e.Handled = true;
        }

        private async void OnCommitAndPushClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.CommitAndPushSelectedNodeAsync();

            e.Handled = true;
        }

        private async void OnUndoCommitClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.UndoSelectedNodeCommitAsync();

            e.Handled = true;
        }

        private void OnCommitIncludeToggled(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.NotifyCommitIncludeChanged();

            e.Handled = true;
        }

        private void OnChangesContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm ||
                sender is not ChangeCollectionView { SelectedChanges: { Count: > 0 } changes } view)
                return;

            var menu = new ContextMenu();
            var revert = new MenuItem
            {
                Header = $"Revert {changes.Count} selected change{(changes.Count == 1 ? string.Empty : "s")}",
                Icon = this.CreateMenuIcon("Icons.Undo"),
            };
            revert.Click += async (_, ev) =>
            {
                var confirmed = await App.AskConfirmAsync(
                    this.FindAncestorOfType<Window>(),
                    $"Revert {changes.Count} selected change{(changes.Count == 1 ? string.Empty : "s")} in Commit Flow?\n\nThis cannot be undone.",
                    Models.ConfirmButtonType.YesNo);
                if (confirmed)
                    await vm.RevertSelectedChangesAsync();

                ev.Handled = true;
            };
            menu.Items.Add(revert);

            if (changes.Count == 1 && vm.SelectedNode is { } node)
            {
                var change = changes[0];
                var fullPath = Native.OS.GetAbsPath(node.RepoPath, change.Path);
                var fileName = Path.GetFileName(change.Path);

                var copyFileName = new MenuItem
                {
                    Header = "Copy file name",
                    Icon = this.CreateMenuIcon("Icons.Copy"),
                };
                copyFileName.Click += async (_, ev) =>
                {
                    await this.CopyTextAsync(fileName);
                    ev.Handled = true;
                };

                var copyFullPath = new MenuItem
                {
                    Header = "Copy file path + name",
                    Icon = this.CreateMenuIcon("Icons.Copy"),
                };
                copyFullPath.Click += async (_, ev) =>
                {
                    await this.CopyTextAsync(fullPath);
                    ev.Handled = true;
                };

                Models.ExternalTool vscode = null;
                foreach (var tool in Native.OS.ExternalTools)
                {
                    if (tool.Name.Equals("Visual Studio Code", StringComparison.Ordinal))
                    {
                        vscode = tool;
                        break;
                    }
                }

                var openInVSCode = new MenuItem
                {
                    Header = "Open file in VS Code",
                    Icon = this.CreateMenuIcon("Icons.OpenWith"),
                    IsEnabled = File.Exists(fullPath) && vscode != null,
                };
                openInVSCode.Click += (_, ev) =>
                {
                    vscode?.Launch(fullPath.Quoted());
                    ev.Handled = true;
                };

                var openContainingFolder = new MenuItem
                {
                    Header = "Open containing folder",
                    Icon = this.CreateMenuIcon("Icons.Explore"),
                    IsEnabled = File.Exists(fullPath),
                };
                openContainingFolder.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(fullPath);
                    ev.Handled = true;
                };

                menu.Items.Add(new MenuItem { Header = "-" });
                menu.Items.Add(copyFileName);
                menu.Items.Add(copyFullPath);
                menu.Items.Add(new MenuItem { Header = "-" });
                menu.Items.Add(openInVSCode);
                menu.Items.Add(openContainingFolder);
            }

            menu.Open(view);
            e.Handled = true;
        }

        private void OnIncludeAllChangesClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.IncludeAllSelectedNodeChanges();

            e.Handled = true;
        }

        private void OnExcludeAllChangesClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                vm.ExcludeAllSelectedNodeChanges();

            e.Handled = true;
        }

        private async void OnSaveSelectedChangesEncodingClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm)
                await vm.SaveSelectedChangesWithEncodingAsync();

            e.Handled = true;
        }

        private void OnModuleListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None)
                return;

            ChangesList.TakeFocus();
            e.Handled = true;
        }

        private void OnNodeDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow vm &&
                sender is Control { DataContext: ViewModels.SubmoduleCommitFlowNode node })
            {
                vm.OpenGitGraphForNode(node);
            }

            e.Handled = true;
        }

        private void OnNodeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.SubmoduleCommitFlow vm ||
                sender is not Control { DataContext: ViewModels.SubmoduleCommitFlowNode node } control)
                return;

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Go to Git Graph" };
            open.Click += (_, ev) =>
            {
                vm.OpenGitGraphForNode(node);
                ev.Handled = true;
            };

            menu.Items.Add(open);
            menu.Open(control);
            e.Handled = true;
        }

        private void OnOpenTerminalClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SubmoduleCommitFlow { SelectedNode: { } node })
                Native.OS.OpenTerminal(node.RepoPath);

            e.Handled = true;
        }

        private readonly DispatcherTimer _saveLayoutTimer;
        private bool _applyingSavedWidths = false;
    }
}
