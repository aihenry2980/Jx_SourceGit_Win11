using System.Collections.Generic;
using System.IO;

using SourceGit.Models;

namespace SourceGit.ViewModels
{
    public partial class Repository
    {
        public RepositoryMemoryProfile BuildMemoryProfile()
        {
            var components = new List<MemoryProfileComponent>();

            AddComponent(components, "History commits", EstimateCommits(_histories?.Commits), $"{_histories?.Commits?.Count ?? 0} commits");
            AddComponent(components, "Git graph", EstimateGraph(_histories?.Graph), BuildGraphDetails(_histories?.Graph));
            AddComponent(components, "Commit detail", EstimateHistoryDetail(_histories?.DetailContext), BuildHistoryDetailLabel(_histories?.DetailContext));
            AddComponent(components, "Refs and sidebar", EstimateRefsAndSidebar(), BuildRefsAndSidebarDetails());
            AddComponent(components, "Working copy", EstimateWorkingCopy(_workingCopy), BuildWorkingCopyDetails(_workingCopy));
            AddComponent(components, "Stashes", EstimateStashes(_stashesPage), BuildStashDetails(_stashesPage));
            AddComponent(components, "Search cache", EstimateSearchCache(_searchCommitContext), BuildSearchDetails(_searchCommitContext));
            AddComponent(components, "Command logs", EstimateLogs(Logs), $"{Logs.Count} retained logs");

            var repoName = Path.GetFileName(FullPath.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(repoName))
                repoName = FullPath;

            var counts = $"branches {_branches.Count}, tags {_tags.Count}, submodules {_submodules.Count}, remotes {_remotes.Count}, worktrees {_worktrees.Count}";
            const string notes = "Approximate SourceGit-owned memory only. Shared caches like avatars are shown separately.";
            return new RepositoryMemoryProfile(repoName, FullPath, counts, notes, components);
        }

        private static void AddComponent(List<MemoryProfileComponent> components, string name, long bytes, string details)
        {
            if (bytes <= 0 && string.IsNullOrWhiteSpace(details))
                return;

            components.Add(new MemoryProfileComponent(name, bytes, details));
        }

        private long EstimateRefsAndSidebar()
        {
            long bytes = 0;

            foreach (var branch in _branches)
            {
                bytes += 120;
                bytes += MemoryProfileEstimator.EstimateString(branch.Name);
                bytes += MemoryProfileEstimator.EstimateString(branch.FullName);
                bytes += MemoryProfileEstimator.EstimateString(branch.Head);
                bytes += MemoryProfileEstimator.EstimateString(branch.Upstream);
                bytes += MemoryProfileEstimator.EstimateString(branch.Remote);
                bytes += MemoryProfileEstimator.EstimateString(branch.WorktreePath);
                bytes += MemoryProfileEstimator.EstimateListReferences(branch.Ahead);
                bytes += MemoryProfileEstimator.EstimateListReferences(branch.Behind);
                bytes += EstimateStrings(branch.Ahead);
                bytes += EstimateStrings(branch.Behind);
            }

            foreach (var remote in _remotes)
                bytes += 96 + MemoryProfileEstimator.EstimateString(remote.Name) + MemoryProfileEstimator.EstimateString(remote.URL);

            foreach (var tag in _tags)
            {
                bytes += 96;
                bytes += MemoryProfileEstimator.EstimateString(tag.Name);
                bytes += MemoryProfileEstimator.EstimateString(tag.SHA);
                bytes += MemoryProfileEstimator.EstimateString(tag.Message);
                bytes += EstimateUser(tag.Creator);
            }

            foreach (var submodule in _submodules)
            {
                bytes += 96;
                bytes += MemoryProfileEstimator.EstimateString(submodule.Path);
                bytes += MemoryProfileEstimator.EstimateString(submodule.SHA);
                bytes += MemoryProfileEstimator.EstimateString(submodule.URL);
                bytes += MemoryProfileEstimator.EstimateString(submodule.Branch);
            }

            foreach (var worktree in _worktrees)
            {
                bytes += 112;
                bytes += MemoryProfileEstimator.EstimateString(worktree.Branch);
                bytes += MemoryProfileEstimator.EstimateString(worktree.FullPath);
                bytes += MemoryProfileEstimator.EstimateString(worktree.RelativePath);
                bytes += MemoryProfileEstimator.EstimateString(worktree.Head);
            }

            bytes += EstimateBranchTreeNodes(_localBranchTrees);
            bytes += EstimateBranchTreeNodes(_remoteBranchTrees);
            bytes += MemoryProfileEstimator.EstimateString(_filter);
            return bytes;
        }

