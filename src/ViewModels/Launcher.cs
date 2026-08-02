using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Launcher : ObservableObject
    {
        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public AvaloniaList<LauncherPage> Pages
        {
            get;
            private set;
        }

        public Workspace ActiveWorkspace
        {
            get => _activeWorkspace;
            private set => SetProperty(ref _activeWorkspace, value);
        }

        public LauncherPage ActivePage
        {
            get => _activePage;
            set
            {
                if (_activePage == value)
                    return;

                UnsubscribeActivePageEvents(_activePage);
                if (SetProperty(ref _activePage, value))
                {
                    SubscribeActivePageEvents(_activePage);
                    OnPropertyChanged(nameof(ActivePageTitleBarBackground));
                    PostActivePageChanged();
                }
            }
        }

        public IBrush ActivePageTitleBarBackground
        {
            get
            {
                if (_activePage?.Node?.Bookmark is int bookmark &&
                    Models.Bookmarks.Get(bookmark) is ISolidColorBrush solid)
                {
                    var c = solid.Color;
                    if (Application.Current?.ActualThemeVariant == ThemeVariant.Dark)
                    {
                        byte Darken(byte v) => (byte)Math.Clamp((int)Math.Round(v * 0.42 + 0x18 * 0.58), 0, 255);
                        return new SolidColorBrush(Color.FromArgb(0xFF, Darken(c.R), Darken(c.G), Darken(c.B)));
                    }

                    byte Lighten(byte v) => (byte)Math.Clamp((int)Math.Round(v * 0.34 + 0xFF * 0.66), 0, 255);
                    return new SolidColorBrush(Color.FromArgb(0xFF, Lighten(c.R), Lighten(c.G), Lighten(c.B)));
                }

                if (Application.Current?.Resources.TryGetValue("Brush.TitleBar", out var brush) == true)
                    return brush as IBrush;

                return Brushes.Transparent;
            }
        }

        public ICommandPalette CommandPalette
        {
            get => _commandPalette;
            set => SetProperty(ref _commandPalette, value);
        }

        public Models.Version NewVersion
        {
            get => _newVersion;
            set => SetProperty(ref _newVersion, value);
        }

        public Launcher(string startupRepo)
        {
            Models.Notification.Raised += DispatchNotification;
            _ignoreIndexChange = true;

            ActiveWorkspace = Preferences.Instance.GetActiveWorkspace();
            Pages = new AvaloniaList<LauncherPage>();
            AddNewTab();

            _ = RestoreWorkspaceAsync(startupRepo);
        }

        private async Task RestoreWorkspaceAsync(string startupRepo)
        {
            try
            {
                var repos = ActiveWorkspace.Repositories.ToArray();
                foreach (var repo in repos)
                    await OpenRepositoryInTabAsync(repo, null);

                _ignoreIndexChange = false;

                if (!await TryOpenRepositoryFromPathAsync(startupRepo))
                {
                    var activeIdx = ActiveWorkspace.ActiveIdx;
                    if (activeIdx > 0 && activeIdx < Pages.Count)
                        ActivePage = Pages[activeIdx];
                    else
                        ActivePage = Pages[0];
                }
            }
            catch (Exception ex)
            {
                App.LogException(ex);
                _ignoreIndexChange = false;
            }

            PostActivePageChanged();
        }

        public bool TryOpenRepositoryFromPath(string repo)
        {
            if (string.IsNullOrEmpty(repo) || !Directory.Exists(repo))
                return false;

            _ = TryOpenRepositoryFromPathAndReportAsync(repo);
            return true;
        }

        private async Task TryOpenRepositoryFromPathAndReportAsync(string repo)
        {
            try
            {
                await TryOpenRepositoryFromPathAsync(repo);
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private async Task<bool> TryOpenRepositoryFromPathAsync(string repo)
        {
            if (!string.IsNullOrEmpty(repo) && Directory.Exists(repo))
            {
                var isBare = await new Commands.IsBareRepository(repo).GetResultAsync();
                if (isBare)
                {
                    var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(repo, null, false);
                    Welcome.Instance.Refresh();
                    await OpenRepositoryInTabAsync(node, null);
                    return true;
                }

                var test = await new Commands.QueryRepositoryRootPath(repo).GetResultAsync();
                if (test.IsSuccess && !string.IsNullOrEmpty(test.StdOut))
                {
                    var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(test.StdOut.Trim(), null, false);
                    Welcome.Instance.Refresh();
                    await OpenRepositoryInTabAsync(node, null);
                    return true;
                }
                else
                {
                    if (ActivePage is not { Data: Welcome { }, Popup: null })
                        AddNewTab();

                    ActivePage.Popup = new Init(ActivePage.Node.Id, repo, null, 0, test.StdErr ?? "Unknown error occurred while opening the repository.");
                    return true;
                }
            }

            return false;
        }

        public void CloseAll()
        {
            _ignoreIndexChange = true;

            foreach (var one in Pages)
                CloseRepositoryInTab(one, false, false);

            _ignoreIndexChange = false;
        }

        public void SwitchWorkspace(Workspace to)
        {
            if (to == null || to.IsActive)
                return;

            _ignoreIndexChange = true;

            var pref = Preferences.Instance;
            foreach (var w in pref.Workspaces)
                w.IsActive = false;

            ActiveWorkspace = to;
            to.IsActive = true;

            foreach (var one in Pages)
                CloseRepositoryInTab(one, false, false);

            Pages.Clear();
            AddNewTab();

            var repos = to.Repositories.ToArray();
            foreach (var repo in repos)
                OpenRepositoryInTab(repo, null);

            var activeIdx = to.ActiveIdx;
            if (activeIdx >= 0 && activeIdx < Pages.Count)
                ActivePage = Pages[activeIdx];
            else
                ActivePage = Pages[0];

            _ignoreIndexChange = false;
            PostActivePageChanged();
            Preferences.Instance.Save();
            GC.Collect();
        }

        public void AddNewTab()
        {
            var page = new LauncherPage();
            Pages.Add(page);
            ActivePage = page;
        }

        public void OpenCommandPalette(ICommandPalette palette)
        {
            CommandPalette = palette;
        }

        public void CancelCommandPalette()
        {
            CommandPalette = null;
        }

        public void MoveTab(LauncherPage from, LauncherPage to)
        {
            _ignoreIndexChange = true;

            var fromIdx = Pages.IndexOf(from);
            var toIdx = Pages.IndexOf(to);
            Pages.Move(fromIdx, toIdx);

            _activeWorkspace.Repositories.Clear();
            foreach (var p in Pages)
            {
                if (p.Data is Repository r)
                    _activeWorkspace.Repositories.Add(r.FullPath);
            }

            _ignoreIndexChange = false;
            ActivePage = from;
        }

        public void GotoNextTab()
        {
            if (Pages.Count == 1)
                return;

            var activeIdx = Pages.IndexOf(_activePage);
            var nextIdx = (activeIdx + 1) % Pages.Count;
            ActivePage = Pages[nextIdx];
        }

        public void GotoPrevTab()
        {
            if (Pages.Count == 1)
                return;

            var activeIdx = Pages.IndexOf(_activePage);
            var prevIdx = activeIdx == 0 ? Pages.Count - 1 : activeIdx - 1;
            ActivePage = Pages[prevIdx];
        }

        public void CloseTab(LauncherPage page)
        {
            if (Pages.Count == 1)
            {
                var last = Pages[0];
                if (last.Data is Repository repo)
                {
                    RememberClosedRepository(repo, 0);

                    _activeWorkspace.Repositories.Clear();
                    _activeWorkspace.ActiveIdx = 0;

                    if (last.Node.IsUnmanaged)
                        last.Node.SaveMinimalInfo(repo.GitDir);
                    repo.Close();

                    Welcome.Instance.ClearSearchFilter();
                    last.Node = new RepositoryNode() { Id = Guid.NewGuid().ToString() };
                    last.Data = Welcome.Instance;
                    last.Popup?.Cleanup();
                    last.Popup = null;

                    PostActivePageChanged();
                    GC.Collect();
                }
                else
                {
                    App.Quit(0);
                }

                return;
            }

            page ??= _activePage;

            var removeIdx = Pages.IndexOf(page);
            var activeIdx = Pages.IndexOf(_activePage);
            if (removeIdx == activeIdx)
                ActivePage = Pages[removeIdx > 0 ? removeIdx - 1 : removeIdx + 1];

            CloseRepositoryInTab(page);
            Pages.RemoveAt(removeIdx);
            GC.Collect();
        }

        public void ReopenLastClosedTab()
        {
            _ = ReopenLastClosedTabAndReportAsync();
        }

        public void OpenDroppedFolders(string[] paths)
        {
            _ = OpenDroppedFoldersAndReportAsync(paths);
        }

        public void CloseOtherTabs()
        {
            if (Pages.Count == 1)
                return;

            _ignoreIndexChange = true;

            var id = ActivePage.Node.Id;
            foreach (var one in Pages)
            {
                if (one.Node.Id != id)
                    CloseRepositoryInTab(one);
            }

            Pages = new AvaloniaList<LauncherPage> { ActivePage };
            OnPropertyChanged(nameof(Pages));

            _activeWorkspace.ActiveIdx = 0;
            _ignoreIndexChange = false;
            GC.Collect();
        }

        public void CloseRightTabs()
        {
            _ignoreIndexChange = true;

            var endIdx = Pages.IndexOf(ActivePage);
            for (var i = Pages.Count - 1; i > endIdx; i--)
            {
                CloseRepositoryInTab(Pages[i]);
                Pages.Remove(Pages[i]);
            }

            _ignoreIndexChange = false;
            GC.Collect();
        }

        public void OpenRepositoryInTab(string repo, LauncherPage page)
        {
            _ = OpenRepositoryInTabAndReportAsync(repo, page);
        }

        private async Task OpenRepositoryInTabAndReportAsync(string repo, LauncherPage page)
        {
            try
            {
                await OpenRepositoryInTabAsync(repo, page);
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private Task OpenRepositoryInTabAsync(string repo, LauncherPage page)
        {
            var normalizedPath = repo.Replace('\\', '/').TrimEnd('/');
            var node = Preferences.Instance.FindNode(normalizedPath) ?? new RepositoryNode
            {
                Id = normalizedPath,
                Name = Path.GetFileName(normalizedPath),
                Bookmark = 0,
                IsRepository = true,
                IsUnmanaged = true
            };

            return OpenRepositoryInTabAsync(node, page);
        }

        public void OpenRepositoryInTab(RepositoryNode node, LauncherPage page)
        {
            OpenRepositoryInTab(node, page, null, null);
        }

        public void OpenRepositoryInTab(
            RepositoryNode node,
            LauncherPage page,
            string superProjectSubmoduleSHA = null,
            LauncherPage insertAfter = null)
        {
            _ = OpenRepositoryInTabAndReportAsync(node, page, superProjectSubmoduleSHA, insertAfter);
        }

        private async Task OpenRepositoryInTabAndReportAsync(
            RepositoryNode node,
            LauncherPage page,
            string superProjectSubmoduleSHA = null,
            LauncherPage insertAfter = null)
        {
            try
            {
                await OpenRepositoryInTabAsync(node, page, superProjectSubmoduleSHA, insertAfter);
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private async Task OpenRepositoryInTabAsync(
            RepositoryNode node,
            LauncherPage page,
            string superProjectSubmoduleSHA = null,
            LauncherPage insertAfter = null)
        {
            foreach (var one in Pages)
            {
                if (one.Node.Id == node.Id)
                {
                    if (!string.IsNullOrWhiteSpace(superProjectSubmoduleSHA) && one.Data is Repository existed)
                        existed.UpdateSuperProjectSubmoduleSHA(superProjectSubmoduleSHA);

                    ActivePage = one;
                    return;
                }
            }

            if (!Directory.Exists(node.Id))
            {
                ActivePage.Notifications.Add(new Models.Notification
                {
                    Group = node.Id,
                    Message = "Repository does NOT exist any more. Please remove it.",
                    IsError = true,
                });
                return;
            }

            var isBare = await new Commands.IsBareRepository(node.Id).GetResultAsync();
            var gitDir = isBare ? node.Id : await GetRepositoryGitDirAsync(node.Id);
            if (string.IsNullOrEmpty(gitDir))
            {
                ActivePage.Notifications.Add(new Models.Notification
                {
                    Group = node.Id,
                    Message = "Given path is not a valid git repository!",
                    IsError = true,
                });
                return;
            }

            if (node.IsUnmanaged)
                node.LoadMinimalInfo(gitDir);

            var repo = new Repository(isBare, node.Id, gitDir, superProjectSubmoduleSHA);
            repo.Open();

            if (page == null)
            {
                if (_activePage == null || _activePage.Node.IsRepository)
                {
                    page = new LauncherPage(node, repo);
                    var insertIdx = -1;
                    if (insertAfter != null)
                        insertIdx = Pages.IndexOf(insertAfter);

                    if (insertIdx >= 0 && insertIdx < Pages.Count)
                        Pages.Insert(insertIdx + 1, page);
                    else
                        Pages.Add(page);
                }
                else
                {
                    page = _activePage;
                    page.Node = node;
                    page.Data = repo;
                }
            }
            else
            {
                page.Node = node;
                page.Data = repo;
            }

            repo.NotifyAccentColorChanged();

            _activeWorkspace.Repositories.Clear();
            foreach (var p in Pages)
            {
                if (p.Data is Repository r)
                    _activeWorkspace.Repositories.Add(r.FullPath);
            }

            if (_activePage == page)
                PostActivePageChanged();
            else
                ActivePage = page;
        }

        private void DispatchNotification(Models.Notification notification)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(() => DispatchNotification(notification));
                return;
            }

            if (string.IsNullOrEmpty(notification.Group))
            {
                _activePage?.Notifications.Add(notification);
                return;
            }

            foreach (var page in Pages)
            {
                var id = page.Node.Id.Replace('\\', '/').TrimEnd('/');
                if (id.Equals(notification.Group, StringComparison.OrdinalIgnoreCase))
                {
                    page.Notifications.Add(notification);
                    return;
                }
            }

            _activePage?.Notifications.Add(notification);
        }

        public void NotifyTitleBarBrushChanged()
        {
            OnPropertyChanged(nameof(ActivePageTitleBarBackground));
        }

        private async Task<string> GetRepositoryGitDirAsync(string repo)
        {
            var fullpath = Path.Combine(repo, ".git");
            if (Directory.Exists(fullpath))
            {
                if (Directory.Exists(Path.Combine(fullpath, "refs")) &&
                    Directory.Exists(Path.Combine(fullpath, "objects")) &&
                    File.Exists(Path.Combine(fullpath, "HEAD")))
                    return fullpath;

                return null;
            }

            if (File.Exists(fullpath))
            {
                var redirect = File.ReadAllText(fullpath).Trim();
                if (redirect.StartsWith("gitdir: ", StringComparison.Ordinal))
                    redirect = redirect.Substring(8);

                if (!Path.IsPathRooted(redirect))
                    redirect = Path.GetFullPath(Path.Combine(repo, redirect));

                if (Directory.Exists(redirect))
                    return redirect;

                return null;
            }

            return await new Commands.QueryGitDir(repo).GetResultAsync();
        }

        private async Task ReopenLastClosedTabAndReportAsync()
        {
            try
            {
                await ReopenLastClosedTabAsync();
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private async Task OpenDroppedFoldersAndReportAsync(string[] paths)
        {
            try
            {
                await OpenDroppedFoldersAsync(paths);
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private async Task OpenDroppedFoldersAsync(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return;

            if (!Preferences.Instance.IsGitConfigured())
            {
                Models.Notification.Send(null, App.Text("NotConfigured"), true);
                return;
            }

            var fileCount = 0;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (!Directory.Exists(path))
                {
                    if (File.Exists(path))
                        fileCount++;

                    continue;
                }

                await TryOpenRepositoryFolderAsTabAsync(path, "Dropped folder is not a git repository");
            }

            if (fileCount > 0)
            {
                ActivePage.Notifications.Add(new Models.Notification
                {
                    Message = fileCount == 1
                        ? "Dropped item is a file. Please drop a git repository folder."
                        : $"Dropped {fileCount} files. Please drop git repository folders.",
                    IsError = true,
                });
            }
        }

        private async Task<bool> TryOpenRepositoryFolderAsTabAsync(string path, string invalidMessage)
        {
            if (!Preferences.Instance.IsGitConfigured())
            {
                Models.Notification.Send(null, App.Text("NotConfigured"), true);
                return false;
            }

            var isBare = await new Commands.IsBareRepository(path).GetResultAsync();
            if (isBare)
            {
                var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(path, null, false);
                Welcome.Instance.Refresh();
                await OpenRepositoryInTabAsync(node, null);
                return true;
            }

            var test = await new Commands.QueryRepositoryRootPath(path).GetResultAsync();
            if (test.IsSuccess && !string.IsNullOrWhiteSpace(test.StdOut))
            {
                var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(test.StdOut.Trim(), null, false);
                Welcome.Instance.Refresh();
                await OpenRepositoryInTabAsync(node, null);
                return true;
            }

            ActivePage.Notifications.Add(new Models.Notification
            {
                Message = $"{invalidMessage}: {path}",
                IsError = true,
            });
            return false;
        }

        private async Task ReopenLastClosedTabAsync()
        {
            while (_recentlyClosedRepositories.Count > 0)
            {
                var lastIdx = _recentlyClosedRepositories.Count - 1;
                var repo = _recentlyClosedRepositories[lastIdx];
                _recentlyClosedRepositories.RemoveAt(lastIdx);

                if (string.IsNullOrWhiteSpace(repo.Repository) || !Directory.Exists(repo.Repository))
                    continue;

                foreach (var page in Pages)
                {
                    if (page.Data is Repository opened &&
                        string.Equals(NormalizeRepositoryPath(opened.FullPath), repo.Repository, StringComparison.OrdinalIgnoreCase))
                    {
                        ActivePage = page;
                        return;
                    }
                }

                await OpenRepositoryInTabAsync(repo.Repository, null);

                var reopened = ActivePage;
                MoveReopenedPageToOriginalIndex(reopened, repo.Index);
                FocusReopenedPage(reopened);
                return;
            }
        }

        private void RememberClosedRepository(Repository repo, int index)
        {
            if (repo == null || string.IsNullOrWhiteSpace(repo.FullPath))
                return;

            var path = NormalizeRepositoryPath(repo.FullPath);
            _recentlyClosedRepositories.RemoveAll(x => string.Equals(x.Repository, path, StringComparison.OrdinalIgnoreCase));
            _recentlyClosedRepositories.Add(new ClosedRepositoryTab(path, Math.Max(0, index)));

            while (_recentlyClosedRepositories.Count > 30)
                _recentlyClosedRepositories.RemoveAt(0);
        }

        private void MoveReopenedPageToOriginalIndex(LauncherPage page, int index)
        {
            var fromIdx = Pages.IndexOf(page);
            if (fromIdx < 0)
                return;

            var toIdx = Math.Clamp(index, 0, Pages.Count - 1);
            if (fromIdx == toIdx)
                return;

            _ignoreIndexChange = true;
            Pages.Move(fromIdx, toIdx);

            _activeWorkspace.Repositories.Clear();
            foreach (var p in Pages)
            {
                if (p.Data is Repository r)
                    _activeWorkspace.Repositories.Add(r.FullPath);
            }

            _ignoreIndexChange = false;
            PostActivePageChanged();
        }

        private void FocusReopenedPage(LauncherPage page)
        {
            if (page == null || !Pages.Contains(page))
                return;

            if (_activePage != page)
                ActivePage = page;
            else
                OnPropertyChanged(nameof(ActivePage));
        }

        private static string NormalizeRepositoryPath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private void CloseRepositoryInTab(LauncherPage page, bool removeFromWorkspace = true, bool rememberClosedRepository = true)
        {
            if (page.Data is Repository repo)
            {
                if (rememberClosedRepository)
                    RememberClosedRepository(repo, Pages.IndexOf(page));

                if (removeFromWorkspace)
                    _activeWorkspace.Repositories.Remove(repo.FullPath);

                if (page.Node.IsUnmanaged)
                    page.Node.SaveMinimalInfo(repo.GitDir);

                repo.Close();
            }

            page.Popup?.Cleanup();
            page.Popup = null;
            page.Data = null;
        }

        private void PostActivePageChanged()
        {
            if (_ignoreIndexChange)
                return;

            if (_activePage is { Data: Repository repo })
                _activeWorkspace.ActiveIdx = _activeWorkspace.Repositories.IndexOf(repo.FullPath);

            var builder = new StringBuilder(512);
            builder.Append(string.IsNullOrEmpty(_activePage.Node.Name) ? "Repositories" : _activePage.Node.Name);

            var workspaces = Preferences.Instance.Workspaces;
            if (workspaces.Count == 0 || workspaces.Count > 1 || workspaces[0] != _activeWorkspace)
                builder.Append(" - ").Append(_activeWorkspace.Name);

            Title = builder.ToString();
            CommandPalette = null;
        }

        private void SubscribeActivePageEvents(LauncherPage page)
        {
            if (page == null)
                return;

            page.PropertyChanged += OnActivePagePropertyChanged;
            _subscribedNode = page.Node;
            if (_subscribedNode != null)
                _subscribedNode.PropertyChanged += OnActivePageNodePropertyChanged;
        }

        private void UnsubscribeActivePageEvents(LauncherPage page)
        {
            if (page == null)
                return;

            page.PropertyChanged -= OnActivePagePropertyChanged;
            if (_subscribedNode != null)
            {
                _subscribedNode.PropertyChanged -= OnActivePageNodePropertyChanged;
                _subscribedNode = null;
            }
        }

        private void OnActivePagePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not LauncherPage page || e.PropertyName != nameof(LauncherPage.Node))
                return;

            if (_subscribedNode != null)
                _subscribedNode.PropertyChanged -= OnActivePageNodePropertyChanged;

            _subscribedNode = page.Node;
            if (_subscribedNode != null)
                _subscribedNode.PropertyChanged += OnActivePageNodePropertyChanged;

            OnPropertyChanged(nameof(ActivePageTitleBarBackground));
        }

        private void OnActivePageNodePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RepositoryNode.Bookmark))
                OnPropertyChanged(nameof(ActivePageTitleBarBackground));
        }

        private Workspace _activeWorkspace;
        private LauncherPage _activePage;
        private bool _ignoreIndexChange;
        private string _title = string.Empty;
        private ICommandPalette _commandPalette;
        private RepositoryNode _subscribedNode;
        private Models.Version _newVersion = null;
        private readonly List<ClosedRepositoryTab> _recentlyClosedRepositories = new();

        private readonly record struct ClosedRepositoryTab(string Repository, int Index);
    }
}
