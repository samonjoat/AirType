using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AirType.Services;

/// <summary>
/// Renders real-time waveform visualization for the widget
/// </summary>
public class WaveformRenderer : IDisposable
{
    private int _maxHistoryPoints;
    private readonly object _lockObject = new();
    private double[] _rawLevels;
    private int _activeCount;
    private int _startIndex;
    private int _nextIndex;
    private Path? _waveformPath;
    private bool _disposed = false;
    private DispatcherTimer? _loadingDotsTimer;
    private double _loadingDotsPhase;
    private double _loadingDotsStep = 0.15;
    private int _visibleBarCount;

    // Waveform display parameters
    private double _canvasWidth = 60;
    private double _canvasHeight = 16;
    private WaveformDisplayMode _displayMode = WaveformDisplayMode.Flat;
    private const double MinNormalizedAmplitude = 0.0;
    private const double MaxAmplitudeScale = 0.75; // 75% scale with 26px canvas height (2px border + no vertical margin)
    private const double CenterOverlap = 2.0;
    private const double MinHalfHeightPx = 0.1;
    private readonly double _barWidth = 3.0;
    private readonly double _barSpacing = 4.0;
    private const double BarCornerRadius = 1.5;
    private const int DefaultVisibleBars = 7;
    private const int MaxDynamicHistoryPoints = 64;
    // Dynamic shaping - boost mid-level samples while keeping headroom for true peaks
    private const double AmplitudeBoost = 1.35;
    private const double DynamicExponent = 0.85;
    private const double VisualFloor = 0.04;

    private const double LoadingDotsFrameMilliseconds = 16.0;
    private const double LoadingDotsCycleSeconds = 0.9;
    private const int LoadingDotsPhaseBufferBars = 3;
    
    /// <summary>
    /// Waveform display modes for different widget states
    /// </summary>
    public enum WaveformDisplayMode
    {
        /// <summary>
        /// Flat line for hover state (no recording)
        /// </summary>
        Flat,
        
        /// <summary>
        /// Live waveform for recording state
        /// </summary>
        Live,

        /// <summary>
        /// Hidden waveform for minimal state
        /// </summary>
        Hidden,

        /// <summary>
        /// Animated loading dots used during transcription
        /// </summary>
        LoadingDots,

        /// <summary>
        /// Calmer bar sweep used during transcript cleanup
        /// </summary>
        CleaningSweep
    }

