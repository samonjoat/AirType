using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Transcription;
using NAudio.Wave;

namespace AirType.Services.Transcription;

/// <summary>
/// Performs silence trimming and validation on 16-bit PCM WAV audio.
/// </summary>
public class AudioPreprocessor : IAudioPreprocessor
{
    private const int SilenceThresholdDb = -40; // RMS threshold
    private const int TrimThresholdMilliseconds = 500;
    private const int MinDurationAfterTrimMilliseconds = 3000;

    public async Task<AudioPreprocessingResult> PreprocessAsync(
        byte[] wavData,
        RecordingSession session,
        CancellationToken cancellationToken = default)
    {
        if (wavData == null || wavData.Length == 0)
            throw new ArgumentException("WAV data cannot be null or empty", nameof(wavData));

        using var ms = new MemoryStream(wavData, writable: false);
        using var reader = new WaveFileReader(ms);

        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
            reader.WaveFormat.BitsPerSample != 16 ||
            reader.WaveFormat.Channels != 1)
        {
            throw new NotSupportedException("Only 16-bit PCM mono WAV is supported for preprocessing.");
        }

        int sampleRate = reader.WaveFormat.SampleRate;
        int bytesPerSample = reader.BlockAlign;
        int bytesToRead = (int)reader.Length;
        var buffer = new byte[bytesToRead];
        int bytesRead = reader.Read(buffer, 0, bytesToRead);
        if (bytesRead != bytesToRead)
            throw new InvalidOperationException("Failed to read full WAV data for preprocessing.");

        int totalSamples = bytesRead / bytesPerSample;
        short[] samples = new short[totalSamples];
        Buffer.BlockCopy(buffer, 0, samples, 0, bytesRead);

        double sampleDurationMs = 1000.0 / sampleRate;
        double originalMs = samples.Length * sampleDurationMs;

        // Detect leading/trailing silence in parallel (both are CPU-bound, read-only on shared array)
        int leadingSamples = 0;
        int trailingSamples = 0;

        await Task.WhenAll(
            Task.Run(() => leadingSamples = DetectLeadingSilence(samples, SilenceThresholdDb), cancellationToken),
            Task.Run(() => trailingSamples = DetectTrailingSilence(samples, SilenceThresholdDb), cancellationToken)
        );

        int trimThresholdSamples = (int)(TrimThresholdMilliseconds / sampleDurationMs);
        int start = leadingSamples >= trimThresholdSamples ? leadingSamples : 0;
        int end = samples.Length - (trailingSamples >= trimThresholdSamples ? trailingSamples : 0);
        if (end < start) end = start; // safety

        short[] trimmed = samples[start..end];
        double trimmedMs = trimmed.Length * sampleDurationMs;

        // Validation for short audio
        bool isValid = trimmedMs >= MinDurationAfterTrimMilliseconds;
        string? validation = isValid ? null : "Recording may be too short after trimming.";

        // Reconstruct WAV
        byte[] processedBytes;
        using (var outStream = new MemoryStream())
        {
            using var writer = new WaveFileWriter(outStream, reader.WaveFormat);
            writer.WriteSamples(trimmed, 0, trimmed.Length);
            writer.Flush();
            processedBytes = outStream.ToArray();
        }

        return new AudioPreprocessingResult
        {
            ProcessedAudioData = processedBytes,
            OriginalDuration = TimeSpan.FromMilliseconds(originalMs),
            TrimmedDuration = TimeSpan.FromMilliseconds(trimmedMs),
            OriginalSize = wavData.Length,
            ProcessedSize = processedBytes.Length,
            LeadingSilenceTrimmed = TimeSpan.FromMilliseconds(leadingSamples * sampleDurationMs),
            TrailingSilenceTrimmed = TimeSpan.FromMilliseconds(trailingSamples * sampleDurationMs),
            IsValid = isValid,
            ValidationMessage = validation
        };
    }

    private static int DetectLeadingSilence(short[] samples, int thresholdDb)
    {
        int frameSize = 512;
        for (int i = 0; i < samples.Length; i += frameSize)
        {
            double rms = Rms(samples, i, Math.Min(frameSize, samples.Length - i));
            double db = RmsToDb(rms);
            if (db > thresholdDb) return i;
        }
        return samples.Length;
    }

    private static int DetectTrailingSilence(short[] samples, int thresholdDb)
    {
        int frameSize = 512;
        for (int i = samples.Length - frameSize; i >= 0; i -= frameSize)
        {
            int len = Math.Min(frameSize, samples.Length - i);
            double rms = Rms(samples, i, len);
            double db = RmsToDb(rms);
            if (db > thresholdDb) return samples.Length - (i + len);
        }
        return samples.Length;
    }

    private static double Rms(short[] data, int offset, int count)
    {
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double v = data[offset + i];
            sum += v * v;
        }
        return Math.Sqrt(sum / count);
    }

    private static double RmsToDb(double rms)
    {
        if (rms <= 0) return double.NegativeInfinity;
        return 20 * Math.Log10(rms / short.MaxValue);
    }
}
