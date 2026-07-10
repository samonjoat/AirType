namespace AirType.Models.Transcription;

/// <summary>
/// Response from Gemini API transcription containing transcribed text and metadata.
/// </summary>
public class GeminiTranscriptionResponse
{
    /// <summary>
    /// The transcribed text cleaned and formatted according to system instructions.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Token usage metadata for the API call.
    /// </summary>
    public UsageMetadata? UsageMetadata { get; set; }

    /// <summary>
    /// Model version used for transcription (e.g., "gemini-3.1-flash-lite").
    /// </summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// Thinking process from the model (only if IncludeThoughts was enabled in request).
    /// </summary>
    public List<string>? Thoughts { get; set; }
}

/// <summary>
/// Token usage information from Gemini API response.
/// Tracks input tokens, output tokens, and thinking tokens for cost calculation.
/// </summary>
public class UsageMetadata
{
    /// <summary>
    /// Number of tokens in the prompt (audio + system instruction).
    /// </summary>
    public int PromptTokenCount { get; set; }

    /// <summary>
    /// Number of tokens in the generated transcription.
    /// </summary>
    public int CandidatesTokenCount { get; set; }

    /// <summary>
    /// Total token count (prompt + candidates).
    /// </summary>
    public int TotalTokenCount { get; set; }

    /// <summary>
    /// Number of tokens used for thinking (if thinking_config was enabled).
    /// Billed separately at a different rate.
    /// </summary>
    public int? ThoughtsTokenCount { get; set; }
}
