using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AirType.Converters;

/// <summary>
/// Hides day separators when their day is currently displayed in the sticky day catcher.
/// This creates a seamless visual experience where each day label appears only once.
/// </summary>
public class DaySeparatorVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return Visibility.Visible;

        // Check for unset bindings (happens during virtualization)
        if (values[0] == DependencyProperty.UnsetValue || 
            values[1] == DependencyProperty.UnsetValue || 
            values[2] == DependencyProperty.UnsetValue)
        {
            return Visibility.Collapsed;
        }

        // Value 0: This entry's date label (e.g., "TODAY", "YESTERDAY")
        string entryDateLabel = values[0] as string ?? string.Empty;
        
        // Value 1: Current day showing in the sticky catcher
        string catcherDay = values[1] as string ?? string.Empty;
        
        // Value 2: Is this entry the first of its day (has a separator)?
        bool isFirstOfDay = values[2] is bool b && b;

        // Hide the separator if:
        // 1. This entry has a day separator (isFirstOfDay = true)
        // 2. AND its day matches the day currently captured in the sticky header
        if (isFirstOfDay && !string.IsNullOrEmpty(entryDateLabel) && entryDateLabel == catcherDay)
        {
            return Visibility.Collapsed; // Hide - day is captured!
        }

        // Otherwise, show normally (for non-first entries or different days)
        return isFirstOfDay ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
