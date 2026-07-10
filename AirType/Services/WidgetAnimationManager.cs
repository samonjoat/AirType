using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// CSS-inspired transform-only animation manager
/// </summary>
public class WidgetAnimationManager : IDisposable
{
    private readonly FrameworkElement _targetElement;
    private readonly FrameworkElement _borderElement;
    private readonly FrameworkElement _cancelButton;
    private readonly FrameworkElement _stopButton;
    private readonly FrameworkElement _waveformCanvas;
    
    private Storyboard? _currentAnimation;
    private bool _isAnimating = false;
    private bool _disposed = false;

    // CSS-like scale states (transform-origin: center bottom)
    private static readonly ScaleState MinimalScale = new(0.4, 0.3333); // 40x10 from 100x30
    private static readonly ScaleState HoverScale = new(1.0, 1.0);      // 100x30 full size
    private static readonly ScaleState RecordingScale = new(1.0, 1.0);  // 100x30 same as hover

    public WidgetAnimationManager(
        FrameworkElement targetElement,
        FrameworkElement borderElement,
        FrameworkElement cancelButton,
        FrameworkElement stopButton,
        FrameworkElement waveformCanvas)
    {
        _targetElement = targetElement ?? throw new ArgumentNullException(nameof(targetElement));
        _borderElement = borderElement ?? throw new ArgumentNullException(nameof(borderElement));
        _cancelButton = cancelButton ?? throw new ArgumentNullException(nameof(cancelButton));
        _stopButton = stopButton ?? throw new ArgumentNullException(nameof(stopButton));
        _waveformCanvas = waveformCanvas ?? throw new ArgumentNullException(nameof(waveformCanvas));
    }

    public event EventHandler<AnimationCompletedEventArgs>? AnimationCompleted;
    public bool IsAnimating => _isAnimating;

    /// <summary>
    /// Animate to minimal state (CSS: transform: scale(0.5, 0.233))
    /// </summary>
    public void AnimateToMinimal(WidgetDimensions targetDimensions, double targetOpacity)
    {
        AnimateToScale(MinimalScale, targetOpacity, TimeSpan.FromMilliseconds(280), 
            new CubicEase { EasingMode = EasingMode.EaseInOut }, WidgetStateManager.WidgetState.IdleMinimal);
    }

    /// <summary>
    /// Animate to hover state (CSS: transform: scale(1.0, 1.0))
    /// </summary>
    public void AnimateToHover(WidgetDimensions targetDimensions, double targetOpacity)
    {
        AnimateToScale(HoverScale, targetOpacity, TimeSpan.FromMilliseconds(300), 
            new CubicEase { EasingMode = EasingMode.EaseOut }, WidgetStateManager.WidgetState.IdleHover);
    }

    /// <summary>
    /// Animate to recording state (CSS: transform: scale(1.0, 1.0) - same as hover)
    /// </summary>
    public void AnimateToRecording(WidgetDimensions targetDimensions, double targetOpacity, bool showControls)
    {
        // Fast transition since no scale change from hover
        var duration = TimeSpan.FromMilliseconds(150);
        AnimateToScale(RecordingScale, targetOpacity, duration, 
            new CubicEase { EasingMode = EasingMode.EaseOut }, WidgetStateManager.WidgetState.Recording, showControls);
    }

    /// <summary>
    /// Core CSS-like animation method
    /// </summary>
    private void AnimateToScale(ScaleState targetScale, double targetOpacity, TimeSpan duration, 
        IEasingFunction easing, WidgetStateManager.WidgetState targetState, bool showControls = false)
    {
        if (_isAnimating)
        {
            StopCurrentAnimation();
        }

        var storyboard = new Storyboard();
        
        // Get current scale transform
        var currentScale = GetCurrentScale();
        
        // Only animate scale if there's a meaningful change
        if (Math.Abs(currentScale.ScaleX - targetScale.ScaleX) > 0.01 ||
            Math.Abs(currentScale.ScaleY - targetScale.ScaleY) > 0.01)
        {
            // ScaleX animation (targeting TransformGroup)
            var scaleXAnimation = new DoubleAnimation
            {
                From = currentScale.ScaleX,
                To = targetScale.ScaleX,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(scaleXAnimation, _borderElement);
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleXAnimation);

            // ScaleY animation (targeting TransformGroup) - bottom-anchored
            var scaleYAnimation = new DoubleAnimation
            {
                From = currentScale.ScaleY,
                To = targetScale.ScaleY,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(scaleYAnimation, _borderElement);
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleYAnimation);
        }
        
        // Opacity animation
        AddOpacityAnimation(storyboard, targetOpacity, duration);
        
        // Button animations
        if (targetState == WidgetStateManager.WidgetState.Recording && showControls)
        {
            AddButtonFadeAnimation(storyboard, 1.0, TimeSpan.FromMilliseconds(200));
        }
        else
        {
            AddButtonFadeAnimation(storyboard, 0.0, TimeSpan.FromMilliseconds(150));
        }
        
        // Waveform margin
        var targetMargin = showControls ? new Thickness(25, 0, 25, 0) : new Thickness(15, 0, 15, 0);
        AddWaveformMarginAnimation(storyboard, targetMargin, duration);

        StartAnimation(storyboard, targetState);
    }

