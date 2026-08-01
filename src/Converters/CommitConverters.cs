using System;
using System.Collections.Generic;
using System.Globalization;
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
                if (commit is { IsCurrentHead: true })
                    return new SolidColorBrush(Color.Parse("#FFD13438"));

                if (commit is { HasSubmodulePointerChange: true })
                    return new SolidColorBrush(Color.Parse("#FF7C3AED"));

                return Application.Current?.FindResource("Brush.FG1") as IBrush;
            });

        public static readonly FuncValueConverter<Models.Commit, FontWeight> SHAFontWeight =
            new(commit => commit is { IsCurrentHead: true } ? FontWeight.Bold : FontWeight.Regular);

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
                    return s_headForegroundBrush;

                return Application.Current?.FindResource("Brush.CommitSubject") as IBrush;
            });

        public static readonly FuncValueConverter<Models.Commit, IBrush> SubjectLinkToBrush =
            new(commit =>
            {
                if (commit is { IsCurrentHead: true })
                    return s_headForegroundBrush;

                return Application.Current?.FindResource("Brush.Link") as IBrush;
            });

        public static readonly FuncValueConverter<Models.Commit, IBrush> HeadAwareForeground =
            new(commit => commit is { IsCurrentHead: true } ? s_headForegroundBrush : Application.Current?.FindResource("Brush.FG1") as IBrush);

        public static readonly FuncValueConverter<Models.Commit, FontWeight> SubjectFontWeight =
            new(commit => commit is { IsCurrentHead: true } ? FontWeight.Bold : FontWeight.Regular);

        public static readonly IMultiValueConverter HeadSubjectBackground =
            new HeadSubjectBackgroundConverter();

        private sealed class HeadSubjectBackgroundConverter : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values.Count == 0 || values[0] is not Models.Commit { IsCurrentHead: true })
                    return Brushes.Transparent;

                var isSelected = values.Count > 1 && values[1] is true;
                return isSelected ? s_headSubjectSelectedBackground : s_headSubjectBackground;
            }
        }

        private static readonly IBrush s_headSubjectBackground = new SolidColorBrush(Color.Parse("#FFFFE9E9"));
        private static readonly IBrush s_headSubjectSelectedBackground = new SolidColorBrush(Color.Parse("#FFFFC7C7"));
        private static readonly IBrush s_headForegroundBrush = new SolidColorBrush(Color.Parse("#FFD13438"));
    }
}