        private string BuildRefsAndSidebarDetails()
        {
            var treeNodes = CountBranchTreeNodes(_localBranchTrees) + CountBranchTreeNodes(_remoteBranchTrees);
            return $"{_branches.Count} branches, {_tags.Count} tags, {_submodules.Count} submodules, {treeNodes} tree nodes";
        }

        private long EstimateWorkingCopy(WorkingCopy wc)
        {
            if (wc == null)
                return 0;

            long bytes = 0;
            bytes += EstimateChanges(wc.Unstaged);
            bytes += EstimateChangesForUniquePaths(wc.Staged, wc.Unstaged);
            bytes += MemoryProfileEstimator.EstimateListReferences(wc.VisibleUnstaged);
            bytes += MemoryProfileEstimator.EstimateListReferences(wc.VisibleStaged);
            bytes += MemoryProfileEstimator.EstimateListReferences(wc.SelectedUnstaged.Changes);
            bytes += MemoryProfileEstimator.EstimateListReferences(wc.SelectedStaged.Changes);
            bytes += MemoryProfileEstimator.EstimateString(wc.Filter);
            bytes += MemoryProfileEstimator.EstimateString(wc.CommitMessage);

            if (wc.DetailContext is DiffContext diff)
                bytes += EstimateDiffContext(diff);
            else if (wc.DetailContext is Conflict conflict)
                bytes += EstimateConflict(conflict);

            return bytes;
        }

        private static string BuildWorkingCopyDetails(WorkingCopy wc)
        {
            if (wc == null)
                return "Not loaded";

            var detail = wc.DetailContext switch
            {
                DiffContext => "diff preview",
                Conflict => "conflict preview",
                _ => "no preview",
            };
            return $"{wc.Unstaged?.Count ?? 0} unstaged, {wc.Staged?.Count ?? 0} staged, {detail}";
        }

        private long EstimateStashes(StashesPage stashes)
        {
            if (stashes == null)
                return 0;

            long bytes = 0;
            bytes += EstimateStashEntries(stashes.Stashes);
            bytes += MemoryProfileEstimator.EstimateListReferences(stashes.VisibleStashes);
            bytes += EstimateChanges(stashes.Changes);
            bytes += MemoryProfileEstimator.EstimateListReferences(stashes.ChangeSelection.Changes);
            bytes += MemoryProfileEstimator.EstimateString(stashes.SearchFilter);
            bytes += EstimateDiffContext(stashes.DiffContext);
            return bytes;
        }

        private static string BuildStashDetails(StashesPage stashes)
        {
            if (stashes == null)
                return "Not loaded";

            return $"{stashes.Stashes?.Count ?? 0} stashes, {stashes.Changes?.Count ?? 0} selected changes";
        }

        private long EstimateSearchCache(SearchCommitContext search)
        {
            if (search == null)
                return 0;

            long bytes = 0;
            bytes += MemoryProfileEstimator.EstimateString(search.Filter);
            bytes += EstimateCommits(search.Results);
            bytes += MemoryProfileEstimator.EstimateListReferences(search.Suggestions);
            bytes += EstimateSearchSuggestions(search.Suggestions);
            bytes += search.CachedWorktreeFileCount * 72L;
            return bytes;
        }

        private static string BuildSearchDetails(SearchCommitContext search)
        {
            if (search == null)
                return "Idle";

            return $"{search.Results?.Count ?? 0} results, {search.Suggestions?.Count ?? 0} suggestions, {search.CachedWorktreeFileCount} cached paths";
        }

        private long EstimateHistoryDetail(object detailContext)
        {
            return detailContext switch
            {
                CommitDetail detail => EstimateCommitDetail(detail),
                RevisionCompare compare => EstimateRevisionCompare(compare),
                Models.Count => 32,
                _ => 0,
            };
        }

        private static string BuildHistoryDetailLabel(object detailContext)
        {
            return detailContext switch
            {
                CommitDetail => "commit detail panel",
                RevisionCompare => "revision compare panel",
                Models.Count many => $"{many.Value} selected commits",
                _ => "no detail panel",
            };
        }

