namespace AirType.Models.Transcription;

/// <summary>
/// Request parameters for OpenRouter's dedicated speech-to-text endpoint.
/// </summary>
public sealed class OpenRouterSttTranscriptionRequest
{
    public string ModelId { get; set; } = "openai/gpt-4o-mini-transcribe";
    public string? DictionaryBiasPrompt { get; set; }
    public string? Language { get; set; }
    public double Temperature { get; set; } = 0;
}
