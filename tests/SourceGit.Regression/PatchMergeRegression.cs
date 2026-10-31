using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestPatchMergeIntegration()
    {
        var path = NewRepository("mailbox apply integration");
        File.WriteAllText(Path.Combine(path, "file.txt"), "base\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "base");
        Git(path, "checkout", "-b", "incoming");
        File.WriteAllText(Path.Combine(path, "file.txt"), "incoming\n");
        Git(path, "commit", "-am", "mail subject");
        var patch = Path.Combine(_root, "mailbox.patch");
        File.WriteAllText(patch, Git(path, "format-patch", "-1", "--stdout") + "\n");
        Git(path, "checkout", "main");
        File.WriteAllText(Path.Combine(path, "file.txt"), "ours\n");
        Git(path, "commit", "-am", "local subject");
        var original = Git(path, "rev-parse", "HEAD");
        var repo = Open(path);
        try
        {
            var apply = new SourceGit.ViewModels.Apply(repo) { PatchFile = patch, CommitChanges = true };
            Check(apply.CanCreateCommit, "Format-patch files enable creating commits while applying a patch");
            Check(!Wait(apply.Sure()), "Conflicting mailbox patch enters git am state without pretending to succeed");
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => repo.WorkingCopy.InProgressContext is AmInProgress, "Mailbox progress state did not load.");
            Check(repo.WorkingCopy.CommitMessage.Contains("mail subject"), "Mailbox progress loads its final commit message");
            Wait(repo.WorkingCopy.InProgressContext.AbortAsync(new CommandLog("Abort mailbox fixture")));
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => repo.WorkingCopy.InProgressContext == null, "Mailbox progress did not clear after abort.");
            Check(Git(path, "rev-parse", "HEAD") == original && File.ReadAllText(Path.Combine(path, "file.txt")).TrimEnd('\r', '\n') == "ours",
                "Aborting mailbox application restores the branch and original file content");
            Check(string.IsNullOrEmpty(repo.WorkingCopy.CommitMessage), "Aborting mailbox application clears its auto-loaded message");

            var rebase = new SourceGit.Commands.Command { WorkingDirectory = path, Args = "rebase incoming", RaiseError = false };
            Check(!Wait(rebase.ExecAsync()), "Rebase fixture stops at a real conflict");
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => repo.WorkingCopy.InProgressContext is RebaseInProgress, "Async rebase progress did not load.");
            Check(((RebaseInProgress)repo.WorkingCopy.InProgressContext).StoppedAt?.SHA == original,
                "Mailbox integration preserves asynchronous rebase metadata loading");
            Wait(repo.WorkingCopy.InProgressContext.AbortAsync(new CommandLog("Abort rebase fixture")));
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => repo.WorkingCopy.InProgressContext == null, "Rebase progress did not clear after abort.");
            Check(Git(path, "rev-parse", "HEAD") == original, "Aborting rebase restores its original branch");

            var staleApply = Path.Combine(path, ".git", "rebase-apply");
            Directory.CreateDirectory(staleApply);
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => !Directory.Exists(staleApply), "Stale apply metadata was not cleaned up.");
            Check(repo.WorkingCopy.InProgressContext == null, "A leftover apply directory without an applying marker is not mistaken for an active mailbox operation");
        }
        finally
        {
            PumpFor(100);
            repo.Close();
        }
    }
}
