namespace AirType.Models.Transcription;

/// <summary>
/// Response from a text-only transcript cleanup pass.
/// </summary>
public sealed class TranscriptCleanupResponse
{
    public string Text { get; set; } = string.Empty;
    public string? ModelUsed { get; set; }
    public string? RawResponseJson { get; set; }
}
