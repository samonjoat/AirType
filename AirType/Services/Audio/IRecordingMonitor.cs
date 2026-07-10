using AirType.Models;

namespace AirType.Services.Audio;

/// <summary>
/// Monitors recording for microphone disconnection and max duration.
/// </summary>
public interface IRecordingMonitor : IDisposable
{
    /// <summary>
    /// Starts monitoring for the current recording session.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops monitoring.
    /// </summary>
    void Stop();

    /// <summary>
    /// Called when audio data is received to reset the microphone timeout.
    /// </summary>
    void OnAudioReceived();

    /// <summary>
    /// Gets the current recording session start time.
    /// </summary>
    DateTime? SessionStartTime { get; set; }

    /// <summary>
    /// Gets the current recording mode.
    /// </summary>
    RecordingMode? CurrentMode { get; set; }

    /// <summary>
    /// Fired when microphone disconnection is detected (no audio for timeout period).
    /// </summary>
    event EventHandler? MicrophoneDisconnected;

    /// <summary>
    /// Fired when recording is approaching max duration.
    /// Uses TimeSpan to indicate remaining time.
    /// </summary>
    event EventHandler<TimeSpan>? MaxDurationWarning;

    /// <summary>
    /// Fired when recording has reached max duration and should be stopped.
    /// </summary>
    event EventHandler? MaxDurationReached;
}
