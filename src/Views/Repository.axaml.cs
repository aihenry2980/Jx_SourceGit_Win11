using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public partial class Repository : UserControl
    {
        public Repository()
        {
            InitializeComponent();
            DashboardScrollViewer.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnDashboardScrollViewerPointerWheelChanged,
                RoutingStrategies.Tunnel,
                true);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            UpdateLeftSidebarLayout();
        }

        private void OnToggleFilter(object _, RoutedEventArgs e)
        {
            TxtSearchCommitsBox?.Focus();
            e.Handled = true;
        }

        private void OnSearchCommitPanelPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty && sender is Grid { IsVisible: true })
                TxtSearchCommitsBox.Focus();
        }

        private void OnDashboardScrollViewerPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && !scrollViewer.IsPointerOver)
                e.Handled = true;
        }

        private void OnRefreshSubmodules(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.RefreshSubmodules(true);

            e.Handled = true;
        }

        private void OnSearchKeyDown(object _, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            if (e.Key == Key.Enter)
            {
                repo.SearchCommitContext.StartSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                if (repo.SearchCommitContext.Suggestions is { Count: > 0 })
                {
                    SearchSuggestionBox.Focus(NavigationMethod.Tab);
                    SearchSuggestionBox.SelectedIndex = 0;
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                repo.SearchCommitContext.ClearSuggestions();
                e.Handled = true;
            }
        }

        private void OnClearSearchCommitFilter(object _, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            repo.SearchCommitContext.ClearFilter();
            e.Handled = true;
        }

        private void OnHistoryQuickFindBoxPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != TextBox.TagProperty || sender is not TextBox box)
                return;

            if (box.Tag is not long requestId || requestId <= 0)
                return;

            box.Focus();
            box.SelectAll();
        }

        private void OnHistoryQuickFindKeyDown(object _, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            if (e.Key == Key.Escape)
            {
                repo.ClearHistoryQuickFind();
                e.Handled = true;
            }
        }

        private void OnOpenRecursiveLocalChanges(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                App.ShowWindow(new ViewModels.RecursiveLocalChanges(repo));

            e.Handled = true;
        }

        private async void OnRestoreCleanStateRecursively(object _, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo || !repo.CanCreatePopup())
                return;

            var confirmed = await App.AskConfirmAsync(
                "Restore the parent repository and all initialized submodules to a clean tracked state?\n\nThis will permanently discard tracked changes, but it will keep untracked files.",
                Models.ConfirmButtonType.YesNo);
            if (!confirmed)
            {
                e.Handled = true;
                return;
            }

            var operation = new ViewModels.ToolbarRecursiveOperation(
                repo,
                ViewModels.ToolbarRecursiveOperationKind.RestoreCleanStateRecursively)
            {
                ShowEmbeddedHeader = false,
            };

            App.ShowWindow(new ToolbarRecursiveOperationWindow
            {
                DataContext = operation,
            });

            e.Handled = true;
        }

        private void OnLocalBranchTreeSelectionChanged(object _1, RoutedEventArgs _2)
        {
            RemoteBranchTree.UnselectAll();
            TagsList.UnselectAll();
        }

        private void OnRemoteBranchTreeSelectionChanged(object _1, RoutedEventArgs _2)
        {
            LocalBranchTree.UnselectAll();
            TagsList.UnselectAll();
        }

        private void OnTagsSelectionChanged(object _1, RoutedEventArgs _2)
        {
            LocalBranchTree.UnselectAll();
            RemoteBranchTree.UnselectAll();
        }

        private void OnWorktreeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is Control { DataContext: ViewModels.Worktree worktree } ctrl && DataContext is ViewModels.Repository repo)
            {
                var menu = new ContextMenu();

                var switchTo = new MenuItem();
                switchTo.Header = App.Text("Worktree.Open");
                switchTo.Icon = this.CreateMenuIcon("Icons.Folder.Open");
                switchTo.Click += (_, ev) =>
                {
                    repo.OpenWorktree(worktree);
                    ev.Handled = true;
                };
                menu.Items.Add(switchTo);
                menu.Items.Add(new MenuItem() { Header = "-" });

                if (worktree.IsLocked)
                {
                    var unlock = new MenuItem();
                    unlock.Header = App.Text("Worktree.Unlock");
                    unlock.Icon = this.CreateMenuIcon("Icons.Unlock");
                    unlock.Click += async (_, ev) =>
                    {
                        await repo.UnlockWorktreeAsync(worktree);
                        ev.Handled = true;
                    };
                    menu.Items.Add(unlock);
                }
                else
                {
                    var loc = new MenuItem();
                    loc.Header = App.Text("Worktree.Lock");
                    loc.Icon = this.CreateMenuIcon("Icons.Lock");
                    loc.IsEnabled = !worktree.IsMain;
                    loc.Click += async (_, ev) =>
                    {
                        await repo.LockWorktreeAsync(worktree);
                        ev.Handled = true;
                    };
                    menu.Items.Add(loc);
                }

                var remove = new MenuItem();
                remove.Header = App.Text("Worktree.Remove");
                remove.Icon = this.CreateMenuIcon("Icons.Clear");
                remove.IsEnabled = !worktree.IsCurrent && !worktree.IsMain;
                remove.Click += (_, ev) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.RemoveWorktree(repo, worktree));
                    ev.Handled = true;
                };
                menu.Items.Add(remove);

                var copy = new MenuItem();
                copy.Header = App.Text("Worktree.CopyPath");
                copy.Icon = this.CreateMenuIcon("Icons.Copy");
                copy.Click += async (_, ev) =>
                {
                    await this.CopyTextAsync(worktree.FullPath);
                    ev.Handled = true;
                };
                menu.Items.Add(new MenuItem() { Header = "-" });
                menu.Items.Add(copy);
                menu.Open(ctrl);
            }

            e.Handled = true;
        }

        private void OnWorktreeDoubleTapped(object sender, TappedEventArgs e)
        {
            if (sender is Control { DataContext: ViewModels.Worktree worktree } && DataContext is ViewModels.Repository repo)
                repo.OpenWorktree(worktree);

            e.Handled = true;
        }

        private void OnWorktreeListPropertyChanged(object _, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ItemsControl.ItemsSourceProperty || e.Property == IsVisibleProperty)
                UpdateLeftSidebarLayout();
        }

        private void OnLeftSidebarRowsChanged(object _, RoutedEventArgs e)
        {
            UpdateLeftSidebarLayout();
            e.Handled = true;
        }

        private void OnLeftSidebarSizeChanged(object _, SizeChangedEventArgs e)
        {
            if (e.HeightChanged)
                UpdateLeftSidebarLayout();
        }

        private void UpdateLeftSidebarLayout()
        {
            var vm = DataContext as ViewModels.Repository;
            if (vm?.Settings == null)
                return;

            if (!IsLoaded)
                return;

            var visibleHeaderHeight = 28.0 + 35.0 * 2 + (vm.IsInfrequentGroupExpanded ? 28.0 * 3 : 0);
            var leftHeight = LeftSidebarGroups.Bounds.Height - visibleHeaderHeight - 4;
            if (leftHeight <= 0)
                return;

            var localBranchRows = vm.IsLocalBranchGroupExpanded ? LocalBranchTree.Rows.Count : 0;
            var remoteBranchRows = vm.IsInfrequentGroupExpanded && vm.IsRemoteGroupExpanded ? RemoteBranchTree.Rows.Count : 0;
            var desiredLocalBranches = localBranchRows * 24.0;
            var desiredSubmodule = vm.IsSubmoduleGroupExpanded ? 24.0 * SubmoduleList.Rows : 0;
            var desiredRemoteBranches = remoteBranchRows * 24.0;
            var desiredTag = vm.IsInfrequentGroupExpanded && vm.IsTagGroupExpanded ? 24.0 * TagsList.Rows : 0;
            var desiredWorktree = vm.IsInfrequentGroupExpanded && vm.IsWorktreeGroupExpanded ? 24.0 * vm.Worktrees.Count : 0;
            var desiredFrequent = desiredLocalBranches + desiredSubmodule;
            var desiredInfrequent = desiredRemoteBranches + desiredTag + desiredWorktree;
            var hasOverflow = (desiredFrequent + desiredInfrequent > leftHeight);

            if (vm.IsInfrequentGroupExpanded && vm.IsWorktreeGroupExpanded)
            {
                var height = desiredWorktree;
                if (hasOverflow)
                {
                    var test = leftHeight - desiredFrequent - desiredRemoteBranches - desiredTag;
                    if (test < 0)
                        height = Math.Min(120, height);
                    else
                        height = Math.Max(120, test);
                }

                leftHeight -= height;
                WorktreeList.Height = height;
                hasOverflow = (desiredFrequent + desiredRemoteBranches + desiredTag) > leftHeight;
            }

            if (vm.IsInfrequentGroupExpanded && vm.IsTagGroupExpanded)
            {
                var height = desiredTag;
                if (hasOverflow)
                {
                    var test = leftHeight - desiredFrequent - desiredRemoteBranches;
                    if (test < 0)
                        height = Math.Min(120, height);
                    else
                        height = Math.Max(120, test);
                }

                leftHeight -= height;
                TagsList.Height = height;
                hasOverflow = (desiredFrequent + desiredRemoteBranches) > leftHeight;
            }

            if (vm.IsInfrequentGroupExpanded && vm.IsRemoteGroupExpanded)
            {
                var height = desiredRemoteBranches;
                if (hasOverflow)
                {
                    var test = leftHeight - desiredFrequent;
                    if (test < 0)
                        height = Math.Min(120, height);
                    else
                        height = Math.Max(120, test);
                }

                leftHeight -= height;
                RemoteBranchTree.Height = height;
            }

            var desiredPrimary = desiredLocalBranches + desiredSubmodule;
            if (leftHeight > 0 && desiredPrimary > leftHeight)
            {
                var half = leftHeight / 2;
                if (vm.IsLocalBranchGroupExpanded)
                {
                    if (vm.IsSubmoduleGroupExpanded)
                    {
                        if (desiredLocalBranches < half)
                        {
                            LocalBranchTree.Height = desiredLocalBranches;
                            SubmoduleList.Height = leftHeight - desiredLocalBranches;
                        }
                        else if (desiredSubmodule < half)
                        {
                            SubmoduleList.Height = desiredSubmodule;
                            LocalBranchTree.Height = leftHeight - desiredSubmodule;
                        }
                        else
                        {
                            LocalBranchTree.Height = half;
                            SubmoduleList.Height = half;
                        }
                    }
                    else
                    {
                        LocalBranchTree.Height = leftHeight;
                    }
                }
                else if (vm.IsSubmoduleGroupExpanded)
                {
                    SubmoduleList.Height = leftHeight;
                }
            }
            else
            {
                if (vm.IsLocalBranchGroupExpanded)
                {
                    LocalBranchTree.Height = desiredLocalBranches;
                }

                if (vm.IsSubmoduleGroupExpanded)
                {
                    SubmoduleList.Height = desiredSubmodule;
                }
            }
        }

        private void OnSearchSuggestionBoxKeyDown(object _, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            if (e.Key == Key.Escape)
            {
                repo.SearchCommitContext.ClearSuggestions();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var selected = SearchSuggestionBox.SelectedItem;
                if (selected is string content)
                {
                    repo.SearchCommitContext.Filter = content;
                    TxtSearchCommitsBox.CaretIndex = content.Length;
                }
                else if (selected is Models.User user)
                {
                    var apply = user.ToString().EscapeForBRE();
                    repo.SearchCommitContext.Filter = apply;
                    TxtSearchCommitsBox.CaretIndex = apply.Length;
                }

                repo.SearchCommitContext.StartSearch();
                e.Handled = true;
            }
        }

        private void OnSearchSuggestionTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            var ctx = (sender as Control)?.DataContext;
            if (ctx is string content)
            {
                repo.SearchCommitContext.Filter = content;
                TxtSearchCommitsBox.CaretIndex = content.Length;
            }
            else if (ctx is Models.User user)
            {
                var apply = user.ToString().EscapeForBRE();
                repo.SearchCommitContext.Filter = apply;
                TxtSearchCommitsBox.CaretIndex = apply.Length;
            }

            repo.SearchCommitContext.StartSearch();
            e.Handled = true;
        }

        private void OnHistoryOrderByDateClicked(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.EnableTopoOrderInHistory = false;

            e.Handled = true;
        }

        private void OnHistoryOrderTopoClicked(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.EnableTopoOrderInHistory = true;

            e.Handled = true;
        }

        private void OnHistoryTagsShownClicked(object _, RoutedEventArgs e)
        {
            ViewModels.Preferences.Instance.ShowTagsInGraph = true;
            e.Handled = true;
        }

        private void OnHistoryTagsHiddenClicked(object _, RoutedEventArgs e)
        {
            ViewModels.Preferences.Instance.ShowTagsInGraph = false;
            e.Handled = true;
        }

        private void OnOpenAdvancedHistoriesOption(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var pref = ViewModels.Preferences.Instance;

                var layout = new MenuItem();
                layout.Header = App.Text("Repository.HistoriesLayout");
                layout.IsEnabled = false;

                var isHorizontal = pref.UseTwoColumnsLayoutInHistories;
                var horizontal = new MenuItem();
                horizontal.Header = App.Text("Repository.HistoriesLayout.Horizontal");
                if (isHorizontal)
                    horizontal.Icon = this.CreateMenuIcon("Icons.Check");
                horizontal.Click += (_, ev) =>
                {
                    pref.UseTwoColumnsLayoutInHistories = true;
                    ev.Handled = true;
                };

                var vertical = new MenuItem();
                vertical.Header = App.Text("Repository.HistoriesLayout.Vertical");
                if (!isHorizontal)
                    vertical.Icon = this.CreateMenuIcon("Icons.Check");
                vertical.Click += (_, ev) =>
                {
                    pref.UseTwoColumnsLayoutInHistories = false;
                    ev.Handled = true;
                };

                var showFlags = new MenuItem();
                showFlags.Header = App.Text("Repository.ShowFlags");
                showFlags.IsEnabled = false;

                var reflog = new MenuItem();
                reflog.Header = App.Text("Repository.ShowLostCommits");
                reflog.Tag = "--reflog";
                if (repo.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.Reflog))
                    reflog.Icon = this.CreateMenuIcon("Icons.Check");
                reflog.Click += (_, ev) =>
                {
                    repo.ToggleHistoryShowFlag(Models.HistoryShowFlags.Reflog);
                    ev.Handled = true;
                };

                var firstParentOnly = new MenuItem();
                firstParentOnly.Header = App.Text("Repository.ShowFirstParentOnly");
                firstParentOnly.Tag = "--first-parent";
                if (repo.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly))
                    firstParentOnly.Icon = this.CreateMenuIcon("Icons.Check");
                firstParentOnly.Click += (_, ev) =>
                {
                    repo.ToggleHistoryShowFlag(Models.HistoryShowFlags.FirstParentOnly);
                    ev.Handled = true;
                };

                var simplifyByDecoration = new MenuItem();
                simplifyByDecoration.Header = App.Text("Repository.ShowDecoratedCommitsOnly");
                simplifyByDecoration.Tag = "--simplify-by-decoration";
                if (repo.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.SimplifyByDecoration))
                    simplifyByDecoration.Icon = this.CreateMenuIcon("Icons.Check");
                simplifyByDecoration.Click += (_, ev) =>
                {
                    repo.ToggleHistoryShowFlag(Models.HistoryShowFlags.SimplifyByDecoration);
                    ev.Handled = true;
                };

                var order = new MenuItem();
                order.Header = App.Text("Repository.HistoriesOrder");
                order.IsEnabled = false;

                var dateOrder = new MenuItem();
                dateOrder.Header = App.Text("Repository.HistoriesOrder.ByDate");
                dateOrder.Tag = "--date-order";
                if (!repo.EnableTopoOrderInHistory)
                    dateOrder.Icon = this.CreateMenuIcon("Icons.Check");
                dateOrder.Click += (_, ev) =>
                {
                    repo.EnableTopoOrderInHistory = false;
                    ev.Handled = true;
                };

                var topoOrder = new MenuItem();
                topoOrder.Header = App.Text("Repository.HistoriesOrder.Topo");
                topoOrder.Tag = "--topo-order";
                if (repo.EnableTopoOrderInHistory)
                    topoOrder.Icon = this.CreateMenuIcon("Icons.Check");
                topoOrder.Click += (_, ev) =>
                {
                    repo.EnableTopoOrderInHistory = true;
                    ev.Handled = true;
                };

                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
                menu.Items.Add(layout);
                menu.Items.Add(horizontal);
                menu.Items.Add(vertical);
                menu.Items.Add(new MenuItem() { Header = "-" });
                menu.Items.Add(showFlags);
                menu.Items.Add(reflog);
                menu.Items.Add(firstParentOnly);
                menu.Items.Add(simplifyByDecoration);
                menu.Items.Add(new MenuItem() { Header = "-" });
                menu.Items.Add(order);
                menu.Items.Add(dateOrder);
                menu.Items.Add(topoOrder);
                menu.Open(button);
            }

            e.Handled = true;
        }

        private void OnOpenSortLocalBranchMenu(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var isSortByName = repo.IsSortingLocalBranchByName;
                var byNameAsc = new MenuItem();
                byNameAsc.Header = App.Text("Repository.BranchSort.ByName");
                if (isSortByName)
                    byNameAsc.Icon = this.CreateMenuIcon("Icons.Check");
                byNameAsc.Click += (_, ev) =>
                {
                    if (!isSortByName)
                        repo.IsSortingLocalBranchByName = true;
                    ev.Handled = true;
                };

                var byCommitterDate = new MenuItem();
                byCommitterDate.Header = App.Text("Repository.BranchSort.ByCommitterDate");
                if (!isSortByName)
                    byCommitterDate.Icon = this.CreateMenuIcon("Icons.Check");
                byCommitterDate.Click += (_, ev) =>
                {
                    if (isSortByName)
                        repo.IsSortingLocalBranchByName = false;
                    ev.Handled = true;
                };

                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
                menu.Items.Add(byNameAsc);
                menu.Items.Add(byCommitterDate);
                AddBranchVisibilityMenuItems(menu, repo);
                menu.Open(button);
            }

            e.Handled = true;
        }

        private void OnOpenSortRemoteBranchMenu(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var isSortByName = repo.IsSortingRemoteBranchByName;
                var byNameAsc = new MenuItem();
                byNameAsc.Header = App.Text("Repository.BranchSort.ByName");
                if (isSortByName)
                    byNameAsc.Icon = this.CreateMenuIcon("Icons.Check");
                byNameAsc.Click += (_, ev) =>
                {
                    if (!isSortByName)
                        repo.IsSortingRemoteBranchByName = true;
                    ev.Handled = true;
                };

                var byCommitterDate = new MenuItem();
                byCommitterDate.Header = App.Text("Repository.BranchSort.ByCommitterDate");
                if (!isSortByName)
                    byCommitterDate.Icon = this.CreateMenuIcon("Icons.Check");
                byCommitterDate.Click += (_, ev) =>
                {
                    if (isSortByName)
                        repo.IsSortingRemoteBranchByName = false;
                    ev.Handled = true;
                };

                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
                menu.Items.Add(byNameAsc);
                menu.Items.Add(byCommitterDate);
                AddBranchVisibilityMenuItems(menu, repo);
                menu.Open(button);
            }

            e.Handled = true;
        }

        private void OnOpenSortTagMenu(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var isSortByName = repo.IsSortingTagsByName;
                var byCreatorDate = new MenuItem();
                byCreatorDate.Header = App.Text("Repository.Tags.OrderByCreatorDate");
                if (!isSortByName)
                    byCreatorDate.Icon = this.CreateMenuIcon("Icons.Check");
                byCreatorDate.Click += (_, ev) =>
                {
                    if (isSortByName)
                        repo.IsSortingTagsByName = false;
                    ev.Handled = true;
                };

                var byName = new MenuItem();
                byName.Header = App.Text("Repository.Tags.OrderByName");
                if (isSortByName)
                    byName.Icon = this.CreateMenuIcon("Icons.Check");
                byName.Click += (_, ev) =>
                {
                    if (!isSortByName)
                        repo.IsSortingTagsByName = true;
                    ev.Handled = true;
                };

                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
                menu.Items.Add(byName);
                menu.Items.Add(byCreatorDate);
                menu.Open(button);
            }

            e.Handled = true;
        }

        private async void OnPruneWorktrees(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                await repo.PruneWorktreesAsync();

            e.Handled = true;
        }

        private async void OnSkipInProgress(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                await repo.SkipMergeAsync();

            e.Handled = true;
        }

        private void OnResolveInProgress(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.SelectedViewIndex = 1;

            e.Handled = true;
        }

        private async void OnAbortInProgress(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                await repo.AbortMergeAsync();

            e.Handled = true;
        }

        private void OnOpenIncludedHistoryFiltersMenu(object sender, RoutedEventArgs e)
        {
            OpenHistoryFiltersMenuByMode(sender, e, Models.FilterMode.Included, "Repository.FilterCommits.Visible");
        }

        private void OnOpenExcludedHistoryFiltersMenu(object sender, RoutedEventArgs e)
        {
            OpenHistoryFiltersMenuByMode(sender, e, Models.FilterMode.Excluded, "Repository.FilterCommits.Invisible");
        }

        private void OnFoldVisibleBranchesInGraph(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.FoldVisibleBranchesInGraph();

            e.Handled = true;
        }

        private void OnUnfoldAllBranchesInGraph(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.UnfoldAllBranchesInGraph();

            e.Handled = true;
        }

        private void OpenHistoryFiltersMenuByMode(object sender, RoutedEventArgs e, Models.FilterMode mode, string titleKey)
        {
            if (sender is not Button button || DataContext is not ViewModels.Repository repo)
            {
                e.Handled = true;
                return;
            }

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

            var title = new MenuItem();
            title.Header = App.Text(titleKey);
            title.IsEnabled = false;
            menu.Items.Add(title);

            foreach (var filter in repo.UIStates.HistoryFilters)
            {
                if (filter.Mode != mode)
                    continue;

                var dump = filter;
                var item = new MenuItem();
                item.Header = BuildRemovableHistoryFilterHeader(dump);
                item.Icon = App.CreateMenuIcon(dump.Type switch
                {
                    Models.FilterType.Tag => "Icons.Tag",
                    Models.FilterType.Path => "Icons.Folder",
                    _ => mode == Models.FilterMode.Included ? "Icons.Filter" : "Icons.EyeClose",
                });
                item.Click += (_, ev) =>
                {
                    repo.RemoveHistoryFilter(dump);
                    ev.Handled = true;
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new MenuItem() { Header = "-" });

            var clear = new MenuItem();
            clear.Header = App.Text("Repository.ClearAllCommitsFilter");
            clear.Icon = App.CreateMenuIcon("Icons.RemoveAll");
            clear.Click += (_, ev) =>
            {
                repo.ClearHistoryFilters();
                ev.Handled = true;
            };
            menu.Items.Add(clear);

            menu.Open(button);
            e.Handled = true;
        }

        private async void OnOpenLocalIgnoreConfigure(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                var dialog = new RepositoryConfigure()
                {
                    DataContext = new ViewModels.RepositoryConfigure(repo),
                };

                dialog.OpenLocalIgnoreTab();
                await App.ShowDialog(dialog);
            }

            e.Handled = true;
        }

        private async void OnApplyLocalIgnoreRules(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                var config = new ViewModels.RepositoryConfigure(repo);
                await config.ApplyRepoLocalIgnoreRulesAsync();
            }

            e.Handled = true;
        }

        private void OnSetPresetBranchFilter(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                repo.OpenPresetBranchFilterEditor();
            }

            e.Handled = true;
        }

        private void OnShowAllBranches(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.ShowAllBranchesForSession();

            e.Handled = true;
        }

        private void OnToggleShowAllBranches(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.ToggleShowAllBranchesAndApplyGraphFilter();

            e.Handled = true;
        }

        private void OnUsePresetBranchFilter(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.UsePresetBranchFilterForSession();

            e.Handled = true;
        }

        private void OnApplyPresetBranchFilter(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                TryCommitPresetBranchExactNameInput(repo);
                TryCommitPresetBranchContainsPatternInput(repo);
                TryCommitPresetBranchExcludeNameInput(repo);
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnClearPresetBranchExactNames(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                repo.PresetBranchExactNames = string.Empty;
                repo.ApplyPresetBranchFilter();
            }

            PresetBranchExactNameInputBox.Text = string.Empty;
            e.Handled = true;
        }

        private void OnPresetBranchExactNameInputKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not ViewModels.Repository repo)
                return;

            if (HandlePresetBranchSuggestionSelectionKey(textBox, e))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab && TryAutocompletePresetBranchInput(textBox, true))
            {
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter && e.Key != Key.Tab && e.Key != Key.Space)
                return;

            if (e.Key != Key.Tab)
                TryAutocompletePresetBranchInput(textBox);

            var changed = TryCommitPresetBranchExactNameInput(repo, textBox.Text);
            if (changed)
                repo.ApplyPresetBranchFilter();

            textBox.Text = string.Empty;
            e.Handled = true;
        }

        private void OnRemovePresetBranchExactName(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string name } || DataContext is not ViewModels.Repository repo)
                return;

            var next = RemovePresetBranchRule(repo.PresetBranchExactNames, name);
            if (!next.Equals(repo.PresetBranchExactNames, StringComparison.Ordinal))
            {
                repo.PresetBranchExactNames = next;
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnClearPresetBranchContainsPatterns(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                repo.PresetBranchContainsPatterns = string.Empty;
                repo.ApplyPresetBranchFilter();
            }

            PresetBranchContainsPatternInputBox.Text = string.Empty;
            e.Handled = true;
        }

        private void OnClearPresetBranchExcludeNames(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                repo.PresetBranchExcludeNames = string.Empty;
                repo.ApplyPresetBranchFilter();
            }

            PresetBranchExcludeNameInputBox.Text = string.Empty;
            e.Handled = true;
        }

        private void OnPresetBranchInputTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            QueuePresetBranchInputSuggestions(textBox);
        }

        private void OnPresetBranchContainsPatternInputKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not ViewModels.Repository repo)
                return;

            if (HandlePresetBranchSuggestionSelectionKey(textBox, e))
            {
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter && e.Key != Key.Tab && e.Key != Key.Space)
                return;

            var changed = TryCommitPresetBranchContainsPatternInput(repo, textBox.Text);
            if (changed)
                repo.ApplyPresetBranchFilter();

            textBox.Text = string.Empty;
            e.Handled = true;
        }

        private void OnPresetBranchExcludeNameInputKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not ViewModels.Repository repo)
                return;

            if (HandlePresetBranchSuggestionSelectionKey(textBox, e))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab && TryAutocompletePresetBranchInput(textBox, true))
            {
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter && e.Key != Key.Tab && e.Key != Key.Space)
                return;

            if (e.Key != Key.Tab)
                TryAutocompletePresetBranchInput(textBox);

            var changed = TryCommitPresetBranchExcludeNameInput(repo, textBox.Text);
            if (changed)
                repo.ApplyPresetBranchFilter();

            textBox.Text = string.Empty;
            e.Handled = true;
        }

        private void OnPresetBranchFilterTextBoxDragOver(object sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(_dndPresetBranchNameFormat))
                e.DragEffects = DragDropEffects.Copy;
            else
                e.DragEffects = DragDropEffects.None;

            e.Handled = true;
        }

        private void OnDropToPresetBranchExactNames(object sender, DragEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            if (TryGetDroppedPresetBranchName(e, out var name))
            {
                repo.PresetBranchExactNames = AppendPresetBranchRule(repo.PresetBranchExactNames, name);
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnDropToPresetBranchExcludeNames(object sender, DragEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            if (TryGetDroppedPresetBranchName(e, out var name))
            {
                repo.PresetBranchExcludeNames = AppendPresetBranchRule(repo.PresetBranchExcludeNames, name);
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnDropToPresetBranchContainsPatterns(object sender, DragEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            if (TryGetDroppedPresetBranchName(e, out var name))
            {
                repo.PresetBranchContainsPatterns = AppendPresetBranchRule(repo.PresetBranchContainsPatterns, name);
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnRemovePresetBranchContainsPattern(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string pattern } || DataContext is not ViewModels.Repository repo)
                return;

            var next = RemovePresetBranchRule(repo.PresetBranchContainsPatterns, pattern);
            if (!next.Equals(repo.PresetBranchContainsPatterns, StringComparison.Ordinal))
            {
                repo.PresetBranchContainsPatterns = next;
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnRemovePresetBranchExcludeName(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string name } || DataContext is not ViewModels.Repository repo)
                return;

            var next = RemovePresetBranchRule(repo.PresetBranchExcludeNames, name);
            if (!next.Equals(repo.PresetBranchExcludeNames, StringComparison.Ordinal))
            {
                repo.PresetBranchExcludeNames = next;
                repo.ApplyPresetBranchFilter();
            }

            e.Handled = true;
        }

        private void OnOpenPresetBranchExactColorMenu(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.DataContext is not ViewModels.PresetBranchExactColorItem item ||
                DataContext is not ViewModels.Repository repo)
            {
                e.Handled = true;
                return;
            }

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

            foreach (var option in repo.PresetBranchColorOptions)
            {
                var color = option.Color;
                var colorItem = new MenuItem();
                colorItem.Header = BuildColorOptionHeader(option.Name, option.Brush);
                if (item.Color == color)
                    colorItem.Icon = App.CreateMenuIcon("Icons.Check");

                colorItem.Click += (_, ev) =>
                {
                    repo.UpdatePresetBranchExactNameColor(item.Name, color);
                    ev.Handled = true;
                };
                menu.Items.Add(colorItem);
            }

            menu.Open(button);
            e.Handled = true;
        }

        private void AddBranchVisibilityMenuItems(ContextMenu menu, ViewModels.Repository repo)
        {
            var setPreset = new MenuItem();
            setPreset.Header = App.Text("Repository.BranchesVisibility.SetPresetFilters");
            setPreset.Icon = App.CreateMenuIcon("Icons.Settings");
            setPreset.Click += (_, ev) =>
            {
                repo.OpenPresetBranchFilterEditor();
                ev.Handled = true;
            };

            var usePreset = new MenuItem();
            usePreset.Header = App.Text("Repository.BranchesVisibility.UsePresetFilter");
            if (!repo.IsShowingAllBranches)
                usePreset.Icon = App.CreateMenuIcon("Icons.Check");
            usePreset.Click += (_, ev) =>
            {
                repo.UsePresetBranchFilterForSession();
                ev.Handled = true;
            };

            var showAll = new MenuItem();
            showAll.Header = App.Text("Repository.BranchesVisibility.ShowAll");
            if (repo.IsShowingAllBranches)
                showAll.Icon = App.CreateMenuIcon("Icons.Check");
            showAll.Click += (_, ev) =>
            {
                repo.ShowAllBranchesForSession();
                ev.Handled = true;
            };

            var applyFilter = new MenuItem();
            applyFilter.Header = App.Text("Repository.BranchesVisibility.ApplyFilter");
            applyFilter.Icon = App.CreateMenuIcon("Icons.Filter");
            applyFilter.Click += (_, ev) =>
            {
                repo.ApplyPresetBranchFilter();
                ev.Handled = true;
            };

            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(setPreset);
            menu.Items.Add(usePreset);
            menu.Items.Add(showAll);
            menu.Items.Add(applyFilter);
        }

        private static string TrimHistoryFilterPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.Empty;
            if (pattern.StartsWith("refs/heads/", StringComparison.Ordinal))
                return pattern.Substring(11);
            if (pattern.StartsWith("refs/remotes/", StringComparison.Ordinal))
                return pattern.Substring(13);
            if (pattern.StartsWith("refs/tags/", StringComparison.Ordinal))
                return pattern.Substring(10);
            return pattern;
        }

        private static StackPanel BuildRemovableHistoryFilterHeader(Models.HistoryFilter filter)
        {
            var panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Spacing = 8;

            if (filter.Color != 0)
            {
                panel.Children.Add(new Border()
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                    Background = new SolidColorBrush(Color.FromUInt32(filter.Color)),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var name = filter.Type == Models.FilterType.Path
                ? $"path: {filter.Pattern}"
                : TrimHistoryFilterPattern(filter.Pattern);
            panel.Children.Add(new TextBlock() { Text = name });
            panel.Children.Add(new TextBlock() { Text = "x", Opacity = 0.7 });

            return panel;
        }

        private bool TryGetDroppedPresetBranchName(DragEventArgs e, out string name)
        {
            name = string.Empty;
            if (e.DataTransfer.TryGetValue(_dndPresetBranchNameFormat) is not { Length: > 0 } raw)
                return false;

            name = raw.Trim();
            return !string.IsNullOrEmpty(name);
        }

        private static string AppendPresetBranchRule(string current, string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return current ?? string.Empty;

            var target = branchName.Trim();
            if (string.IsNullOrWhiteSpace(current))
                return target;

            var normalized = current.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                if (line.Trim().Equals(target, StringComparison.Ordinal))
                    return current;
            }

            if (normalized.EndsWith('\n'))
                return normalized + target;

            return normalized + "\n" + target;
        }

        private static string AppendPresetBranchRules(string current, string rawInput)
        {
            var result = current ?? string.Empty;
            foreach (var token in ParsePresetBranchInputTokens(rawInput))
                result = AppendPresetBranchRule(result, token);
            return result;
        }

        private static string RemovePresetBranchRule(string current, string branchName)
        {
            var target = branchName?.Trim();
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(current))
                return current ?? string.Empty;

            var rules = ParsePresetBranchRules(current);
            rules.RemoveAll(x => x.Equals(target, StringComparison.Ordinal));
            return string.Join('\n', rules);
        }

        private static List<string> ParsePresetBranchRules(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var line in raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                var exists = false;
                foreach (var added in result)
                {
                    if (added.Equals(trimmed, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    result.Add(trimmed);
            }

            return result;
        }

        private static List<string> ParsePresetBranchInputTokens(string rawInput)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(rawInput))
                return result;

            var normalized = rawInput
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace('\t', '\n')
                .Replace(' ', '\n');
            foreach (var part in normalized.Split('\n'))
            {
                var token = part.Trim();
                if (!string.IsNullOrEmpty(token))
                    result.Add(token);
            }

            return result;
        }

        private bool TryCommitPresetBranchExactNameInput(ViewModels.Repository repo, string rawInput = null)
        {
            rawInput ??= PresetBranchExactNameInputBox.Text;
            var next = AppendPresetBranchRules(repo.PresetBranchExactNames, rawInput);
            if (next.Equals(repo.PresetBranchExactNames, StringComparison.Ordinal))
                return false;

            repo.PresetBranchExactNames = next;
            return true;
        }

        private bool TryCommitPresetBranchContainsPatternInput(ViewModels.Repository repo, string rawInput = null)
        {
            rawInput ??= PresetBranchContainsPatternInputBox.Text;
            var next = AppendPresetBranchRules(repo.PresetBranchContainsPatterns, rawInput);
            if (next.Equals(repo.PresetBranchContainsPatterns, StringComparison.Ordinal))
                return false;

            repo.PresetBranchContainsPatterns = next;
            return true;
        }

        private bool TryCommitPresetBranchExcludeNameInput(ViewModels.Repository repo, string rawInput = null)
        {
            rawInput ??= PresetBranchExcludeNameInputBox.Text;
            var next = AppendPresetBranchRules(repo.PresetBranchExcludeNames, rawInput);
            if (next.Equals(repo.PresetBranchExcludeNames, StringComparison.Ordinal))
                return false;

            repo.PresetBranchExcludeNames = next;
            return true;
        }

        private bool TryAutocompletePresetBranchInput(TextBox textBox, bool handledWhenAlreadyMatched = false)
        {
            if (DataContext is not ViewModels.Repository repo)
                return false;

            var suggestions = BuildPresetBranchInputSuggestions(repo, textBox);
            _presetBranchInputSuggestions[textBox] = suggestions;
            if (suggestions == null || suggestions.Count == 0)
            {
                _presetBranchInputSuggestionIndexes.Remove(textBox);
                return false;
            }

            var selected = GetPresetBranchSuggestionSelectedIndex(textBox, suggestions.Count);
            _presetBranchInputSuggestionIndexes[textBox] = selected;

            var target = suggestions[selected];
            if (string.IsNullOrEmpty(target))
                return false;

            var current = textBox.Text?.Trim() ?? string.Empty;
            if (target.Equals(current, StringComparison.Ordinal))
                return handledWhenAlreadyMatched;

            textBox.Text = target;
            textBox.CaretIndex = target.Length;
            QueuePresetBranchInputSuggestions(textBox);
            return true;
        }

        private bool HandlePresetBranchSuggestionSelectionKey(TextBox textBox, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
                return MovePresetBranchSuggestionSelection(textBox, +1, true, true);
            if (e.Key == Key.Up)
                return MovePresetBranchSuggestionSelection(textBox, -1, true, true);
            if (e.Key == Key.Escape)
            {
                ClosePresetBranchSuggestionMenu(textBox);
                _presetBranchInputSuggestionIndexes.Remove(textBox);
                return true;
            }

            return false;
        }

        private bool MovePresetBranchSuggestionSelection(TextBox textBox, int delta, bool ensureSuggestions, bool focusMenu = false)
        {
            if (ensureSuggestions)
            {
                if (!_presetBranchInputSuggestions.TryGetValue(textBox, out var existing) || existing == null || existing.Count == 0)
                {
                    var prepared = BuildAndShowPresetBranchInputSuggestions(textBox);
                    if (prepared == null || prepared.Count == 0)
                        return false;
                }
            }

            if (!_presetBranchInputSuggestions.TryGetValue(textBox, out var suggestions) || suggestions == null || suggestions.Count == 0)
                return false;

            var selected = GetPresetBranchSuggestionSelectedIndex(textBox, suggestions.Count);
            selected = (selected + delta + suggestions.Count) % suggestions.Count;
            _presetBranchInputSuggestionIndexes[textBox] = selected;

            // Avoid menu close/reopen on every arrow navigation to prevent flicker.
            if (_presetBranchSuggestionMenus.TryGetValue(textBox, out var openedMenu) && openedMenu.IsOpen)
            {
                if (focusMenu)
                    FocusPresetBranchSuggestionMenu(textBox);

                return true;
            }

            if (focusMenu)
                _presetBranchSuggestionMenuFocusRequested[textBox] = true;

            OpenPresetBranchSuggestionMenu(textBox, suggestions);
            if (focusMenu)
                FocusPresetBranchSuggestionMenu(textBox);
            return true;
        }

        private List<string> BuildAndShowPresetBranchInputSuggestions(TextBox textBox)
        {
            if (DataContext is not ViewModels.Repository repo)
            {
                ClosePresetBranchSuggestionMenu(textBox);
                _presetBranchInputSuggestions[textBox] = [];
                _presetBranchInputSuggestionIndexes.Remove(textBox);
                return [];
            }

            var suggestions = BuildPresetBranchInputSuggestions(repo, textBox);
            _presetBranchInputSuggestions[textBox] = suggestions;
            if (suggestions.Count == 0)
                _presetBranchInputSuggestionIndexes.Remove(textBox);
            else
                _presetBranchInputSuggestionIndexes[textBox] = GetPresetBranchSuggestionSelectedIndex(textBox, suggestions.Count);

            OpenPresetBranchSuggestionMenu(textBox, suggestions);
            return suggestions;
        }

        private int GetPresetBranchSuggestionSelectedIndex(TextBox textBox, int count)
        {
            if (count <= 0)
                return -1;

            if (_presetBranchInputSuggestionIndexes.TryGetValue(textBox, out var selected) && selected >= 0 && selected < count)
                return selected;

            return 0;
        }

        private HashSet<string> GetPresetBranchInputExistingRules(ViewModels.Repository repo, TextBox textBox)
        {
            var rules = textBox.Name switch
            {
                nameof(PresetBranchExactNameInputBox) => repo.PresetBranchExactNames,
                nameof(PresetBranchContainsPatternInputBox) => repo.PresetBranchContainsPatterns,
                nameof(PresetBranchExcludeNameInputBox) => repo.PresetBranchExcludeNames,
                _ => string.Empty,
            };
            return new HashSet<string>(ParsePresetBranchRules(rules), StringComparer.Ordinal);
        }

        private void OpenPresetBranchSuggestionMenu(TextBox textBox, List<string> suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
            {
                ClosePresetBranchSuggestionMenu(textBox);
                return;
            }

            var menu = new ContextMenu()
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
            };

            var selected = GetPresetBranchSuggestionSelectedIndex(textBox, suggestions.Count);
            for (var i = 0; i < suggestions.Count; i++)
            {
                var captured = suggestions[i];
                var idx = i;
                var item = new MenuItem()
                {
                    Header = captured,
                    Icon = App.CreateMenuIcon(idx == selected ? "Icons.Check" : "Icons.Branch"),
                };
                item.Click += (_, e) =>
                {
                    textBox.Text = captured;
                    textBox.CaretIndex = captured.Length;
                    textBox.Focus();
                    _presetBranchInputSuggestionIndexes[textBox] = idx;
                    QueuePresetBranchInputSuggestions(textBox);
                    e.Handled = true;
                };
                menu.Items.Add(item);
            }

            menu.Opened += (_, _) =>
            {
                if (_presetBranchSuggestionMenuFocusRequested.Remove(textBox))
                    FocusPresetBranchSuggestionMenu(textBox);
                else
                    RestorePresetBranchInputFocus(textBox);
            };
            menu.Closed += (_, _) =>
            {
                if (_presetBranchSuggestionMenus.TryGetValue(textBox, out var opened) && ReferenceEquals(opened, menu))
                    _presetBranchSuggestionMenus.Remove(textBox);

                _presetBranchSuggestionMenuFocusRequested.Remove(textBox);
            };

            ClosePresetBranchSuggestionMenu(textBox);
            _presetBranchSuggestionMenus[textBox] = menu;
            menu.Open(textBox);
        }

        private void RestorePresetBranchInputFocus(TextBox textBox)
        {
            // Keep typing focus in the input even while suggestion popup is visible.
            var caret = textBox.CaretIndex;
            Dispatcher.UIThread.Post(() =>
            {
                if (textBox.IsVisible)
                {
                    textBox.Focus();
                    textBox.CaretIndex = Math.Min(caret, textBox.Text?.Length ?? 0);
                }
            }, DispatcherPriority.Background);
        }

        private void FocusPresetBranchSuggestionMenu(TextBox textBox)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_presetBranchSuggestionMenus.TryGetValue(textBox, out var menu) || !menu.IsOpen)
                    return;

                if (menu.Items is not IEnumerable<object> allItems)
                    return;

                var items = allItems.OfType<MenuItem>().ToList();
                if (items.Count == 0)
                    return;

                var selected = GetPresetBranchSuggestionSelectedIndex(textBox, items.Count);
                if (selected < 0 || selected >= items.Count)
                    selected = 0;

                menu.Focus(NavigationMethod.Directional);
                items[selected].Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Input);
        }

        private void ClosePresetBranchSuggestionMenu(TextBox textBox)
        {
            if (_presetBranchSuggestionMenus.TryGetValue(textBox, out var menu))
                menu.Close();

            _presetBranchSuggestionMenuFocusRequested.Remove(textBox);
        }

        private void QueuePresetBranchInputSuggestions(TextBox textBox)
        {
            if (_presetBranchSuggestionDebouncers.TryGetValue(textBox, out var previous))
            {
                _presetBranchSuggestionDebouncers.Remove(textBox);
                try
                {
                    previous.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed by a completed debounce task.
                }
            }

            if (string.IsNullOrWhiteSpace(textBox.Text) || DataContext is not ViewModels.Repository)
            {
                ClosePresetBranchSuggestionMenu(textBox);
                _presetBranchInputSuggestions[textBox] = [];
                _presetBranchInputSuggestionIndexes.Remove(textBox);
                return;
            }

            _presetBranchSuggestionMenuFocusRequested.Remove(textBox);
            var cts = new CancellationTokenSource();
            _presetBranchSuggestionDebouncers[textBox] = cts;
            _ = DebouncedUpdatePresetBranchInputSuggestionsAsync(textBox, cts);
        }

        private async Task DebouncedUpdatePresetBranchInputSuggestionsAsync(TextBox textBox, CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                await Task.Delay(250, token);
                if (token.IsCancellationRequested)
                    return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || !textBox.IsFocused || DataContext is not ViewModels.Repository repo)
                        return;

                    BuildAndShowPresetBranchInputSuggestions(textBox);
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                // Expected when user keeps typing and previous debounce task gets canceled.
            }
            finally
            {
                if (_presetBranchSuggestionDebouncers.TryGetValue(textBox, out var current) && ReferenceEquals(current, cts))
                    _presetBranchSuggestionDebouncers.Remove(textBox);

                cts.Dispose();
            }
        }

        private List<string> BuildPresetBranchInputSuggestions(ViewModels.Repository repo, TextBox textBox)
        {
            var query = textBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
                return [];

            var existing = GetPresetBranchInputExistingRules(repo, textBox);
            var allBranchNames = repo.Branches
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.Ordinal)
                .Where(x => !existing.Contains(x))
                .ToList();
            var starts = allBranchNames
                .Where(x => x.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            var contains = allBranchNames
                .Where(x => !x.StartsWith(query, StringComparison.OrdinalIgnoreCase) && x.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            return starts.Concat(contains).Take(12).ToList();
        }
        private readonly DataFormat<string> _dndPresetBranchNameFormat = DataFormat.CreateStringApplicationFormat("sourcegit-dnd-branch-filter-name");
        private readonly Dictionary<TextBox, ContextMenu> _presetBranchSuggestionMenus = [];
        private readonly Dictionary<TextBox, List<string>> _presetBranchInputSuggestions = [];
        private readonly Dictionary<TextBox, int> _presetBranchInputSuggestionIndexes = [];
        private readonly Dictionary<TextBox, bool> _presetBranchSuggestionMenuFocusRequested = [];
        private readonly Dictionary<TextBox, CancellationTokenSource> _presetBranchSuggestionDebouncers = [];

        private static StackPanel BuildColorOptionHeader(string name, IBrush brush)
        {
            var panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Spacing = 8;

            panel.Children.Add(new Border()
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Background = brush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(new TextBlock() { Text = name });

            return panel;
        }

        private async void OnBisectCommand(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                DataContext is ViewModels.Repository { IsBisectCommandRunning: false } repo &&
                repo.CanCreatePopup())
                await repo.ExecBisectCommandAsync(button.Tag as string);

            e.Handled = true;
        }
    }
}
