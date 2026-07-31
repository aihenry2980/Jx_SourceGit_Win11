using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public partial class CreateBranchWithoutCommit : Popup
    {
        public override double PopupWidth => 720;

        [GeneratedRegex(@"[^A-Za-z0-9._/-]+")]
        private static partial Regex InvalidBranchNameChars();

        [Required(ErrorMessage = "Branch name is required!")]
        [RegularExpression(@"^[\w\-/\.#\+]+$", ErrorMessage = "Bad branch name format!")]
        [CustomValidation(typeof(CreateBranchWithoutCommit), nameof(ValidateBranchName))]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        public Models.Branch SourceBranch { get; }

        public IReadOnlyList<Models.Commit> DroppedCommits { get; }

        public Models.Commit DroppedCommit => DroppedCommits.Count == 1 ? DroppedCommits[0] : null;

        public int DroppedCommitCount => DroppedCommits.Count;

        public string DroppedCommitCountDescription => DroppedCommits.Count == 1 ? "1 commit" : $"{DroppedCommits.Count} commits";

        public string DroppedCommitShortSHA => DroppedCommits.Count == 1 ? ShortSHA(DroppedCommits[0].SHA) : $"{DroppedCommits.Count} commits";

        public string PopupTitle => DroppedCommits.Count == 1 ? "Create Branch Without Commit" : "Create Branch Without Commits";

        public string SourceBranchName => SourceBranch?.Name ?? string.Empty;

        public string SafetyNote =>
            $"The original branch '{SourceBranchName}' will not be changed. A new branch will be created and rebased without the selected {DroppedCommitCountDescription}.";

        public CreateBranchWithoutCommit(Repository repo, Models.Branch sourceBranch, Models.Commit droppedCommit)
            : this(repo, sourceBranch, [droppedCommit])
        {
        }

        public CreateBranchWithoutCommit(Repository repo, Models.Branch sourceBranch, IReadOnlyList<Models.Commit> droppedCommits)
        {
            _repo = repo;
            SourceBranch = sourceBranch;
            DroppedCommits = droppedCommits?.Where(x => x != null).DistinctBy(x => x.SHA).ToList() ?? [];
            Name = GenerateDefaultBranchName(sourceBranch, DroppedCommits);
            ProgressDescription = $"Creating {Name} without {DroppedCommitCountDescription} ...";
        }

        public static ValidationResult ValidateBranchName(string name, ValidationContext ctx)
        {
            if (ctx.ObjectInstance is CreateBranchWithoutCommit vm)
            {
                foreach (var b in vm._repo.Branches)
                {
                    if (b.FriendlyName.Equals(name, StringComparison.Ordinal))
                        return new ValidationResult("A branch with same name already exists!");
                }

                return ValidationResult.Success;
            }

            return new ValidationResult("Missing runtime context to create branch!");
        }

        public override async Task<bool> Sure()
        {
            if (SourceBranch is not { IsLocal: true } || DroppedCommits.Count == 0)
            {
                _repo.SendNotification("Can only create a branch without a commit from a local branch.", true);
                return true;
            }

            if (DroppedCommits.Any(x => x.Parents.Count != 1))
            {
                _repo.SendNotification("Can not drop merge/root commits into a new branch.", true);
                return true;
            }

            var localChanges = await new Commands.CountLocalChanges(_repo.FullPath, true)
                .GetResultAsync();
            if (localChanges > 0)
            {
                _repo.SendNotification("Can not drop commit into a new branch while local changes exist.", true);
                return true;
            }

            var firstParentShas = await new Commands.QueryFirstParentCommitHashes(_repo.FullPath, SourceBranch.Head)
                .GetResultAsync();
            var firstParentOrder = BuildFirstParentOrder(firstParentShas);
            if (DroppedCommits.Any(x => !firstParentOrder.ContainsKey(x.SHA)))
            {
                _repo.SendNotification($"Selected commits are not on the first-parent line of '{SourceBranch.Name}'.", true);
                return true;
            }

            var oldestDropped = FindOldestDroppedCommit(firstParentOrder);
            var rebaseBase = oldestDropped.Parents[0];
            var log = _repo.CreateLog($"Create Branch '{Name}' without {DroppedCommitCountDescription}");
            Use(log);

            var created = new Models.Branch()
            {
                Name = Name,
                FullName = $"refs/heads/{Name}",
                CommitterDate = SourceBranch.CommitterDate,
                Head = SourceBranch.Head,
                IsLocal = true,
            };

            var succ = false;
            var checkoutSucceeded = false;
            using (var lockWatcher = _repo.LockWatcher())
            {
                log.AppendLine($"=== Create `{Name}` from `{SourceBranch.Name}` ===");
                succ = await new Commands.Checkout(_repo.FullPath)
                    .Use(log)
                    .BranchAsync(Name, SourceBranch.Name, false, false);
                checkoutSucceeded = succ;

                if (succ)
                {
                    ProgressDescription = $"Dropping {DroppedCommitCountDescription} from {Name} ...";
                    log.AppendLine($"=== Drop {DroppedCommitCountDescription} from `{Name}` ===");
                    succ = DroppedCommits.Count == 1 ?
                        await new Commands.RebaseOnto(_repo.FullPath, rebaseBase, DroppedCommits[0].SHA, Name)
                            .Use(log)
                            .RunAsync() :
                        await RunInteractiveDropRebaseAsync(log, rebaseBase);
                }
            }

            if (checkoutSucceeded)
            {
                var head = await new Commands.QueryRevisionByRefName(_repo.FullPath, "HEAD")
                    .GetResultAsync();
                if (!string.IsNullOrWhiteSpace(head))
                    created.Head = head;
            }

            if (succ)
            {
                _repo.RefreshAfterCreateBranch(created, true);
                _repo.RefreshSuperProjectSubmodulePointer();
                _repo.SendNotification($"Created `{Name}` without {DroppedCommitCountDescription}.");
            }
            else
            {
                if (checkoutSucceeded)
                    _repo.RefreshAfterCreateBranch(created, true);
                else
                    _repo.MarkWorkingCopyDirtyManually();

                _repo.SendNotification($"Failed to create `{Name}` without {DroppedCommitCountDescription}. Review repository log for details.", true);
            }

            log.Complete();
            return true;
        }

        private async Task<bool> RunInteractiveDropRebaseAsync(CommandLog log, string rebaseBase)
        {
            var commits = await new Commands.QueryCommitsForInteractiveRebase(_repo.FullPath, rebaseBase)
                .GetResultAsync();
            var dropShas = new HashSet<string>(DroppedCommits.Select(x => x.SHA), StringComparer.Ordinal);
            foreach (var sha in dropShas)
            {
                if (!commits.Exists(x => x.Commit.SHA.Equals(sha, StringComparison.Ordinal)))
                {
                    log.AppendLine($"Commit `{ShortSHA(sha)}` is not available for interactive rebase.");
                    return false;
                }
            }

            var origHead = await new Commands.QueryRevisionByRefName(_repo.FullPath, "HEAD")
                .GetResultAsync();
            if (string.IsNullOrWhiteSpace(origHead))
                return false;

            var collection = new Models.InteractiveRebaseJobCollection
            {
                OrigHead = origHead,
                Onto = rebaseBase,
            };

            for (var i = commits.Count - 1; i >= 0; i--)
            {
                var commit = commits[i];
                collection.Jobs.Add(new Models.InteractiveRebaseJob
                {
                    SHA = commit.Commit.SHA,
                    Action = dropShas.Contains(commit.Commit.SHA) ? Models.InteractiveRebaseAction.Drop : Models.InteractiveRebaseAction.Pick,
                    Message = commit.Message,
                });
            }

            var saveFile = Path.Combine(_repo.GitDir, "sourcegit.interactive_rebase");
            await using (var stream = File.Create(saveFile))
            {
                await JsonSerializer.SerializeAsync(stream, collection, JsonCodeGen.Default.InteractiveRebaseJobCollection);
            }

            return await new Commands.InteractiveRebase(_repo.FullPath, rebaseBase, false, false)
                .Use(log)
                .ExecAsync();
        }

        private static string GenerateDefaultBranchName(Models.Branch sourceBranch, IReadOnlyList<Models.Commit> droppedCommits)
        {
            var branch = string.IsNullOrWhiteSpace(sourceBranch?.Name) ? "branch" : sourceBranch.Name.Trim();
            var suffix = droppedCommits.Count == 1 ? BuildSubjectSlug(droppedCommits[0].Subject) : $"{droppedCommits.Count}_commits";
            if (string.IsNullOrEmpty(suffix))
                suffix = droppedCommits.Count == 1 ? ShortSHA(droppedCommits[0].SHA) : "commits";

            return $"{branch}_drop_{suffix}";
        }

        private static string BuildSubjectSlug(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return string.Empty;

            var normalized = InvalidBranchNameChars().Replace(subject.Trim().ToLowerInvariant(), "_").Trim('_', '.', '/', '-');
            var builder = new StringBuilder(normalized.Length);
            var lastUnderscore = false;
            foreach (var ch in normalized)
            {
                if (ch == '_')
                {
                    if (!lastUnderscore)
                        builder.Append(ch);
                    lastUnderscore = true;
                }
                else
                {
                    builder.Append(ch);
                    lastUnderscore = false;
                }

                if (builder.Length >= 32)
                    break;
            }

            return builder.ToString().Trim('_', '.', '/', '-');
        }

        private static Dictionary<string, int> BuildFirstParentOrder(List<string> shas)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < shas.Count; i++)
                map[shas[i]] = i;
            return map;
        }

        private Models.Commit FindOldestDroppedCommit(Dictionary<string, int> firstParentOrder)
        {
            var oldest = DroppedCommits[0];
            var oldestIndex = firstParentOrder[oldest.SHA];
            foreach (var commit in DroppedCommits)
            {
                var index = firstParentOrder[commit.SHA];
                if (index > oldestIndex)
                {
                    oldest = commit;
                    oldestIndex = index;
                }
            }

            return oldest;
        }

        private static string ShortSHA(string sha)
        {
            return string.IsNullOrEmpty(sha) ? string.Empty : sha.Substring(0, Math.Min(10, sha.Length));
        }

        private readonly Repository _repo;
        private string _name = string.Empty;
    }
}
