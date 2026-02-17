using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class CommitConverters
    {
        public static readonly FuncValueConverter<Models.Commit, IBrush> SubjectToBrush =
            new(commit =>
            {
                if (commit is { IsCurrentHead: true })
                    return Brushes.Red;
                if (commit is { IsSuperProjectPointer: true })
                    return Brushes.DodgerBlue;
                return Application.Current?.FindResource("Brush.FG1") as IBrush;
            });
    }
}
