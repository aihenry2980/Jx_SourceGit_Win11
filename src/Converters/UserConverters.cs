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
                return ViewModels.Preferences.Instance.GetUserNameBrush(user?.Email) ??
                    (values.Count > 2 ? values[2] as IBrush : null) ?? Brushes.Gray;
            }
        }
    }
}
