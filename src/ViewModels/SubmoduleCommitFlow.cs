using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class SubmoduleCommitFlow : ObservableObject
    {
        private const int MAX_SCAN_DEPTH = 4;
        private const int MAX_SCAN_NODES = 200;

        public List<SubmoduleCommitFlowNode> Nodes
        {
            get => _nodes;
            private set
            {
                if (SetProperty(ref _nodes, value))
                    NotifyCommitPlanChanged();
            }
        }

        public SubmoduleCommitFlowNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    OnPropertyChanged(nameof(HasSelectedNode));
                    OnPropertyChanged(nameof(SelectedNodeTitle));
                    OnPropertyChanged(nameof(CanCommitSelectedNode));
                    NotifyCommitAndPushStateChanged();
                    OnPropertyChanged(nameof(CanUndoSelectedNodeCommit));
                    NotifyCommitPlanChanged();
                    _ = LoadSelectedNodeChangesAsync();
                }
            }
        }

        public bool HasSelectedNode => _selectedNode != null;

        public string SelectedNodeTitle
        {
            get
            {
                if (_selectedNode == null)
                    return "Select a repository or submodule";

                return string.IsNullOrEmpty(_selectedNode.DisplayPath) ? "root" : _selectedNode.DisplayPath;
            }
        }

        public List<Models.Change> Changes
        {
            get => _changes;
            private set
            {
                if (SetProperty(ref _changes, value))
                {
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(CanCommitSelectedNode));
                    NotifyCommitAndPushStateChanged();
                    NotifyCommitPlanChanged();
                }
            }
        }

        public List<Models.Change> SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetProperty(ref _selectedChanges, value))
                    UpdateDetailContext();
            }
        }

        public bool HasChanges => _changes.Count > 0;

        public object DetailContext
        {
            get => _detailContext;
            private set => SetProperty(ref _detailContext, value);
        }

        public string CommitMessage
        {
            get => _commitMessage;
            set
            {
                if (SetProperty(ref _commitMessage, value))
                {
                    OnPropertyChanged(nameof(CanCommitSelectedNode));
                    NotifyCommitAndPushStateChanged();
                    NotifyCommitPlanChanged();
                }
            }
        }

        public bool CanUndoSelectedNodeCommit =>
            _selectedNode != null &&
            !_isLoadingChanges &&
            !_isCommitting &&
            _undoCommits.ContainsKey(_selectedNode.DisplayPath);

        public bool CanCommitAndPushSelectedNode => CanCommitSelectedNode && _selectedNode.HasPushRemote;
        public IBrush CommitAndPushButtonBackground => CanCommitAndPushSelectedNode ? _commitAndPushEnabledBackground : _commitAndPushDisabledBackground;
        public IBrush CommitAndPushButtonForeground => CanCommitAndPushSelectedNode ? Brushes.White : _commitAndPushDisabledForeground;
        public string CommitAndPushButtonText => GetNextActionNodeAfterSelected() != null ? "Commit & Push -> Next" : "Commit & Push";
        public string CommitButtonText => GetNextActionNodeAfterSelected() != null ? "Stage All & Commit -> Next" : "Stage All & Commit";
        public string CommitPlanPreview
        {
            get
            {
                var node = _selectedNode;
                if (node == null)
                    return "Select a repository or submodule to preview the commit commands.";

                var builder = new StringBuilder();
                builder.Append("Target: ").Append(node.DisplayPath)
                    .Append("  |  Branch: ").Append(node.Branch)
                    .Append("  |  Changes: ").Append(_changes.Count);

                builder.AppendLine();
                builder.Append("Message: ").Append(ToSingleLine(_commitMessage));

                builder.AppendLine();
                builder.Append("Commit: stage listed changes; git commit --file=<message>");

                if (node.HasPushRemote)
                {
                    builder.AppendLine();
                    builder.Append("Commit & Push adds: git push --progress --verbose ");
                    if (node.Children.Count > 0)
                        builder.Append("--recurse-submodules=check ");
                    if (node.SetPushTracking)
                        builder.Append("-u ");
                    builder.Append(node.PushRemote).Append(' ').Append(node.Branch).Append(':').Append(node.PushRemoteBranch);
                }
                else
                {
                    builder.AppendLine();
                    builder.Append("Commit & Push: unavailable because no remote target was found.");
                }

                var next = GetNextActionNodeAfterSelected();
                if (next != null)
                {
                    builder.AppendLine();
                    builder.Append("After success: select next node ").Append(next.DisplayPath).Append('.');
                }

                return builder.ToString();
            }
        }

        public bool IsLoading
        {
            get => _isScanning || _isLoadingChanges;
        }

        public bool IsCommitting
        {
            get => _isCommitting;
            private set
            {
                if (SetProperty(ref _isCommitting, value))
                {
                    OnPropertyChanged(nameof(CanCommitSelectedNode));
                    NotifyCommitAndPushStateChanged();
                    OnPropertyChanged(nameof(CanUndoSelectedNodeCommit));
                    NotifyCommitPlanChanged();
                }
            }
        }

        public bool CanCommitSelectedNode =>
            _selectedNode != null &&
            _changes.Count > 0 &&
            !string.IsNullOrWhiteSpace(_commitMessage) &&
            !_isLoadingChanges &&
            !_isCommitting;

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string ToastMessage
        {
            get => _toastMessage;
            private set => SetProperty(ref _toastMessage, value);
        }

        public double ToastOpacity
        {
            get => _toastOpacity;
            private set => SetProperty(ref _toastOpacity, value);
        }

        public bool IsToastVisible
        {
            get => _isToastVisible;
            private set => SetProperty(ref _isToastVisible, value);
        }

        public IBrush ToastBackground
        {
            get => _toastBackground;
            private set => SetProperty(ref _toastBackground, value);
        }

        public SubmoduleCommitFlow(Repository repo)
        {
            _repo = repo;
        }

        public void Activate()
        {
            if (_nodes.Count == 0)
                _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            var version = Interlocked.Increment(ref _version);
            SetScanning(true);
            Summary = "Discovering submodule tree...";
            _changeCache.Clear();

            try
            {
                var build = await Task.Run(BuildNodeTreeAsync).ConfigureAwait(false);
                if (version != _version)
                    return;

                var nodes = build.Nodes;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _allNodes = nodes;
                    Nodes = BuildVisibleNodes(nodes);
                    SelectedNode = Nodes.FirstOrDefault();
                    Summary = $"Scanning status 0/{nodes.Count}...";
                    if (!string.IsNullOrWhiteSpace(build.Warning))
                        ShowFlowToast(build.Warning);
                });

                _ = ScanNodeStatusesAsync(version, nodes);
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Summary = $"Failed to scan submodules: {ex.Message}";
                    SetScanning(false);
                    _repo.SendNotification(Summary, true);
                });
            }
        }

        public async Task CommitSelectedNodeAsync()
        {
            await CommitSelectedNodeAsync(false);
        }

        public async Task CommitAndPushSelectedNodeAsync()
        {
            await CommitSelectedNodeAsync(true);
        }

        private async Task CommitSelectedNodeAsync(bool pushAfterCommit)
        {
            if (!CanCommitSelectedNode)
                return;

            var node = _selectedNode;
            if (pushAfterCommit && !node.HasPushRemote)
                return;

            var message = _commitMessage.Trim();
            Interlocked.Increment(ref _version);
            SetScanning(false);
            IsCommitting = true;
            Summary = pushAfterCommit ? $"Committing and pushing {node.DisplayPath}..." : $"Committing {node.DisplayPath}...";

            using var lockWatcher = _repo.LockWatcher();
            var log = _repo.CreateLog(pushAfterCommit ? $"Commit Flow - commit & push {node.DisplayPath}" : $"Commit Flow - {node.DisplayPath}");
            var succ = false;
            var committed = false;
            var pushed = false;
            var beforeHead = string.Empty;
            var afterHead = string.Empty;
            try
            {
                var commitChanges = _changes.ToList();
                beforeHead = await new Commands.QueryRevisionByRefName(node.RepoPath, "HEAD").GetResultAsync().ConfigureAwait(false);
                var pathspecFile = await WriteCommitFlowPathspecAsync(commitChanges).ConfigureAwait(false);
                try
                {
                    succ = await new Commands.Add(node.RepoPath, pathspecFile)
                        .Use(log)
                        .ExecAsync()
                        .ConfigureAwait(false);
                }
                finally
                {
                    DeleteTempFile(pathspecFile);
                }

                if (succ)
                {
                    succ = await new Commands.Commit(node.RepoPath, message, false, false, false, false)
                        .Use(log)
                        .RunAsync()
                        .ConfigureAwait(false);
                }

                committed = succ;
                if (committed)
                    afterHead = await new Commands.QueryRevisionByRefName(node.RepoPath, "HEAD").GetResultAsync().ConfigureAwait(false);

                if (committed && pushAfterCommit)
                {
                    succ = await new Commands.Push(
                            node.RepoPath,
                            node.Branch,
                            node.PushRemote,
                            node.PushRemoteBranch,
                            false,
                            node.Children.Count > 0,
                            node.SetPushTracking,
                            false)
                        .Use(log)
                        .RunAsync()
                        .ConfigureAwait(false);
                    pushed = succ;
                }
            }
            finally
            {
                log.Complete();
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IsCommitting = false;
                if (committed)
                {
                    _donePaths.Add(node.DisplayPath);
                    if (!string.IsNullOrWhiteSpace(beforeHead) &&
                        !string.IsNullOrWhiteSpace(afterHead) &&
                        !beforeHead.Equals(afterHead, StringComparison.Ordinal))
                    {
                        _undoCommits[node.DisplayPath] = new UndoCommit(beforeHead, afterHead, message, pushed, node.PushRemote, node.PushRemoteBranch);
                    }

                    node.State = SubmoduleCommitFlowNodeState.Done;
                    node.ChangeCount = 0;
                    node.FileChangeCount = 0;
                    node.SubmodulePointerChangeCount = 0;
                    CommitMessage = string.Empty;
                    OnPropertyChanged(nameof(CanUndoSelectedNodeCommit));
                    if (pushAfterCommit && !pushed)
                        ShowFlowToast($"Committed {node.DisplayPath}, but push failed.", true);
                    else
                        ShowFlowToast(pushed ? $"Committed and pushed {node.DisplayPath}." : $"Committed {node.DisplayPath}.");

                    _repo.RefreshBranches();
                    _repo.RefreshCommits();
                    _repo.RefreshSubmodules(true);
                    _repo.RefreshWorkingCopyChanges();
                    _selectNextActionAfterScan = true;
                    await RefreshAsync();
                }
                else
                {
                    Summary = $"Commit failed for {node.DisplayPath}. Review repository logs.";
                    _repo.SendNotification(Summary, true);
                }
            });
        }

        public async Task UndoSelectedNodeCommitAsync()
        {
            if (!CanUndoSelectedNodeCommit)
                return;

            var node = _selectedNode;
            var undo = _undoCommits[node.DisplayPath];
            var message =
                $"Undo last Commit Flow commit for '{node.DisplayPath}'?\n\n" +
                (undo.WasPushed
                    ? $"This first force-with-lease pushes {undo.Remote}/{undo.RemoteBranch} back to {ShortenSHA(undo.BeforeHead)}, then runs git reset --mixed {ShortenSHA(undo.BeforeHead)}."
                    : $"This runs git reset --mixed {ShortenSHA(undo.BeforeHead)} and returns the committed content to local changes.");
            var confirmed = await App.AskConfirmAsync(message, Models.ConfirmButtonType.YesNo);
            if (!confirmed)
                return;

            Interlocked.Increment(ref _version);
            SetScanning(false);
            IsCommitting = true;
            Summary = $"Undoing commit for {node.DisplayPath}...";

            using var lockWatcher = _repo.LockWatcher();
            var log = _repo.CreateLog($"Commit Flow - undo {node.DisplayPath}");
            var succ = false;
            var error = string.Empty;
            try
            {
                var currentHead = await new Commands.QueryRevisionByRefName(node.RepoPath, "HEAD").GetResultAsync().ConfigureAwait(false);
                if (!undo.AfterHead.Equals(currentHead, StringComparison.Ordinal))
                {
                    error = $"HEAD has moved in {node.DisplayPath}. Undo canceled.";
                }
                else
                {
                    if (undo.WasPushed)
                    {
                        succ = await new Commands.Push(
                                node.RepoPath,
                                undo.BeforeHead,
                                undo.Remote,
                                undo.RemoteBranch,
                                false,
                                false,
                                false,
                                true)
                            .Use(log)
                            .RunAsync()
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        succ = true;
                    }

                    if (succ)
                    {
                        succ = await new Commands.Reset(node.RepoPath, undo.BeforeHead, "--mixed")
                            .Use(log)
                            .ExecAsync()
                            .ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                log.Complete();
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IsCommitting = false;
                if (succ)
                {
                    _donePaths.Remove(node.DisplayPath);
                    _undoCommits.Remove(node.DisplayPath);
                    _changeCache.Remove(node.DisplayPath);
                    CommitMessage = string.Empty;
                    OnPropertyChanged(nameof(CanUndoSelectedNodeCommit));
                    ShowFlowToast($"Undid commit for {node.DisplayPath}.");
                    _repo.RefreshBranches();
                    _repo.RefreshCommits();
                    _repo.RefreshSubmodules(true);
                    _repo.RefreshWorkingCopyChanges();
                    await RefreshAsync();
                }
                else
                {
                    Summary = string.IsNullOrWhiteSpace(error) ? $"Undo failed for {node.DisplayPath}. Review repository logs." : error;
                    _repo.SendNotification(Summary, true);
                }
            });
        }

        private async Task<NodeBuildResult> BuildNodeTreeAsync()
        {
            var nodesByPath = new Dictionary<string, SubmoduleCommitFlowNode>(StringComparer.Ordinal);
            var root = new SubmoduleCommitFlowNode()
            {
                Name = Path.GetFileName(_repo.FullPath.TrimEnd('/', '\\')),
                DisplayPath = "root",
                RepoPath = _repo.FullPath,
                Depth = 0,
            };
            nodesByPath[string.Empty] = root;

            var requestedDepth = Preferences.Instance.RecursiveSubmoduleDisplayDepth;
            var depth = Math.Clamp(requestedDepth, 1, MAX_SCAN_DEPTH);
            var queryDepth = requestedDepth > MAX_SCAN_DEPTH ? MAX_SCAN_DEPTH + 1 : depth;
            var maxSubmodules = MAX_SCAN_NODES - 1;
            var submodules = await new Commands.QuerySubmodules(_repo.FullPath, queryDepth, false, maxSubmodules + 1)
                .GetResultAsync()
                .ConfigureAwait(false);
            var limitedSubmodules = submodules
                .Select(x => new { Submodule = x, Path = NormalizePath(x.Path) })
                .Where(x => GetSubmoduleDepth(x.Path) <= MAX_SCAN_DEPTH)
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Take(maxSubmodules)
                .Select(x => x.Submodule)
                .ToList();
            var wasDepthLimited = submodules.Exists(x => GetSubmoduleDepth(NormalizePath(x.Path)) > MAX_SCAN_DEPTH);
            var wasNodeLimited = submodules.Count(x => GetSubmoduleDepth(NormalizePath(x.Path)) <= MAX_SCAN_DEPTH) > maxSubmodules;

            foreach (var submodule in limitedSubmodules)
            {
                var path = NormalizePath(submodule.Path);
                if (string.IsNullOrWhiteSpace(path) || nodesByPath.ContainsKey(path))
                    continue;

                var parentPath = GetParentPath(path, nodesByPath);
                var node = new SubmoduleCommitFlowNode()
                {
                    Name = Path.GetFileName(path),
                    DisplayPath = path,
                    ParentDisplayPath = string.IsNullOrEmpty(parentPath) ? "root" : parentPath,
                    SubmodulePathInParent = path.Substring(parentPath.Length).TrimStart('/'),
                    RepoPath = Native.OS.GetAbsPath(_repo.FullPath, path),
                    Depth = path.Count(c => c == '/') + 1,
                };

                nodesByPath[path] = node;
                nodesByPath[parentPath].Children.Add(node);
            }

            var nodes = new List<SubmoduleCommitFlowNode>();
            Flatten(root, nodes);
            var warnings = new List<string>();
            if (wasDepthLimited)
                warnings.Add($"depth limited to {MAX_SCAN_DEPTH}");
            if (wasNodeLimited)
                warnings.Add($"first {maxSubmodules} submodules scanned");

            var warning = warnings.Count == 0
                ? string.Empty
                : $"Submodule Commit Flow scan was limited: {string.Join(", ", warnings)}.";

            return new NodeBuildResult(nodes, warning);
        }

        private async Task ScanNodeStatusesAsync(int version, List<SubmoduleCommitFlowNode> nodes)
        {
            var processed = 0;
            var maxParallel = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
            using var gate = new SemaphoreSlim(maxParallel);
            var tasks = nodes.Select(async node =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                NodeStatus status;
                try
                {
                    status = await QueryNodeStatusAsync(node).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }

                var done = Interlocked.Increment(ref processed);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (version != _version)
                        return;

                    ApplyNodeStatus(node, status);
                    if (SelectedNode == node)
                    {
                        NotifyCommitAndPushStateChanged();
                        NotifyCommitPlanChanged();
                    }

                    _changeCache[node.DisplayPath] = status.Changes;
                    if (done % 5 == 0 || status.State != SubmoduleCommitFlowNodeState.Clean || done == nodes.Count)
                    {
                        Nodes = BuildVisibleNodes(nodes);
                        var next = PickNextActionNode(Nodes);
                        if (next != null && (SelectedNode == null || SelectedNode.State is SubmoduleCommitFlowNodeState.Clean or SubmoduleCommitFlowNodeState.Scanning))
                            SelectedNode = next;
                        else if (SelectedNode == null || !Nodes.Contains(SelectedNode))
                            SelectedNode = next ?? Nodes.FirstOrDefault();
                    }

                    if (SelectedNode == node && !IsSameChangeList(_changes, status.Changes))
                        ApplySelectedNodeChanges(node, status.Changes);

                    Summary = $"Scanning status {done}/{nodes.Count}...";
                });
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _version)
                    return;

                Nodes = BuildVisibleNodes(nodes);
                var next = PickNextActionNode(Nodes);
                if (_selectNextActionAfterScan)
                {
                    _selectNextActionAfterScan = false;
                    SelectedNode = next ?? Nodes.FirstOrDefault();
                }
                else if (SelectedNode == null || !Nodes.Contains(SelectedNode) || SelectedNode.State is SubmoduleCommitFlowNodeState.Clean or SubmoduleCommitFlowNodeState.Scanning)
                {
                    SelectedNode = next ?? Nodes.FirstOrDefault();
                }

                Summary = BuildSummary(nodes);
                SetScanning(false);
            });
        }

        private async Task<NodeStatus> QueryNodeStatusAsync(SubmoduleCommitFlowNode node)
        {
            if (!Directory.Exists(node.RepoPath))
                return new NodeStatus("missing", string.Empty, string.Empty, string.Empty, string.Empty, false, 0, 0, 0, SubmoduleCommitFlowNodeState.Error, []);

            var changes = await new Commands.QueryLocalChanges(node.RepoPath, true, true, false).GetResultAsync().ConfigureAwait(false);
            changes = FilterGeneratedUntrackedChanges(changes);
            changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));
            UpdateChangeKinds(node, changes, out var fileChangeCount, out var submodulePointerChangeCount);
            var state = ResolveNodeState(node, changes);

            if (state == SubmoduleCommitFlowNodeState.Clean && node.Depth > 0)
                return new NodeStatus("--", string.Empty, string.Empty, string.Empty, string.Empty, false, changes.Count, fileChangeCount, submodulePointerChangeCount, state, changes);

            var branch = await new Commands.QueryCurrentBranch(node.RepoPath).GetResultAsync().ConfigureAwait(false);
            var head = await new Commands.QueryRevisionByRefName(node.RepoPath, "HEAD").GetResultAsync().ConfigureAwait(false);
            var upstream = string.IsNullOrWhiteSpace(branch)
                ? string.Empty
                : await new Commands.QueryBranchUpstream(node.RepoPath).GetResultAsync().ConfigureAwait(false);
            var pushRemote = string.Empty;
            var pushRemoteBranch = string.Empty;
            var setPushTracking = false;
            if (!string.IsNullOrWhiteSpace(branch))
            {
                var remotes = await new Commands.QueryRemotes(node.RepoPath).GetResultAsync().ConfigureAwait(false);
                if (TrySplitUpstream(upstream, out pushRemote, out pushRemoteBranch) &&
                    IsPushServerRemote(remotes, pushRemote))
                {
                    setPushTracking = false;
                }
                else
                {
                    pushRemote = PickPushRemote(remotes);
                    pushRemoteBranch = string.IsNullOrWhiteSpace(pushRemote) ? string.Empty : branch;
                    setPushTracking = !string.IsNullOrWhiteSpace(pushRemote);
                }
            }

            return new NodeStatus(
                string.IsNullOrWhiteSpace(branch) ? "(detached)" : branch,
                head ?? string.Empty,
                upstream,
                pushRemote,
                pushRemoteBranch,
                setPushTracking,
                changes.Count,
                fileChangeCount,
                submodulePointerChangeCount,
                state,
                changes);
        }

        private static void ApplyNodeStatus(SubmoduleCommitFlowNode node, NodeStatus status)
        {
            node.Branch = status.Branch;
            node.Head = status.Head;
            node.Upstream = status.Upstream;
            node.PushRemote = status.PushRemote;
            node.PushRemoteBranch = status.PushRemoteBranch;
            node.SetPushTracking = status.SetPushTracking;
            node.ChangeCount = status.ChangeCount;
            node.FileChangeCount = status.FileChangeCount;
            node.SubmodulePointerChangeCount = status.SubmodulePointerChangeCount;
            node.State = status.State;
        }

        private async Task LoadSelectedNodeChangesAsync()
        {
            var requestId = Interlocked.Increment(ref _loadChangesVersion);
            var node = _selectedNode;
            DetailContext = null;
            SelectedChanges = [];
            Changes = [];

            if (node == null || !Directory.Exists(node.RepoPath))
                return;

            SetLoadingChanges(true);
            if (!_changeCache.TryGetValue(node.DisplayPath, out var changes))
            {
                try
                {
                    changes = await new Commands.QueryLocalChanges(node.RepoPath, true, true, false).GetResultAsync().ConfigureAwait(false);
                    changes = FilterGeneratedUntrackedChanges(changes);
                    changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));
                    UpdateChangeKinds(node, changes, out _, out _);
                    _changeCache[node.DisplayPath] = changes;
                }
                catch
                {
                    changes = [];
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_selectedNode != node || requestId != _loadChangesVersion)
                {
                    SetLoadingChanges(false);
                    return;
                }

                ApplySelectedNodeChanges(node, changes);
                SetLoadingChanges(false);
            });
        }

        private void ApplySelectedNodeChanges(SubmoduleCommitFlowNode node, List<Models.Change> changes)
        {
            UpdateChangeKinds(node, changes, out var fileChangeCount, out var submodulePointerChangeCount);
            Changes = changes;
            node.ChangeCount = changes.Count;
            node.FileChangeCount = fileChangeCount;
            node.SubmodulePointerChangeCount = submodulePointerChangeCount;
            node.State = ResolveNodeState(node, changes);
            if (node.State == SubmoduleCommitFlowNodeState.Clean && HasActionableDescendant(node))
                node.State = SubmoduleCommitFlowNodeState.HasChildChanges;
            CommitMessage = BuildDefaultCommitMessage(node, changes);
            NotifyCommitPlanChanged();
            Summary = BuildSummary(_allNodes.Count > 0 ? _allNodes : _nodes);
        }

        private void UpdateDetailContext()
        {
            var node = _selectedNode;
            if (node == null || _selectedChanges is not { Count: 1 })
            {
                DetailContext = null;
                return;
            }

            var change = _selectedChanges[0];
            var isUnstaged = change.WorkTree != Models.ChangeState.None;
            DetailContext = new DiffContext(node.RepoPath, new Models.DiffOption(change, isUnstaged), _detailContext as DiffContext);
        }

        private SubmoduleCommitFlowNodeState ResolveNodeState(SubmoduleCommitFlowNode node, List<Models.Change> changes)
        {
            if (changes.Count == 0)
                return _donePaths.Contains(node.DisplayPath) ? SubmoduleCommitFlowNodeState.Done : SubmoduleCommitFlowNodeState.Clean;

            var onlySubmodulePointerChanges = changes.All(x => IsSubmodulePointerChange(node, x));
            if (onlySubmodulePointerChanges && node.Children.Count > 0)
                return SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges;

            return changes.Exists(x => x.IsSubmodulePointerChange)
                ? SubmoduleCommitFlowNodeState.HasMixedChanges
                : SubmoduleCommitFlowNodeState.HasChanges;
        }

        private static string BuildDefaultCommitMessage(SubmoduleCommitFlowNode node, List<Models.Change> changes)
        {
            if (changes.Count == 0)
                return string.Empty;

            var submodulePointerChanges = changes
                .Where(x => IsSubmodulePointerChange(node, x))
                .Select(x => x.Path)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            string message;
            if (submodulePointerChanges.Count == changes.Count && submodulePointerChanges.Count > 0)
            {
                message = BuildSubmodulePointerCommitMessage(node, submodulePointerChanges);
                return PrefixIssueTagFromBranchOrChildren(node, submodulePointerChanges, message);
            }
            else
            {
                message = $"Update {node.Name}";
            }

            return PrefixIssueTagFromBranch(node.Branch, message);
        }

        private static string BuildSubmodulePointerCommitMessage(SubmoduleCommitFlowNode node, List<string> paths)
        {
            if (paths.Count == 1)
            {
                var path = paths[0];
                var child = node.Children.Find(x => x.SubmodulePathInParent.Equals(path, StringComparison.Ordinal));
                return child != null && !string.IsNullOrWhiteSpace(child.Head)
                    ? $"Peg {path} SPP to {child.HeadShort}"
                    : $"Peg {path} SPP";
            }

            if (paths.Count <= 3)
                return $"Peg {string.Join(", ", paths)} SPP";

            return $"Peg {paths.Count} submodule SPP updates";
        }

        private static string PrefixIssueTagFromBranchOrChildren(SubmoduleCommitFlowNode node, List<string> paths, string message)
        {
            var prefixed = PrefixIssueTagFromBranch(node.Branch, message);
            if (!prefixed.Equals(message, StringComparison.Ordinal))
                return prefixed;

            var issue = ExtractIssueTagFromSubmodulePointerChildren(node, paths);
            if (string.IsNullOrWhiteSpace(issue))
                return message;

            var prefix = $"[{issue}]";
            return message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? message : $"{prefix} {message}";
        }

        private static string ExtractIssueTagFromSubmodulePointerChildren(SubmoduleCommitFlowNode node, List<string> paths)
        {
            var issue = string.Empty;
            foreach (var path in paths)
            {
                var child = node.Children.Find(x => x.SubmodulePathInParent.Equals(path, StringComparison.Ordinal));
                if (child == null)
                    continue;

                var childIssue = ExtractIssueTagFromBranch(child.Branch);
                if (string.IsNullOrWhiteSpace(childIssue))
                    continue;

                if (string.IsNullOrWhiteSpace(issue))
                {
                    issue = childIssue;
                    continue;
                }

                if (!issue.Equals(childIssue, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
            }

            return issue;
        }

        private static string PrefixIssueTagFromBranch(string branch, string message)
        {
            if (string.IsNullOrWhiteSpace(branch) ||
                string.IsNullOrWhiteSpace(message) ||
                branch is "(detached)" or "--")
                return message;

            var issue = ExtractIssueTagFromBranch(branch);
            if (string.IsNullOrWhiteSpace(issue))
                return message;

            var prefix = $"[{issue}]";
            return message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? message : $"{prefix} {message}";
        }

        private static string ExtractIssueTagFromBranch(string branch)
        {
            var pattern = Preferences.Instance.CommitMessageIssueTagPattern?.Trim();
            if (string.IsNullOrWhiteSpace(pattern))
                return string.Empty;

            var issue = MatchBranchIssuePattern(branch, pattern);
            if (string.IsNullOrWhiteSpace(issue))
                issue = MatchBranchIssuePattern(branch, $@"(?<![A-Za-z0-9]){Regex.Escape(pattern)}-\d+(?![A-Za-z0-9])");
            else
                issue = ExpandIssueTagSeed(branch, issue);

            return issue.Trim();
        }

        private static string ExpandIssueTagSeed(string branch, string seed)
        {
            seed = seed.Trim();
            if (string.IsNullOrWhiteSpace(seed) || Regex.IsMatch(seed, @"-\d+$", RegexOptions.CultureInvariant))
                return seed;

            var expanded = MatchBranchIssuePattern(branch, $@"(?<![A-Za-z0-9]){Regex.Escape(seed)}-\d+(?![A-Za-z0-9])");
            return string.IsNullOrWhiteSpace(expanded) ? seed : expanded;
        }

        private static string MatchBranchIssuePattern(string branch, string pattern)
        {
            try
            {
                var match = Regex.Match(branch, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (!match.Success)
                    return string.Empty;

                if (match.Groups.Count > 1)
                {
                    for (var i = 1; i < match.Groups.Count; i++)
                    {
                        if (match.Groups[i].Success && !string.IsNullOrWhiteSpace(match.Groups[i].Value))
                            return match.Groups[i].Value;
                    }
                }

                return match.Value;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (RegexMatchTimeoutException)
            {
                return string.Empty;
            }
        }

        private static bool IsSubmodulePointerChange(SubmoduleCommitFlowNode node, Models.Change change)
        {
            if (change.WorkTree == Models.ChangeState.Untracked)
                return false;

            return node.Children.Exists(x => x.SubmodulePathInParent.Equals(change.Path, StringComparison.Ordinal));
        }

        private static List<Models.Change> FilterGeneratedUntrackedChanges(List<Models.Change> changes)
        {
            var rules = GetGeneratedFileFilterRules();
            if (rules.Count == 0 || changes.Count == 0)
                return changes;

            return changes
                .Where(x => !IsGeneratedUntrackedChange(x, rules))
                .ToList();
        }

        private static bool IsGeneratedUntrackedChange(Models.Change change, List<string> rules)
        {
            if (change.WorkTree != Models.ChangeState.Untracked)
                return false;

            var path = NormalizePath(change.Path);
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return rules.Exists(x => MatchesGeneratedFileRule(path, x));
        }

        private static List<string> GetGeneratedFileFilterRules()
        {
            var raw = Preferences.Instance.CommitFlowGeneratedFileFilters ?? string.Empty;
            return raw
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => NormalizePath(x.Trim()))
                .Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal))
                .ToList();
        }

        private static bool MatchesGeneratedFileRule(string path, string rule)
        {
            rule = rule.TrimStart('/');
            if (rule.Length == 0)
                return false;

            if (rule.EndsWith('/'))
            {
                var dir = rule.TrimEnd('/');
                return path.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith($"{dir}/", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains($"/{dir}/", StringComparison.OrdinalIgnoreCase);
            }

            var fileName = GetFileNameFromNormalizedPath(path);
            if (rule.Contains('*') || rule.Contains('?'))
            {
                return rule.Contains('/')
                    ? WildcardMatch(path, rule)
                    : WildcardMatch(fileName, rule);
            }

            return rule.Contains('/')
                ? path.Equals(rule, StringComparison.OrdinalIgnoreCase)
                : fileName.Equals(rule, StringComparison.OrdinalIgnoreCase);
        }

        private static bool WildcardMatch(string text, string pattern)
        {
            try
            {
                var regex = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private static string GetFileNameFromNormalizedPath(string path)
        {
            var idx = path.LastIndexOf('/');
            return idx >= 0 ? path[(idx + 1)..] : path;
        }

        private static async Task<string> WriteCommitFlowPathspecAsync(List<Models.Change> changes)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in changes)
            {
                if (!string.IsNullOrWhiteSpace(change.OriginalPath))
                    paths.Add(change.OriginalPath);
                if (!string.IsNullOrWhiteSpace(change.Path))
                    paths.Add(change.Path);
            }

            var pathspecFile = Path.GetTempFileName();
            await File.WriteAllLinesAsync(pathspecFile, paths).ConfigureAwait(false);
            return pathspecFile;
        }

        private static void DeleteTempFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        private static void UpdateChangeKinds(SubmoduleCommitFlowNode node, List<Models.Change> changes, out int fileChangeCount, out int submodulePointerChangeCount)
        {
            fileChangeCount = 0;
            submodulePointerChangeCount = 0;

            foreach (var change in changes)
            {
                change.IsSubmodulePointerChange = IsSubmodulePointerChange(node, change);
                if (change.IsSubmodulePointerChange)
                    submodulePointerChangeCount++;
                else
                    fileChangeCount++;
            }
        }

        private static bool IsSameChangeList(List<Models.Change> left, List<Models.Change> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (!left[i].Path.Equals(right[i].Path, StringComparison.Ordinal) ||
                    left[i].Index != right[i].Index ||
                    left[i].WorkTree != right[i].WorkTree)
                    return false;
            }

            return true;
        }

        private static SubmoduleCommitFlowNode PickNextActionNode(IEnumerable<SubmoduleCommitFlowNode> nodes)
        {
            return nodes
                .Where(x => x.State is SubmoduleCommitFlowNodeState.HasChanges or SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges or SubmoduleCommitFlowNodeState.HasMixedChanges)
                .OrderByDescending(x => x.Depth)
                .FirstOrDefault();
        }

        private SubmoduleCommitFlowNode GetNextActionNodeAfterSelected()
        {
            if (_nodes.Count == 0)
                return null;

            return PickNextActionNode(_nodes.Where(x => !ReferenceEquals(x, _selectedNode)));
        }

        private static string BuildSummary(List<SubmoduleCommitFlowNode> nodes)
        {
            var dirty = nodes.Count(x => x.State is SubmoduleCommitFlowNodeState.HasChanges or SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges or SubmoduleCommitFlowNodeState.HasMixedChanges);
            var done = nodes.Count(x => x.State == SubmoduleCommitFlowNodeState.Done);
            var scanning = nodes.Count(x => x.State == SubmoduleCommitFlowNodeState.Scanning);
            if (scanning > 0)
                return $"Scanning status {nodes.Count - scanning}/{nodes.Count}...";

            return dirty == 0
                ? $"All clean. {done} node(s) committed in this flow."
                : $"{dirty} node(s) need commits. Work deepest first, then peg upward.";
        }

        private static List<SubmoduleCommitFlowNode> BuildVisibleNodes(List<SubmoduleCommitFlowNode> nodes)
        {
            var root = nodes.FirstOrDefault();
            if (root == null)
                return [];

            var visible = new List<SubmoduleCommitFlowNode>();
            AppendVisibleSubtree(root, visible);
            return visible;
        }

        private static bool AppendVisibleSubtree(SubmoduleCommitFlowNode node, List<SubmoduleCommitFlowNode> visible)
        {
            var before = visible.Count;
            var keepSelf = node.Depth == 0 || IsActionableState(node.State);
            var selfAdded = false;
            var hasVisibleChild = false;
            if (keepSelf)
            {
                visible.Add(node);
                selfAdded = true;
            }

            foreach (var child in node.Children.OrderBy(x => x.DisplayPath, StringComparer.Ordinal))
            {
                if (AppendVisibleSubtree(child, visible) && !keepSelf)
                {
                    visible.Insert(before, node);
                    keepSelf = true;
                    selfAdded = true;
                    hasVisibleChild = true;
                }
                else if (visible.Count > before + (selfAdded ? 1 : 0))
                {
                    hasVisibleChild = true;
                }
            }

            if (selfAdded && hasVisibleChild && !IsActionableState(node.State) && node.State != SubmoduleCommitFlowNodeState.Scanning)
                node.State = SubmoduleCommitFlowNodeState.HasChildChanges;

            return keepSelf;
        }

        private static bool IsActionableState(SubmoduleCommitFlowNodeState state)
        {
            return state is SubmoduleCommitFlowNodeState.HasChanges
                or SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges
                or SubmoduleCommitFlowNodeState.HasMixedChanges
                or SubmoduleCommitFlowNodeState.Done
                or SubmoduleCommitFlowNodeState.Error;
        }

        private static bool HasActionableDescendant(SubmoduleCommitFlowNode node)
        {
            foreach (var child in node.Children)
            {
                if (IsActionableState(child.State) || HasActionableDescendant(child))
                    return true;
            }

            return false;
        }

        private static void Flatten(SubmoduleCommitFlowNode node, List<SubmoduleCommitFlowNode> output)
        {
            output.Add(node);
            foreach (var child in node.Children.OrderBy(x => x.DisplayPath, StringComparer.Ordinal))
                Flatten(child, output);
        }

        private static string GetParentPath(string path, Dictionary<string, SubmoduleCommitFlowNode> known)
        {
            var test = path;
            while (true)
            {
                var idx = test.LastIndexOf('/');
                if (idx <= 0)
                    return string.Empty;

                test = test.Substring(0, idx);
                if (known.ContainsKey(test))
                    return test;
            }
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim('/');
        }

        private static int GetSubmoduleDepth(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? 0 : path.Count(c => c == '/') + 1;
        }

        private static string ShortenSHA(string sha)
        {
            return string.IsNullOrWhiteSpace(sha) ? "--" : sha.Substring(0, Math.Min(8, sha.Length));
        }

        private static string ToSingleLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "(empty)";

            var single = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return single.Length <= 96 ? single : $"{single.Substring(0, 93)}...";
        }

        private static string PickPushRemote(List<Models.Remote> remotes)
        {
            var serverRemotes = remotes
                .Where(x => IsPushServerRemoteURL(x.URL))
                .ToList();

            return serverRemotes.Find(x => x.Name.Equals("origin", StringComparison.Ordinal))?.Name ??
                serverRemotes.FirstOrDefault()?.Name ??
                string.Empty;
        }

        private static bool IsPushServerRemote(List<Models.Remote> remotes, string name)
        {
            var remote = remotes.Find(x => x.Name.Equals(name, StringComparison.Ordinal));
            return remote != null && IsPushServerRemoteURL(remote.URL);
        }

        private static bool IsPushServerRemoteURL(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("git://", StringComparison.OrdinalIgnoreCase) ||
                Models.Remote.IsSSH(url);
        }

        private static bool TrySplitUpstream(string upstream, out string remote, out string branch)
        {
            remote = string.Empty;
            branch = string.Empty;

            if (string.IsNullOrWhiteSpace(upstream))
                return false;

            var idx = upstream.IndexOf('/');
            if (idx <= 0 || idx == upstream.Length - 1)
                return false;

            remote = upstream.Substring(0, idx);
            branch = upstream.Substring(idx + 1);
            return true;
        }

        private void ShowFlowToast(string message, bool isError = false)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ShowFlowToast(message, isError));
                return;
            }

            var version = Interlocked.Increment(ref _toastVersion);
            ToastMessage = message;
            ToastBackground = isError
                ? new SolidColorBrush(Color.FromRgb(160, 44, 44))
                : new SolidColorBrush(Color.FromRgb(34, 120, 72));
            IsToastVisible = true;
            ToastOpacity = 1.0;

            _ = Task.Run(async () =>
            {
                await Task.Delay(2700).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (version != _toastVersion)
                        return;

                    ToastOpacity = 0.0;
                    await Task.Delay(300).ConfigureAwait(true);
                    if (version == _toastVersion)
                        IsToastVisible = false;
                });
            });
        }

        private void SetScanning(bool value)
        {
            if (_isScanning == value)
                return;

            _isScanning = value;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(CanCommitSelectedNode));
            NotifyCommitAndPushStateChanged();
            OnPropertyChanged(nameof(CanUndoSelectedNodeCommit));
        }

        private void SetLoadingChanges(bool value)
        {
            if (_isLoadingChanges == value)
                return;

            _isLoadingChanges = value;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(CanCommitSelectedNode));
            NotifyCommitAndPushStateChanged();
            OnPropertyChanged(nameof(CanUndoSelectedNodeCommit));
        }

        private void NotifyCommitAndPushStateChanged()
        {
            OnPropertyChanged(nameof(CanCommitAndPushSelectedNode));
            OnPropertyChanged(nameof(CommitAndPushButtonBackground));
            OnPropertyChanged(nameof(CommitAndPushButtonForeground));
            OnPropertyChanged(nameof(CommitAndPushButtonText));
        }

        private void NotifyCommitPlanChanged()
        {
            OnPropertyChanged(nameof(CommitPlanPreview));
            OnPropertyChanged(nameof(CommitButtonText));
            OnPropertyChanged(nameof(CommitAndPushButtonText));
        }

        private sealed record NodeStatus(string Branch, string Head, string Upstream, string PushRemote, string PushRemoteBranch, bool SetPushTracking, int ChangeCount, int FileChangeCount, int SubmodulePointerChangeCount, SubmoduleCommitFlowNodeState State, List<Models.Change> Changes);
        private sealed record NodeBuildResult(List<SubmoduleCommitFlowNode> Nodes, string Warning);
        private sealed record UndoCommit(string BeforeHead, string AfterHead, string Message, bool WasPushed, string Remote, string RemoteBranch);

        private readonly Repository _repo;
        private List<SubmoduleCommitFlowNode> _allNodes = [];
        private List<SubmoduleCommitFlowNode> _nodes = [];
        private readonly Dictionary<string, List<Models.Change>> _changeCache = new(StringComparer.Ordinal);
        private SubmoduleCommitFlowNode _selectedNode = null;
        private List<Models.Change> _changes = [];
        private List<Models.Change> _selectedChanges = [];
        private object _detailContext = null;
        private string _commitMessage = string.Empty;
        private string _summary = string.Empty;
        private string _toastMessage = string.Empty;
        private double _toastOpacity = 0.0;
        private bool _isToastVisible = false;
        private IBrush _toastBackground = new SolidColorBrush(Color.FromRgb(34, 120, 72));
        private static readonly IBrush _commitAndPushEnabledBackground = new SolidColorBrush(Color.FromRgb(30, 125, 70));
        private static readonly IBrush _commitAndPushDisabledBackground = new SolidColorBrush(Color.FromRgb(160, 166, 174));
        private static readonly IBrush _commitAndPushDisabledForeground = new SolidColorBrush(Color.FromRgb(82, 86, 92));
        private bool _isScanning = false;
        private bool _isLoadingChanges = false;
        private bool _isCommitting = false;
        private bool _selectNextActionAfterScan = false;
        private int _version = 0;
        private int _loadChangesVersion = 0;
        private int _toastVersion = 0;
        private readonly HashSet<string> _donePaths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, UndoCommit> _undoCommits = new(StringComparer.Ordinal);
    }
}