        private long EstimateCommitDetail(CommitDetail detail)
        {
            long bytes = 0;
            bytes += EstimateCommit(detail.Commit);
            bytes += MemoryProfileEstimator.EstimateString(detail.FullMessage?.Message);
            bytes += EstimateChanges(detail.Changes);
            bytes += MemoryProfileEstimator.EstimateListReferences(detail.VisibleChanges);
            bytes += MemoryProfileEstimator.EstimateListReferences(detail.ChangeSelection.Changes);
            bytes += MemoryProfileEstimator.EstimateString(detail.SearchChangeFilter);
            bytes += MemoryProfileEstimator.EstimateString(detail.ViewRevisionFilePath);
            bytes += EstimateRevisionFileContent(detail.ViewRevisionFileContent);
            bytes += MemoryProfileEstimator.EstimateString(detail.RevisionFileSearchFilter);
            bytes += MemoryProfileEstimator.EstimateListReferences(detail.RevisionFileSearchSuggestion);
            bytes += EstimateStrings(detail.RevisionFileSearchSuggestion);
            bytes += EstimateDiffContext(detail.DiffContext);
            return bytes;
        }

        private long EstimateRevisionCompare(RevisionCompare compare)
        {
            if (compare == null)
                return 0;

            long bytes = 0;
            bytes += EstimateChanges(compare.VisibleChanges);
            bytes += MemoryProfileEstimator.EstimateListReferences(compare.ChangeSelection.Changes);
            bytes += MemoryProfileEstimator.EstimateString(compare.SearchFilter);
            bytes += EstimateDiffContext(compare.DiffContext);
            return bytes;
        }

        private static long EstimateConflict(Conflict conflict)
        {
            if (conflict == null)
                return 0;

            long bytes = 96;
            if (conflict.Mine is Models.Commit mine)
                bytes += EstimateCommit(mine);
            else if (conflict.Mine is string mineText)
                bytes += MemoryProfileEstimator.EstimateString(mineText);

            if (conflict.Theirs is Models.Commit theirs)
                bytes += EstimateCommit(theirs);
            else if (conflict.Theirs is string theirsText)
                bytes += MemoryProfileEstimator.EstimateString(theirsText);

            return bytes;
        }

        private static long EstimateLogs(IEnumerable<CommandLog> logs)
        {
            if (logs == null)
                return 0;

            long bytes = 0;
            foreach (var log in logs)
            {
                if (log == null)
                    continue;

                bytes += 128;
                bytes += MemoryProfileEstimator.EstimateString(log.Name);
                bytes += MemoryProfileEstimator.EstimateString(log.LatestCommand);
                bytes += log.EstimatedContentLength * 2L;
            }

            return bytes;
        }

        private static long EstimateCommits(List<Models.Commit> commits)
        {
            if (commits == null || commits.Count == 0)
                return 0;

            long bytes = MemoryProfileEstimator.EstimateListReferences(commits);
            foreach (var commit in commits)
                bytes += EstimateCommit(commit);
            return bytes;
        }

        private static long EstimateCommit(Models.Commit commit)
        {
            if (commit == null)
                return 0;

            long bytes = 144;
            bytes += MemoryProfileEstimator.EstimateString(commit.SHA);
            bytes += MemoryProfileEstimator.EstimateString(commit.Subject);
            bytes += EstimateUser(commit.Author);
            bytes += EstimateUser(commit.Committer);
            bytes += MemoryProfileEstimator.EstimateListReferences(commit.Parents);
            bytes += EstimateStrings(commit.Parents);
            bytes += MemoryProfileEstimator.EstimateListReferences(commit.Decorators);
            foreach (var decorator in commit.Decorators)
                bytes += 48 + MemoryProfileEstimator.EstimateString(decorator?.Name);
            bytes += MemoryProfileEstimator.EstimateListReferences(commit.SubmoduleUpdateBadges);
            foreach (var badge in commit.SubmoduleUpdateBadges)
                bytes += 64 + MemoryProfileEstimator.EstimateString(badge?.Path) + MemoryProfileEstimator.EstimateString(badge?.Name);
            return bytes;
        }

        private static long EstimateUser(Models.User user)
        {
            if (user == null)
                return 0;

            return 32 + MemoryProfileEstimator.EstimateString(user.Name) + MemoryProfileEstimator.EstimateString(user.Email);
        }

        private static long EstimateChanges(List<Models.Change> changes)
        {
            if (changes == null || changes.Count == 0)
                return 0;

            long bytes = MemoryProfileEstimator.EstimateListReferences(changes);
            foreach (var change in changes)
                bytes += EstimateChange(change);
            return bytes;
        }

