using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RepositoryToolbar : UserControl
    {
        private const double COMPACT_WIDTH_THRESHOLD = 1260;
        private const double NARROW_WIDTH_THRESHOLD = 1160;

        private ToolbarDensity _toolbarDensity = ToolbarDensity.Default;

        public RepositoryToolbar()
        {
            InitializeComponent();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            var width = e.NewSize.Width;
            var density =
                width < NARROW_WIDTH_THRESHOLD ? ToolbarDensity.Narrow :
                width < COMPACT_WIDTH_THRESHOLD ? ToolbarDensity.Compact :
                ToolbarDensity.Default;

            if (density != _toolbarDensity)
                ApplyToolbarDensity(density);
        }

        private void OpenWithExternalTools(object sender, RoutedEventArgs ev)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var fullpath = repo.FullPath;
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                RenderOptions.SetBitmapInterpolationMode(menu, BitmapInterpolationMode.HighQuality);
                RenderOptions.SetEdgeMode(menu, EdgeMode.Antialias);
                RenderOptions.SetTextRenderingMode(menu, TextRenderingMode.Antialias);

                var explore = new MenuItem();
                explore.Header = App.Text("Repository.Explore");
                explore.Icon = App.CreateMenuIcon("Icons.Explore");
                explore.Click += (_, e) =>
                {
                    Native.OS.OpenInFileManager(fullpath);
                    e.Handled = true;
                };

                var terminal = new MenuItem();
                terminal.Header = App.Text("Repository.Terminal");
                terminal.Icon = App.CreateMenuIcon("Icons.Terminal");
                terminal.Click += (_, e) =>
                {
                    Native.OS.OpenTerminal(fullpath);
                    e.Handled = true;
                };

                menu.Items.Add(explore);
                menu.Items.Add(terminal);

                var tools = Native.OS.ExternalTools;
                if (tools.Count > 0)
                {
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    foreach (var tool in tools)
                    {
                        var dupTool = tool;

                        var item = new MenuItem();
                        item.Header = App.Text("Repository.OpenIn", dupTool.Name);
                        item.Icon = new Image { Width = 16, Height = 16, Source = dupTool.IconImage };

                        var options = dupTool.MakeLaunchOptions(fullpath);
                        if (options is { Count: > 0 })
                        {
                            foreach (var opt in options)
                            {
                                var subItem = new MenuItem();
                                subItem.Header = opt.Title;
                                subItem.Click += (_, e) =>
                                {
                                    dupTool.Launch(opt.Args);
                                    e.Handled = true;
                                };

                                item.Items.Add(subItem);
                            }

                            var openAsFolder = new MenuItem();
                            openAsFolder.Header = App.Text("Repository.OpenAsFolder");
                            openAsFolder.Click += (_, e) =>
                            {
                                dupTool.Launch(fullpath.Quoted());
                                e.Handled = true;
                            };
                            item.Items.Add(new MenuItem() { Header = "-" });
                            item.Items.Add(openAsFolder);
                        }
                        else
                        {
                            item.Click += (_, e) =>
                            {
                                dupTool.Launch(fullpath.Quoted());
                                e.Handled = true;
                            };
                        }

                        menu.Items.Add(item);
                    }
                }

                var urls = new Dictionary<string, string>();
                foreach (var r in repo.Remotes)
                {
                    if (r.TryGetVisitURL(out var visit))
                        urls.Add(r.Name, visit);
                }

                if (urls.Count > 0)
                {
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    foreach (var (name, addr) in urls)
                    {
                        var dupUrl = addr;

                        var item = new MenuItem();
                        item.Header = App.Text("Repository.Visit", name);
                        item.Icon = App.CreateMenuIcon("Icons.Remotes");
                        item.Click += (_, e) =>
                        {
                            Native.OS.OpenBrowser(dupUrl);
                            e.Handled = true;
                        };

                        menu.Items.Add(item);
                    }
                }

                menu.Open(button);
                ev.Handled = true;
            }
        }

        private void OpenWithExternalToolsByHotKey(object sender, RoutedEventArgs e)
        {
            OpenWithExternalTools(OpenWithExternalToolsButton, e);
        }

        private void OnLogsContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is Control control)
                OpenLogsContextMenu(control);

            e.Handled = true;
        }

        private void OnLogsPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control)
                return;

            var point = e.GetCurrentPoint(control);
            if (!point.Properties.IsRightButtonPressed)
                return;

            OpenLogsContextMenu(control);
            e.Handled = true;
        }

        private async void OpenStatistics(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await App.ShowDialog(new ViewModels.Statistics(repo.FullPath));
                e.Handled = true;
            }
        }

        private async void OpenConfigure(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await App.ShowDialog(new ViewModels.RepositoryConfigure(repo));
                e.Handled = true;
            }
        }

        private async void Fetch(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.FetchAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private async void FetchDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.FetchAsync(true);
                e.Handled = true;
            }
        }

        private async void QuickFetch(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.QuickFetchAsync();
                e.Handled = true;
            }
        }

        private async void SuperQuickFetch(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.QuickFetchAsync(true);
                e.Handled = true;
            }
        }

        private async void SuperQuickFetchByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.QuickFetchAsync(true);
                e.Handled = true;
            }
        }

        private async void QuickFetchByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.QuickFetchAsync();
                e.Handled = true;
            }
        }

        private async void FetchRecursivelyWithOptionalPrune(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.CanCreatePopup())
            {
                var prune = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)
                    ? false
                    : true;
                OpenToolbarRecursiveOperationWindow(new ViewModels.ToolbarRecursiveOperation(
                    repo,
                    prune ? ViewModels.ToolbarRecursiveOperationKind.FetchAndPruneRecursively : ViewModels.ToolbarRecursiveOperationKind.FetchRecursively));
                e.Handled = true;
            }
        }

        private async void FetchAndPruneRecursively(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.CanCreatePopup())
            {
                OpenToolbarRecursiveOperationWindow(new ViewModels.ToolbarRecursiveOperation(
                    repo,
                    ViewModels.ToolbarRecursiveOperationKind.FetchAndPruneRecursively));
                e.Handled = true;
            }
        }

        private void RefreshHistoryGraph(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                repo.RefreshSuperProjectSubmodulePointer();
                repo.RefreshBranches();
                repo.RefreshCommits();
                e.Handled = true;
            }
        }

        private async void FetchRecursively(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.CanCreatePopup())
            {
                OpenToolbarRecursiveOperationWindow(new ViewModels.ToolbarRecursiveOperation(
                    repo,
                    ViewModels.ToolbarRecursiveOperationKind.FetchRecursively));
                e.Handled = true;
            }
        }

        private async void UndoRecentCommands(object sender, RoutedEventArgs e)
        {
            if (sender is Control control)
                await OpenUndoRecentCommandsMenuAsync(control);

            e.Handled = true;
        }

        private async void UndoLastRebase(object sender, RoutedEventArgs e)
        {
            await OpenUndoRecentCommandsMenuAsync(UndoRecentCommandsButton);
            e.Handled = true;
        }

        private async void UpdateSubmodulesRecursively(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.CanCreatePopup())
            {
                OpenToolbarRecursiveOperationWindow(new ViewModels.ToolbarRecursiveOperation(
                    repo,
                    ViewModels.ToolbarRecursiveOperationKind.UpdateSubmodulesRecursively));
                e.Handled = true;
            }
        }

        private async void SyncAll(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.CanCreatePopup())
            {
                var needsSelection = repo.Submodules.Count > 0 && repo.Settings?.NeedsRecursiveSubmoduleUpdateTargetsConfiguration() == true;
                var kind = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)
                    ? ViewModels.ToolbarRecursiveOperationKind.PullUpdateAndFetchPruneRecursively
                    : ViewModels.ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively;
                var popup = new ViewModels.ToolbarRecursiveOperation(
                    repo,
                    kind,
                    needsSelection);
                OpenToolbarRecursiveOperationWindow(popup);
                e.Handled = true;
            }
        }

        private void OnSyncAllContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is Control control)
                OpenSyncAllContextMenu(control);

            e.Handled = true;
        }

        private void OnSyncAllPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control)
                return;

            var point = e.GetCurrentPoint(control);
            if (!point.Properties.IsRightButtonPressed)
                return;

            OpenSyncAllContextMenu(control);
            e.Handled = true;
        }

        private void OpenSyncAllContextMenu(Control control)
        {
            if (DataContext is not ViewModels.Repository repo || !repo.CanCreatePopup())
                return;

            var menu = new ContextMenu();

            var choose = new MenuItem();
            choose.Header = "Choose submodules...";
            choose.Icon = App.CreateMenuIcon("Icons.Submodule");
            choose.IsEnabled = repo.Submodules.Count > 0;
            choose.Click += (_, ev) =>
            {
                menu.Close();
                Dispatcher.UIThread.Post(() =>
                {
                    OpenToolbarRecursiveOperationWindow(new ViewModels.ToolbarRecursiveOperation(
                        repo,
                        ViewModels.ToolbarRecursiveOperationKind.PullAndUpdateSubmodulesRecursively,
                        true,
                        true));
                }, DispatcherPriority.Background);
                ev.Handled = true;
            };

            menu.Items.Add(choose);
            menu.Open(control);
        }

        private async Task OpenUndoRecentCommandsMenuAsync(Control anchor)
        {
            if (DataContext is not ViewModels.Repository repo || !repo.CanCreatePopup())
                return;

            var current = repo.CurrentBranch;
            if (current == null || !current.IsLocal)
            {
                App.SendNotification("Undo Recent Commands", "Current local branch is not available.");
                return;
            }

            var reflog = await new Commands.QueryHeadReflog(repo.FullPath, 4).GetResultAsync();
            if (reflog.Count < 2)
            {
                App.SendNotification("Undo Recent Commands", "Not enough recent HEAD movements were found to undo.");
                return;
            }

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.MinWidth = 720;

            var maxCount = Math.Min(3, reflog.Count - 1);
            for (var count = 1; count <= maxCount; count++)
            {
                var targetEntry = reflog[count];
                var recentSummaries = new List<string>();
                for (var i = 0; i < count && i < reflog.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(reflog[i].Summary))
                        recentSummaries.Add(ShortenUndoSummary(reflog[i].Summary));
                }

                var item = new MenuItem();
                item.Icon = App.CreateMenuIcon("Icons.Undo");
                item.MinHeight = 52;
                item.Header = CreateUndoRecentMenuHeader(
                    $"Undo recent {count} cmd{(count == 1 ? string.Empty : "s")}",
                    recentSummaries.Count > 0
                        ? string.Join(" | ", recentSummaries)
                        : $"Reset to {targetEntry.SHA.Substring(0, Math.Min(10, targetEntry.SHA.Length))}");
                item.Click += async (_, ev) =>
                {
                    var target = await new Commands.QuerySingleCommit(repo.FullPath, targetEntry.SHA).GetResultAsync();
                    if (target == null)
                    {
                        App.SendNotification("Undo Recent Commands", $"Target commit '{targetEntry.SHA}' was not found.");
                        ev.Handled = true;
                        return;
                    }

                    repo.ShowPopup(new ViewModels.Reset(repo, current, target));
                    ev.Handled = true;
                };
                menu.Items.Add(item);
            }

            menu.Open(anchor);
        }

        private async void Pull(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PullAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private async void PullDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PullAsync(true);
                e.Handled = true;
            }
        }

        private async void Push(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PushAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private async void PushDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PushAsync(true);
                e.Handled = true;
            }
        }

        private async void StashAll(object _, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.StashAllAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private async void StashAllByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.StashAllAsync(false);
                e.Handled = true;
            }
        }

        private void OpenGitFlowMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                if (repo.IsGitFlowEnabled())
                {
                    var startFeature = new MenuItem();
                    startFeature.Header = App.Text("GitFlow.StartFeature");
                    startFeature.Icon = App.CreateMenuIcon("Icons.GitFlow.Feature");
                    startFeature.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Feature));
                        e.Handled = true;
                    };

                    var startRelease = new MenuItem();
                    startRelease.Header = App.Text("GitFlow.StartRelease");
                    startRelease.Icon = App.CreateMenuIcon("Icons.GitFlow.Release");
                    startRelease.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Release));
                        e.Handled = true;
                    };

                    var startHotfix = new MenuItem();
                    startHotfix.Header = App.Text("GitFlow.StartHotfix");
                    startHotfix.Icon = App.CreateMenuIcon("Icons.GitFlow.Hotfix");
                    startHotfix.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Hotfix));
                        e.Handled = true;
                    };

                    menu.Items.Add(startFeature);
                    menu.Items.Add(startRelease);
                    menu.Items.Add(startHotfix);
                }
                else
                {
                    var init = new MenuItem();
                    init.Header = App.Text("GitFlow.Init");
                    init.Icon = App.CreateMenuIcon("Icons.Init");
                    init.Click += (_, e) =>
                    {
                        if (repo.CurrentBranch == null)
                            App.RaiseException(repo.FullPath, "Git flow init failed: No branch found!!!");
                        else if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.InitGitFlow(repo));

                        e.Handled = true;
                    };
                    menu.Items.Add(init);
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private void OpenGitLFSMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                if (repo.IsLFSEnabled())
                {
                    var addPattern = new MenuItem();
                    addPattern.Header = App.Text("GitLFS.AddTrackPattern");
                    addPattern.Icon = App.CreateMenuIcon("Icons.File.Add");
                    addPattern.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.LFSTrackCustomPattern(repo));

                        e.Handled = true;
                    };
                    menu.Items.Add(addPattern);
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    var fetch = new MenuItem();
                    fetch.Header = App.Text("GitLFS.Fetch");
                    fetch.Icon = App.CreateMenuIcon("Icons.Fetch");
                    fetch.IsEnabled = repo.Remotes.Count > 0;
                    fetch.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                        {
                            if (repo.Remotes.Count == 1)
                                await repo.ShowAndStartPopupAsync(new ViewModels.LFSFetch(repo));
                            else
                                repo.ShowPopup(new ViewModels.LFSFetch(repo));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(fetch);

                    var pull = new MenuItem();
                    pull.Header = App.Text("GitLFS.Pull");
                    pull.Icon = App.CreateMenuIcon("Icons.Pull");
                    pull.IsEnabled = repo.Remotes.Count > 0;
                    pull.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                        {
                            if (repo.Remotes.Count == 1)
                                await repo.ShowAndStartPopupAsync(new ViewModels.LFSPull(repo));
                            else
                                repo.ShowPopup(new ViewModels.LFSPull(repo));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(pull);

                    var push = new MenuItem();
                    push.Header = App.Text("GitLFS.Push");
                    push.Icon = App.CreateMenuIcon("Icons.Push");
                    push.IsEnabled = repo.Remotes.Count > 0;
                    push.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                        {
                            if (repo.Remotes.Count == 1)
                                await repo.ShowAndStartPopupAsync(new ViewModels.LFSPush(repo));
                            else
                                repo.ShowPopup(new ViewModels.LFSPush(repo));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(push);

                    var prune = new MenuItem();
                    prune.Header = App.Text("GitLFS.Prune");
                    prune.Icon = App.CreateMenuIcon("Icons.Clean");
                    prune.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            await repo.ShowAndStartPopupAsync(new ViewModels.LFSPrune(repo));

                        e.Handled = true;
                    };
                    menu.Items.Add(new MenuItem() { Header = "-" });
                    menu.Items.Add(prune);

                    var locks = new MenuItem();
                    locks.Header = App.Text("GitLFS.Locks");
                    locks.Icon = App.CreateMenuIcon("Icons.Lock");
                    locks.IsEnabled = repo.Remotes.Count > 0;
                    if (repo.Remotes.Count == 1)
                    {
                        locks.Click += async (_, e) =>
                        {
                            await App.ShowDialog(new ViewModels.LFSLocks(repo, repo.Remotes[0].Name));
                            e.Handled = true;
                        };
                    }
                    else
                    {
                        foreach (var remote in repo.Remotes)
                        {
                            var remoteName = remote.Name;
                            var lockRemote = new MenuItem();
                            lockRemote.Header = remoteName;
                            lockRemote.Click += async (_, e) =>
                            {
                                await App.ShowDialog(new ViewModels.LFSLocks(repo, remoteName));
                                e.Handled = true;
                            };
                            locks.Items.Add(lockRemote);
                        }
                    }

                    menu.Items.Add(new MenuItem() { Header = "-" });
                    menu.Items.Add(locks);
                }
                else
                {
                    var install = new MenuItem();
                    install.Header = App.Text("GitLFS.Install");
                    install.Icon = App.CreateMenuIcon("Icons.Init");
                    install.Click += async (_, e) =>
                    {
                        await repo.InstallLFSAsync();
                        e.Handled = true;
                    };
                    menu.Items.Add(install);
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private async void StartBisect(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository { IsBisectCommandRunning: false, InProgressContext: null } repo &&
                repo.CanCreatePopup())
            {
                if (repo.LocalChangesCount > 0)
                    App.RaiseException(repo.FullPath, "You have un-committed local changes. Please discard or stash them first.");
                else if (repo.IsBisectCommandRunning || repo.BisectState != Models.BisectState.None)
                    App.RaiseException(repo.FullPath, "Bisect is running! Please abort it before starting a new one.");
                else
                    await repo.ExecBisectCommandAsync("start");
            }

            e.Handled = true;
        }

        private async void Cleanup(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.CleanupAsync();
                e.Handled = true;
            }
        }

        private void OpenCustomActionMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                var actions = repo.GetCustomActions(Models.CustomActionScope.Repository);
                if (actions.Count > 0)
                {
                    foreach (var action in actions)
                    {
                        var (dup, label) = action;
                        var item = new MenuItem();
                        item.Icon = App.CreateMenuIcon("Icons.Action");
                        item.Header = label;
                        item.Click += async (_, e) =>
                        {
                            await repo.ExecCustomActionAsync(dup, null);
                            e.Handled = true;
                        };

                        menu.Items.Add(item);
                    }
                }
                else
                {
                    menu.Items.Add(new MenuItem() { Header = App.Text("Repository.CustomActions.Empty") });
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private void OpenCustomActionMenuByHotKey(object sender, RoutedEventArgs e)
        {
            OpenCustomActionMenu(OpenCustomActionMenuButton, e);
        }

        private void OpenLogsContextMenu(Control control)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

            var logs = new MenuItem();
            logs.Header = App.Text("Repository.ViewLogs");
            logs.Icon = App.CreateMenuIcon("Icons.Logs");
            logs.Click += async (_, ev) =>
            {
                await App.ShowDialog(new ViewModels.ViewLogs(repo));
                ev.Handled = true;
            };

            var profiler = new MenuItem();
            profiler.Header = "Memory Profile";
            profiler.Icon = App.CreateMenuIcon("Icons.Statistics");
            profiler.Click += (_, ev) =>
            {
                App.ShowWindow(new ViewModels.MemoryProfiler());
                ev.Handled = true;
            };

            menu.Items.Add(logs);
            menu.Items.Add(profiler);
            menu.Open(control);
        }

        private static object CreateUndoRecentMenuHeader(string title, string detail)
        {
            var stack = new StackPanel() { Orientation = Orientation.Vertical, Spacing = 1 };
            stack.Children.Add(new TextBlock()
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
            });
            stack.Children.Add(new TextBlock()
            {
                Text = detail,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 620,
            });
            return stack;
        }

        private static string ShortenUndoSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return string.Empty;

            var trimmed = summary.Trim();
            return trimmed.Length <= 72 ? trimmed : $"{trimmed.Substring(0, 69)}...";
        }

        private async void OpenGitLogs(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await App.ShowDialog(new ViewModels.ViewLogs(repo));
                e.Handled = true;
            }
        }

        private void OpenMemoryProfilerByHotKey(object sender, RoutedEventArgs e)
        {
            App.ShowWindow(new ViewModels.MemoryProfiler());
            e.Handled = true;
        }

        private void CreateNewBranchByHotKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                repo.CreateNewBranch();
                e.Handled = true;
            }
        }

        private void NavigateToHead(object sender, RoutedEventArgs e)
        {
            TryNavigateToCurrentHead();
            e.Handled = true;
        }

        private void NavigateToSuperProjectPointer(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.NavigateToSuperProjectPointerCommit();

            e.Handled = true;
        }

        private void OnCurrentBranchNamePointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                TryNavigateToCurrentHead();
                e.Handled = true;
            }
        }

        private void TryNavigateToCurrentHead()
        {
            if (DataContext is not ViewModels.Repository { CurrentBranch: not null } repo)
                return;

            var repoView = TopLevel.GetTopLevel(this)?.FindDescendantOfType<Repository>();
            repoView?.LocalBranchTree?.Select(repo.CurrentBranch);
            repo.NavigateToCommit(repo.CurrentBranch.Head);
        }

        private static void OpenToolbarRecursiveOperationWindow(ViewModels.ToolbarRecursiveOperation operation)
        {
            operation.ShowEmbeddedHeader = false;
            App.ShowWindow(new ToolbarRecursiveOperationWindow
            {
                DataContext = operation,
            });
        }

        private void ApplyToolbarDensity(ToolbarDensity density)
        {
            _toolbarDensity = density;

            var buttonWidth = density switch
            {
                ToolbarDensity.Narrow => 48,
                ToolbarDensity.Compact => 52,
                _ => 56,
            };
            var utilityButtonWidth = density switch
            {
                ToolbarDensity.Narrow => 36,
                ToolbarDensity.Compact => 40,
                _ => 44,
            };
            var primaryGap = density switch
            {
                ToolbarDensity.Narrow => 2,
                ToolbarDensity.Compact => 4,
                _ => 8,
            };
            var secondaryGap = density switch
            {
                ToolbarDensity.Narrow => 1,
                ToolbarDensity.Compact => 2,
                _ => 4,
            };
            var sideMargin = density == ToolbarDensity.Default ? 4 : 2;

            LeftToolbarGroup.Margin = new Thickness(sideMargin, 0, 0, 0);
            RightToolbarGroup.Margin = new Thickness(0, 0, sideMargin, 0);
            UpdateToolbarButtons(LeftToolbarGroup, utilityButtonWidth, 0);
            UpdateToolbarButtons(CenterToolbarGroup, buttonWidth, primaryGap);
            UpdateToolbarButtons(RightToolbarGroup, buttonWidth, secondaryGap);
            ActionSeparator.Margin = new Thickness(primaryGap, 0, 0, 0);
        }

        private static void UpdateToolbarButtons(StackPanel panel, double buttonWidth, double gap)
        {
            foreach (var button in panel.Children.OfType<Button>().Where(x => x.Classes.Contains("icon_button")))
            {
                button.Width = buttonWidth;

                var margin = button.Margin;
                button.Margin = new Thickness(
                    margin.Left > 0 ? gap : 0,
                    margin.Top,
                    margin.Right,
                    margin.Bottom);
            }
        }

        private enum ToolbarDensity
        {
            Default,
            Compact,
            Narrow,
        }
    }
}
