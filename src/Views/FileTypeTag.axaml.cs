using Avalonia;
using Avalonia.Controls;

namespace SourceGit.Views
{
    public partial class FileTypeTag : UserControl
    {
        public static readonly StyledProperty<string> FilePathProperty =
            AvaloniaProperty.Register<FileTypeTag, string>(nameof(FilePath), string.Empty);

        public string FilePath
        {
            get => GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        public FileTypeTag()
        {
            InitializeComponent();
        }
    }
}
