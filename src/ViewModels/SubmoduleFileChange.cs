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

        public SubmoduleFileChange(string repo, string based, string target, Models.Change change)
        {
            Title = change?.Path ?? "Submodule File Change";
            DiffContext = new DiffContext(repo, new Models.DiffOption(based, target, change));
        }
    }
}