    /// <summary>
    /// Current display mode
    /// </summary>
    public WaveformDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value)
                return;

            _displayMode = value;

            if (_displayMode is WaveformDisplayMode.LoadingDots or WaveformDisplayMode.CleaningSweep)
            {
                StartLoadingDotsAnimation();
            }
            else
            {
                StopLoadingDotsAnimation();
            }

            UpdateWaveformDisplay();
        }
    }

    /// <summary>
    /// Canvas dimensions for waveform rendering
    /// </summary>
    public System.Windows.Size CanvasSize
    {
        get => new(_canvasWidth, _canvasHeight);
        set
        {
            _canvasWidth = value.Width;
            _canvasHeight = value.Height;
            ResizeHistoryForCanvas();
            UpdateWaveformDisplay();
        }
    }

    /// <summary>
    /// Initializes a new instance of WaveformRenderer
    /// </summary>
    /// <param name="maxHistoryPoints">Maximum number of amplitude points to keep in history</param>
    public WaveformRenderer(int maxHistoryPoints = DefaultVisibleBars)
    {
        _maxHistoryPoints = Math.Max(1, maxHistoryPoints);
        _rawLevels = new double[_maxHistoryPoints];
        _activeCount = 0;
        _startIndex = 0;
        _nextIndex = 0;
        _visibleBarCount = _maxHistoryPoints;

        // Initialize with flat waveform
        InitializeFlatWaveform();
        UpdateLoadingDotsStep();
    }

    /// <summary>
    /// Sets the WPF Path element for waveform rendering
    /// </summary>
    /// <param name="waveformPath">Path element to render waveform into</param>
    public void SetWaveformPath(Path waveformPath)
    {
        _waveformPath = waveformPath ?? throw new ArgumentNullException(nameof(waveformPath));
        UpdateWaveformDisplay();
    }

    /// <summary>
    /// Updates waveform with new amplitude data
    /// </summary>
    /// <param name="amplitude">Amplitude value (0.0 to 1.0)</param>
    public void UpdateWaveform(double amplitude)
    {
        if (_disposed || _displayMode != WaveformDisplayMode.Live)
            return;

        lock (_lockObject)
        {
            double clamped = Math.Clamp(amplitude, 0.0, 1.0);

            int writeIndex = _nextIndex;
            _rawLevels[writeIndex] = clamped;

            if (_activeCount == _maxHistoryPoints)
            {
                _startIndex = (_startIndex + 1) % _maxHistoryPoints;
            }
            else
            {
                _activeCount++;
            }

            _nextIndex = (_nextIndex + 1) % _maxHistoryPoints;
        }

        // Task 17.2: Update UI on main thread with optimized priority
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (!_disposed)
            {
                UpdateWaveformDisplay();
            }
        });
    }

    /// <summary>
    /// Clears waveform history and resets to flat display
    /// </summary>
    public void ClearWaveform()
    {
        lock (_lockObject)
        {
            Array.Clear(_rawLevels, 0, _rawLevels.Length);
            _activeCount = 0;
            _startIndex = 0;
            _nextIndex = 0;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                UpdateWaveformDisplay();
            }
        });
    }

    /// <summary>
    /// Updates the waveform display based on current mode and data
    /// </summary>
    private void UpdateWaveformDisplay()
    {
        if (_waveformPath == null || _disposed)
            return;

        Geometry pathGeometry;

        switch (_displayMode)
        {
            case WaveformDisplayMode.Flat:
                pathGeometry = CreateFlatWaveform();
                break;
                
            case WaveformDisplayMode.Live:
                pathGeometry = CreateLiveWaveform();
                break;

            case WaveformDisplayMode.Hidden:
                pathGeometry = Geometry.Empty;
                break;

            case WaveformDisplayMode.LoadingDots:
                pathGeometry = CreateLoadingDotsWaveform();
                break;

            case WaveformDisplayMode.CleaningSweep:
                pathGeometry = CreateCleaningSweepWaveform();
                break;
            
            default:
                pathGeometry = CreateFlatWaveform();
                break;
        }

        _waveformPath.Data = pathGeometry;
    }

    /// <summary>
    /// Creates a static staggered bar waveform for hover state
    /// </summary>
    private Geometry CreateFlatWaveform()
    {
        var geometry = new StreamGeometry();
        double centerY = _canvasHeight > 0 ? _canvasHeight / 2.0 : 0;

        int barCount = Math.Max(1, _maxHistoryPoints);
        double totalBarsWidth = barCount * _barWidth + Math.Max(0, barCount - 1) * _barSpacing;
        double leftOffset = Math.Max(0, (_canvasWidth - totalBarsWidth) / 2.0);

        // Static bar height for hover state (small, consistent height)
        double staticBarHeight = Math.Max(3.0, _canvasHeight * 0.15);

        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < barCount; i++)
            {
                // Apply same stagger pattern as live waveform
                double staggerOffset = (i % 2 == 0) ? -1.5 : 1.5;
                
                // Apply same wave motion as live waveform
                double wavePhase = (double)i / barCount * Math.PI * 2;
                double waveOffset = Math.Sin(wavePhase) * 1.0;
                
                double verticalOffset = staggerOffset + waveOffset;
                
                double left = leftOffset + i * (_barWidth + _barSpacing);
                double right = Math.Min(_canvasWidth, left + _barWidth);

                // Position static bar with stagger effect
                double top = Math.Max(0, centerY - staticBarHeight / 2 + verticalOffset);
                double bottom = Math.Min(_canvasHeight, top + staticBarHeight);
                
                AddRoundedRectangleFigure(ctx, left, top, right, bottom);
            }
        }

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    /// <summary>
    /// Creates animated wave sweep geometry for transcription state
    /// Task 17.2: Optimized for 60fps rendering
    /// </summary>
    private Geometry CreateLoadingDotsWaveform()
    {
        var geometry = new StreamGeometry();

        if (_canvasWidth <= 0 || _canvasHeight <= 0)
        {
            return geometry;
        }

        int barCount = Math.Max(1, _maxHistoryPoints);
        double totalWidth = barCount * _barWidth + Math.Max(0, barCount - 1) * _barSpacing;
        double leftOffset = Math.Max(0, (_canvasWidth - totalWidth) / 2.0);
        double centerY = _canvasHeight / 2.0;
        
        // Wave position moves left to right continuously
        // Add extra range so wave fully exits before restarting
        double wavePosition = _loadingDotsPhase % (barCount + LoadingDotsPhaseBufferBars);
        
        // Height range for the wave
        double baseHeight = _canvasHeight * 0.15;
        double maxHeight = _canvasHeight * 0.4;
        
        // Wave influence distance (how many bars are affected)
        const double waveInfluenceRadius = 3.0;
        const double invWaveInfluenceRadius = 1.0 / waveInfluenceRadius; // Pre-calculate division

        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < barCount; i++)
            {
                // Apply same stagger pattern as other states for consistency
                double staggerOffset = (i % 2 == 0) ? -1.5 : 1.5;
                double wavePhase = (double)i / barCount * Math.PI * 2;
                double waveOffset = Math.Sin(wavePhase) * 1.0;
                double verticalOffset = staggerOffset + waveOffset;
                
                // Calculate distance from wave center
                double distance = Math.Abs(i - wavePosition);
                
                // Task 17.2: Optimize influence calculation
                double influence = Math.Max(0.0, 1.0 - (distance * invWaveInfluenceRadius));
                
                // Interpolate bar height based on wave influence
                double barHeight = baseHeight + (maxHeight - baseHeight) * influence;
                
                double left = leftOffset + i * (_barWidth + _barSpacing);
                double right = Math.Min(_canvasWidth, left + _barWidth);
                
                // Position bar with stagger effect
                double top = Math.Max(0, centerY - barHeight / 2 + verticalOffset);
                double bottom = Math.Min(_canvasHeight, top + barHeight);
                
                AddRoundedRectangleFigure(ctx, left, top, right, bottom);
            }
        }

        // Task 17.2: Freeze geometry for better rendering performance
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    /// <summary>
    /// Creates a quieter sweep variant for cleanup while preserving the capsule bar language.
    /// </summary>
    private Geometry CreateCleaningSweepWaveform()
    {
        var geometry = new StreamGeometry();

        if (_canvasWidth <= 0 || _canvasHeight <= 0)
        {
            return geometry;
        }

        int barCount = Math.Max(1, _maxHistoryPoints);
        double totalWidth = barCount * _barWidth + Math.Max(0, barCount - 1) * _barSpacing;
        double leftOffset = Math.Max(0, (_canvasWidth - totalWidth) / 2.0);
        double centerY = _canvasHeight / 2.0;
        double wavePosition = _loadingDotsPhase % (barCount + LoadingDotsPhaseBufferBars);

        double baseHeight = _canvasHeight * 0.18;
        double maxHeight = _canvasHeight * 0.32;
        const double waveInfluenceRadius = 4.0;
        const double invWaveInfluenceRadius = 1.0 / waveInfluenceRadius;

        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < barCount; i++)
            {
                double distance = Math.Abs(i - wavePosition);
                double influence = Math.Max(0.0, 1.0 - (distance * invWaveInfluenceRadius));
                double barHeight = baseHeight + (maxHeight - baseHeight) * influence;

                double wavePhase = (double)i / barCount * Math.PI * 2;
                double verticalOffset = Math.Sin(wavePhase) * 0.75;

                double left = leftOffset + i * (_barWidth + _barSpacing);
                double right = Math.Min(_canvasWidth, left + _barWidth);
                double top = Math.Max(0, centerY - barHeight / 2 + verticalOffset);
                double bottom = Math.Min(_canvasHeight, top + barHeight);

                AddRoundedRectangleFigure(ctx, left, top, right, bottom);
            }
        }

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private void UpdateLoadingDotsStep()
    {
        double intervalSeconds = _loadingDotsTimer?.Interval.TotalSeconds ?? (LoadingDotsFrameMilliseconds / 1000.0);
        if (intervalSeconds <= 0)
        {
            intervalSeconds = LoadingDotsFrameMilliseconds / 1000.0;
        }

        int phaseSpan = Math.Max(1, _visibleBarCount) + LoadingDotsPhaseBufferBars;
        _loadingDotsStep = phaseSpan * intervalSeconds / LoadingDotsCycleSeconds;
    }

    /// <summary>
    /// Creates a live waveform from amplitude history
    /// </summary>
    private Geometry CreateLiveWaveform()
    {
        double[] amplitudes;
        lock (_lockObject)
        {
            if (_activeCount == 0)
            {
                // No data, return flat waveform
                return CreateFlatWaveform();
            }
            
            amplitudes = CaptureSnapshotLocked();
        }

        var geometry = new StreamGeometry
        {
            FillRule = FillRule.Nonzero
        };

        using (var ctx = geometry.Open())
        {
            CreateMirroredBarGeometry(ctx, amplitudes);
        }

        // Note: Centering is now handled by the layout system via proper canvas sizing
        // No need for manual geometry transforms

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private double[] CaptureSnapshotLocked()
    {
        if (_activeCount == 0)
        {
            return Array.Empty<double>();
        }

        var snapshot = new double[_activeCount];
        for (int i = 0; i < _activeCount; i++)
        {
            int bufferIndex = (_startIndex + i) % _maxHistoryPoints;
            snapshot[i] = _rawLevels[bufferIndex];
        }

        return snapshot;
    }

    /// <summary>
    /// Builds staggered bar rectangles with depth effect from amplitude history
    /// </summary>
    private void CreateMirroredBarGeometry(StreamGeometryContext ctx, double[] amplitudes)
    {
        double centerY = _canvasHeight / 2.0;
        double maxAmplitude = _canvasHeight * MaxAmplitudeScale;

        if (amplitudes.Length == 0 || maxAmplitude <= 0 || _canvasWidth <= 0)
            return;

        int maxBars = Math.Max(1, (int)Math.Floor((_canvasWidth + _barSpacing) / (_barWidth + _barSpacing)));
        int barCount = Math.Min(amplitudes.Length, maxBars);

        if (barCount <= 0)
            return;

        int startIndex = Math.Max(0, amplitudes.Length - barCount);
        double totalBarsWidth = barCount * _barWidth + (barCount - 1) * _barSpacing;
        double leftOffset = Math.Max(0, (_canvasWidth - totalBarsWidth) / 2.0);

        for (int barIndex = 0; barIndex < barCount; barIndex++)
        {
            double normalized = Math.Clamp(amplitudes[startIndex + barIndex], MinNormalizedAmplitude, 1.0);
            normalized = Math.Min(1.0, normalized * AmplitudeBoost);
            normalized = Math.Max(VisualFloor, normalized);
            normalized = Math.Pow(normalized, DynamicExponent);

            // Create stagger pattern - alternating bars go up/down for depth effect
            double staggerOffset = (barIndex % 2 == 0) ? -1.5 : 1.5;
            
            // Add subtle wave motion across bars for organic flow
            double wavePhase = (double)barIndex / barCount * Math.PI * 2;
            double waveOffset = Math.Sin(wavePhase) * 1.0;
            
            double verticalOffset = staggerOffset + waveOffset;
            
            // Calculate bar height - maxAmplitude already accounts for full height
            double barHeight = Math.Max(MinHalfHeightPx * 2, normalized * maxAmplitude);
            double left = leftOffset + barIndex * (_barWidth + _barSpacing);
            double right = Math.Min(_canvasWidth, left + _barWidth);

            // Position bar with vertical offset for stagger effect
            double top = Math.Max(0, centerY - barHeight / 2 + verticalOffset);
            double bottom = Math.Min(_canvasHeight, top + barHeight);
            
            AddRoundedRectangleFigure(ctx, left, top, right, bottom);
        }
    }

    private static void AddCircleFigure(StreamGeometryContext ctx, double centerX, double centerY, double radius)
    {
        if (double.IsNaN(centerX) || double.IsNaN(centerY) || radius <= 0)
            return;

        var start = new System.Windows.Point(centerX + radius, centerY);
        ctx.BeginFigure(start, true, true);

        ctx.ArcTo(new System.Windows.Point(centerX, centerY + radius), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(new System.Windows.Point(centerX - radius, centerY), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(new System.Windows.Point(centerX, centerY - radius), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(start, new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
    }

    /// <summary>
    /// Adds a filled rounded rectangle figure to the geometry context if it has visible height
    /// </summary>
    private static void AddRoundedRectangleFigure(StreamGeometryContext ctx, double left, double top, double right, double bottom)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(right) || double.IsNaN(bottom))
            return;

        if (bottom - top <= 0.1 || right - left <= 0.1)
            return;

        double width = right - left;
        double height = bottom - top;
        double radius = Math.Min(BarCornerRadius, Math.Min(width, height) / 2.0);

        var start = new System.Windows.Point(left + radius, top);
        ctx.BeginFigure(start, true, true);

        var topRight = new System.Windows.Point(right - radius, top);
        ctx.LineTo(topRight, true, false);
        if (radius > 0)
        {
            ctx.ArcTo(new System.Windows.Point(right, top + radius), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        var bottomRight = new System.Windows.Point(right, bottom - radius);
        ctx.LineTo(bottomRight, true, false);
        if (radius > 0)
        {
            ctx.ArcTo(new System.Windows.Point(right - radius, bottom), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        var bottomLeft = new System.Windows.Point(left + radius, bottom);
        ctx.LineTo(bottomLeft, true, false);
        if (radius > 0)
        {
            ctx.ArcTo(new System.Windows.Point(left, bottom - radius), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        var topLeft = new System.Windows.Point(left, top + radius);
        ctx.LineTo(topLeft, true, false);
        if (radius > 0)
        {
            ctx.ArcTo(new System.Windows.Point(left + radius, top), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
    }

    /// <summary>
    /// Initializes the waveform with flat display
    /// </summary>
    private void InitializeFlatWaveform()
    {
        _displayMode = WaveformDisplayMode.Flat;
    }

    /// <summary>
    /// Updates canvas size and refreshes display
    /// </summary>
    /// <param name="width">New canvas width</param>
    /// <param name="height">New canvas height</param>
    public void UpdateCanvasSize(double width, double height)
    {
        _canvasWidth = width;
        _canvasHeight = height;
        ResizeHistoryForCanvas();
        UpdateWaveformDisplay();
    }

    private void ResizeHistoryForCanvas()
    {
        int desiredCapacity = CalculateDesiredHistoryPoints();
        if (desiredCapacity == _maxHistoryPoints)
        {
            _visibleBarCount = Math.Max(1, desiredCapacity);
            UpdateLoadingDotsStep();
            return;
        }

        lock (_lockObject)
        {
            if (desiredCapacity == _maxHistoryPoints)
            {
                return;
            }

            double[] snapshot = _activeCount > 0 ? CaptureSnapshotLocked() : Array.Empty<double>();
            var newLevels = new double[desiredCapacity];

            int copyCount = Math.Min(snapshot.Length, desiredCapacity);
            if (copyCount > 0)
            {
                int srcOffset = snapshot.Length - copyCount;
                Array.Copy(snapshot, srcOffset, newLevels, 0, copyCount);
            }

            _rawLevels = newLevels;
            _maxHistoryPoints = desiredCapacity;
            _activeCount = copyCount;
            _startIndex = 0;
            _nextIndex = copyCount % desiredCapacity;
            _visibleBarCount = Math.Max(1, desiredCapacity);
        }

        UpdateLoadingDotsStep();
    }

    private int CalculateDesiredHistoryPoints()
    {
        if (_canvasWidth <= 0)
        {
            return _maxHistoryPoints > 0 ? _maxHistoryPoints : 1;
        }

        double step = _barWidth + _barSpacing;
        if (step <= 0)
        {
            return _maxHistoryPoints > 0 ? _maxHistoryPoints : 1;
        }

        int bars = (int)Math.Floor((_canvasWidth + _barSpacing) / step);
        bars = Math.Max(1, bars);

        if (MaxDynamicHistoryPoints > 0)
        {
            bars = Math.Min(bars, MaxDynamicHistoryPoints);
        }

        return bars;
    }

    /// <summary>
    /// Sets display mode for different widget states
    /// </summary>
    /// <param name="isRecording">Whether recording is active</param>
    /// <param name="isHover">Whether widget is in hover state</param>
    public void SetDisplayModeForState(bool isRecording, bool isHover, bool isTranscribing = false, bool isCleaning = false)
    {
        if (isCleaning)
        {
            ClearWaveform();
            DisplayMode = WaveformDisplayMode.CleaningSweep;
        }
        else if (isTranscribing)
        {
            ClearWaveform();
            DisplayMode = WaveformDisplayMode.LoadingDots;
        }
        else if (isRecording)
        {
            DisplayMode = WaveformDisplayMode.Live;
        }
        else if (isHover)
        {
            DisplayMode = WaveformDisplayMode.Flat;
            ClearWaveform(); // Clear any previous recording data
        }
        else
        {
            // Minimal state - could hide waveform entirely or show minimal flat line
            DisplayMode = WaveformDisplayMode.Hidden;
            ClearWaveform();
        }
    }

    private void StartLoadingDotsAnimation()
    {
        if (_loadingDotsTimer == null)
        {
            // Task 17.2: Optimize animation timer for 60fps (16.67ms per frame)
            _loadingDotsTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(LoadingDotsFrameMilliseconds) // ~60fps
            };
            _loadingDotsTimer.Tick += OnLoadingDotsTick;
            UpdateLoadingDotsStep();
        }
        else
        {
            UpdateLoadingDotsStep();
        }

        if (!_loadingDotsTimer.IsEnabled)
        {
            _loadingDotsPhase = 0;
            _loadingDotsTimer.Start();
        }
    }

    private void StopLoadingDotsAnimation()
    {
        if (_loadingDotsTimer != null)
        {
            _loadingDotsTimer.Stop();
            _loadingDotsPhase = 0;
        }
    }

    private void OnLoadingDotsTick(object? sender, EventArgs e)
    {
        // Task 17.2: Optimize animation update - increment phase and update directly
        _loadingDotsPhase += _loadingDotsStep;
        
        // Update directly without additional dispatcher invoke (already on UI thread)
        if (!_disposed)
        {
            UpdateWaveformDisplay();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            lock (_lockObject)
            {
                Array.Clear(_rawLevels, 0, _rawLevels.Length);
                _activeCount = 0;
                _startIndex = 0;
                _nextIndex = 0;
            }
            
            _waveformPath = null;
            if (_loadingDotsTimer != null)
            {
                _loadingDotsTimer.Tick -= OnLoadingDotsTick;
                _loadingDotsTimer.Stop();
                _loadingDotsTimer = null;
            }

            _disposed = true;
        }
    }
}
