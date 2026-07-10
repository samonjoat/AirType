namespace AirType.Models.Transcription;

/// <summary>
/// Response from OpenRouter's dedicated speech-to-text endpoint.
/// </summary>
public sealed class OpenRouterSttTranscriptionResponse
{
    public string Text { get; set; } = string.Empty;
    public string? ModelUsed { get; set; }
    public string? GenerationId { get; set; }
    public OpenRouterSttUsageMetadata? UsageMetadata { get; set; }
    public string? RawResponseJson { get; set; }
}

public sealed class OpenRouterSttUsageMetadata
{
    public double? Cost { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public double? Seconds { get; set; }
}
