using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using AirType.Models.Transcription;
using NAudio.Wave;

namespace AirType.Services.Transcription;

/// <summary>
/// Encodes 16-bit PCM WAV files into OGG/Opus for efficient upload.
/// </summary>
public sealed class OpusEncodingService : IOpusEncodingService
{
    public async Task<OpusEncodingResult> EncodeAsync(string wavFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wavFilePath))
            throw new ArgumentException("WAV file path cannot be null or empty.", nameof(wavFilePath));

        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException("WAV file not found.", wavFilePath);

        return await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();

            using var reader = new WaveFileReader(wavFilePath);

            if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16)
            {
                throw new NotSupportedException("Only 16-bit PCM WAV files are supported for Opus encoding.");
            }

            int sampleRate = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            long pcmBytesProcessed;
            var opusBytes = EncodeWaveStreamToOgg(reader, sampleRate, channels, cancellationToken, out pcmBytesProcessed);

            stopwatch.Stop();

            double pcmBytes = pcmBytesProcessed;
            double compressionRatio = opusBytes.Length == 0 ? 1 : pcmBytes / opusBytes.Length;

            return new OpusEncodingResult
            {
                OggBytes = opusBytes,
                DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                CompressionRatio = compressionRatio,
                SampleRate = sampleRate,
                Channels = channels
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Encodes WAV audio data directly from memory to OGG/Opus format.
    /// This avoids the overhead of writing to a temp file and reading it back.
    /// </summary>
    public async Task<OpusEncodingResult> EncodeFromBytesAsync(byte[] wavData, CancellationToken cancellationToken = default)
    {
        if (wavData == null || wavData.Length == 0)
            throw new ArgumentException("WAV data cannot be null or empty.", nameof(wavData));

        return await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();

            using var memoryStream = new MemoryStream(wavData, writable: false);
            using var reader = new WaveFileReader(memoryStream);

            if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16)
            {
                throw new NotSupportedException("Only 16-bit PCM WAV files are supported for Opus encoding.");
            }

            int sampleRate = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            long pcmBytesProcessed;
            var opusBytes = EncodeWaveStreamToOgg(reader, sampleRate, channels, cancellationToken, out pcmBytesProcessed);

            stopwatch.Stop();

            double pcmBytes = pcmBytesProcessed;
            double compressionRatio = opusBytes.Length == 0 ? 1 : pcmBytes / opusBytes.Length;

            return new OpusEncodingResult
            {
                OggBytes = opusBytes,
                DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                CompressionRatio = compressionRatio,
                SampleRate = sampleRate,
                Channels = channels
            };
        }, cancellationToken);
    }

    private static byte[] EncodeWaveStreamToOgg(
        WaveFileReader reader,
        int sampleRate,
        int channels,
        CancellationToken cancellationToken,
        out long pcmBytesProcessed)
    {
        var encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP, null);
        encoder.Bitrate = 24000;
        encoder.UseVBR = true;
        encoder.Complexity = 5;

        int frameSizePerChannel = Math.Max(1, sampleRate / 50); // ~20 ms
        int frameSamples = frameSizePerChannel * channels;
        int bytesPerSample = sizeof(short);
        int bufferSampleCount = Math.Max(frameSamples * 10, frameSamples);
        int bufferByteCount = bufferSampleCount * bytesPerSample;

        using var output = new MemoryStream();
        var ogg = new OpusOggWriteStream(encoder, output);
        var byteBuffer = new byte[bufferByteCount];
        var shortBuffer = new short[bufferSampleCount];
        pcmBytesProcessed = 0;

        int bytesRead;
        while ((bytesRead = reader.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((bytesRead & 1) == 1)
            {
                bytesRead -= 1;
            }

            if (bytesRead <= 0)
            {
                continue;
            }

            pcmBytesProcessed += bytesRead;
            int sampleCount = bytesRead / bytesPerSample;
            Buffer.BlockCopy(byteBuffer, 0, shortBuffer, 0, bytesRead);

            int sampleOffset = 0;
            while (sampleOffset < sampleCount)
            {
                int samplesToEncode = Math.Min(frameSamples, sampleCount - sampleOffset);
                ogg.WriteSamples(shortBuffer, sampleOffset, samplesToEncode);
                sampleOffset += samplesToEncode;
            }
        }

        ogg.Finish();

        return output.ToArray();
    }
}
