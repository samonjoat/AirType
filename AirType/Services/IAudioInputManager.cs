using System;
using System.Threading.Tasks;
using AirType.Models;
using System.Collections.Generic;

namespace AirType.Services;

/// <summary>
/// Interface for audio input management
/// </summary>
public interface IAudioInputManager : IDisposable
{
    /// <summary>
    /// Event fired when waveform data is available for visualization
    /// </summary>
    event EventHandler<WaveformDataEventArgs>? WaveformDataAvailable;

    /// <summary>
    /// Event fired with copied PCM16 mono 16 kHz audio frames for local streaming ASR.
    /// </summary>
    event EventHandler<AudioPcmFrameEventArgs>? PcmFrameAvailable;

    /// <summary>
    /// Event fired when recording state changes
    /// </summary>
    event EventHandler<RecordingStateEventArgs>? RecordingStateChanged;

    /// <summary>
    /// Event fired when silent audio is detected (no speech in recording)
    /// </summary>
    event EventHandler<SilentAudioDetectedEventArgs>? SilentAudioDetected;

    /// <summary>
    /// Event fired when a recording is rejected by policy (e.g., hotkey tap too short).
    /// </summary>
    event EventHandler<ShortRecordingRejectedEventArgs>? ShortRecordingRejected;

    /// <summary>
    /// Event fired when microphone disconnection is detected during recording
    /// </summary>
    event EventHandler? MicrophoneDisconnected;

    /// <summary>
    /// Event fired when audio is detected but is very quiet (may not transcribe well)
    /// </summary>
    event EventHandler<QuietAudioDetectedEventArgs>? QuietAudioDetected;

    /// <summary>
    /// Event fired when recording is about to reach max duration (warning)
    /// </summary>
    event EventHandler<MaxDurationWarningEventArgs>? MaxDurationWarning;

    /// <summary>
    /// Event fired when recording reaches max duration and is auto-stopped
    /// </summary>
    event EventHandler<MaxDurationReachedEventArgs>? MaxDurationReached;

    /// <summary>
    /// Event fired when audio file is corrupted or invalid
    /// </summary>
    event EventHandler<AudioFileCorruptedEventArgs>? AudioFileCorrupted;

    /// <summary>
    /// Whether recording is currently active
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Current recording session, if any
    /// </summary>
    RecordingSession? CurrentSession { get; }

    /// <summary>
    /// Provides details about the last recording error encountered, if any.
    /// </summary>
    string? LastErrorMessage { get; }

    /// <summary>
    /// Starts audio recording with the specified mode
    /// </summary>
    /// <param name="mode">Recording mode (Hotkey or Unattended)</param>
    /// <param name="source">Where the recording originated from (Main or Notes)</param>
    /// <returns>True if recording started successfully, false otherwise</returns>
    Task<bool> StartRecordingAsync(RecordingMode mode, RecordingSource source = RecordingSource.Main);

    /// <summary>
    /// Stops recording and saves the file
    /// </summary>
    Task StopRecordingAsync();

    /// <summary>
    /// Cancels recording and deletes the partial file
    /// </summary>
    Task CancelRecordingAsync();

    /// <summary>
    /// Returns the list of available capture devices.
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetAvailableDevices();

    /// <summary>
    /// The user-selected preferred device index, if any.
    /// </summary>
    int? PreferredDeviceNumber { get; }

    /// <summary>
    /// The last device index used to start a recording.
    /// </summary>
    int LastUsedDeviceNumber { get; }

    /// <summary>
    /// Sets the preferred capture device. Pass null to revert to OS default.
    /// </summary>
    void SetPreferredDevice(int? deviceNumber);

    /// <summary>
    /// Gets the buffered audio data from the last completed recording, if available.
    /// Returns null if no buffered data is available (falls back to file read).
    /// This is an optimization to avoid reading the file from disk when data is already in memory.
    /// </summary>
    byte[]? GetBufferedAudioData();

    /// <summary>
    /// Clears the in-memory audio buffer. Call after transcription is complete.
    /// </summary>
    void ClearAudioBuffer();
}
