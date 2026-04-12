namespace SourceGit.ViewModels
{
    public class RecursiveLocalChangeDiff
    {
        public string Title => Diff.Title;
        public double InitialWindowWidth
        {
            get
            {
                var saved = Preferences.Instance.Layout.RecursiveLocalChangeDiffWindowWidth;
                return saved >= 720 ? saved : Preferences.Instance.UseSideBySideDiff ? 2200 : 1100;
            }
        }

        public double InitialWindowHeight
        {
            get
            {
                var saved = Preferences.Instance.Layout.RecursiveLocalChangeDiffWindowHeight;
                return saved >= 420 ? saved : 760;
            }
        }

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
