using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class SubmoduleFileChange : ObservableObject
    {
        public string Title
        {
            get;
        }

        public DiffContext DiffContext
        {
            get;
        }

        public double InitialWindowWidth
        {
            get
            {
                var saved = Preferences.Instance.Layout.SubmoduleFileChangeDiffWindowWidth;
                return saved >= 960 ? saved : 1200;
            }
        }

        public double InitialWindowHeight
        {
            get
            {
                var saved = Preferences.Instance.Layout.SubmoduleFileChangeDiffWindowHeight;
                return saved >= 600 ? saved : 760;
            }
        }

        public SubmoduleFileChange(string repo, string based, string target, Models.Change change)
        {
            Title = change?.Path ?? "Submodule File Change";
            DiffContext = new DiffContext(repo, new Models.DiffOption(based, target, change));
        }
    }
}
