namespace SourceGit.ViewModels
{
    public class RecursiveLocalChangeDiff
    {
        public string Title => Diff.Title;

        public DiffContext Diff
        {
            get;
        }

        public RecursiveLocalChangeDiff(string repoPath, Models.Change change)
        {
            var isUnstaged = change.WorkTree != Models.ChangeState.None;
            Diff = new DiffContext(repoPath, new Models.DiffOption(change, isUnstaged));
        }
    }
}
