using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AirType.Converters;

/// <summary>
/// Multi-value converter that checks if a transcription entry is being edited.
/// Takes two values: current entry ID (Guid) and editing entry ID (Guid?).
/// Returns true if they match (this entry is being edited).
/// </summary>
public class IsBeingEditedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return Visibility.Collapsed;

        // Value 1: The ID of the current card (from the entry)
        if (values[0] is not Guid entryId) return Visibility.Collapsed;

        // Value 2: The ID being edited (from the ViewModel)
        bool isEditing = false;
        if (values[1] is Guid editingId)
        {
            isEditing = editingId == entryId;
        }

        // Handle Inverse for visibility (used to hide things when editing)
        bool inverse = parameter?.ToString() == "Inverse";
        bool result = inverse ? !isEditing : isEditing;

        if (targetType == typeof(Visibility))
        {
            return result ? Visibility.Visible : Visibility.Collapsed;
        }

        return result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
