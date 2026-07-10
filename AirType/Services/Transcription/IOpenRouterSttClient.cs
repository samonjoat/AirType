using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Abstraction for OpenRouter's dedicated speech-to-text endpoint.
/// </summary>
public interface IOpenRouterSttClient
{
    Task<OpenRouterSttTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        OpenRouterSttTranscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task WarmupConnectionAsync(CancellationToken cancellationToken = default);

    ConnectionStatus GetConnectionStatus();

    Task<ApiKeyValidationResult> ValidateApiKeyAsync(string? apiKey = null, CancellationToken cancellationToken = default);
}
