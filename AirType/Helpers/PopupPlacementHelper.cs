using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AirType.Helpers;

/// <summary>
/// Attached property for ComboBox that auto-scrolls the parent ScrollViewer
/// when a dropdown opens near the bottom of the visible area.
///
/// Strategy: On DropDownOpened, if scrolling is needed, close the dropdown,
/// scroll, then reopen on the next dispatcher frame so the Popup recalculates
/// its screen position from the post-scroll layout.
/// </summary>
public static class PopupPlacementHelper
{
    private const double DropdownEstimatedHeight = 230;
    private const double BottomPadding = 16;

    /// <summary>
    /// Guard flag to prevent the reopen from triggering the scroll logic again.
    /// </summary>
    private static bool _isRepositioning;

    public static readonly DependencyProperty AutoScrollIntoViewProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollIntoView",
            typeof(bool),
            typeof(PopupPlacementHelper),
            new PropertyMetadata(false, OnAutoScrollIntoViewChanged));

    public static bool GetAutoScrollIntoView(DependencyObject obj) => (bool)obj.GetValue(AutoScrollIntoViewProperty);
    public static void SetAutoScrollIntoView(DependencyObject obj, bool value) => obj.SetValue(AutoScrollIntoViewProperty, value);

    // Keep the old AutoFlip property name working for backward compatibility
    public static readonly DependencyProperty AutoFlipProperty =
        DependencyProperty.RegisterAttached(
            "AutoFlip",
            typeof(bool),
            typeof(PopupPlacementHelper),
            new PropertyMetadata(false, OnAutoScrollIntoViewChanged));

    public static bool GetAutoFlip(DependencyObject obj) => (bool)obj.GetValue(AutoFlipProperty);
    public static void SetAutoFlip(DependencyObject obj, bool value) => obj.SetValue(AutoFlipProperty, value);

    private static void OnAutoScrollIntoViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox && (bool)e.NewValue)
        {
            comboBox.DropDownOpened += OnDropDownOpened;
        }
    }

    private static void OnDropDownOpened(object? sender, System.EventArgs e)
    {
        if (_isRepositioning)
            return;

        if (sender is not ComboBox comboBox)
            return;

        var scrollViewer = FindParent<ScrollViewer>(comboBox);
        if (scrollViewer == null)
            return;

        // Get the ComboBox's position relative to the ScrollViewer
        var comboBoxPos = comboBox.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));

        // Calculate how much space is needed: ComboBox bottom + dropdown height + padding
        double neededBottom = comboBoxPos.Y + comboBox.ActualHeight + DropdownEstimatedHeight + BottomPadding;
        double visibleHeight = scrollViewer.ViewportHeight;

        // If enough space exists, do nothing — the common case has zero overhead
        if (neededBottom <= visibleHeight)
            return;

        double scrollBy = neededBottom - visibleHeight;

        // Close the dropdown, scroll, then reopen on the next frame
        comboBox.IsDropDownOpen = false;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollBy);
        scrollViewer.UpdateLayout();

        comboBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _isRepositioning = true;
            try
            {
                comboBox.IsDropDownOpen = true;
            }
            finally
            {
                _isRepositioning = false;
            }
        });
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is T found)
                return found;
        }
        return null;
    }
}
