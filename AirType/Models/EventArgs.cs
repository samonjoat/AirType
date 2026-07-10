using System;

namespace AirType.Models;

/// <summary>
/// Event arguments for waveform data updates
/// </summary>
public class WaveformDataEventArgs : EventArgs
{
    /// <summary>
    /// Audio amplitude value (0.0 to 1.0)
    /// </summary>
    public double Amplitude { get; }

    /// <summary>
    /// Timestamp when the amplitude was captured
    /// </summary>
    public DateTime Timestamp { get; }

    public WaveformDataEventArgs(double amplitude)
    {
        Amplitude = Math.Max(0.0, Math.Min(1.0, amplitude)); // Clamp to valid range
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Identifies where a recording originated from
/// </summary>
public enum RecordingSource
{
    /// <summary>
    /// Recording from CapsuleWidget or global hotkey (main app flow)
    /// </summary>
    Main,

    /// <summary>
    /// Recording from Notes page (isolated flow)
    /// </summary>
    Notes
}

/// <summary>
/// Event arguments for recording state changes
/// </summary>
public class RecordingStateEventArgs : EventArgs
{
    /// <summary>
    /// Whether recording is currently active
    /// </summary>
    public bool IsRecording { get; }

    /// <summary>
    /// Recording mode being used
    /// </summary>
    public RecordingMode Mode { get; }

    /// <summary>
    /// Where the recording originated from
    /// </summary>
    public RecordingSource Source { get; }

    /// <summary>
    /// Timestamp when the state changed
    /// </summary>
    public DateTime Timestamp { get; }

    public RecordingStateEventArgs(bool isRecording, RecordingMode mode, RecordingSource source = RecordingSource.Main)
    {
        IsRecording = isRecording;
        Mode = mode;
        Source = source;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event arguments for screen changes
/// </summary>
public class ScreenChangedEventArgs : EventArgs
{
    /// <summary>
    /// The new active screen
    /// </summary>
    public System.Windows.Forms.Screen NewScreen { get; }

    public ScreenChangedEventArgs(System.Windows.Forms.Screen newScreen)
    {
        NewScreen = newScreen ?? throw new ArgumentNullException(nameof(newScreen));
    }
}

/// <summary>
/// Event arguments for animation completion
/// </summary>
public class AnimationCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Type of animation that completed
    /// </summary>
    public string AnimationType { get; }

    /// <summary>
    /// Whether the animation completed successfully
    /// </summary>
    public bool Success { get; }

    public AnimationCompletedEventArgs(string animationType, bool success = true)
    {
        AnimationType = animationType ?? throw new ArgumentNullException(nameof(animationType));
        Success = success;
    }
}

/// <summary>
/// Event arguments for silent audio detection
/// </summary>
public class SilentAudioDetectedEventArgs : EventArgs
{
    /// <summary>
    /// RMS level of the recording in decibels
    /// </summary>
    public double RmsLevelDb { get; }

    /// <summary>
    /// Timestamp when silent audio was detected
    /// </summary>
    public DateTime Timestamp { get; }

    public SilentAudioDetectedEventArgs(double rmsLevelDb)
    {
        RmsLevelDb = rmsLevelDb;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event arguments for recordings that were rejected for being too short.
/// </summary>
public class ShortRecordingRejectedEventArgs : EventArgs
{
    /// <summary>
    /// The recording mode that was rejected.
    /// </summary>
    public RecordingMode Mode { get; }

    /// <summary>
    /// Duration recorded before rejection.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Minimum required duration for the mode.
    /// </summary>
    public TimeSpan MinimumRequired { get; }

    public ShortRecordingRejectedEventArgs(RecordingMode mode, TimeSpan duration, TimeSpan minimumRequired)
    {
        Mode = mode;
        Duration = duration;
        MinimumRequired = minimumRequired;
    }
}
/// <summary>
/// Event arguments for max duration warning (approaching limit)
/// </summary>
public class MaxDurationWarningEventArgs : EventArgs
{
    /// <summary>
    /// Current recording duration
    /// </summary>
    public TimeSpan CurrentDuration { get; }

    /// <summary>
    /// Maximum allowed duration
    /// </summary>
    public TimeSpan MaxDuration { get; }

    /// <summary>
    /// Time remaining before auto-stop
    /// </summary>
    public TimeSpan RemainingTime { get; }

    /// <summary>
    /// Timestamp when warning was triggered
    /// </summary>
    public DateTime Timestamp { get; }

    public MaxDurationWarningEventArgs(TimeSpan currentDuration, TimeSpan maxDuration, TimeSpan remainingTime)
    {
        CurrentDuration = currentDuration;
        MaxDuration = maxDuration;
        RemainingTime = remainingTime;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event arguments for max duration reached (auto-stopped)
/// </summary>
public class MaxDurationReachedEventArgs : EventArgs
{
    /// <summary>
    /// Maximum duration that was reached
    /// </summary>
    public TimeSpan MaxDuration { get; }

    /// <summary>
    /// Recording mode that was auto-stopped
    /// </summary>
    public RecordingMode Mode { get; }

    /// <summary>
    /// Timestamp when max duration was reached
    /// </summary>
    public DateTime Timestamp { get; }

    public MaxDurationReachedEventArgs(TimeSpan maxDuration, RecordingMode mode)
    {
        MaxDuration = maxDuration;
        Mode = mode;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event arguments for quiet audio detection (audio present but very low volume)
/// </summary>
public class QuietAudioDetectedEventArgs : EventArgs
{
    /// <summary>
    /// RMS level of the recording in decibels
    /// </summary>
    public double RmsLevelDb { get; }

    /// <summary>
    /// Timestamp when quiet audio was detected
    /// </summary>
    public DateTime Timestamp { get; }

    public QuietAudioDetectedEventArgs(double rmsLevelDb)
    {
        RmsLevelDb = rmsLevelDb;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event arguments for audio file corruption detection
/// </summary>
public class AudioFileCorruptedEventArgs : EventArgs
{
    /// <summary>
    /// Error message describing the corruption issue
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Timestamp when corruption was detected
    /// </summary>
    public DateTime Timestamp { get; }

    public AudioFileCorruptedEventArgs(string errorMessage)
    {
        ErrorMessage = errorMessage ?? "Unknown corruption error";
        Timestamp = DateTime.Now;
    }
}
