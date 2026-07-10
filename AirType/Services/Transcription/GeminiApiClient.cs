using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Dictionary;

namespace AirType.Services.Transcription;

/// <summary>
/// HTTP client for Google Gemini 2.5 Flash API transcription.
/// Handles audio transcription requests with hybrid API key management and error handling.
/// </summary>
public class GeminiApiClient : IGeminiApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ICredentialManager _credentialManager;
    private readonly IDictionaryPromptBuilder? _dictionaryPromptBuilder;
    private bool _disposed;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(10);

    public GeminiApiClient(ICredentialManager credentialManager, IDictionaryPromptBuilder? dictionaryPromptBuilder = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _dictionaryPromptBuilder = dictionaryPromptBuilder;

        var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = 10,
            UseProxy = false
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        TranscriptionApiClientMechanics.ConfigureJsonClient(_httpClient);
    }

    private static TimeSpan CalculateTimeout(TimeSpan? audioDuration)
    {
        // Relax base from 10s to 15s to handle API cold starts/throttling on first attempts
        return TranscriptionApiClientMechanics.CalculateTimeout(audioDuration, MinTimeout, MaxTimeout, baseSeconds: 15);
    }

    public async Task<GeminiTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        GeminiTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("GeminiApiClient", "TranscribeAudioAsync called");
        Logger.Debug("GeminiApiClient", $"Audio payload mime: {payload?.MimeType}, size: {payload?.Data.Length ?? 0:N0} bytes");

        if (payload == null || payload.Data.Length == 0)
            throw new ArgumentException("Audio payload cannot be null or empty", nameof(payload));
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        const int maxRetries = 3;
        int attempt = 0;
        Exception? lastException = null;
        TimeSpan timeout = CalculateTimeout(payload.AudioDuration);

        while (attempt < maxRetries)
        {
            attempt++;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);
            try
            {
                Logger.Info("GeminiAPI", $"Transcription attempt {attempt}/{maxRetries} (timeout {timeout.TotalSeconds:F1}s)");
                var result = await TranscribeAudioInternalAsync(payload, request, linkedCts.Token);
                _connectionStatus = ConnectionStatus.Connected;
                return result;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _connectionStatus = ConnectionStatus.Disconnected;
                Logger.Warn("GeminiAPI", $"Timeout on attempt {attempt}/{maxRetries}");
                if (attempt < maxRetries)
                {
                    var delay = attempt switch
                    {
                        1 => TimeSpan.FromSeconds(1),
                        2 => TimeSpan.FromSeconds(2),
                        _ => TimeSpan.Zero
                    };
                    if (delay > TimeSpan.Zero)
                    {
                        Logger.Info("GeminiAPI", $"Retrying in {delay.TotalSeconds} seconds...");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _connectionStatus = ConnectionStatus.Disconnected;
                Logger.Warn("GeminiAPI", $"Network error on attempt {attempt}/{maxRetries}: {ex.Message}");
                if (attempt < maxRetries)
                {
                    var delay = attempt switch
                    {
                        1 => TimeSpan.FromSeconds(1),
                        2 => TimeSpan.FromSeconds(2),
                        _ => TimeSpan.Zero
                    };
                    if (delay > TimeSpan.Zero)
                    {
                        Logger.Info("GeminiAPI", $"Retrying in {delay.TotalSeconds} seconds...");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        Logger.Error("GeminiAPI", $"All {maxRetries} transcription attempts failed. Last error: {lastException?.Message}", lastException);
        throw new HttpRequestException(
            $"Transcription failed after {maxRetries} attempts.\n\n" +
            $"Reason: {lastException?.Message}\n\n" +
            "Please check:\n" +
            "• Your internet connection\n" +
            "• Firewall settings\n" +
            "• API key validity in Settings",
            lastException);
    }

    private async Task<GeminiTranscriptionResponse> TranscribeAudioInternalAsync(
        TranscriptionPayload payload,
        GeminiTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("GeminiApiClient", "Requesting API key from CredentialManager...");

        string? apiKey = _credentialManager.GetActiveApiKey();
        ApiKeySource keySource = _credentialManager.GetApiKeySource();

        Logger.Debug("GeminiApiClient", $"API Key Source: {keySource}");
        Logger.Debug("GeminiApiClient", $"API Key present: {!string.IsNullOrEmpty(apiKey)}");

        if (string.IsNullOrEmpty(apiKey))
        {
            if (keySource == ApiKeySource.None)
            {
                throw new InvalidOperationException(
                    "No API key available. Please add your Gemini API key in Settings.");
            }
            else
            {
                throw new InvalidOperationException(
                    "No additional Gemini API quota is available. Please add your own key in Settings. " +
                    "Get a free key at: https://ai.google.dev/");
            }
        }

        Logger.Debug("GeminiApiClient", "Converting audio to base64...");
        string base64Audio = payload.GetBase64Data();
        Logger.Debug("GeminiApiClient", $"Base64 length: {base64Audio.Length:N0} characters");

        // Build enhanced system instruction with dictionary section
        string systemInstruction = TranscriptionApiClientMechanics.ComposeSystemInstruction(
            request.SystemInstruction,
            _dictionaryPromptBuilder,
            "GeminiApiClient");

        Logger.Debug("GeminiApiClient", "Building JSON payload...");
        var requestPayload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = request.UserInstruction },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = payload.MimeType,
                                data = base64Audio
                            }
                        }
                    }
                }
            },
            generation_config = new
            {
                temperature = request.Temperature,
                max_output_tokens = request.MaxOutputTokens
            }
        };

        string modelId = request.ModelId ?? "gemini-2.5-pro";
        string apiUrl = $"{BaseUrl}/{modelId}:generateContent?key={apiKey}";
        // Note: Intentionally not logging the full URL to avoid exposing the API key
        Logger.Debug("GeminiApiClient", $"API endpoint: {BaseUrl}/{modelId}:generateContent");

        Logger.Debug("GeminiApiClient", "Serializing JSON payload...");
        string jsonPayload = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions { WriteIndented = false });
        Logger.Debug("GeminiApiClient", $"JSON payload size: {jsonPayload.Length:N0} characters");

        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        try
        {
            Logger.Debug("GeminiApiClient", "Sending POST request to Gemini API...");
            response = await _httpClient.PostAsync(apiUrl, httpContent, cancellationToken);
            Logger.Debug("GeminiApiClient", $"Response received - Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Error("GeminiAPI", $"API request failed with status {response.StatusCode}", null, new { ErrorBody = errorBody });

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new UnauthorizedAccessException(
                        "Invalid API key. Please check your Gemini API key in Settings.\n\n" +
                        "Get a free API key at: https://ai.google.dev/");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new HttpRequestException(
                        $"Gemini API returned {response.StatusCode}. The request will not be retried.\n\nDetails: {errorBody}");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException(
                        "Rate limit exceeded. The API is receiving too many requests.\n\n" +
                        "Please try again in a few moments.");
                }

                throw new HttpRequestException(
                    $"Gemini API returned {response.StatusCode}. Details: {errorBody}");
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            Logger.Debug("GeminiApiClient", "Reading response content...");
            Logger.Debug("GeminiApiClient", $"Response JSON size: {responseJson.Length:N0} characters");

            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            var candidates = root.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No candidates returned from Gemini API.");
            }

            var firstCandidate = candidates[0];
            var parts = firstCandidate.GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No parts returned from Gemini API.");
            }

            string transcribedText = parts[0].GetProperty("text").GetString() ?? string.Empty;
            string? modelVersion = firstCandidate.TryGetProperty("modelVersion", out var mv) ? mv.GetString() : modelId;
            UsageMetadata? usageMetadata = null;
            if (firstCandidate.TryGetProperty("usageMetadata", out var usageElement))
            {
                usageMetadata = new UsageMetadata
                {
                    PromptTokenCount = usageElement.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0,
                    CandidatesTokenCount = usageElement.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0,
                    TotalTokenCount = usageElement.TryGetProperty("totalTokenCount", out var t) ? t.GetInt32() : 0
                };
            }

            Logger.Debug("GeminiApiClient", "Transcription successful!");
            Logger.Debug("GeminiApiClient", $"Transcribed text length: {transcribedText.Length} characters");
            return new GeminiTranscriptionResponse
            {
                Text = transcribedText,
                UsageMetadata = usageMetadata,
                ModelVersion = modelVersion,
                Thoughts = null
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Error("GeminiAPI", $"API request timed out after {_httpClient.Timeout.TotalSeconds} seconds", ex);
            throw new TimeoutException(
                $"API request timed out after {_httpClient.Timeout.TotalSeconds} seconds.\n\n" +
                "The Gemini API did not respond in time.\n\n" +
                "Please try again. If this persists:\n" +
                "• Check your internet connection\n" +
                "• Try a shorter recording\n" +
                "• Check API status at: https://status.cloud.google.com/");
        }
        catch (HttpRequestException ex)
        {
            Logger.Debug("GeminiApiClient", $"HTTP exception: {ex.Message}");
            throw new HttpRequestException(
                $"Network error while calling Gemini API: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            Logger.Error("GeminiAPI", "Invalid API response format - failed to parse JSON", ex);
            throw new InvalidOperationException(
                "Invalid API response format.\n\n" +
                "The Gemini API returned a response that could not be parsed.\n\n" +
                $"Technical details: {ex.Message}", ex);
        }
        catch (Exception)
        {
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
            string? apiKey = _credentialManager.GetActiveApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                _connectionStatus = ConnectionStatus.Disconnected;
                return;
            }

            string url = $"{BaseUrl}?key={apiKey}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            _connectionStatus = response.IsSuccessStatusCode ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
            Logger.Info("GeminiAPI", $"Warmup {(response.IsSuccessStatusCode ? "succeeded" : "failed")} with status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            Logger.Warn("GeminiAPI", $"Warmup failed: {ex.Message}");
        }
    }

    public ConnectionStatus GetConnectionStatus() => _connectionStatus;

    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            apiKey ??= _credentialManager.GetActiveApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorType = ApiKeyErrorType.Missing,
                    ErrorMessage = "No Gemini API key configured. Please add your API key in Settings.\n\nGet a free key at: https://ai.google.dev/"
                };
            }

            string url = $"{BaseUrl}?key={apiKey}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
                    ErrorMessage = "Invalid Gemini API key. Please update it in Settings."
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorType = ApiKeyErrorType.QuotaExceeded,
                    ErrorMessage = "API quota exceeded. Please check your usage at:\nhttps://console.cloud.google.com/apis/api/generativelanguage.googleapis.com/quotas"
                };
            }

            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.Unknown,
                ErrorMessage = $"API validation failed with status {response.StatusCode}.\nPlease check your API key and try again."
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.NetworkError,
                ErrorMessage = "Network timeout while validating API key.\nPlease check your internet connection."
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.NetworkError,
                ErrorMessage = $"Network error while validating API key: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.Unknown,
                ErrorMessage = $"Unexpected error during validation: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }
}
