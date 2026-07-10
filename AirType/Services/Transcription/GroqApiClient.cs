using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;
using AirType.Services.Configuration;

namespace AirType.Services.Transcription;

/// <summary>
/// HTTP client for Groq speech-to-text (OpenAI-compatible) endpoints.
/// </summary>
public class GroqApiClient : IGroqApiClient, IDisposable
{
    private const string BaseUrl = "https://api.groq.com/openai/v1";
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly ICredentialManager _credentialManager;
    private bool _disposed;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;

    public GroqApiClient(ICredentialManager credentialManager, HttpMessageHandler? handler = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));

        var effectiveHandler = handler ?? new HttpClientHandler
        {
            MaxConnectionsPerServer = 10,
            UseProxy = false
        };

        _httpClient = new HttpClient(effectiveHandler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        TranscriptionApiClientMechanics.ConfigureJsonClient(_httpClient);
    }

    public async Task<GroqTranscriptionResponse> TranscribeAudioAsync(
        TranscriptionPayload payload,
        GroqTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (payload == null || payload.Data.Length == 0)
            throw new ArgumentException("Audio payload cannot be null or empty", nameof(payload));
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string? apiKey = _credentialManager.GetActiveGroqApiKey();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("No Groq API key available. Please configure it in Settings.");

        int maxAttempts = 3;
        int attempt = 0;
        bool attemptedFallback = false;
        var currentPayload = payload;
        TimeSpan timeout = CalculateTimeout(payload.AudioDuration);

        while (attempt < maxAttempts)
        {
            attempt++;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            try
            {
                Logger.Info("GroqAPI", $"Transcription attempt {attempt}/{maxAttempts} (timeout {timeout.TotalSeconds:F1}s)");
                var response = await SendTranscriptionAsync(apiKey, currentPayload, request, linkedCts.Token);
                _connectionStatus = ConnectionStatus.Connected;
                return response;
            }
            catch (GroqFormatRejectedException) when (!attemptedFallback && currentPayload.FallbackWavBytes != null)
            {
                Logger.Warn("GroqAPI", $"Provider rejected format {currentPayload.MimeType}; falling back to WAV.");
                currentPayload = new TranscriptionPayload("audio/wav", currentPayload.FallbackWavBytes!, audioDuration: currentPayload.AudioDuration);
                attemptedFallback = true;
                attempt--; // fallback should not count against retry budget
                continue;
            }
            catch (HttpRequestException ex) when (IsRetryableStatus(ex) && attempt < maxAttempts)
            {
                _connectionStatus = ConnectionStatus.Disconnected;
                var delay = attempt switch
                {
                    1 => TimeSpan.FromSeconds(1),
                    2 => TimeSpan.FromSeconds(2),
                    _ => TimeSpan.Zero
                };
                if (delay > TimeSpan.Zero)
                {
                    Logger.Warn("GroqAPI", $"Transient error: {ex.Message}. Retrying in {delay.TotalSeconds} seconds.");
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                _connectionStatus = ConnectionStatus.Disconnected;
                var delay = attempt switch
                {
                    1 => TimeSpan.FromSeconds(1),
                    2 => TimeSpan.FromSeconds(2),
                    _ => TimeSpan.Zero
                };
                if (delay > TimeSpan.Zero)
                {
                    Logger.Warn("GroqAPI", $"Request timed out; retrying in {delay.TotalSeconds} seconds.");
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        _connectionStatus = ConnectionStatus.Disconnected;
        throw new HttpRequestException($"Groq transcription failed after {maxAttempts} attempts.");
    }

    private async Task<GroqTranscriptionResponse> SendTranscriptionAsync(
        string apiKey,
        TranscriptionPayload payload,
        GroqTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var apiUrl = $"{BaseUrl}/audio/transcriptions";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var multipart = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(payload.Data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(payload.MimeType);
        string extension = GetExtensionFromMime(payload.MimeType);
        multipart.Add(fileContent, "file", $"audio{extension}");

        multipart.Add(new StringContent(request.ModelId), "model");
        multipart.Add(new StringContent(request.Temperature.ToString("F2")), "temperature");
        multipart.Add(new StringContent(request.ResponseFormat), "response_format");

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            multipart.Add(new StringContent(request.Prompt), "prompt");
        }

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            multipart.Add(new StringContent(request.Language), "language");
        }

        httpRequest.Content = multipart;

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Error("GroqAPI", $"API request failed with status {response.StatusCode}", null, new { Error = errorBody });

                if (response.StatusCode == HttpStatusCode.UnsupportedMediaType)
                {
                    throw new GroqFormatRejectedException($"Groq rejected payload format ({payload.MimeType}). Response: {errorBody}");
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var message = TryExtractErrorMessage(errorBody);
                    if (IsFormatMessage(message))
                    {
                        throw new GroqFormatRejectedException($"Groq rejected payload format ({payload.MimeType}). Response: {errorBody}");
                    }

                    throw new HttpRequestException($"Groq API returned 400: {message ?? errorBody}", null, response.StatusCode);
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new HttpRequestException("Groq API key invalid or unauthorized.", null, response.StatusCode);
                }

                throw new HttpRequestException($"Groq API returned {(int)response.StatusCode}: {errorBody}", null, response.StatusCode);
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? text = root.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
                string? modelUsed = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : request.ModelId;
                string? language = root.TryGetProperty("language", out var langElement) ? langElement.GetString() : null;

                Logger.Debug("GroqAPI", $"Transcription successful (len {text?.Length ?? 0})");
                return new GroqTranscriptionResponse
                {
                    Text = text,
                    ModelUsed = modelUsed,
                    Language = language,
                    RawResponseJson = json
                };
            }
            catch (JsonException)
            {
                throw new HttpRequestException($"Groq response was not valid JSON. Body: {json}", null, response.StatusCode);
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Groq request timed out after {_httpClient.Timeout.TotalSeconds} seconds", ex);
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
            string? apiKey = _credentialManager.GetActiveGroqApiKey();
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
            Logger.Info("GroqAPI", $"Warmup {(response.IsSuccessStatusCode ? "succeeded" : "failed")} with status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            Logger.Warn("GroqAPI", $"Warmup failed: {ex.Message}");
        }
    }

    public ConnectionStatus GetConnectionStatus() => _connectionStatus;

    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string? apiKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            apiKey ??= _credentialManager.GetActiveGroqApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorType = ApiKeyErrorType.Missing,
                    ErrorMessage = "No Groq API key configured. Please add your API key in Settings."
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

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorType = ApiKeyErrorType.Invalid,
                    ErrorMessage = "Invalid Groq API key. Please update it in Settings."
                };
            }

            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.Unknown,
                ErrorMessage = $"Groq API validation failed with status {response.StatusCode}."
            };
        }
        catch (Exception ex)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorType = ApiKeyErrorType.NetworkError,
                ErrorMessage = $"Network error while validating Groq API key: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }

    private static TimeSpan CalculateTimeout(TimeSpan? audioDuration)
    {
        return TranscriptionApiClientMechanics.CalculateTimeout(audioDuration, MinTimeout, MaxTimeout, baseSeconds: 10);
    }

    private static string GetExtensionFromMime(string mimeType)
    {
        if (mimeType.Contains("ogg", StringComparison.OrdinalIgnoreCase))
            return ".ogg";
        if (mimeType.Contains("wav", StringComparison.OrdinalIgnoreCase))
            return ".wav";
        return ".bin";
    }

    private static bool IsRetryableStatus(HttpRequestException ex)
    {
        if (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return false;
        }

        return ex.StatusCode is null
               or HttpStatusCode.RequestTimeout
               or HttpStatusCode.ServiceUnavailable
               or HttpStatusCode.GatewayTimeout
               or HttpStatusCode.TooManyRequests;
    }

    private static string? TryExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString();
            }
        }
        catch
        {
            // ignore parse failures
        }

        return null;
    }

    private static bool IsFormatMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var lower = message.ToLowerInvariant();
        return lower.Contains("unsupported media") ||
               lower.Contains("content-type") ||
               lower.Contains("mime") ||
               lower.Contains("format") ||
               lower.Contains("audio/ogg") ||
               lower.Contains("opus");
    }

    private sealed class GroqFormatRejectedException : Exception
    {
        public GroqFormatRejectedException(string message) : base(message) { }
    }
}
