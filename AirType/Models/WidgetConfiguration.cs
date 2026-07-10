using System;

namespace AirType.Models;

/// <summary>
/// Configuration model for widget sizing and positioning parameters
/// </summary>
public class WidgetConfiguration
{
    /// <summary>
    /// Widget theme settings
    /// </summary>
    public WidgetTheme Theme { get; set; } = WidgetTheme.Default;
    
    /// <summary>
    /// Whether the widget should auto-hide when not in use
    /// </summary>
    public bool AutoHide { get; set; } = true;
    
    /// <summary>
    /// Delay before showing hover state
    /// </summary>
    public TimeSpan HoverDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    
    /// <summary>
    /// Widget opacity (0.0 to 1.0) when in minimal state
    /// </summary>
    public double MinimalOpacity { get; set; } = 0.6;

    /// <summary>
    /// Widget opacity (0.0 to 1.0) when in hover state
    /// </summary>
    public double HoverOpacity { get; set; } = 0.65;

    /// <summary>
    /// Widget opacity (0.0 to 1.0) when in recording state
    /// </summary>
    public double RecordingOpacity { get; set; } = 1.0;
    
    /// <summary>
    /// Margin from bottom of screen (pixels) - 10px for better visibility
    /// </summary>
    public int BottomMargin { get; set; } = 10;
    
    /// <summary>
    /// Minimal state dimensions (Normal state from requirements)
    /// </summary>
    public WidgetDimensions MinimalState { get; set; } = new()
    {
        Width = 40,
        Height = 10,
        CornerRadius = 5
    };
    
    /// <summary>
    /// Hover state dimensions
    /// </summary>
    public WidgetDimensions HoverState { get; set; } = new()
    {
        Width = 100,
        Height = 30,
        CornerRadius = 15
    };
    
    /// <summary>
    /// Recording state dimensions (Function state from requirements)
    /// </summary>
    public WidgetDimensions RecordingState { get; set; } = new()
    {
        Width = 100,
        Height = 30,
        CornerRadius = 15
    };
}

/// <summary>
/// Widget theme enumeration
/// </summary>
public enum WidgetTheme
{
    Default,
    Dark,
    Light,
    HighContrast
}

/// <summary>
/// Widget dimensions for different states
/// </summary>
public class WidgetDimensions
{
    /// <summary>
    /// Widget width in pixels
    /// </summary>
    public double Width { get; set; }
    
    /// <summary>
    /// Widget height in pixels
    /// </summary>
    public double Height { get; set; }
    
    /// <summary>
    /// Corner radius for rounded corners
    /// </summary>
    public double CornerRadius { get; set; }
}
