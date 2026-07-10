namespace AirType.Models.Transcription;

/// <summary>
/// Response from OpenRouter API transcription containing transcribed text and metadata.
/// </summary>
public class OpenRouterTranscriptionResponse
{
    /// <summary>
    /// The transcribed text from the audio.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The actual model that was used for transcription.
    /// May differ from requested model if fallback models were configured.
    /// </summary>
    public string? ModelUsed { get; set; }

    /// <summary>
    /// Token usage metadata for the API call.
    /// </summary>
    public OpenRouterUsageMetadata? UsageMetadata { get; set; }

    /// <summary>
    /// Unique ID for this completion from OpenRouter.
    /// </summary>
    public string? CompletionId { get; set; }
}

/// <summary>
/// Token usage information from OpenRouter API response.
/// Tracks input tokens, output tokens for cost calculation.
/// </summary>
public class OpenRouterUsageMetadata
{
    /// <summary>
    /// Number of tokens in the prompt (audio + system instruction).
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Number of tokens in the generated transcription.
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Total token count (prompt + completion).
    /// </summary>
    public int TotalTokens { get; set; }
}
