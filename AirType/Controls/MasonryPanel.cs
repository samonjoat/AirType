using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AirType.Controls;

public class MasonryPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(nameof(MinColumnWidth), typeof(double), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(420.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Width))
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(MinColumnWidth, double.PositiveInfinity));
            }

            return new Size(MinColumnWidth, InternalChildren.Cast<UIElement>().Sum(child => child.DesiredSize.Height));
        }

        var columnCount = GetColumnCount(availableSize.Width);
        var columnWidth = GetColumnWidth(availableSize.Width, columnCount);
        var columnHeights = new double[columnCount];

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            var columnIndex = GetShortestColumnIndex(columnHeights);
            if (columnHeights[columnIndex] > 0)
            {
                columnHeights[columnIndex] += RowSpacing;
            }

            columnHeights[columnIndex] += child.DesiredSize.Height;
        }

        return new Size(availableSize.Width, columnHeights.Length == 0 ? 0 : columnHeights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columnCount = GetColumnCount(finalSize.Width);
        var columnWidth = GetColumnWidth(finalSize.Width, columnCount);
        var columnHeights = new double[columnCount];

        foreach (UIElement child in InternalChildren)
        {
            var columnIndex = GetShortestColumnIndex(columnHeights);
            if (columnHeights[columnIndex] > 0)
            {
                columnHeights[columnIndex] += RowSpacing;
            }

            var x = columnIndex * (columnWidth + ColumnSpacing);
            var y = columnHeights[columnIndex];
            child.Arrange(new Rect(x, y, columnWidth, child.DesiredSize.Height));
            columnHeights[columnIndex] += child.DesiredSize.Height;
        }

        return finalSize;
    }

    private int GetColumnCount(double availableWidth)
    {
        if (availableWidth <= 0)
        {
            return 1;
        }

        var maxByWidth = Math.Max(1, (int)((availableWidth + ColumnSpacing) / (MinColumnWidth + ColumnSpacing)));
        return Math.Max(1, Math.Min(MaxColumns, maxByWidth));
    }

    private double GetColumnWidth(double availableWidth, int columnCount)
    {
        return (availableWidth - (columnCount - 1) * ColumnSpacing) / columnCount;
    }

    private static int GetShortestColumnIndex(double[] columnHeights)
    {
        var index = 0;
        for (var i = 1; i < columnHeights.Length; i++)
        {
            if (columnHeights[i] < columnHeights[index])
            {
                index = i;
            }
        }

        return index;
    }
}
