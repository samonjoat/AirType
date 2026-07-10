namespace AirType.Models.Transcription;

/// <summary>
/// Configuration for OpenRouter API transcription requests.
/// Supports any OpenRouter model by specifying the model ID.
/// </summary>
public class OpenRouterTranscriptionRequest
{
    /// <summary>
    /// OpenRouter model ID (e.g., "openai/gpt-4o-mini-transcribe", "openai/whisper-1").
    /// User can paste any model ID from https://openrouter.ai/models
    /// </summary>
    public string ModelId { get; set; } = "openai/gpt-4o-mini-transcribe";

    /// <summary>
    /// System instruction that guides the model's transcription behavior.
    /// Uses OpenRouter-optimized prompt focused on disfluency removal and retraction handling.
    /// </summary>
    public string SystemInstruction { get; set; } = DefaultSystemInstruction;

    /// <summary>
    /// User instruction for the specific transcription task.
    /// Universal instruction that explicitly references the system rules.
    /// </summary>
    public string UserInstruction { get; set; } = "Transform the audio into structured text strictly adhering to the system rules.";

    /// <summary>
    /// Temperature for response generation (0.0-2.0).
    /// Lower = more deterministic. Default: 0.2 for consistent transcription.
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Maximum output tokens for transcribed text.
    /// Default: 4096 (handles ~10-15 minutes of speech).
    /// </summary>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>
    /// Default system instruction optimized for OpenRouter chat-audio models.
    /// Matches the Classic built-in prompt for consistency across providers.
    /// </summary>
    public const string DefaultSystemInstruction = DefaultTranscriptionInstructions.Classic;

    /// <summary>
    /// Returns a default transcription request with recommended settings.
    /// </summary>
    public static OpenRouterTranscriptionRequest Default => new();
}
