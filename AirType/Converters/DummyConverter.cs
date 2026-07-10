using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;

namespace AirType.Converters;

public class DummyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Return a static list of providers for the provider ComboBox
        return new ObservableCollection<string> { "Gemini", "OpenRouter", "Groq" };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
