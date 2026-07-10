using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AirType.Controls;

public partial class WaveVisualizer : UserControl
{
    private Storyboard? _waveStoryboard;

    public static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.Register("IsAnimating", typeof(bool), typeof(WaveVisualizer),
            new PropertyMetadata(false, OnIsAnimatingChanged));

    public bool IsAnimating
    {
        get { return (bool)GetValue(IsAnimatingProperty); }
        set { SetValue(IsAnimatingProperty, value); }
    }

    public WaveVisualizer()
    {
        InitializeComponent();
        CreateStoryboard();
    }

    private void CreateStoryboard()
    {
        _waveStoryboard = new Storyboard();
        _waveStoryboard.RepeatBehavior = RepeatBehavior.Forever;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = -40,
            Duration = TimeSpan.FromSeconds(1.5)
        };

        Storyboard.SetTarget(animation, WaveTransform);
        Storyboard.SetTargetProperty(animation, new PropertyPath("X"));

        _waveStoryboard.Children.Add(animation);
    }

    private static void OnIsAnimatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (WaveVisualizer)d;
        var isAnimating = (bool)e.NewValue;

        if (isAnimating)
        {
            control._waveStoryboard?.Begin();
        }
        else
        {
            control._waveStoryboard?.Stop();
        }
    }
}
