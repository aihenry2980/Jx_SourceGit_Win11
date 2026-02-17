using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace SourceGit.Views
{
    public partial class Repository : UserControl
    {
        public Repository()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            UpdateLeftSidebarLayout();
        }

        private void OnSearchCommitPanelPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty && sender is Grid { IsVisible: true })
                TxtSearchCommitsBox.Focus();
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
            if (sender is ListBox { SelectedItem: Models.Worktree worktree } grid && DataContext is ViewModels.Repository repo)
            {
                var menu = new ContextMenu();

                var switchTo = new MenuItem();
                switchTo.Header = App.Text("Worktree.Open");
                switchTo.Icon = App.CreateMenuIcon("Icons.Folder.Open");
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
                    unlock.Icon = App.CreateMenuIcon("Icons.Unlock");
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
                    loc.Icon = App.CreateMenuIcon("Icons.Lock");
                    loc.Click += async (_, ev) =>
                    {
                        await repo.LockWorktreeAsync(worktree);
                        ev.Handled = true;
                    };
                    menu.Items.Add(loc);
                }

                var remove = new MenuItem();
                remove.Header = App.Text("Worktree.Remove");
                remove.Icon = App.CreateMenuIcon("Icons.Clear");
                remove.Click += (_, ev) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.RemoveWorktree(repo, worktree));
                    ev.Handled = true;
                };
                menu.Items.Add(remove);

                var copy = new MenuItem();
                copy.Header = App.Text("Worktree.CopyPath");
                copy.Icon = App.CreateMenuIcon("Icons.Copy");
                copy.Click += async (_, ev) =>
                {
                    await App.CopyTextAsync(worktree.FullPath);
                    ev.Handled = true;
                };
                menu.Items.Add(new MenuItem() { Header = "-" });
                menu.Items.Add(copy);
                menu.Open(grid);
            }

            e.Handled = true;
        }

        private void OnDoubleTappedWorktree(object sender, TappedEventArgs e)
        {
            if (sender is ListBox { SelectedItem: Models.Worktree worktree } && DataContext is ViewModels.Repository repo)
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

            var leftHeight = LeftSidebarGroups.Bounds.Height - 28.0 * 5 - 4;
            if (leftHeight <= 0)
                return;

            var localBranchRows = vm.IsLocalBranchGroupExpanded ? LocalBranchTree.Rows.Count : 0;
            var remoteBranchRows = vm.IsRemoteGroupExpanded ? RemoteBranchTree.Rows.Count : 0;
            var desiredBranches = (localBranchRows + remoteBranchRows) * 24.0;
            var desiredTag = vm.IsTagGroupExpanded ? 24.0 * TagsList.Rows : 0;
            var desiredSubmodule = vm.IsSubmoduleGroupExpanded ? 24.0 * SubmoduleList.Rows : 0;
            var desiredWorktree = vm.IsWorktreeGroupExpanded ? 24.0 * vm.Worktrees.Count : 0;
            var desiredOthers = desiredTag + desiredSubmodule + desiredWorktree;
            var hasOverflow = (desiredBranches + desiredOthers > leftHeight);

            if (vm.IsWorktreeGroupExpanded)
            {
                var height = desiredWorktree;
                if (hasOverflow)
                {
                    var test = leftHeight - desiredBranches - desiredTag - desiredSubmodule;
                    if (test < 0)
                        height = Math.Min(120, height);
                    else
                        height = Math.Max(120, test);
                }

                leftHeight -= height;
                WorktreeList.Height = height;
                hasOverflow = (desiredBranches + desiredTag + desiredSubmodule) > leftHeight;
            }

            if (vm.IsSubmoduleGroupExpanded)
            {
                var height = desiredSubmodule;
                if (hasOverflow)
                {
                    var test = leftHeight - desiredBranches - desiredTag;
                    if (test < 0)
                        height = Math.Min(120, height);
                    else
                        height = Math.Max(120, test);
                }

                leftHeight -= height;
                SubmoduleList.Height = height;
                hasOverflow = (desiredBranches + desiredTag) > leftHeight;
            }

            if (vm.IsTagGroupExpanded)
            {
                var height = desiredTag;
                if (hasOverflow)
                {
                    var test = leftHeight - desiredBranches;
                    if (test < 0)
                        height = Math.Min(120, height);
                    else
                        height = Math.Max(120, test);
                }

                leftHeight -= height;
                TagsList.Height = height;
            }

            if (leftHeight > 0 && desiredBranches > leftHeight)
            {
                var local = localBranchRows * 24.0;
                var remote = remoteBranchRows * 24.0;
                var half = leftHeight / 2;
                if (vm.IsLocalBranchGroupExpanded)
                {
                    if (vm.IsRemoteGroupExpanded)
                    {
                        if (local < half)
                        {
                            LocalBranchTree.Height = local;
                            RemoteBranchTree.Height = leftHeight - local;
                        }
                        else if (remote < half)
                        {
                            RemoteBranchTree.Height = remote;
                            LocalBranchTree.Height = leftHeight - remote;
                        }
                        else
                        {
                            LocalBranchTree.Height = half;
                            RemoteBranchTree.Height = half;
                        }
                    }
                    else
                    {
                        LocalBranchTree.Height = leftHeight;
                    }
                }
                else if (vm.IsRemoteGroupExpanded)
                {
                    RemoteBranchTree.Height = leftHeight;
                }
            }
            else
            {
                if (vm.IsLocalBranchGroupExpanded)
                {
                    var height = localBranchRows * 24;
                    LocalBranchTree.Height = height;
                }

                if (vm.IsRemoteGroupExpanded)
                {
                    var height = remoteBranchRows * 24;
                    RemoteBranchTree.Height = height;
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
            else if (e.Key == Key.Enter && SearchSuggestionBox.SelectedItem is string content)
            {
                repo.SearchCommitContext.Filter = content;
                TxtSearchCommitsBox.CaretIndex = content.Length;
                repo.SearchCommitContext.StartSearch();
                e.Handled = true;
            }
        }

        private void OnSearchSuggestionDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            var content = (sender as StackPanel)?.DataContext as string;
            if (!string.IsNullOrEmpty(content))
            {
                repo.SearchCommitContext.Filter = content;
                TxtSearchCommitsBox.CaretIndex = content.Length;
                repo.SearchCommitContext.StartSearch();
            }
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
                    horizontal.Icon = App.CreateMenuIcon("Icons.Check");
                horizontal.Click += (_, ev) =>
                {
                    pref.UseTwoColumnsLayoutInHistories = true;
                    ev.Handled = true;
                };

                var vertical = new MenuItem();
                vertical.Header = App.Text("Repository.HistoriesLayout.Vertical");
                if (!isHorizontal)
                    vertical.Icon = App.CreateMenuIcon("Icons.Check");
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
                    reflog.Icon = App.CreateMenuIcon("Icons.Check");
                reflog.Click += (_, ev) =>
                {
                    repo.ToggleHistoryShowFlag(Models.HistoryShowFlags.Reflog);
                    ev.Handled = true;
                };

                var firstParentOnly = new MenuItem();
                firstParentOnly.Header = App.Text("Repository.ShowFirstParentOnly");
                firstParentOnly.Tag = "--first-parent";
                if (repo.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.FirstParentOnly))
                    firstParentOnly.Icon = App.CreateMenuIcon("Icons.Check");
                firstParentOnly.Click += (_, ev) =>
                {
                    repo.ToggleHistoryShowFlag(Models.HistoryShowFlags.FirstParentOnly);
                    ev.Handled = true;
                };

                var simplifyByDecoration = new MenuItem();
                simplifyByDecoration.Header = App.Text("Repository.ShowDecoratedCommitsOnly");
                simplifyByDecoration.Tag = "--simplify-by-decoration";
                if (repo.HistoryShowFlags.HasFlag(Models.HistoryShowFlags.SimplifyByDecoration))
                    simplifyByDecoration.Icon = App.CreateMenuIcon("Icons.Check");
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
                    dateOrder.Icon = App.CreateMenuIcon("Icons.Check");
                dateOrder.Click += (_, ev) =>
                {
                    repo.EnableTopoOrderInHistory = false;
                    ev.Handled = true;
                };

                var topoOrder = new MenuItem();
                topoOrder.Header = App.Text("Repository.HistoriesOrder.Topo");
                topoOrder.Tag = "--topo-order";
                if (repo.EnableTopoOrderInHistory)
                    topoOrder.Icon = App.CreateMenuIcon("Icons.Check");
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
                    byNameAsc.Icon = App.CreateMenuIcon("Icons.Check");
                byNameAsc.Click += (_, ev) =>
                {
                    if (!isSortByName)
                        repo.IsSortingLocalBranchByName = true;
                    ev.Handled = true;
                };

                var byCommitterDate = new MenuItem();
                byCommitterDate.Header = App.Text("Repository.BranchSort.ByCommitterDate");
                if (!isSortByName)
                    byCommitterDate.Icon = App.CreateMenuIcon("Icons.Check");
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
                    byNameAsc.Icon = App.CreateMenuIcon("Icons.Check");
                byNameAsc.Click += (_, ev) =>
                {
                    if (!isSortByName)
                        repo.IsSortingRemoteBranchByName = true;
                    ev.Handled = true;
                };

                var byCommitterDate = new MenuItem();
                byCommitterDate.Header = App.Text("Repository.BranchSort.ByCommitterDate");
                if (!isSortByName)
                    byCommitterDate.Icon = App.CreateMenuIcon("Icons.Check");
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
                    byCreatorDate.Icon = App.CreateMenuIcon("Icons.Check");
                byCreatorDate.Click += (_, ev) =>
                {
                    if (isSortByName)
                        repo.IsSortingTagsByName = false;
                    ev.Handled = true;
                };

                var byName = new MenuItem();
                byName.Header = App.Text("Repository.Tags.OrderByName");
                if (isSortByName)
                    byName.Icon = App.CreateMenuIcon("Icons.Check");
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

        private void OnOpenHistoryFiltersMenu(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not ViewModels.Repository repo)
            {
                e.Handled = true;
                return;
            }

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

            var title = new MenuItem();
            title.Header = App.Text("Repository.FilterCommits");
            title.IsEnabled = false;
            menu.Items.Add(title);

            foreach (var filter in repo.UIStates.HistoryFilters)
            {
                var dump = filter;
                var item = new MenuItem();
                item.Header = BuildRemovableHistoryFilterHeader(dump.Pattern, dump.Color);
                item.Icon = App.CreateMenuIcon(dump.IsBranch ? "Icons.Branch" : "Icons.Tag");
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
                repo.ApplyPresetBranchFilter();

            e.Handled = true;
        }

        private void OnClearPresetBranchExactNames(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.PresetBranchExactNames = string.Empty;

            e.Handled = true;
        }

        private void OnClearPresetBranchContainsPatterns(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.PresetBranchContainsPatterns = string.Empty;

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

        private static StackPanel BuildRemovableHistoryFilterHeader(string pattern, uint color)
        {
            var panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Spacing = 8;

            if (color != 0)
            {
                panel.Children.Add(new Border()
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                    Background = new SolidColorBrush(Color.FromUInt32(color)),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            panel.Children.Add(new TextBlock() { Text = TrimHistoryFilterPattern(pattern) });
            panel.Children.Add(new TextBlock() { Text = "x", Opacity = 0.7 });

            return panel;
        }

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
