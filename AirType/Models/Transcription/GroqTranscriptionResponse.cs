namespace AirType.Models.Transcription;

/// <summary>
/// Response from Groq transcription endpoint.
/// </summary>
public class GroqTranscriptionResponse
{
    public string? Text { get; set; }
    public string? ModelUsed { get; set; }
    public string? Language { get; set; }
    public string? RawResponseJson { get; set; }
}
