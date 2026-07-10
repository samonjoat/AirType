using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Abstraction for Groq speech-to-text client.
/// </summary>
public interface IGroqApiClient
{
    Task<GroqTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        GroqTranscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task WarmupConnectionAsync(CancellationToken cancellationToken = default);

    ConnectionStatus GetConnectionStatus();

    /// <summary>
    /// Validates the API key by making a lightweight test request.
    /// </summary>
    /// <param name="apiKey">Optional API key to validate. If null, uses the active key from CredentialManager.</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Validation result with success status and error message</returns>
    Task<ApiKeyValidationResult> ValidateApiKeyAsync(string? apiKey = null, CancellationToken cancellationToken = default);
}