        private static long EstimateChangesForUniquePaths(List<Models.Change> changes, List<Models.Change> existing)
        {
            if (changes == null || changes.Count == 0)
                return 0;

            var seen = new HashSet<string>();
            if (existing != null)
            {
                foreach (var change in existing)
                {
                    if (change != null)
                        seen.Add(change.Path);
                }
            }

            long bytes = 0;
            foreach (var change in changes)
            {
                if (change == null || !seen.Add(change.Path))
                    continue;

                bytes += EstimateChange(change);
            }

            return bytes;
        }

        private static long EstimateChange(Models.Change change)
        {
            if (change == null)
                return 0;

            long bytes = 120;
            bytes += MemoryProfileEstimator.EstimateString(change.Path);
            bytes += MemoryProfileEstimator.EstimateString(change.OriginalPath);
            bytes += MemoryProfileEstimator.EstimateString(change.IndexSubmodulePointerOldSHA);
            bytes += MemoryProfileEstimator.EstimateString(change.IndexSubmodulePointerNewSHA);
            bytes += MemoryProfileEstimator.EstimateString(change.WorkTreeSubmodulePointerOldSHA);
            bytes += MemoryProfileEstimator.EstimateString(change.WorkTreeSubmodulePointerNewSHA);
            bytes += MemoryProfileEstimator.EstimateString(change.AddedLines);
            bytes += MemoryProfileEstimator.EstimateString(change.DeletedLines);

            if (change.DataForAmend != null)
            {
                bytes += 64;
                bytes += MemoryProfileEstimator.EstimateString(change.DataForAmend.FileMode);
                bytes += MemoryProfileEstimator.EstimateString(change.DataForAmend.ObjectHash);
                bytes += MemoryProfileEstimator.EstimateString(change.DataForAmend.ParentSHA);
            }

            return bytes;
        }

        private static long EstimateStashEntries(List<Models.Stash> stashes)
        {
            if (stashes == null || stashes.Count == 0)
                return 0;

            long bytes = MemoryProfileEstimator.EstimateListReferences(stashes);
            foreach (var stash in stashes)
            {
                if (stash == null)
                    continue;

                bytes += 96;
                bytes += MemoryProfileEstimator.EstimateString(stash.Name);
                bytes += MemoryProfileEstimator.EstimateString(stash.SHA);
                bytes += MemoryProfileEstimator.EstimateString(stash.Message);
                bytes += MemoryProfileEstimator.EstimateListReferences(stash.Parents);
                bytes += EstimateStrings(stash.Parents);
            }

            return bytes;
        }

        private static long EstimateGraph(Models.CommitGraph graph)
        {
            if (graph == null)
                return 0;

            long bytes = 0;

            bytes += MemoryProfileEstimator.EstimateListReferences(graph.Paths);
            foreach (var path in graph.Paths)
            {
                if (path == null)
                    continue;

                bytes += 56;
                bytes += MemoryProfileEstimator.EstimateListReferences(path.Points);
                bytes += path.Points.Count * 16L;
            }

            bytes += MemoryProfileEstimator.EstimateListReferences(graph.Links);
            bytes += (graph.Links?.Count ?? 0) * 56L;
            bytes += MemoryProfileEstimator.EstimateListReferences(graph.Dots);
            bytes += (graph.Dots?.Count ?? 0) * 40L;
            return bytes;
        }

        private static string BuildGraphDetails(Models.CommitGraph graph)
        {
            if (graph == null)
                return "Not loaded";

            return $"{graph.Dots.Count} dots, {graph.Paths.Count} paths, {graph.Links.Count} links";
        }

        private static long EstimateDiffContext(DiffContext diff)
        {
            if (diff == null)
                return 0;

            long bytes = 64;
            bytes += MemoryProfileEstimator.EstimateString(diff.Title);
            bytes += EstimateDiffContent(diff.Content);
            return bytes;
        }

        private static long EstimateDiffContent(object content)
        {
            return content switch
            {
                null => 0,
                TextDiffContext text => EstimateTextDiffContext(text),
                Models.ImageDiff image => EstimateImageDiff(image),
                Models.BinaryDiff => 32,
                Models.LFSDiff lfs => 48 + EstimateLfsObject(lfs.Old) + EstimateLfsObject(lfs.New),
                LFSImageDiff lfsImage => 64 + EstimateImageDiff(lfsImage.Image) + EstimateLfsObject(lfsImage.LFS?.Old) + EstimateLfsObject(lfsImage.LFS?.New),
                Models.SubmoduleDiff submodule => EstimateSubmoduleDiff(submodule),
                Models.NoOrEOLChange => 16,
                _ => 32,
            };
        }

