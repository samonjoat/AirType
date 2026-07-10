namespace AirType.Models.Transcription;

/// <summary>
/// Request for a text-only cleanup pass after raw speech-to-text.
/// </summary>
public sealed class TranscriptCleanupRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string SystemInstruction { get; set; } = string.Empty;
    public string UserInstruction { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.1;
    public int MaxOutputTokens { get; set; } = 4096;
}
