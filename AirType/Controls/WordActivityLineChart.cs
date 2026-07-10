using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AirType.Controls;

public class WordActivityLineChart : Control
{
    private Canvas? _canvas;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(WordActivityLineChart),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    static WordActivityLineChart()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WordActivityLineChart), new FrameworkPropertyMetadata(typeof(WordActivityLineChart)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _canvas = GetTemplateChild("PART_ChartCanvas") as Canvas;
        SizeChanged += (_, _) => RedrawChart();
        RedrawChart();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new WordActivityLineChartAutomationPeer(this);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WordActivityLineChart chart)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= chart.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += chart.OnCollectionChanged;
        }

        chart.RedrawChart();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawChart();
    }

    private void RedrawChart()
    {
        if (_canvas == null)
        {
            return;
        }

        _canvas.Children.Clear();

        var values = ReadValues();
        if (values.Count < 2)
        {
            return;
        }

        var width = ActualWidth > 0 ? ActualWidth : 848;
        var height = ActualHeight > 0 ? ActualHeight : 108;
        const double horizontalPadding = 12;
        const double verticalPadding = 8;
        var usableWidth = Math.Max(1, width - (horizontalPadding * 2));
        var usableHeight = Math.Max(1, height - (verticalPadding * 2));
        var max = Math.Max(1, values.Max());

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var x = horizontalPadding + (usableWidth * i / (values.Count - 1));
                var y = verticalPadding + usableHeight - (usableHeight * values[i] / max);
                var point = new Point(x, y);

                if (i == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }
        geometry.Freeze();

        var line = new Path
        {
            Data = geometry,
            Stroke = (Brush)FindResource("ChartAccentBrush"),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true
        };

        _canvas.Children.Add(line);
    }

    private List<double> ReadValues()
    {
        if (ItemsSource == null)
        {
            return new List<double>();
        }

        return ItemsSource
            .Cast<object>()
            .Select(GetValueFromItem)
            .ToList();
    }

    private static double GetValueFromItem(object item)
    {
        return item switch
        {
            double d => Math.Max(0, d),
            float f => Math.Max(0, f),
            int i => Math.Max(0, i),
            long l => Math.Max(0, l),
            _ => 0
        };
    }

    internal string GetAutomationSummary() => WordActivityLineChartAutomationSummary.DescribeValues(ReadValues());
}

internal sealed class WordActivityLineChartAutomationPeer : FrameworkElementAutomationPeer
{
    public WordActivityLineChartAutomationPeer(WordActivityLineChart owner)
        : base(owner)
    {
    }

    protected override string GetClassNameCore() => nameof(WordActivityLineChart);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

    protected override string GetNameCore()
    {
        string configuredName = base.GetNameCore();
        return string.IsNullOrWhiteSpace(configuredName)
            ? ((WordActivityLineChart)Owner).GetAutomationSummary()
            : configuredName;
    }
}