        private static long EstimateTextDiffContext(TextDiffContext text)
        {
            if (text?.Data == null)
                return 0;

            long bytes = 96;
            bytes += EstimateTextDiff(text.Data);
            if (text is TwoSideTextDiff sideBySide)
            {
                bytes += MemoryProfileEstimator.EstimateListReferences(sideBySide.Old);
                bytes += MemoryProfileEstimator.EstimateListReferences(sideBySide.New);
            }

            return bytes;
        }

        private static long EstimateTextDiff(Models.TextDiff diff)
        {
            if (diff == null)
                return 0;

            long bytes = MemoryProfileEstimator.EstimateListReferences(diff.Lines);
            foreach (var line in diff.Lines)
            {
                if (line == null)
                    continue;

                bytes += 64;
                bytes += MemoryProfileEstimator.EstimateString(line.Content);
                bytes += MemoryProfileEstimator.EstimateListReferences(line.Highlights);
                bytes += (line.Highlights?.Count ?? 0) * 16L;
            }

            return bytes;
        }

        private static long EstimateImageDiff(Models.ImageDiff image)
        {
            if (image == null)
                return 0;

            return 48 +
                MemoryProfileEstimator.EstimateBitmap(image.Old) +
                MemoryProfileEstimator.EstimateBitmap(image.New);
        }

        private static long EstimateSubmoduleDiff(Models.SubmoduleDiff submodule)
        {
            if (submodule == null)
                return 0;

            long bytes = 64;
            bytes += EstimateRevisionSubmodule(submodule.Old);
            bytes += EstimateRevisionSubmodule(submodule.New);
            bytes += EstimateChanges(submodule.Changes);
            bytes += MemoryProfileEstimator.EstimateString(submodule.RepositoryPath);
            bytes += MemoryProfileEstimator.EstimateString(submodule.BaseRevision);
            bytes += MemoryProfileEstimator.EstimateString(submodule.TargetRevision);
            bytes += MemoryProfileEstimator.EstimateString(submodule.OldPointerURL);
            bytes += MemoryProfileEstimator.EstimateString(submodule.NewPointerURL);
            return bytes;
        }

        private static long EstimateRevisionFileContent(object content)
        {
            return content switch
            {
                null => 0,
                Models.RevisionTextFile text => 96 + MemoryProfileEstimator.EstimateString(text.FileName) + MemoryProfileEstimator.EstimateString(text.Content),
                Models.RevisionBinaryFile => 32,
                Models.RevisionImageFile image => 48 + MemoryProfileEstimator.EstimateBitmap(image.Image),
                Models.RevisionLFSObject lfs => 48 + EstimateLfsObject(lfs.Object),
                RevisionLFSImage lfsImage => 64 + EstimateRevisionFileContent(lfsImage.Image) + EstimateLfsObject(lfsImage.LFS?.Object),
                Models.RevisionSubmodule submodule => EstimateRevisionSubmodule(submodule),
                _ => 32,
            };
        }

        private static long EstimateRevisionSubmodule(Models.RevisionSubmodule submodule)
        {
            if (submodule == null)
                return 0;

            return 64 + EstimateCommit(submodule.Commit) + MemoryProfileEstimator.EstimateString(submodule.FullMessage?.Message);
        }

        private static long EstimateLfsObject(Models.LFSObject lfs)
        {
            if (lfs == null)
                return 0;

            return 32 + MemoryProfileEstimator.EstimateString(lfs.Oid);
        }

        private static long EstimateBranchTreeNodes(List<BranchTreeNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return 0;

            long bytes = MemoryProfileEstimator.EstimateListReferences(nodes);
            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                bytes += 96;
                bytes += MemoryProfileEstimator.EstimateString(node.Name);
                bytes += MemoryProfileEstimator.EstimateString(node.Path);
                bytes += EstimateBranchTreeNodes(node.Children);
            }

            return bytes;
        }

        private static int CountBranchTreeNodes(List<BranchTreeNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return 0;

            var count = nodes.Count;
            foreach (var node in nodes)
                count += CountBranchTreeNodes(node.Children);
            return count;
        }

        private static long EstimateStrings(List<string> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            long bytes = 0;
            foreach (var value in values)
                bytes += MemoryProfileEstimator.EstimateString(value);
            return bytes;
        }

        private static long EstimateSearchSuggestions(List<object> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            long bytes = 0;
            foreach (var value in values)
            {
                if (value is string text)
                    bytes += MemoryProfileEstimator.EstimateString(text);
                else if (value is Models.User user)
                    bytes += EstimateUser(user);
            }

            return bytes;
        }
    }
}
