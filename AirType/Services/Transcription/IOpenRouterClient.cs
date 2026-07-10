using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Interface for OpenRouter API client for audio transcription.
/// Mirrors IGeminiApiClient interface for consistency.
/// </summary>
public interface IOpenRouterClient
{
    /// <summary>
    /// Transcribes audio using OpenRouter API with the specified model.
    /// </summary>
    /// <param name="payload">Audio payload (WAV or Opus) ready for upload</param>
    /// <param name="request">Transcription request configuration with model ID</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Transcription response with text and metadata</returns>
    /// <exception cref="ArgumentException">Thrown when audio data is null or empty</exception>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when no API key is available</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when API key is invalid</exception>
    /// <exception cref="HttpRequestException">Thrown when network error occurs</exception>
    /// <exception cref="TimeoutException">Thrown when request times out</exception>
    Task<OpenRouterTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        OpenRouterTranscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Warms up HTTP connection to OpenRouter using a lightweight request.
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
