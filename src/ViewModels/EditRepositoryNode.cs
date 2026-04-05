using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class EditRepositoryNode : Popup
    {
        public string Target
        {
            get;
        }

        public bool IsRepository
        {
            get;
        }

        public List<int> Bookmarks
        {
            get;
        }

        [Required(ErrorMessage = "Name is required!")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        public int Bookmark
        {
            get => _bookmark;
            set => SetProperty(ref _bookmark, value);
        }

        public EditRepositoryNode(RepositoryNode node)
        {
            _node = node;
            _name = node.Name;
            _bookmark = node.Bookmark;

            Target = node.IsRepository ? node.Id : node.Name;
            IsRepository = node.IsRepository;
            Bookmarks = new List<int>();
            for (var i = 0; i < Models.Bookmarks.Brushes.Length; i++)
                Bookmarks.Add(i);
        }

        public override Task<bool> Sure()
        {
            bool needSort = _node.Name != _name;
            _node.Name = _name;
            _node.Bookmark = _bookmark;

            var launcher = App.GetLauncher();
            if (launcher != null)
            {
                foreach (var page in launcher.Pages)
                {
                    if (page.Data is Repository repo && page.Node.Id.Equals(_node.Id, StringComparison.Ordinal))
                        repo.NotifyAccentColorChanged();
                }
            }

            if (needSort)
            {
                Preferences.Instance.SortByRenamedNode(_node);
                Welcome.Instance.Refresh();
            }

            return Task.FromResult(true);
        }

        private RepositoryNode _node = null;
        private string _name = null;
        private int _bookmark = 0;
    }
}
