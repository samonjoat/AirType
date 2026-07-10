using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace AirType.Controls
{
    public class LatencyChart : Control
    {
        private const double MinimumSegmentWidth = 42;
        private const double MinimumCompressedSegmentWidth = 32;
        private Grid? _internalGrid;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(LatencyChart),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        static LatencyChart()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LatencyChart), new FrameworkPropertyMetadata(typeof(LatencyChart)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_internalGrid != null)
            {
                _internalGrid.SizeChanged -= OnLatencyGridSizeChanged;
            }

            _internalGrid = GetTemplateChild("PART_LatencyGrid") as Grid;

            if (_internalGrid != null)
            {
                _internalGrid.SizeChanged += OnLatencyGridSizeChanged;
            }

            RedrawChart();
        }

        protected override AutomationPeer OnCreateAutomationPeer() =>
            new LatencyChartAutomationPeer(this);

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LatencyChart chart)
            {
                // Unsubscribe from old
                if (e.OldValue is INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= chart.OnCollectionChanged;
                }

                // Subscribe to new
                if (e.NewValue is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += chart.OnCollectionChanged;
                }

                chart.RedrawChart();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RedrawChart();
        }

        private void OnLatencyGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (System.Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            {
                RedrawChart();
            }
        }

        private void RedrawChart()
        {
            if (_internalGrid == null || ItemsSource == null) return;

            _internalGrid.ColumnDefinitions.Clear();
            _internalGrid.Children.Clear();

            var list = ItemsSource.Cast<object>().ToList();
            if (list.Count == 0) return;

            var values = list.Select(item => System.Math.Max(GetValueFromItem(item), 0.01)).ToList();
            var widths = CalculateSegmentWidths(values, _internalGrid.ActualWidth);

            for (int i = 0; i < list.Count; i++)
            {
                double val = values[i];

                _internalGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = widths.Count == list.Count
                        ? new GridLength(widths[i], GridUnitType.Pixel)
                        : new GridLength(val, GridUnitType.Star)
                });

                // Create the segment border
                var segment = new Border
                {
                    Background = (Brush)FindResource("LatencyBarBrush"),
                    Margin = new Thickness(1, 0, 1, 0),
                    CornerRadius = new CornerRadius(4),
                    ToolTip = $"{val:F2}s",
                    Child = new TextBlock
                    {
                        Text = $"{val:F2}s",
                        Foreground = Brushes.White,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 9,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(1, 0, 1, 0)
                    }
                };

                Grid.SetColumn(segment, i);
                _internalGrid.Children.Add(segment);
            }
        }

        private double GetValueFromItem(object item)
        {
            if (item is double d) return d;
            if (item is float f) return f;
            if (item is int i) return i;
            return 1.0; 
        }

        private List<double> ReadValues()
        {
            if (ItemsSource == null)
            {
                return new List<double>();
            }

            return ItemsSource
                .Cast<object>()
                .Select(item => System.Math.Max(GetValueFromItem(item), 0.01))
                .ToList();
        }

        private static List<double> CalculateSegmentWidths(IReadOnlyList<double> values, double availableWidth)
        {
            if (values.Count == 0 || availableWidth <= 0)
            {
                return new List<double>();
            }

            var equalFitWidth = availableWidth / values.Count;
            var minimumWidth = availableWidth >= MinimumSegmentWidth * values.Count
                ? MinimumSegmentWidth
                : System.Math.Min(MinimumCompressedSegmentWidth, equalFitWidth);

            var remainingWidth = System.Math.Max(0, availableWidth - (minimumWidth * values.Count));
            var totalValue = values.Sum();

            if (totalValue <= 0)
            {
                return values.Select(_ => availableWidth / values.Count).ToList();
            }

            return values
                .Select(value => minimumWidth + (remainingWidth * (value / totalValue)))
                .ToList();
        }

        internal string GetAutomationSummary() => LatencyChartAutomationSummary.DescribeValues(ReadValues());
    }

    internal sealed class LatencyChartAutomationPeer : FrameworkElementAutomationPeer
    {
        public LatencyChartAutomationPeer(LatencyChart owner)
            : base(owner)
        {
        }

        protected override string GetClassNameCore() => nameof(LatencyChart);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override string GetNameCore()
        {
            string configuredName = base.GetNameCore();
            return string.IsNullOrWhiteSpace(configuredName)
                ? ((LatencyChart)Owner).GetAutomationSummary()
                : configuredName;
        }
    }
}
