using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace AirType.Services;

/// <summary>
/// Service for managing widget positioning across screens with DPI awareness.
/// </summary>
public interface IWidgetPositioningService
{
    /// <summary>
    /// Gets the DPI scale for the specified visual element.
    /// </summary>
    DpiScale GetDpiScale(Visual visual);

    /// <summary>
    /// Converts a device point to DIPs (device-independent pixels).
    /// </summary>
    System.Windows.Point ConvertDevicePointToDips(System.Windows.Point devicePoint, DpiScale dpiScale, Visual? visual);

    /// <summary>
    /// Calculates the center-bottom position for a widget on the specified screen.
    /// </summary>
    WidgetPosition CalculateCenterBottomPosition(
        Screen screen,
        double widgetWidth,
        double widgetHeight,
        int bottomMargin,
        DpiScale dpiScale);

    /// <summary>
    /// Checks if the specified position is visible on any available screen.
    /// </summary>
    bool IsPositionOnAnyScreen(double left, double top, double width, double height);

    /// <summary>
    /// Validates that the positioning system components are working correctly.
    /// </summary>
    bool ValidatePositioningSystem(Visual visual, ScreenMonitor? screenMonitor);

    /// <summary>
    /// Validates that a position is reasonable (not NaN, not Infinity, within screen bounds).
    /// </summary>
    bool ValidatePosition(double left, double top, double width, double height);

    /// <summary>
    /// Gets the primary screen fallback position.
    /// </summary>
    WidgetPosition GetPrimaryScreenFallbackPosition(double widgetWidth, double widgetHeight, int bottomMargin);

    /// <summary>
    /// Gets a safe default position when all else fails.
    /// </summary>
    WidgetPosition GetSafeDefaultPosition();
}

/// <summary>
/// Represents a calculated widget position.
/// </summary>
public readonly struct WidgetPosition
{
    public double Left { get; }
    public double Top { get; }
    public bool IsValid { get; }

    public WidgetPosition(double left, double top, bool isValid = true)
    {
        Left = left;
        Top = top;
        IsValid = isValid;
    }

    public static WidgetPosition Invalid => new(0, 0, false);
}
