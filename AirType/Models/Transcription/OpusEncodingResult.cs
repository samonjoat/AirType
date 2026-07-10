namespace AirType.Models.Transcription;

/// <summary>
/// Result of encoding PCM audio into an Opus payload.
/// </summary>
public sealed class OpusEncodingResult
{
    public byte[] OggBytes { get; set; } = [];
    public double DurationMilliseconds { get; set; }
    public double CompressionRatio { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
}
