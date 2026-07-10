using AirType.Models;

namespace AirType.Services.Audio;

/// <summary>
/// Monitors recording for microphone disconnection and max duration.
/// Extracted from AudioInputManager for single responsibility.
/// </summary>
public class RecordingMonitor : IRecordingMonitor
{
    private readonly object _lockObject = new();

    // Microphone monitoring
    private DateTime _lastAudioReceived = DateTime.MinValue;
    private readonly TimeSpan _microphoneTimeoutThreshold = TimeSpan.FromSeconds(3);
    private System.Threading.Timer? _microphoneMonitorTimer;
    private bool _microphoneWarningShown;

    // Max duration monitoring
    private readonly TimeSpan _maxRecordingDuration = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _maxDurationWarningThreshold = TimeSpan.FromMinutes(4); // warn 1 minute before limit
    private System.Threading.Timer? _maxDurationMonitorTimer;
    private bool _maxDurationWarningShown;

    // State
    private bool _isMonitoring;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the current recording session start time.
    /// </summary>
    public DateTime? SessionStartTime { get; set; }

    /// <summary>
    /// Gets or sets the current recording mode.
    /// </summary>
    public RecordingMode? CurrentMode { get; set; }

    /// <summary>
    /// Fired when microphone disconnection is detected.
    /// </summary>
    public event EventHandler? MicrophoneDisconnected;

    /// <summary>
    /// Fired when recording is approaching max duration.
    /// </summary>
    public event EventHandler<TimeSpan>? MaxDurationWarning;

    /// <summary>
    /// Fired when recording has reached max duration.
    /// </summary>
    public event EventHandler? MaxDurationReached;

    /// <summary>
    /// Starts monitoring for the current recording session.
    /// </summary>
    public void Start()
    {
        lock (_lockObject)
        {
            if (_isMonitoring || _disposed)
                return;

            _isMonitoring = true;
            _lastAudioReceived = DateTime.Now;
            _microphoneWarningShown = false;
            _maxDurationWarningShown = false;

            // Start microphone monitoring
            _microphoneMonitorTimer = new System.Threading.Timer(
                OnMicrophoneMonitorTick,
                null,
                TimeSpan.FromSeconds(1), // Start checking after 1 second
                TimeSpan.FromSeconds(1)  // Check every second
            );
            Logger.Debug("RecordingMonitor", "Microphone monitoring started");

            // Start max duration monitoring
            _maxDurationMonitorTimer = new System.Threading.Timer(
                OnMaxDurationMonitorTick,
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1)
            );
            System.Diagnostics.Debug.WriteLine($"[RecordingMonitor] Max duration monitoring started (limit: {_maxRecordingDuration.TotalMinutes} minutes)");
        }
    }

    /// <summary>
    /// Stops monitoring.
    /// </summary>
    public void Stop()
    {
        lock (_lockObject)
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;

            _microphoneMonitorTimer?.Dispose();
            _microphoneMonitorTimer = null;
            System.Diagnostics.Debug.WriteLine("[RecordingMonitor] Microphone monitoring stopped");

            _maxDurationMonitorTimer?.Dispose();
            _maxDurationMonitorTimer = null;
            System.Diagnostics.Debug.WriteLine("[RecordingMonitor] Max duration monitoring stopped");
        }
    }

    /// <summary>
    /// Called when audio data is received to reset the microphone timeout.
    /// </summary>
    public void OnAudioReceived()
    {
        lock (_lockObject)
        {
            _lastAudioReceived = DateTime.Now;
            _microphoneWarningShown = false; // Reset warning flag
        }
    }

    /// <summary>
    /// Monitors microphone for disconnection during recording.
    /// </summary>
    private void OnMicrophoneMonitorTick(object? state)
    {
        try
        {
            bool shouldFireEvent = false;

            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return;

                var timeSinceLastAudio = DateTime.Now - _lastAudioReceived;

                if (timeSinceLastAudio > _microphoneTimeoutThreshold && !_microphoneWarningShown)
                {
                    _microphoneWarningShown = true;
                    shouldFireEvent = true;
                    System.Diagnostics.Debug.WriteLine("[RecordingMonitor] Microphone timeout detected");
                }
            }

            if (shouldFireEvent)
            {
                MicrophoneDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecordingMonitor] Error in microphone monitor: {ex.Message}");
        }
    }

    /// <summary>
    /// Monitors recording duration and fires events at warning threshold and max duration.
    /// </summary>
    private void OnMaxDurationMonitorTick(object? state)
    {
        try
        {
            TimeSpan? remainingTimeForWarning = null;
            bool shouldFireMaxReached = false;

            lock (_lockObject)
            {
                if (!_isMonitoring || !SessionStartTime.HasValue)
                    return;

                var recordingDuration = DateTime.UtcNow - SessionStartTime.Value;

                // Show warning approaching max duration
                if (!_maxDurationWarningShown && recordingDuration >= _maxDurationWarningThreshold)
                {
                    _maxDurationWarningShown = true;
                    remainingTimeForWarning = _maxRecordingDuration - recordingDuration;
                    System.Diagnostics.Debug.WriteLine($"[RecordingMonitor] Max duration warning - {remainingTimeForWarning.Value.TotalSeconds:F0}s remaining");
                }

                // Fire max duration reached
                if (recordingDuration >= _maxRecordingDuration)
                {
                    System.Diagnostics.Debug.WriteLine($"[RecordingMonitor] Max duration reached ({_maxRecordingDuration.TotalMinutes} minutes)");
                    shouldFireMaxReached = true;
                }
            }

            // Fire events outside of lock
            if (remainingTimeForWarning.HasValue)
            {
                MaxDurationWarning?.Invoke(this, remainingTimeForWarning.Value);
            }

            if (shouldFireMaxReached)
            {
                MaxDurationReached?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecordingMonitor] Error in max duration monitor: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes of resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
            }
            _disposed = true;
        }
    }

    ~RecordingMonitor()
    {
        Dispose(false);
    }
}
