using System;

namespace AirType.Models;

/// <summary>
/// Represents a recording session with metadata
/// </summary>
public class RecordingSession
{
    /// <summary>
    /// Unique identifier for the recording session
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// When the recording started
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.Now;

    /// <summary>
    /// When the recording ended (null if still recording)
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Full path to the recorded audio file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Recording mode used for this session
    /// </summary>
    public RecordingMode Mode { get; set; }

    /// <summary>
    /// Current status of the recording
    /// </summary>
    public RecordingStatus Status { get; set; } = RecordingStatus.Recording;

    /// <summary>
    /// Duration of the recording
    /// </summary>
    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
}

/// <summary>
/// Recording mode enumeration
/// </summary>
public enum RecordingMode
{
    /// <summary>
    /// Recording triggered by hotkey press/release
    /// </summary>
    Hotkey,

    /// <summary>
    /// Recording triggered by clicking the widget (unattended mode)
    /// </summary>
    Unattended
}

/// <summary>
/// Recording status enumeration
/// </summary>
public enum RecordingStatus
{
    /// <summary>
    /// Recording is currently active
    /// </summary>
    Recording,

    /// <summary>
    /// Recording completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Recording was cancelled by user
    /// </summary>
    Cancelled,

    /// <summary>
    /// Recording failed due to an error
    /// </summary>
    Error
}
