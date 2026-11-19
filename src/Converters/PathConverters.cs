using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class PathConverters
    {
        public static readonly FuncValueConverter<string, string> PureFileName =
            new(v => Path.GetFileName(v) ?? "");

        public static readonly FuncValueConverter<string, string> PureDirectoryName =
            new(v => Path.GetDirectoryName(v) ?? "");

        public static readonly FuncValueConverter<string, string> RelativeToHome =
            new(Native.OS.GetRelativePathToHome);

        public static readonly FuncValueConverter<string, string> FileTypeLabel = new(GetFileTypeLabel);

        public static readonly IMultiValueConverter FileTypeBackground = new FileTypeBrushConverter(false);
        public static readonly IMultiValueConverter FileTypeForeground = new FileTypeBrushConverter(true);

        private sealed class FileTypeBrushConverter(bool foreground) : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                var rule = ViewModels.Preferences.Instance.FindFileTypeColor(values.Count > 0 ? values[0] as string : null);
                return foreground ? rule?.ForegroundBrush ?? Brushes.White : rule?.BackgroundBrush ?? s_otherBackground;
            }
        }

        public static readonly FuncValueConverter<Models.Change, bool> ShowFileTypeTag =
            new(change => change is { IsSubmodulePointerChange: false });

        private static string GetFileTypeLabel(string path)
        {
            var extension = Models.FileTypeColor.GetExtensionKey(path);
            return extension.Length == 0 ? "FILE" : extension;
        }

        private static readonly IBrush s_otherBackground = new SolidColorBrush(Color.FromUInt32(Models.FileTypeColor.DEFAULT_COLOR));
    }
}
