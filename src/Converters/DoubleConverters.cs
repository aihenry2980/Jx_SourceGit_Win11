using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class DoubleConverters
    {
        public static readonly IMultiValueConverter ToBranchHistoryRowHeight = new BranchHistoryRowHeightConverter();

        private sealed class BranchHistoryRowHeightConverter : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                var fontSize = values.Count > 0 && values[0] is double size ? size : 13.0;
                var height = Math.Max(16.0, fontSize + 10.0);
                var family = values.Count > 1 && values[1] is FontFamily font ? font : FontFamily.Default;
                var text = new FormattedText("Ag\nAg", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(family, FontStyle.Normal, FontWeight.Bold), fontSize, Brushes.Black);
                return Math.Max(height, Math.Ceiling(text.Height + 7));
            }
        }

        public static readonly FuncValueConverter<double, bool> ShowHistoryToolbarLabels =
            new(v => v >= 1100);

        public static readonly FuncValueConverter<double, double> Increase =
            new FuncValueConverter<double, double>(v => v + 1.0);

        public static readonly FuncValueConverter<double, double> Decrease =
            new FuncValueConverter<double, double>(v => v - 1.0);

        public static readonly FuncValueConverter<double, double> ToRuleChipMaxWidth =
            new FuncValueConverter<double, double>(v => Math.Max(48.0, v - 4.0));

        public static readonly FuncValueConverter<double, double> ToHistoryBadgeFontSize =
            new FuncValueConverter<double, double>(v => Math.Max(8.0, v - 4.0));

        public static readonly FuncValueConverter<double, double> ToHistoryBadgeHeight =
            new FuncValueConverter<double, double>(v => Math.Max(15.0, v + 2.0));

        public static readonly FuncValueConverter<double, double> ToHistoryBadgeIconSize =
            new FuncValueConverter<double, double>(v => Math.Max(7.0, v * 0.55));

        public static readonly FuncValueConverter<double, double> ToHeadSubjectHighlightHeight =
            new FuncValueConverter<double, double>(v => Math.Max(14.0, v * 0.6));

        public static readonly FuncValueConverter<double, double> ToHeadOutlineHeight =
            new(v => Math.Max(0, v - 2));

        public static readonly FuncValueConverter<double, string> ToPercentage =
            new FuncValueConverter<double, string>(v => (v * 100).ToString("F0") + "%");

        public static readonly FuncValueConverter<double, string> OneMinusToPercentage =
            new FuncValueConverter<double, string>(v => ((1.0 - v) * 100).ToString("F0") + "%");

        public static readonly FuncValueConverter<double, Thickness> ToLeftMargin =
            new FuncValueConverter<double, Thickness>(v => new Thickness(v, 0, 0, 0));
    }
}
