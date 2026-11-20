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
                    return GetAccentForeground(accent.ToUInt32());
                }

                if (background)
                    return Brushes.Transparent;
                return commit is { IsMerged: true } ? FindBrush("Brush.FG1", s_currentBranchSHAForegroundBrush) : s_offBranchSHAForegroundBrush;
            }
        }

        public static readonly FuncValueConverter<Models.Commit, FontWeight> SHAFontWeight =
            new(commit => commit is { IsCurrentHead: true } ? FontWeight.Bold : commit is { IsMerged: true } ? FontWeight.Bold : FontWeight.Regular);

        public static readonly FuncValueConverter<Models.Commit, double> SHAOpacity =
            new(commit => commit is { IsCurrentHead: true } or { IsMerged: true } ? 1.0 : 0.65);

        public static readonly IMultiValueConverter HistoryForeground = new HistoryForegroundConverter();
        public static readonly IMultiValueConverter RowBackground = new RowBackgroundConverter();

        private sealed class HistoryForegroundConverter : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values.Count > 0 && values[0] is Models.Commit { IsCurrentHead: true })
                    return GetHeadForeground(values.Count > 1 && values[1] is uint color ? color : ViewModels.Preferences.Instance.MainAccentColor);
                return values.Count > 2 && values[2] is IBrush fallback ? fallback : FindBrush("Brush.FG1", Brushes.Black);
            }
        }

        private sealed class RowBackgroundConverter : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values.Count > 0 && values[0] is Models.Commit { IsCurrentHead: true })
                    return new SolidColorBrush(GetHeadBackground(values.Count > 2 && values[2] is uint color ? color : ViewModels.Preferences.Instance.MainAccentColor));
                return values.Count > 1 && values[1] is true
                    ? FindBrush("Brush.History.SelectionBackground", Brushes.LightGray) : Brushes.Transparent;
            }
        }

        internal static Color GetHeadBackground(uint value)
        {
            var accent = Color.FromUInt32(value);
            // A pale tint leaves the submodule badges' identity colors readable.
            static byte Tint(byte channel) => (byte)Math.Round(255 * 0.85 + channel * 0.15);
            return Color.FromRgb(Tint(accent.R), Tint(accent.G), Tint(accent.B));
        }

        internal static IBrush GetHeadForeground(uint value) => GetAccentForeground(GetHeadBackground(value).ToUInt32());

        internal static IBrush GetAccentForeground(uint value)
        {
            var accent = Color.FromUInt32(value);
            static double Linear(byte channel) => channel <= 10 ? channel / 3294.6 : Math.Pow((channel / 255.0 + 0.055) / 1.055, 2.4);
            var luminance = 0.2126 * Linear(accent.R) + 0.7152 * Linear(accent.G) + 0.0722 * Linear(accent.B);
            return luminance > 0.179 ? Brushes.Black : Brushes.White;
        }

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
                    return s_headSubjectForegroundBrush;

                return Application.Current?.FindResource("Brush.CommitSubject") as IBrush;
            });

        public static readonly FuncValueConverter<Models.Commit, IBrush> SubjectLinkToBrush =
            new(_ => Application.Current?.FindResource("Brush.FG1") as IBrush ?? Brushes.Black);

        public static readonly FuncValueConverter<Models.Commit, IBrush> HeadAwareForeground =
            new(commit => commit is { IsCurrentHead: true } ? GetHeadForeground(ViewModels.Preferences.Instance.MainAccentColor) : Application.Current?.FindResource("Brush.FG1") as IBrush);

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

                return new SolidColorBrush(GetHeadBackground(ViewModels.Preferences.Instance.MainAccentColor));
            }
        }

        private static IBrush FindBrush(string key, IBrush fallback) => Application.Current?.FindResource(key) as IBrush ?? fallback;

        private static readonly IBrush s_currentBranchSHAForegroundBrush = new SolidColorBrush(Color.Parse("#FF22272E"));
        private static readonly IBrush s_offBranchSHAForegroundBrush = new SolidColorBrush(Color.Parse("#FFA4ABB3"));
        private static readonly IBrush s_headSubjectForegroundBrush = new SolidColorBrush(Color.Parse("#FF5C5C5C"));
    }
}
