using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Dictionary;

namespace AirType.Services.Transcription;

/// <summary>
/// HTTP client for OpenRouter API transcription.
/// Supports any OpenRouter model with automatic retry logic using Polly.
/// </summary>
public class OpenRouterClient : IOpenRouterClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ICredentialManager _credentialManager;
    private readonly IDictionaryPromptBuilder? _dictionaryPromptBuilder;
    private bool _disposed;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;

    // OpenRouter API configuration
    private const string BaseUrl = "https://openrouter.ai/api/v1";
    private const string ChatCompletionsEndpoint = "/chat/completions";
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Initializes a new instance of OpenRouterClient with credential management and retry policies.
    /// Configures timeout for API requests.
    /// </summary>
    /// <param name="credentialManager">Credential manager for hybrid API key retrieval</param>
    /// <param name="dictionaryPromptBuilder">Optional dictionary prompt builder for vocabulary hints</param>
    public OpenRouterClient(ICredentialManager credentialManager, IDictionaryPromptBuilder? dictionaryPromptBuilder = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _dictionaryPromptBuilder = dictionaryPromptBuilder;

        var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = 10,
            UseProxy = false
        };

        _httpClient = new HttpClient(new OpenRouterRetryHandler(handler))
        {
            Timeout = MaxTimeout
        };

        TranscriptionApiClientMechanics.ConfigureJsonClient(_httpClient);
    }

    /// <summary>
    /// Transcribes audio using OpenRouter API with the specified model.
    /// </summary>
    public async Task<OpenRouterTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        OpenRouterTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("OpenRouterClient", "TranscribeAudioAsync called");
        Logger.Debug("OpenRouterClient", $"Audio payload mime: {payload?.MimeType}, size: {payload?.Data.Length ?? 0:N0} bytes");
        Logger.Debug("OpenRouterClient", $"Model ID: {request?.ModelId}");

        if (payload == null || payload.Data.Length == 0)
            throw new ArgumentException("Audio payload cannot be null or empty", nameof(payload));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        Logger.Debug("OpenRouterClient", "Requesting API key from CredentialManager...");

        string? apiKey = _credentialManager.GetActiveOpenRouterApiKey();
        Logger.Debug("OpenRouterClient", $"API Key present: {!string.IsNullOrEmpty(apiKey)}");

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "No OpenRouter API key available. Please add your OpenRouter API key in Settings.");
        }

        // Convert audio to base64
        Logger.Debug("OpenRouterClient", "Converting audio to base64...");
        string base64Audio = payload.GetBase64Data();
        Logger.Debug("OpenRouterClient", $"Base64 length: {base64Audio.Length:N0} characters");
        string audioFormat = TranscriptionApiClientMechanics.GetAudioFormatFromMime(payload.MimeType);

        // Build enhanced system instruction with dictionary section
        string systemInstruction = TranscriptionApiClientMechanics.ComposeSystemInstruction(
            request.SystemInstruction,
            _dictionaryPromptBuilder,
            "OpenRouterClient");

        // Build JSON payload for OpenRouter API (correct input_audio format)
        Logger.Debug("OpenRouterClient", "Building JSON payload...");
        var requestPayload = new
        {
            model = request.ModelId,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = systemInstruction
                        }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = request.UserInstruction
                        },
                        new
                        {
                            type = "input_audio",
                            input_audio = new
                            {
                                data = base64Audio,
                                format = audioFormat
                            }
                        }
                    }
                }
            },
            temperature = request.Temperature,
            max_tokens = request.MaxOutputTokens
        };

        Logger.Debug("OpenRouterClient", "Serializing JSON payload...");
        string jsonPayload = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        Logger.Debug("OpenRouterClient", $"JSON payload size: {jsonPayload.Length:N0} characters");

        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        string apiUrl = $"{BaseUrl}{ChatCompletionsEndpoint}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = httpContent
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        requestMessage.Headers.Add("HTTP-Referer", "https://AirType.local");
        requestMessage.Headers.Add("X-Title", "AirType");

        Logger.Debug("OpenRouterClient", $"API URL: {apiUrl}");

        var timeout = CalculateTimeout(payload.AudioDuration);
        HttpResponseMessage? response = null;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            Logger.Debug("OpenRouterClient", $"Sending POST request to OpenRouter API with timeout {timeout.TotalSeconds:F1}s...");
            response = await _httpClient.SendAsync(requestMessage, linkedCts.Token);
            Logger.Debug("OpenRouterClient", $"Response received - Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new UnauthorizedAccessException(
                        $"Invalid OpenRouter API key. Status: {response.StatusCode}. " +
                        $"Please check your API key. Error: {errorBody}");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException(
                        $"Rate limit exceeded (429). Please try again later. Details: {errorBody}");
                }

                throw new HttpRequestException(
                    $"OpenRouter API returned {(int)response.StatusCode} ({response.StatusCode}). Details: {errorBody}");
            }

            string responseJson = await response.Content.ReadAsStringAsync(linkedCts.Token);
            Logger.Debug("OpenRouterClient", "Parsing JSON response...");
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("choices", out JsonElement choices) ||
                choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    "No transcription choices returned from API. " +
                    $"Response: {responseJson}");
            }

            JsonElement firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out JsonElement message) ||
                !message.TryGetProperty("content", out JsonElement content))
            {
                throw new InvalidOperationException(
                    "Invalid response structure: missing message.content. " +
                    $"Response: {responseJson}");
            }

            string transcribedText = content.GetString() ?? string.Empty;

            string? modelUsed = root.TryGetProperty("model", out JsonElement modelElement)
                ? modelElement.GetString()
                : request.ModelId;

            string? completionId = root.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString()
                : null;

            OpenRouterUsageMetadata? usageMetadata = null;
            if (root.TryGetProperty("usage", out JsonElement usageElement))
            {
                usageMetadata = new OpenRouterUsageMetadata
                {
                    PromptTokens = usageElement.TryGetProperty("prompt_tokens", out var promptTokens)
                        ? promptTokens.GetInt32() : 0,
                    CompletionTokens = usageElement.TryGetProperty("completion_tokens", out var completionTokens)
                        ? completionTokens.GetInt32() : 0,
                    TotalTokens = usageElement.TryGetProperty("total_tokens", out var totalTokens)
                        ? totalTokens.GetInt32() : 0
                };
            }

            Logger.Debug("OpenRouterClient", "Transcription successful");
            Logger.Debug("OpenRouterClient", $"Transcribed text length: {transcribedText.Length} characters");
            Logger.Debug("OpenRouterClient", $"Model used: {modelUsed}");
            _connectionStatus = ConnectionStatus.Connected;

            return new OpenRouterTranscriptionResponse
            {
                Text = transcribedText,
                ModelUsed = modelUsed,
                CompletionId = completionId,
                UsageMetadata = usageMetadata
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Debug("OpenRouterClient", $"Timeout exception: {ex.Message}");
            _connectionStatus = ConnectionStatus.Disconnected;
            throw new TimeoutException(
                $"API request timed out after {timeout.TotalSeconds} seconds. " +
                "The OpenRouter API did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            Logger.Debug("OpenRouterClient", $"HTTP exception: {ex.Message}");
            _connectionStatus = ConnectionStatus.Disconnected;
            throw new HttpRequestException(
                $"Network error while calling OpenRouter API: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            Logger.Debug("OpenRouterClient", $"JSON parse exception: {ex.Message}");
            throw new InvalidOperationException(
                $"Failed to parse JSON response from OpenRouter API: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Logger.Debug("OpenRouterClient", $"Unexpected exception: {ex.GetType().Name} - {ex.Message}");
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

            string url = $"{BaseUrl}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            _connectionStatus = response.IsSuccessStatusCode ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
            Logger.Info("OpenRouterClient", $"Warmup {(response.IsSuccessStatusCode ? "succeeded" : "failed")} with status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            Logger.Warn("OpenRouterClient", $"Warmup failed: {ex.Message}");
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

            string url = $"{BaseUrl}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ApiKeyValidationResult { IsValid = true, ErrorType = ApiKeyErrorType.None };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
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

    /// <summary>
    /// Disposes HTTP client resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }

    private static TimeSpan CalculateTimeout(TimeSpan? audioDuration)
    {
        return TranscriptionApiClientMechanics.CalculateTimeout(audioDuration, MinTimeout, MaxTimeout, baseSeconds: 10);
    }
}
