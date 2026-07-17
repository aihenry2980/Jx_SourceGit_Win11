using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class CommitConverters
    {
        public static readonly FuncValueConverter<Models.Commit, IBrush> SHAForeground =
            new(commit =>
            {
                if (commit is { HasSubmodulePointerChange: true })
                    return new SolidColorBrush(Color.Parse("#FF7C3AED"));

                return Application.Current?.FindResource("Brush.FG1") as IBrush;
            });

        public static readonly FuncValueConverter<Models.Commit, FontWeight> SHAFontWeight =
            new(_ => FontWeight.Regular);

        public static readonly FuncValueConverter<Models.Commit, string> SHAToolTip =
            new(commit => commit?.HistoryChangeSummaryToolTip ?? string.Empty);

        public static readonly FuncValueConverter<uint, IBrush> UInt32ToBrush =
            new(value => new SolidColorBrush(Color.FromUInt32(value)));

        public static readonly FuncValueConverter<uint, IBrush> UInt32ToSubtleBrush =
            new(value =>
            {
                var color = Color.FromUInt32(value);
                return new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
            });

        public static readonly FuncValueConverter<Models.Commit, IBrush> SubjectToBrush =
            new(commit =>
            {
                if (commit is { IsCurrentHead: true })
                    return Brushes.Red;
                if (commit is { IsSuperProjectPointer: true })
                    return Brushes.Purple;
                return Application.Current?.FindResource("Brush.FG1") as IBrush;
            });
    }
}
