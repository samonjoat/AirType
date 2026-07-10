namespace AirType.Models.Transcription;

/// <summary>
/// Configuration for Gemini API transcription requests.
/// Contains system instructions and generation parameters for optimal audio transcription.
/// </summary>
public class GeminiTranscriptionRequest
{
    /// <summary>
    /// Gemini model ID (e.g., "gemini-3.1-flash-lite", "gemini-3.5-flash").
    /// Model ID is used WITHOUT "models/" prefix in the API URL.
    /// </summary>
    public string ModelId { get; set; } = "gemini-3.1-flash-lite";

    /// <summary>
    /// System instruction that guides the model's transcription behavior.
    /// Default: removes filler words, fixes grammar, adds punctuation, handles inaudible sections.
    /// </summary>
    public string SystemInstruction { get; set; } = DefaultSystemInstruction;

    /// <summary>
    /// User instruction for the specific transcription task.
    /// Universal instruction that explicitly references the system rules.
    /// </summary>
    public string UserInstruction { get; set; } = "Transform the audio into text strictly adhering to the system instructions.";

    /// <summary>
    /// Thinking budget (tokens) for improved transcription quality.
    /// Higher values = better quality but slower and more expensive.
    /// Range: 256-2048. Default: 1024 (balanced).
    /// </summary>
    public int ThinkingBudget { get; set; } = 0;

    /// <summary>
    /// Temperature for response generation (0.0-1.0).
    /// Lower = more deterministic. Default: 0.3 for consistent transcription.
    /// </summary>
    public double Temperature { get; set; } = 0.95;

    /// <summary>
    /// Maximum output tokens for transcribed text.
    /// Default: 4096 (handles ~10-15 minutes of speech).
    /// </summary>
    public int MaxOutputTokens { get; set; } = 6000;

    /// <summary>
    /// Whether to include thinking process in response (for debugging).
    /// </summary>
    public bool IncludeThoughts { get; set; } = false;

    /// <summary>
    /// Default system instruction optimized for clean dictation transcription.
    /// Matches the Classic built-in prompt for consistency across providers.
    /// </summary>
    public const string DefaultSystemInstruction = DefaultTranscriptionInstructions.Classic;

    /// <summary>
    /// Returns a default transcription request with recommended settings.
    /// </summary>
    public static GeminiTranscriptionRequest Default => new();
}
