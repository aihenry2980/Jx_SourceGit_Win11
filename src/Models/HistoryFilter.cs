using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public enum FilterType
    {
        LocalBranch = 0,
        LocalBranchFolder,
        RemoteBranch,
        RemoteBranchFolder,
        Tag,
        Path,
    }

    public enum FilterMode
    {
        None = 0,
        Included,
        Excluded,
    }

    public class HistoryFilter : ObservableObject
    {
        public string Pattern
        {
            get => _pattern;
            set => SetProperty(ref _pattern, value);
        }

        public FilterType Type
        {
            get;
            set;
        } = FilterType.LocalBranch;

        public FilterMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        public bool IsBranch
        {
            get => Type is not (FilterType.Tag or FilterType.Path);
        }

        public bool IsPath
        {
            get => Type == FilterType.Path;
        }

        public uint Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        public HistoryFilter()
        {
        }

        public HistoryFilter(string pattern, FilterType type, FilterMode mode, uint color = 0)
        {
            _pattern = pattern;
            _mode = mode;
            _color = color;
            Type = type;
        }

        private string _pattern = string.Empty;
        private FilterMode _mode = FilterMode.None;
        private uint _color = 0;
    }
}
