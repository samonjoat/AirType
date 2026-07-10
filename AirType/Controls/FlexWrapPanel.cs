using System;
using System.Windows;
using System.Windows.Controls;

namespace AirType.Controls;

/// <summary>
/// A panel that mimics CSS flexbox with flex-wrap and flex: 1 behavior.
/// Items grow to fill available width, wrap based on MinItemWidth,
/// and each row's items share equal width.
/// </summary>
public class FlexWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(nameof(MinItemWidth), typeof(double),
            typeof(FlexWrapPanel), new FrameworkPropertyMetadata(200.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemSpacingProperty =
        DependencyProperty.Register(nameof(ItemSpacing), typeof(double),
            typeof(FlexWrapPanel), new FrameworkPropertyMetadata(12.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double),
            typeof(FlexWrapPanel), new FrameworkPropertyMetadata(12.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Minimum width for each item. Items will wrap to next row if they can't fit.
    /// </summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>
    /// Horizontal spacing between items in the same row.
    /// </summary>
    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>
    /// Vertical spacing between rows.
    /// </summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Width))
        {
            // Can't calculate flex layout without a width constraint
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(availableSize);
            }
            return base.MeasureOverride(availableSize);
        }

        // Calculate how many items fit per row
        int itemsPerRow = Math.Max(1, (int)((availableSize.Width + ItemSpacing) / (MinItemWidth + ItemSpacing)));
        double itemWidth = (availableSize.Width - (itemsPerRow - 1) * ItemSpacing) / itemsPerRow;

        double totalHeight = 0;
        double rowHeight = 0;
        int itemsInCurrentRow = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            itemsInCurrentRow++;

            if (itemsInCurrentRow >= itemsPerRow)
            {
                totalHeight += rowHeight + RowSpacing;
                rowHeight = 0;
                itemsInCurrentRow = 0;
            }
        }

        // Add last row height
        if (itemsInCurrentRow > 0)
            totalHeight += rowHeight;
        else if (totalHeight > 0)
            totalHeight -= RowSpacing; // Remove extra spacing

        return new Size(availableSize.Width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
            return finalSize;

        // Calculate items per row based on MinItemWidth
        int itemsPerRow = Math.Max(1, (int)((finalSize.Width + ItemSpacing) / (MinItemWidth + ItemSpacing)));

        double y = 0;
        int index = 0;

        while (index < InternalChildren.Count)
        {
            // Determine how many items in this row
            int itemsThisRow = Math.Min(itemsPerRow, InternalChildren.Count - index);

            // Calculate item width for this row (items stretch to fill)
            double itemWidth = (finalSize.Width - (itemsThisRow - 1) * ItemSpacing) / itemsThisRow;

            // Find row height
            double rowHeight = 0;
            for (int i = 0; i < itemsThisRow; i++)
            {
                rowHeight = Math.Max(rowHeight, InternalChildren[index + i].DesiredSize.Height);
            }

            // Arrange items in this row
            double x = 0;
            for (int i = 0; i < itemsThisRow; i++)
            {
                var child = InternalChildren[index + i];
                child.Arrange(new Rect(x, y, itemWidth, rowHeight));
                x += itemWidth + ItemSpacing;
            }

            y += rowHeight + RowSpacing;
            index += itemsThisRow;
        }

        return finalSize;
    }
}