    /// <summary>
    /// Get current scale from TransformGroup
    /// </summary>
    private ScaleState GetCurrentScale()
    {
        if (_borderElement.RenderTransform is TransformGroup group && 
            group.Children.Count > 0 && 
            group.Children[0] is ScaleTransform scale)
        {
            return new ScaleState(scale.ScaleX, scale.ScaleY);
        }
        return MinimalScale; // Default
    }

    private void AddOpacityAnimation(Storyboard storyboard, double targetOpacity, TimeSpan duration)
    {
        var currentOpacity = _targetElement.Opacity;
        if (Math.Abs(currentOpacity - targetOpacity) > 0.01)
        {
            var opacityAnimation = new DoubleAnimation
            {
                From = currentOpacity,
                To = targetOpacity,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(opacityAnimation, _targetElement);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
        }
    }

    private void AddButtonFadeAnimation(Storyboard storyboard, double targetOpacity, TimeSpan duration)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        
        // Cancel button
        if (Math.Abs(_cancelButton.Opacity - targetOpacity) > 0.01)
        {
            var cancelAnimation = new DoubleAnimation
            {
                From = _cancelButton.Opacity,
                To = targetOpacity,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(cancelAnimation, _cancelButton);
            Storyboard.SetTargetProperty(cancelAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(cancelAnimation);
        }

        // Stop button
        if (Math.Abs(_stopButton.Opacity - targetOpacity) > 0.01)
        {
            var stopAnimation = new DoubleAnimation
            {
                From = _stopButton.Opacity,
                To = targetOpacity,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(stopAnimation, _stopButton);
            Storyboard.SetTargetProperty(stopAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(stopAnimation);
        }
    }

    private void AddWaveformMarginAnimation(Storyboard storyboard, Thickness targetMargin, TimeSpan duration)
    {
        var currentMargin = _waveformCanvas.Margin;
        if (Math.Abs(currentMargin.Left - targetMargin.Left) > 1.0)
        {
            var marginAnimation = new ThicknessAnimation
            {
                From = currentMargin,
                To = targetMargin,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(marginAnimation, _waveformCanvas);
            Storyboard.SetTargetProperty(marginAnimation, new PropertyPath("Margin"));
            storyboard.Children.Add(marginAnimation);
        }
    }

    // Visual feedback methods (simplified)
    public void AnimateScaleFeedback()
    {
        if (_isAnimating) return;
        
        var storyboard = new Storyboard();
        var currentScale = GetCurrentScale();
        
        var feedbackAnimation = new DoubleAnimation
        {
            From = currentScale.ScaleX,
            To = currentScale.ScaleX * 0.95,
            Duration = TimeSpan.FromMilliseconds(80),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        Storyboard.SetTarget(feedbackAnimation, _borderElement);
        Storyboard.SetTargetProperty(feedbackAnimation, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        storyboard.Children.Add(feedbackAnimation);
        storyboard.Begin();
    }

    public void AnimateHoverEnter() { /* Optional subtle effect */ }
    public void AnimateHoverExit() { /* Optional subtle effect */ }

    public void StopCurrentAnimation()
    {
        if (_disposed)
            return;

        if (_currentAnimation != null)
        {
            try
            {
                _currentAnimation.Stop();
                _currentAnimation.Remove(_targetElement);
                _currentAnimation.Remove(_borderElement);
                _currentAnimation.Remove(_cancelButton);
                _currentAnimation.Remove(_stopButton);
                _currentAnimation.Remove(_waveformCanvas);
            }
            catch (Exception)
            {
                // Ignore cleanup errors
            }
            finally
            {
                _currentAnimation = null;
                _isAnimating = false;
            }
        }
    }

    /// <summary>
    /// Disposes of all resources and stops any running animations
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for proper disposal pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Stop any running animations
            StopCurrentAnimation();
            _disposed = true;
        }
    }

    private void StartAnimation(Storyboard storyboard, WidgetStateManager.WidgetState targetState)
    {
        _isAnimating = true;
        _currentAnimation = storyboard;
        
        storyboard.Completed += (s, e) =>
        {
            _isAnimating = false;
            _currentAnimation = null;
            AnimationCompleted?.Invoke(this, new AnimationCompletedEventArgs(targetState));
        };

        storyboard.Begin();
    }
}

/// <summary>
/// Scale state for CSS-like transforms
/// </summary>
public record ScaleState(double ScaleX, double ScaleY);

/// <summary>
/// Animation completion event args
/// </summary>
public class AnimationCompletedEventArgs : EventArgs
{
    public WidgetStateManager.WidgetState TargetState { get; }

    public AnimationCompletedEventArgs(WidgetStateManager.WidgetState targetState)
    {
        TargetState = targetState;
    }
}
