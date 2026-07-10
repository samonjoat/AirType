using NAudio.Wave;

namespace AirType.Services.Audio;

/// <summary>
/// Analyzes audio levels for silence/quiet detection and waveform visualization.
/// Extracted from AudioInputManager for single responsibility.
/// </summary>
public class AudioLevelAnalyzer : IAudioLevelAnalyzer
{
    private readonly object _lockObject = new();

    // Audio level tracking
    private double _maxAmplitude;
    private int _totalSamples;
    private double _sumSquares;

    // Thresholds for audio level detection
    private const double SilenceThresholdDb = -45.0;      // dB threshold for silence (no audio at all)
    private const double QuietAudioThresholdDb = -36.0;   // dB threshold for quiet audio
    private const int MinimumSamplesForAnalysis = 8000;   // 0.5 seconds at 16kHz
    private const int SignalDetectionThreshold = 100;     // Sample value threshold for detecting audio vs noise

    /// <summary>
    /// Gets the total number of samples processed.
    /// </summary>
    public int TotalSamplesProcessed
    {
        get
        {
            lock (_lockObject)
            {
                return _totalSamples;
            }
        }
    }

    /// <summary>
    /// Resets the audio level tracking for a new recording.
    /// </summary>
    public void Reset()
    {
        lock (_lockObject)
        {
            _maxAmplitude = 0;
            _totalSamples = 0;
            _sumSquares = 0;
        }
    }

    /// <summary>
    /// Processes an audio buffer and returns the normalized amplitude for waveform visualization.
    /// Also accumulates statistics for silence/quiet detection.
    /// </summary>
    public double ProcessBuffer(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded == 0)
            return 0.0;

        var waveBuffer = new WaveBuffer(buffer);
        int sampleCount = bytesRecorded / 2; // 16-bit samples = 2 bytes each
        double max = 0;
        double sumSquares = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = waveBuffer.ShortBuffer[i];
            double abs = Math.Abs(sample);
            if (abs > max)
            {
                max = abs;
            }
            sumSquares += sample * sample;
        }

        if (sampleCount == 0)
            return 0.0;

        // Accumulate statistics for silence detection
        lock (_lockObject)
        {
            if (max > _maxAmplitude)
            {
                _maxAmplitude = max;
            }
            _totalSamples += sampleCount;
            _sumSquares += sumSquares;
        }

        // Calculate normalized amplitude for waveform display
        double rms = Math.Sqrt(sumSquares / sampleCount);
        double normalized = Math.Max(max / 32768.0, rms / 32768.0);
        return Math.Min(normalized, 1.0);
    }

    /// <summary>
    /// Checks if the buffer contains actual audio signal (not just noise/silence).
    /// Used for microphone disconnect detection.
    /// </summary>
    public bool HasAudioSignal(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2) 
            return false;

        // Convert bytes to 16-bit samples and check for non-zero values
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            if (Math.Abs(sample) > SignalDetectionThreshold)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determines if the accumulated recording is silent (no audio at all).
    /// </summary>
    public bool IsRecordingSilent()
    {
        lock (_lockObject)
        {
            // Need minimum samples to make a determination
            if (_totalSamples < MinimumSamplesForAnalysis)
            {
                System.Diagnostics.Debug.WriteLine($"Recording too short for silence analysis: {_totalSamples} samples");
                return false; // Too short to determine, assume not silent
            }

            double rmsDb = GetRmsDbInternal();
            bool isSilent = rmsDb < SilenceThresholdDb;
            
            System.Diagnostics.Debug.WriteLine(
                $"[AudioLevelAnalyzer] Silence analysis -> {(isSilent ? "SILENT" : "AUDIBLE")} " +
                $"(RMS {rmsDb:F2} dB, threshold {SilenceThresholdDb:F2} dB, samples {_totalSamples:N0})");
            
            return isSilent;
        }
    }

    /// <summary>
    /// Determines if the accumulated recording is quiet but not silent.
    /// </summary>
    public bool IsRecordingQuiet()
    {
        lock (_lockObject)
        {
            // Need minimum samples to make a determination
            if (_totalSamples < MinimumSamplesForAnalysis)
            {
                return false; // Too short to determine
            }

            double rmsDb = GetRmsDbInternal();
            
            // Quiet: between silence and quiet thresholds (audio present but very low)
            bool isQuiet = rmsDb >= SilenceThresholdDb && rmsDb < QuietAudioThresholdDb;
            
            System.Diagnostics.Debug.WriteLine(
                $"[AudioLevelAnalyzer] Quiet analysis -> {(isQuiet ? "QUIET" : "NORMAL")} " +
                $"(RMS {rmsDb:F2} dB, quiet< {QuietAudioThresholdDb:F2} dB, silence< {SilenceThresholdDb:F2} dB)");
            
            return isQuiet;
        }
    }

    /// <summary>
    /// Gets the current RMS level in decibels.
    /// </summary>
    public double GetRmsDb()
    {
        lock (_lockObject)
        {
            return GetRmsDbInternal();
        }
    }

    /// <summary>
    /// Internal method to get RMS in dB (must be called within lock).
    /// </summary>
    private double GetRmsDbInternal()
    {
        if (_totalSamples == 0)
            return double.NegativeInfinity;

        double rms = Math.Sqrt(_sumSquares / _totalSamples);

        if (rms <= 0)
            return double.NegativeInfinity;

        // Convert to dB (reference: 32768 = 0 dB for 16-bit audio)
        return 20 * Math.Log10(rms / 32768.0);
    }
}
