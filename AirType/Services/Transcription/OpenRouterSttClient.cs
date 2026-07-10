using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;
using AirType.Services.Configuration;

namespace AirType.Services.Transcription;

/// <summary>
/// HTTP client for OpenRouter's dedicated speech-to-text endpoint.
/// </summary>
public sealed class OpenRouterSttClient : IOpenRouterSttClient, IDisposable
{
    private const string BaseUrl = "https://openrouter.ai/api/v1";
    private const string TranscriptionsEndpoint = "/audio/transcriptions";
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly ICredentialManager _credentialManager;
    private bool _disposed;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;

    public OpenRouterSttClient(ICredentialManager credentialManager, HttpMessageHandler? handler = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        var effectiveHandler = handler ?? new HttpClientHandler
        {
            MaxConnectionsPerServer = 10,
            UseProxy = false
        };

        _httpClient = new HttpClient(effectiveHandler)
        {
            Timeout = MaxTimeout
        };

        TranscriptionApiClientMechanics.ConfigureJsonClient(_httpClient);
    }

    public async Task<OpenRouterSttTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        OpenRouterSttTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (payload == null || payload.Data.Length == 0)
        {
            throw new ArgumentException("Audio payload cannot be null or empty", nameof(payload));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string? apiKey = _credentialManager.GetActiveOpenRouterApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "No OpenRouter API key available. Please add your OpenRouter API key in Settings.");
        }

        string jsonPayload = JsonSerializer.Serialize(BuildRequestPayload(payload, request));
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{TranscriptionsEndpoint}")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        requestMessage.Headers.Add("HTTP-Referer", "https://AirType.local");
        requestMessage.Headers.Add("X-Title", "AirType");

        var timeout = TranscriptionApiClientMechanics.CalculateTimeout(
            payload.AudioDuration,
            MinTimeout,
            MaxTimeout,
            baseSeconds: 10);

        HttpResponseMessage? response = null;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            response = await _httpClient.SendAsync(requestMessage, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new UnauthorizedAccessException(
                        $"Invalid OpenRouter API key. Status: {response.StatusCode}. Error: {errorBody}");
                }

                throw new HttpRequestException(
                    $"OpenRouter STT API returned {(int)response.StatusCode} ({response.StatusCode}). Details: {errorBody}",
                    null,
                    response.StatusCode);
            }

            string responseJson = await response.Content.ReadAsStringAsync(linkedCts.Token);
            var transcriptionResponse = ParseResponse(responseJson, request.ModelId, response);
            _connectionStatus = ConnectionStatus.Connected;
            return transcriptionResponse;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            throw new TimeoutException($"OpenRouter STT request timed out after {timeout.TotalSeconds} seconds.", ex);
        }
        catch (HttpRequestException)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    public async Task WarmupConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? apiKey = _credentialManager.GetActiveOpenRouterApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                _connectionStatus = ConnectionStatus.Disconnected;
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            _connectionStatus = response.IsSuccessStatusCode ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
            Logger.Info("OpenRouterSTT", $"Warmup {(response.IsSuccessStatusCode ? "succeeded" : "failed")} with status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            Logger.Warn("OpenRouterSTT", $"Warmup failed: {ex.Message}");
        }
    }

    public ConnectionStatus GetConnectionStatus() => _connectionStatus;

    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            apiKey ??= _credentialManager.GetActiveOpenRouterApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorType = ApiKeyErrorType.Missing,
                    ErrorMessage = "No OpenRouter API key configured. Please add your API key in Settings."
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ApiKeyValidationResult { IsValid = true, ErrorType = ApiKeyErrorType.None };
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorType = ApiKeyErrorType.Invalid,
                    ErrorMessage = "Invalid OpenRouter API key. Please update it in Settings."
                };
            }

            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.Unknown,
                ErrorMessage = $"OpenRouter API validation failed with status {response.StatusCode}."
            };
        }
        catch (Exception ex)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.NetworkError,
                ErrorMessage = $"Network error while validating OpenRouter API key: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }

    private static Dictionary<string, object?> BuildRequestPayload(
        TranscriptionPayload payload,
        OpenRouterSttTranscriptionRequest request)
    {
        var requestPayload = new Dictionary<string, object?>
        {
            ["model"] = request.ModelId,
            ["input_audio"] = new Dictionary<string, object?>
            {
                ["data"] = payload.GetBase64Data(),
                ["format"] = TranscriptionApiClientMechanics.GetAudioFormatFromMime(payload.MimeType)
            },
            ["temperature"] = request.Temperature
        };

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            requestPayload["language"] = request.Language;
        }

        string providerOptionKey = TranscriptionApiClientMechanics.GetOpenRouterProviderOptionKey(request.ModelId);
        if (!string.IsNullOrWhiteSpace(providerOptionKey) &&
            !string.IsNullOrWhiteSpace(request.DictionaryBiasPrompt))
        {
            requestPayload["provider"] = new Dictionary<string, object?>
            {
                ["options"] = new Dictionary<string, object?>
                {
                    [providerOptionKey] = new Dictionary<string, object?>
                    {
                        ["prompt"] = request.DictionaryBiasPrompt
                    }
                }
            };
        }

        return requestPayload;
    }

    private static OpenRouterSttTranscriptionResponse ParseResponse(
        string responseJson,
        string requestedModelId,
        HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            string text = root.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            string? modelUsed = root.TryGetProperty("model", out var modelElement)
                ? modelElement.GetString()
                : requestedModelId;

            string? generationId = root.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : TryGetHeaderValue(response, "X-Generation-Id");

            OpenRouterSttUsageMetadata? usageMetadata = null;
            if (root.TryGetProperty("usage", out var usageElement))
            {
                usageMetadata = new OpenRouterSttUsageMetadata
                {
                    Cost = TryGetDouble(usageElement, "cost"),
                    InputTokens = TryGetInt(usageElement, "input_tokens"),
                    OutputTokens = TryGetInt(usageElement, "output_tokens"),
                    TotalTokens = TryGetInt(usageElement, "total_tokens"),
                    Seconds = TryGetDouble(usageElement, "seconds")
                };
            }

            return new OpenRouterSttTranscriptionResponse
            {
                Text = text,
                ModelUsed = modelUsed,
                GenerationId = generationId,
                UsageMetadata = usageMetadata,
                RawResponseJson = responseJson
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse JSON response from OpenRouter STT API: {ex.Message}", ex);
        }
    }

    private static string? TryGetHeaderValue(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values)
            ? System.Linq.Enumerable.FirstOrDefault(values)
            : null;
    }

    private static int TryGetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out double result)
            ? result
            : null;
    }
}
