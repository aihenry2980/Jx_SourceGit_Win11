using Avalonia;
using Avalonia.Data.Converters;
using System;

namespace SourceGit.Converters
{
    public static class DoubleConverters
    {
        public static readonly FuncValueConverter<double, double> Increase =
            new FuncValueConverter<double, double>(v => v + 1.0);

        public static readonly FuncValueConverter<double, double> Decrease =
            new FuncValueConverter<double, double>(v => v - 1.0);

        public static readonly FuncValueConverter<double, double> ToRuleChipMaxWidth =
            new FuncValueConverter<double, double>(v => Math.Max(48.0, v - 4.0));

        public static readonly FuncValueConverter<double, double> ToHistoryBadgeFontSize =
            new FuncValueConverter<double, double>(v => Math.Max(9.0, v - 3.0));

        public static readonly FuncValueConverter<double, double> ToHistoryBadgeHeight =
            new FuncValueConverter<double, double>(v => Math.Max(15.0, v + 2.0));

        public static readonly FuncValueConverter<double, double> ToHistoryBadgeIconSize =
            new FuncValueConverter<double, double>(v => Math.Max(7.0, v * 0.55));

        public static readonly FuncValueConverter<double, double> ToHeadSubjectHighlightHeight =
            new FuncValueConverter<double, double>(v => Math.Max(14.0, v * 0.6));

        public static readonly FuncValueConverter<double, string> ToPercentage =
            new FuncValueConverter<double, string>(v => (v * 100).ToString("F0") + "%");

        public static readonly FuncValueConverter<double, string> OneMinusToPercentage =
            new FuncValueConverter<double, string>(v => ((1.0 - v) * 100).ToString("F0") + "%");

        public static readonly FuncValueConverter<double, Thickness> ToLeftMargin =
            new FuncValueConverter<double, Thickness>(v => new Thickness(v, 0, 0, 0));
    }
}
