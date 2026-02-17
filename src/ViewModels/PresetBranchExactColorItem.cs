using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class PresetBranchExactColorItem : ObservableObject
    {
        public string Name
        {
            get;
        }

        public uint Color
        {
            get => _color;
            set
            {
                if (SetProperty(ref _color, value))
                    OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public IBrush ColorBrush
        {
            get => new SolidColorBrush(Avalonia.Media.Color.FromUInt32(_color));
        }

        public PresetBranchExactColorItem(string name, uint color)
        {
            Name = name;
            _color = color;
        }

        private uint _color = Preferences.PRESET_BRANCH_EXACT_DEFAULT_COLOR;
    }

    public class PresetBranchColorOption
    {
        public string Name
        {
            get;
        }

        public uint Color
        {
            get;
        }

        public IBrush Brush
        {
            get;
        }

        public PresetBranchColorOption(string name, uint color)
        {
            Name = name;
            Color = color;
            Brush = new SolidColorBrush(Avalonia.Media.Color.FromUInt32(color));
        }
    }
}
