using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AirType.ViewModels;
using AirType.Models;
using AirType.Services;
using AirType.Services.Storage;

namespace AirType.Views;

public partial class HistoryView : UserControl, IDisposable
{
    private HistoryViewModel? _viewModel;
    private bool _disposed;

    public HistoryView()
    {
        if (Application.Current is App app && app.Services != null)
        {
            var services = app.Services;
            _viewModel = new HistoryViewModel(
                services.HistoryManager, 
                services.TranscriptionWorkflowService,
                services.DictionaryManager,
                new TextDiffService());
            DataContext = _viewModel;

            // Reactive Day Catcher: Update whenever the list changes (new items, reruns, etc.)
            _viewModel.FilteredEntries.CollectionChanged += OnFilteredEntriesChanged;
        }
        InitializeComponent();
        HistoryItemsControl.ItemContainerGenerator.StatusChanged += OnHistoryItemContainersStatusChanged;
        
        // Initialize day catcher with first entry's date
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateStickyHeader(0);
        QueueTimelineLineAlignment();
    }

    private void OnFilteredEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateStickyHeader(MainScrollViewer?.VerticalOffset ?? 0);
            QueueTimelineLineAlignment();
        }), DispatcherPriority.Background);
    }

    private void OnHistoryItemContainersStatusChanged(object? sender, EventArgs e)
    {
        if (HistoryItemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            QueueTimelineLineAlignment();
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewModel == null) return;

        // 1. Handle Sticky Day Header (only update if scrolling vertically)
        if (e.VerticalChange != 0)
        {
            UpdateStickyHeader(e.VerticalOffset);
        }

        // 2. Update Rendered Count for stats
        UpdateRenderedCount();

        // 3. Handle Incremental Loading (Infinite Scroll)
        // Only trigger when user actively scrolls down (not on initial load or layout changes)
        // e.VerticalChange > 0 means user is scrolling DOWN
        if (e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 100)
        {
            if (_viewModel.HasMoreItems)
            {
                _viewModel.LoadMore();
            }
        }
    }

    private void UpdateStickyHeader(double verticalOffset)
    {
        if (_viewModel == null || StickyHeaderText == null || StickyHeader == null) return;

        // Always keep sticky header visible
        StickyHeader.Visibility = Visibility.Visible;

        // Height of the sticky header (where day separators will slide under)
        double stickyHeaderHeight = StickyHeader.ActualHeight;
        
        // Find which day label should be shown in the catcher
        string? currentDateLabel = null;
        
        // Iterate through entries to find which day owns the top viewport position
        for (int i = 0; i < _viewModel.FilteredEntries.Count; i++)
        {
            var container = HistoryItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container != null)
            {
                try
                {
                    // Get position relative to ScrollViewer
                    var transform = container.TransformToAncestor(MainScrollViewer);
                    var position = transform.Transform(new Point(0, 0));
                    
                    // Check if this entry is at or above the bottom of the sticky header
                    // This means its day "owns" the catcher area
                    if (position.Y <= stickyHeaderHeight)
                    {
                        var entry = _viewModel.FilteredEntries[i];
                        currentDateLabel = entry.DateLabel;
                        // Don't break - keep looking for the last entry still in catcher zone
                    }
                    else
                    {
                        // Once we're past the catcher zone, we're done
                        break;
                    }
                }
                catch
                {
                    // Ignore transform errors during layout
                }
            }
        }

        // Update text or default to first entry's date (already uppercase from model)
        if (!string.IsNullOrEmpty(currentDateLabel))
        {
            StickyHeaderText.Text = currentDateLabel;
            _viewModel.CurrentDayInCatcher = currentDateLabel; // Track for separator hiding
        }
        else if (_viewModel.FilteredEntries.Count > 0)
        {
            StickyHeaderText.Text = _viewModel.FilteredEntries[0].DateLabel;
            _viewModel.CurrentDayInCatcher = _viewModel.FilteredEntries[0].DateLabel; // Track for separator hiding
        }
    }

    private void UpdateRenderedCount()
    {
        if (_viewModel == null || HistoryItemsControl == null) return;

        // In a virtualized list, the number of generated containers 
        // is roughly the number of rendered items
        int count = 0;
        var itemsSource = HistoryItemsControl.ItemsSource;
        if (itemsSource != null)
        {
            int index = 0;
            foreach (var item in itemsSource)
            {
                var container = HistoryItemsControl.ItemContainerGenerator.ContainerFromIndex(index);
                if (container is UIElement)
                {
                    count++;
                }
                index++;
            }
        }
        
        _viewModel.RenderedCount = count;
    }

    private void QueueTimelineLineAlignment()
    {
        if (_disposed || TimelineLine == null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(AlignTimelineLineToFirstDot), DispatcherPriority.Loaded);
    }

    private void AlignTimelineLineToFirstDot()
    {
        if (_disposed || HistoryTimelineHost == null || HistoryItemsControl == null || TimelineLine == null)
        {
            return;
        }

        var firstContainer = HistoryItemsControl.ItemContainerGenerator.ContainerFromIndex(0) as DependencyObject;
        if (firstContainer == null)
        {
            return;
        }

        var firstDot = FindVisualChild<Ellipse>(firstContainer);
        if (firstDot == null || firstDot.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            var dotCenter = firstDot.TransformToAncestor(HistoryTimelineHost)
                .Transform(new Point(firstDot.ActualWidth / 2, firstDot.ActualHeight / 2));
            double lineStart = Math.Max(0, dotCenter.Y);
            var currentMargin = TimelineLine.Margin;
            TimelineLine.Margin = new Thickness(currentMargin.Left, lineStart, currentMargin.Right, currentMargin.Bottom);
        }
        catch (InvalidOperationException)
        {
            // Layout can briefly disconnect visuals while filters or virtualization refresh.
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            if (button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }
    }

    private T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(obj, i);
            if (child != null && child is T t)
                return t;
            else
            {
                T? childOfChild = child != null ? FindVisualChild<T>(child) : null;
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
    }

    public void RefreshNow()
    {
        _viewModel?.RefreshNow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        HistoryItemsControl.ItemContainerGenerator.StatusChanged -= OnHistoryItemContainersStatusChanged;

        if (_viewModel != null)
        {
            _viewModel.FilteredEntries.CollectionChanged -= OnFilteredEntriesChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }

        DataContext = null;
    }
}
