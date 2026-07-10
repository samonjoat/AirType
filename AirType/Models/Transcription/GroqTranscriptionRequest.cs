using System;

namespace AirType.Models.Transcription;

/// <summary>
/// Request parameters for Groq /audio/transcriptions endpoint.
/// </summary>
public class GroqTranscriptionRequest
{
    /// <summary>
    /// Groq model id (e.g., whisper-large-v3).
    /// </summary>
    public string ModelId { get; set; } = "whisper-large-v3";

    /// <summary>
    /// Optional prompt to bias transcription.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Temperature for decoding; defaults to 0 for deterministic output.
    /// </summary>
    public double Temperature { get; set; } = 0.0;

    /// <summary>
    /// Optional language code to force language detection.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Response format requested from Groq. Use "verbose_json" for detailed metadata.
    /// </summary>
    public string ResponseFormat { get; set; } = "verbose_json";
}
