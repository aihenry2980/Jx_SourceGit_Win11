using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class UserConverters
    {
        public static readonly IMultiValueConverter NameForeground = new NameForegroundConverter();

        private sealed class NameForegroundConverter : IMultiValueConverter
        {
            public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
            {
                var user = values.Count > 0 ? values[0] as Models.User : null;
                var fallback = values.Count > 2 ? values[2] as IBrush : null;
                if (values.Count > 4 && values[3] is Models.Commit { IsCurrentHead: true } && values[4] is uint accent)
                    fallback = CommitConverters.GetHeadForeground(accent);
                return ViewModels.Preferences.Instance.GetUserNameBrush(user?.Email) ??
                    fallback ?? Brushes.Gray;
            }
        }
    }
}
