using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Interface for Google Gemini API client for audio transcription.
/// Provides methods for transcribing audio data using Gemini 2.5 Flash model.
/// </summary>
public interface IGeminiApiClient
{
    /// <summary>
    /// Transcribes audio data using Google Gemini 2.5 Flash API.
    /// </summary>
    /// <param name="payload">Audio payload (WAV or Opus) ready for upload</param>
    /// <param name="request">Transcription request configuration</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Transcription response with text and metadata</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when API key is invalid</exception>
    /// <exception cref="TimeoutException">Thrown when API request times out</exception>
    /// <exception cref="HttpRequestException">Thrown when API request fails</exception>
    Task<GeminiTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        GeminiTranscriptionRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Warms up HTTP connection to Gemini API using a lightweight request (e.g., models list).
    /// Non-blocking best-effort.
    /// </summary>
    Task WarmupConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current connection status as observed by the client.
    /// </summary>
    ConnectionStatus GetConnectionStatus();

    /// <summary>
    /// Validates the API key by making a lightweight test request.
    /// </summary>
    /// <param name="apiKey">Optional API key to validate. If null, uses the active key from CredentialManager.</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Validation result with success status and error message</returns>
    Task<ApiKeyValidationResult> ValidateApiKeyAsync(string? apiKey = null, CancellationToken cancellationToken = default);
}
