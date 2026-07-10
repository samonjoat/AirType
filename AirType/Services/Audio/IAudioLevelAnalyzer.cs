namespace AirType.Services.Audio;

/// <summary>
/// Analyzes audio levels for silence/quiet detection and waveform visualization.
/// </summary>
public interface IAudioLevelAnalyzer
{
    /// <summary>
    /// Resets the audio level tracking for a new recording.
    /// </summary>
    void Reset();

    /// <summary>
    /// Processes an audio buffer and returns the normalized amplitude for waveform visualization.
    /// Also accumulates statistics for silence/quiet detection.
    /// </summary>
    /// <param name="buffer">The audio buffer (16-bit PCM samples)</param>
    /// <param name="bytesRecorded">Number of bytes in the buffer</param>
    /// <returns>Normalized amplitude (0.0 to 1.0) for waveform display</returns>
    double ProcessBuffer(byte[] buffer, int bytesRecorded);

    /// <summary>
    /// Checks if the buffer contains actual audio signal (not just noise/silence).
    /// </summary>
    /// <param name="buffer">The audio buffer</param>
    /// <param name="bytesRecorded">Number of bytes in the buffer</param>
    /// <returns>True if audio signal detected above noise threshold</returns>
    bool HasAudioSignal(byte[] buffer, int bytesRecorded);

    /// <summary>
    /// Determines if the accumulated recording is silent (no audio at all).
    /// </summary>
    /// <returns>True if RMS level is below silence threshold</returns>
    bool IsRecordingSilent();

    /// <summary>
    /// Determines if the accumulated recording is quiet but not silent.
    /// </summary>
    /// <returns>True if RMS is between silence and quiet thresholds</returns>
    bool IsRecordingQuiet();

    /// <summary>
    /// Gets the current RMS level in decibels.
    /// </summary>
    /// <returns>RMS level in dB (negative values, 0 dB = maximum)</returns>
    double GetRmsDb();

    /// <summary>
    /// Gets the total number of samples processed.
    /// </summary>
    int TotalSamplesProcessed { get; }
}
