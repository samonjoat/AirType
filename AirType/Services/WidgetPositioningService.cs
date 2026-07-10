using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using DpiScale = System.Windows.DpiScale;
using Point = System.Windows.Point;

namespace AirType.Services;

/// <summary>
/// Service for managing widget positioning across screens with DPI awareness.
/// Extracted from CapsuleWidget.xaml.cs for single responsibility.
/// </summary>
public class WidgetPositioningService : IWidgetPositioningService
{
    private const int DefaultBottomMargin = 10;
    private const double MinDpiScale = 0.1;
    private const double MaxDpiScale = 10.0;
    private const double SafeDefaultLeft = 100;
    private const double SafeDefaultTop = 100;
    private const double MinVisibilityPercentage = 0.25; // 25% of widget must be visible

    /// <summary>
    /// Gets the DPI scale for the specified visual element.
    /// </summary>
    public DpiScale GetDpiScale(Visual visual)
    {
        // Primary method: Use PresentationSource if available
        if (PresentationSource.FromVisual(visual) is { CompositionTarget: { } target })
        {
            var transform = target.TransformToDevice;
            var scaleX = ClampDpiScale(transform.M11);
            var scaleY = ClampDpiScale(transform.M22);
            return new DpiScale(scaleX, scaleY);
        }

        // Fallback method: Use VisualTreeHelper
        try
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            var scaleX = ClampDpiScale(dpi.DpiScaleX);
            var scaleY = ClampDpiScale(dpi.DpiScaleY);
            return new DpiScale(scaleX, scaleY);
        }
        catch
        {
            // Ultimate fallback: Use system DPI if available
            return GetSystemDpiScale();
        }
    }

    /// <summary>
    /// Gets the system DPI scale using GDI+.
    /// </summary>
    private static DpiScale GetSystemDpiScale()
    {
        try
        {
            using var graphics = Graphics.FromHwnd(IntPtr.Zero);
            var dpiX = graphics.DpiX / 96.0; // 96 DPI is 100% scaling
            var dpiY = graphics.DpiY / 96.0;
            var scaleX = ClampDpiScale(dpiX);
            var scaleY = ClampDpiScale(dpiY);
            return new DpiScale(scaleX, scaleY);
        }
        catch
        {
            // Final fallback: No scaling
            return new DpiScale(1.0, 1.0);
        }
    }

    /// <summary>
    /// Clamps a DPI scale value to a reasonable range.
    /// </summary>
    private static double ClampDpiScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return 1.0;
        }
        return Math.Max(MinDpiScale, Math.Min(MaxDpiScale, value));
    }

    /// <summary>
    /// Validates that a DPI scale is usable.
    /// </summary>
    private static bool IsValidDpiScale(DpiScale dpiScale)
    {
        return dpiScale.DpiScaleX > 0 && dpiScale.DpiScaleY > 0 &&
               !double.IsNaN(dpiScale.DpiScaleX) && !double.IsNaN(dpiScale.DpiScaleY) &&
               !double.IsInfinity(dpiScale.DpiScaleX) && !double.IsInfinity(dpiScale.DpiScaleY);
    }

    /// <summary>
    /// Converts a device point to DIPs (device-independent pixels).
    /// </summary>
    public Point ConvertDevicePointToDips(Point devicePoint, DpiScale dpiScale, Visual? visual)
    {
        // Validate input point
        if (double.IsNaN(devicePoint.X) || double.IsNaN(devicePoint.Y) ||
            double.IsInfinity(devicePoint.X) || double.IsInfinity(devicePoint.Y))
        {
            return new Point(0, 0); // Safe fallback
        }

        // Primary method: Use PresentationSource transform
        if (visual != null && PresentationSource.FromVisual(visual) is { CompositionTarget: { } target })
        {
            try
            {
                var result = target.TransformFromDevice.Transform(devicePoint);

                // Validate result
                if (!double.IsNaN(result.X) && !double.IsNaN(result.Y) &&
                    !double.IsInfinity(result.X) && !double.IsInfinity(result.Y))
                {
                    return result;
                }
            }
            catch
            {
                // Fall through to manual calculation
            }
        }

        // Fallback method: Manual DPI conversion
        if (!IsValidDpiScale(dpiScale))
        {
            return devicePoint; // Return as-is if DPI scale is invalid
        }

        try
        {
            var result = new Point(
                devicePoint.X / dpiScale.DpiScaleX,
                devicePoint.Y / dpiScale.DpiScaleY);

            // Validate result
            if (double.IsNaN(result.X) || double.IsNaN(result.Y) ||
                double.IsInfinity(result.X) || double.IsInfinity(result.Y))
            {
                return devicePoint; // Return original if calculation failed
            }

            return result;
        }
        catch
        {
            return devicePoint; // Return original point as ultimate fallback
        }
    }

    /// <summary>
    /// Calculates the center-bottom position for a widget on the specified screen.
    /// </summary>
    public WidgetPosition CalculateCenterBottomPosition(
        Screen screen,
        double widgetWidth,
        double widgetHeight,
        int bottomMargin,
        DpiScale dpiScale)
    {
        try
        {
            var widgetWidthPx = widgetWidth * dpiScale.DpiScaleX;
            var widgetHeightPx = widgetHeight * dpiScale.DpiScaleY;
            var bottomMarginPx = (int)Math.Round(bottomMargin * dpiScale.DpiScaleY);

            var position = ScreenMonitor.CalculateCenterBottomPosition(
                screen, widgetWidthPx, widgetHeightPx, bottomMarginPx);

            // Validate calculated position
            if (double.IsNaN(position.Left) || double.IsNaN(position.Top) ||
                double.IsInfinity(position.Left) || double.IsInfinity(position.Top))
            {
                return WidgetPosition.Invalid;
            }

            return new WidgetPosition(position.Left, position.Top);
        }
        catch
        {
            return WidgetPosition.Invalid;
        }
    }

    /// <summary>
    /// Checks if the specified position is visible on any available screen.
    /// </summary>
    public bool IsPositionOnAnyScreen(double left, double top, double width, double height)
    {
        try
        {
            var allScreens = Screen.AllScreens;
            if (allScreens == null || allScreens.Length == 0)
            {
                return false;
            }

            var widgetRight = left + width;
            var widgetBottom = top + height;
            var widgetCenterX = left + (width / 2);
            var widgetCenterY = top + (height / 2);
            var totalArea = width * height;

            foreach (var screen in allScreens)
            {
                var bounds = screen.Bounds;

                // Check if center is on this screen
                if (widgetCenterX >= bounds.Left && widgetCenterX <= bounds.Right &&
                    widgetCenterY >= bounds.Top && widgetCenterY <= bounds.Bottom)
                {
                    return true;
                }

                // Check if at least 25% of widget is visible on this screen
                var visibleLeft = Math.Max(left, bounds.Left);
                var visibleTop = Math.Max(top, bounds.Top);
                var visibleRight = Math.Min(widgetRight, bounds.Right);
                var visibleBottom = Math.Min(widgetBottom, bounds.Bottom);

                if (visibleRight > visibleLeft && visibleBottom > visibleTop)
                {
                    var visibleArea = (visibleRight - visibleLeft) * (visibleBottom - visibleTop);

                    if (visibleArea >= totalArea * MinVisibilityPercentage)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WidgetPositioning] Error checking position: {ex.Message}");
            return true; // Assume valid on error to avoid unnecessary repositioning
        }
    }

    /// <summary>
    /// Validates that the positioning system components are working correctly.
    /// </summary>
    public bool ValidatePositioningSystem(Visual visual, ScreenMonitor? screenMonitor)
    {
        try
        {
            // Test DPI scale retrieval
            var dpiScale = GetDpiScale(visual);
            if (!IsValidDpiScale(dpiScale))
            {
                return false;
            }

            // Test screen detection
            var activeScreen = screenMonitor?.GetActiveScreen() ?? Screen.PrimaryScreen;
            if (activeScreen == null)
            {
                return false;
            }

            // Test position calculation
            var testPosition = ScreenMonitor.CalculateCenterBottomPosition(
                activeScreen, 100, 30, 10);
            if (double.IsNaN(testPosition.Left) || double.IsNaN(testPosition.Top) ||
                double.IsInfinity(testPosition.Left) || double.IsInfinity(testPosition.Top))
            {
                return false;
            }

            // Test DIP conversion
            var testPoint = ConvertDevicePointToDips(new Point(100, 100), dpiScale, visual);
            if (double.IsNaN(testPoint.X) || double.IsNaN(testPoint.Y) ||
                double.IsInfinity(testPoint.X) || double.IsInfinity(testPoint.Y))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a position is reasonable.
    /// </summary>
    public bool ValidatePosition(double left, double top, double width, double height)
    {
        try
        {
            // Check if position values are valid
            if (double.IsNaN(left) || double.IsNaN(top) ||
                double.IsInfinity(left) || double.IsInfinity(top))
            {
                return false;
            }

            // Check if position is within reasonable screen bounds
            var allScreens = Screen.AllScreens;
            var tolerance = Math.Max(width, height);

            foreach (var screen in allScreens)
            {
                var bounds = screen.Bounds;
                // Allow some tolerance for widgets that might be partially off-screen
                if (left >= bounds.Left - tolerance && left <= bounds.Right + tolerance &&
                    top >= bounds.Top - tolerance && top <= bounds.Bottom + tolerance)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the primary screen fallback position.
    /// </summary>
    public WidgetPosition GetPrimaryScreenFallbackPosition(double widgetWidth, double widgetHeight, int bottomMargin)
    {
        try
        {
            var primaryScreen = Screen.PrimaryScreen;
            if (primaryScreen == null)
            {
                return GetSafeDefaultPosition();
            }

            var dpiScale = GetSystemDpiScale();
            var position = CalculateCenterBottomPosition(
                primaryScreen, widgetWidth, widgetHeight, bottomMargin, dpiScale);

            if (!position.IsValid)
            {
                // Ultimate fallback: center of primary screen
                var bounds = primaryScreen.Bounds;
                var left = bounds.Left + (bounds.Width - widgetWidth) / 2;
                var top = bounds.Bottom - widgetHeight - bottomMargin;
                return new WidgetPosition(left, top);
            }

            return position;
        }
        catch
        {
            return GetSafeDefaultPosition();
        }
    }

    /// <summary>
    /// Gets a safe default position when all else fails.
    /// </summary>
    public WidgetPosition GetSafeDefaultPosition()
    {
        return new WidgetPosition(SafeDefaultLeft, SafeDefaultTop);
    }
}
