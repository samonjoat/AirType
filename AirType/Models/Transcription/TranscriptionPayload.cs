using System;

namespace AirType.Models.Transcription;

/// <summary>
/// Represents a prepared audio payload ready to send to transcription providers.
/// </summary>
public sealed class TranscriptionPayload
{
    public TranscriptionPayload(
        string mimeType,
        byte[] data,
        double? compressionRatio = null,
        double? encodeMilliseconds = null,
        TimeSpan? audioDuration = null,
        byte[]? fallbackWavBytes = null)
    {
        MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
        Data = data ?? throw new ArgumentNullException(nameof(data));
        CompressionRatio = compressionRatio;
        EncodeMilliseconds = encodeMilliseconds;
        AudioDuration = audioDuration;
        FallbackWavBytes = fallbackWavBytes;
    }

    /// <summary>
    /// MIME type of the payload (e.g., audio/ogg, audio/wav).
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Raw audio bytes ready for base64 encoding.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Optional compression ratio (PCM bytes / compressed bytes).
    /// </summary>
    public double? CompressionRatio { get; }

    /// <summary>
    /// Optional encode duration in milliseconds.
    /// </summary>
    public double? EncodeMilliseconds { get; }

    /// <summary>
    /// Optional duration of the audio represented by this payload.
    /// Used for timeout budgeting and diagnostics.
    /// </summary>
    public TimeSpan? AudioDuration { get; }

    /// <summary>
    /// Optional fallback WAV/PCM bytes to retry when compressed format is rejected.
    /// </summary>
    public byte[]? FallbackWavBytes { get; }

    // Phase 2 optimization: Cache Base64 result on first access to avoid recomputation
    private string? _cachedBase64Data;

    /// <summary>
    /// Lazily computed base64 string (cached on first call).
    /// </summary>
    public string GetBase64Data()
    {
        // Thread-safe lazy initialization using null-coalescing assignment
        return _cachedBase64Data ??= Convert.ToBase64String(Data);
    }
}
