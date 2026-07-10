using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AirType.Converters;

public sealed class ApiKeyStatusDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Normalize(value) switch
        {
            "Connected" => "Connected",
            "Configured" => "Configured",
            "Error" => "Error",
            _ => "Not set"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    internal static string Normalize(object? value)
    {
        var text = value?.ToString()?.Trim();
        return text switch
        {
            "Connected" => "Connected",
            "Configured" => "Configured",
            "Error" => "Error",
            "Not set" => "NotConfigured",
            "NotConfigured" => "NotConfigured",
            _ => "NotConfigured"
        };
    }
}

public sealed class ApiKeyStatusKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return ApiKeyStatusDisplayConverter.Normalize(value);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
