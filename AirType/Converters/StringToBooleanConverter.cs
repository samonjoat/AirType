using System;
using System.Globalization;
using System.Windows.Data;

namespace AirType.Converters;

public class StringToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? parameter! : Binding.DoNothing;
    }
}
