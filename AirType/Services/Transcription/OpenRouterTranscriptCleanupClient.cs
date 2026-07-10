using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;

namespace AirType.Services.Transcription;

public sealed class OpenRouterTranscriptCleanupClient : ITranscriptCleanupClient, IDisposable
{
    private const string BaseUrl = "https://openrouter.ai/api/v1";
    private const string ChatCompletionsEndpoint = "/chat/completions";

    private readonly ICredentialManager _credentialManager;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public OpenRouterTranscriptCleanupClient(ICredentialManager credentialManager, HttpMessageHandler? handler = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _httpClient = new HttpClient(handler ?? new HttpClientHandler { MaxConnectionsPerServer = 10, UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        TranscriptionApiClientMechanics.ConfigureJsonClient(_httpClient);
    }

    public async Task<TranscriptCleanupResponse> CleanupTranscriptAsync(
        TranscriptCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserInstruction))
            throw new ArgumentException("Cleanup user instruction cannot be empty.", nameof(request));

        string? apiKey = _credentialManager.GetActiveOpenRouterApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No OpenRouter API key available. Please add your OpenRouter API key in Settings.");

        string modelId = string.IsNullOrWhiteSpace(request.ModelId)
            ? ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.OpenRouter)
            : request.ModelId;

        var requestPayload = new
        {
            model = modelId,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = request.SystemInstruction
                },
                new
                {
                    role = "user",
                    content = request.UserInstruction
                }
            },
            temperature = request.Temperature,
            max_tokens = request.MaxOutputTokens
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{ChatCompletionsEndpoint}")
        {
            Content = httpContent
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        requestMessage.Headers.Add("HTTP-Referer", "https://AirType.local");
        requestMessage.Headers.Add("X-Title", "AirType");

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenRouter cleanup API returned {(int)response.StatusCode} ({response.StatusCode}). Details: {responseJson}");
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        JsonElement root = doc.RootElement;
        JsonElement choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("No cleanup choices returned from OpenRouter API.");
        }

        JsonElement message = choices[0].GetProperty("message");
        string text = message.GetProperty("content").GetString() ?? string.Empty;
        string? modelUsed = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString()
            : modelId;

        return new TranscriptCleanupResponse
        {
            Text = text,
            ModelUsed = modelUsed,
            RawResponseJson = responseJson
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
