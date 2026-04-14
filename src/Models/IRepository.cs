namespace SourceGit.Models
{
    public interface IRepository
    {
        bool MayHaveSubmodules();

        void RefreshBranches();
        void RefreshWorktrees();
        void RefreshTags();
        void RefreshCommits();
        void RefreshSubmodules(bool force = false);
        void RefreshWorkingCopyChanges();
        void RefreshStashes();
    }
}
