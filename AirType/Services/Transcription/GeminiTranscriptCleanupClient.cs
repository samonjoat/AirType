using System.Net.Http;
using System.Text;
using System.Text.Json;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;

namespace AirType.Services.Transcription;

public sealed class GeminiTranscriptCleanupClient : ITranscriptCleanupClient, IDisposable
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly ICredentialManager _credentialManager;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public GeminiTranscriptCleanupClient(ICredentialManager credentialManager, HttpMessageHandler? handler = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _httpClient = new HttpClient(handler ?? new HttpClientHandler { MaxConnectionsPerServer = 10, UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(45)
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

        string? apiKey = _credentialManager.GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key available. Please add your Gemini API key in Settings.");

        string modelId = string.IsNullOrWhiteSpace(request.ModelId)
            ? ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.Gemini)
            : request.ModelId;

        var requestPayload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = request.SystemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = request.UserInstruction } }
                }
            },
            generation_config = new
            {
                temperature = request.Temperature,
                max_output_tokens = request.MaxOutputTokens
            }
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        string apiUrl = $"{BaseUrl}/{modelId}:generateContent?key={apiKey}";

        using var response = await _httpClient.PostAsync(apiUrl, httpContent, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini cleanup API returned {(int)response.StatusCode} ({response.StatusCode}). Details: {responseJson}");
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        JsonElement candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("No cleanup candidates returned from Gemini API.");
        }

        JsonElement firstCandidate = candidates[0];
        JsonElement parts = firstCandidate.GetProperty("content").GetProperty("parts");
        string text = parts.GetArrayLength() > 0
            ? parts[0].GetProperty("text").GetString() ?? string.Empty
            : string.Empty;

        string? modelUsed = firstCandidate.TryGetProperty("modelVersion", out var modelVersion)
            ? modelVersion.GetString()
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
