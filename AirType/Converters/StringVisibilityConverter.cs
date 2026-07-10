using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AirType.Converters
{
    public class StringVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && parameter?.ToString() == "0")
            {
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
