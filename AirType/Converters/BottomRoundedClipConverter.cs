using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AirType.Converters;

/// <summary>
/// Takes ActualWidth and ActualHeight and returns a PathGeometry
/// with sharp top corners and rounded bottom corners (13px radius)
/// for clipping dropdown content.
/// </summary>
public class BottomRoundedClipConverter : IMultiValueConverter
{
    private const double Radius = 13;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2
            && values[0] is double width
            && values[1] is double height
            && width > 0 && height > 0)
        {
            // Build path: sharp top-left → sharp top-right → rounded bottom-right → rounded bottom-left
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(width, 0), isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(width, height - Radius), isStroked: false, isSmoothJoin: false);
                ctx.ArcTo(new Point(width - Radius, height), new Size(Radius, Radius),
                    rotationAngle: 0, isLargeArc: false, sweepDirection: SweepDirection.Clockwise,
                    isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(Radius, height), isStroked: false, isSmoothJoin: false);
                ctx.ArcTo(new Point(0, height - Radius), new Size(Radius, Radius),
                    rotationAngle: 0, isLargeArc: false, sweepDirection: SweepDirection.Clockwise,
                    isStroked: false, isSmoothJoin: false);
            }
            geometry.Freeze();
            return geometry;
        }

        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
