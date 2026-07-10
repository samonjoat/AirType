namespace AirType.Models.Configuration;

/// <summary>
/// Indicates the transcription provider being used for audio transcription.
/// </summary>
public enum TranscriptionProvider
{
    /// <summary>
    /// Local faster-whisper transcription worker.
    /// </summary>
    Local,

    /// <summary>
    /// Google Gemini 2.5 Flash API (direct).
    /// Uses Gemini for fast, accurate transcription with default system prompt.
    /// </summary>
    Gemini,

    /// <summary>
    /// OpenRouter multi-model gateway.
    /// Supports any OpenRouter model ID (e.g., openai/whisper-1, openai/gpt-4o-mini-transcribe).
    /// </summary>
    OpenRouter,

    /// <summary>
    /// Groq speech-to-text API (OpenAI-compatible Whisper endpoints).
    /// </summary>
    Groq
}

/// <summary>
/// Indicates the provider used for post-transcription cleanup.
/// </summary>
public enum CleanupProvider
{
    Gemini,
    OpenRouter,
    Groq
}
