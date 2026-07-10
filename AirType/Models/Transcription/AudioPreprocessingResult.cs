using System;

namespace AirType.Models.Transcription;

/// <summary>
/// Outcome of audio preprocessing (silence trimming, validation).
/// </summary>
public class AudioPreprocessingResult
{
    public byte[] ProcessedAudioData { get; set; } = Array.Empty<byte>();
    public TimeSpan OriginalDuration { get; set; }
    public TimeSpan TrimmedDuration { get; set; }
    public int OriginalSize { get; set; }
    public int ProcessedSize { get; set; }
    public TimeSpan LeadingSilenceTrimmed { get; set; }
    public TimeSpan TrailingSilenceTrimmed { get; set; }
    public bool IsValid { get; set; } = true;
    public string? ValidationMessage { get; set; }
}
