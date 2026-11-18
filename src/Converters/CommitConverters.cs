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
        public static readonly FuncValueConverter<Models.Commit, Thickness> SubjectMargin =
            new(commit => new Thickness(commit is { IsCurrentHead: true } ? 12 : 5, 0));

        public static readonly IMultiValueConverter SHAForeground = new SHAColorConverter(false);

        public static readonly IMultiValueConverter SHABackground = new SHAColorConverter(true);

        private sealed class SHAColorConverter(bool background) : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                var commit = values.Count > 0 ? values[0] as Models.Commit : null;
                if (commit is { IsCurrentHead: true })
                {
                    var accent = Color.FromUInt32(values.Count > 1 && values[1] is uint color ? color : ViewModels.Preferences.Instance.MainAccentColor);
                    if (background)
                        return new SolidColorBrush(accent);
                    static double Linear(byte channel) => channel <= 10 ? channel / 3294.6 : Math.Pow((channel / 255.0 + 0.055) / 1.055, 2.4);
                    var luminance = 0.2126 * Linear(accent.R) + 0.7152 * Linear(accent.G) + 0.0722 * Linear(accent.B);
                    return luminance > 0.179 ? Brushes.Black : Brushes.White;
                }

                if (background)
                    return Brushes.Transparent;
                return commit is { IsMerged: true } ? FindBrush("Brush.FG1", s_currentBranchSHAForegroundBrush) : s_offBranchSHAForegroundBrush;
            }
        }

        public static readonly FuncValueConverter<Models.Commit, FontWeight> SHAFontWeight =
            new(commit => commit is { IsCurrentHead: true } ? FontWeight.Bold : commit is { IsMerged: true } ? FontWeight.Bold : FontWeight.Regular);

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
                    return FindBrush("Brush.History.HeadForeground", s_headForegroundBrush);

                return Application.Current?.FindResource("Brush.CommitSubject") as IBrush;
            });

        public static readonly FuncValueConverter<Models.Commit, IBrush> SubjectLinkToBrush =
            new(_ => Application.Current?.FindResource("Brush.FG1") as IBrush ?? Brushes.Black);

        public static readonly FuncValueConverter<Models.Commit, IBrush> HeadAwareForeground =
            new(commit => commit is { IsCurrentHead: true } ? FindBrush("Brush.History.HeadForeground", s_headForegroundBrush) : Application.Current?.FindResource("Brush.FG1") as IBrush);

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
                return isSelected ? FindBrush("Brush.History.HeadSelectedBackground", s_headSubjectSelectedBackground) :
                    FindBrush("Brush.History.HeadBackground", s_headSubjectBackground);
            }
        }

        private static IBrush FindBrush(string key, IBrush fallback) => Application.Current?.FindResource(key) as IBrush ?? fallback;

        private static readonly IBrush s_headSubjectBackground = new SolidColorBrush(Color.Parse("#FFFFE9E9"));
        private static readonly IBrush s_headSubjectSelectedBackground = new SolidColorBrush(Color.Parse("#FFFFC7C7"));
        private static readonly IBrush s_headForegroundBrush = new SolidColorBrush(Color.Parse("#FFD13438"));
        private static readonly IBrush s_currentBranchSHAForegroundBrush = new SolidColorBrush(Color.Parse("#FF22272E"));
        private static readonly IBrush s_offBranchSHAForegroundBrush = new SolidColorBrush(Color.Parse("#FFA4ABB3"));
    }
}
