using System.Net;
using System.Text.Json;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class GroqTranscriptCleanupClientTests
{
    [Fact]
    public async Task CleanupTranscriptAsync_ForCompoundMiniDisablesAllTools()
    {
        var handler = new CaptureHandler();
        using var client = new GroqTranscriptCleanupClient(new FakeCredentialManager(), handler);

        await client.CleanupTranscriptAsync(new TranscriptCleanupRequest
        {
            ModelId = "groq/compound-mini",
            SystemInstruction = "Clean transcript.",
            UserInstruction = "Raw transcript.",
            Temperature = 0.2,
            MaxOutputTokens = 123
        });

        Assert.NotNull(handler.CapturedJson);
        using JsonDocument doc = JsonDocument.Parse(handler.CapturedJson!);
        JsonElement root = doc.RootElement;

        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", handler.CapturedUri);
        Assert.Equal("Bearer", handler.CapturedAuthorizationScheme);
        Assert.Equal("test-groq-key", handler.CapturedAuthorizationParameter);
        Assert.DoesNotContain("HTTP-Referer", handler.CapturedHeaders);
        Assert.DoesNotContain("X-Title", handler.CapturedHeaders);
        Assert.Equal("groq/compound-mini", root.GetProperty("model").GetString());
        Assert.Equal(123, root.GetProperty("max_completion_tokens").GetInt32());
        JsonElement enabledTools = root
            .GetProperty("compound_custom")
            .GetProperty("tools")
            .GetProperty("enabled_tools");
        Assert.Equal(JsonValueKind.Array, enabledTools.ValueKind);
        Assert.Equal(0, enabledTools.GetArrayLength());
    }

    [Fact]
    public async Task CleanupTranscriptAsync_ForPlainModelOmitsCompoundCustom()
    {
        var handler = new CaptureHandler();
        using var client = new GroqTranscriptCleanupClient(new FakeCredentialManager(), handler);

        await client.CleanupTranscriptAsync(new TranscriptCleanupRequest
        {
            ModelId = "llama-3.1-8b-instant",
            SystemInstruction = "Clean transcript.",
            UserInstruction = "Raw transcript."
        });

        Assert.NotNull(handler.CapturedJson);
        using JsonDocument doc = JsonDocument.Parse(handler.CapturedJson!);

        Assert.Equal("llama-3.1-8b-instant", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.TryGetProperty("compound_custom", out _));
    }

    [Fact]
    public async Task CleanupTranscriptAsync_WhenContentIsMissing_ReturnsEmptyText()
    {
        var handler = new CaptureHandler(
            """
            {
              "choices": [
                {
                  "message": {
                  }
                }
              ],
              "model": "groq/compound-mini"
            }
            """);
        using var client = new GroqTranscriptCleanupClient(new FakeCredentialManager(), handler);

        TranscriptCleanupResponse response = await client.CleanupTranscriptAsync(new TranscriptCleanupRequest
        {
            ModelId = "groq/compound-mini",
            SystemInstruction = "Clean transcript.",
            UserInstruction = "Raw transcript."
        });

        Assert.Equal(string.Empty, response.Text);
        Assert.Equal("groq/compound-mini", response.ModelUsed);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CaptureHandler(string? responseJson = null)
        {
            _responseJson = responseJson ??
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Cleaned transcript."
                      }
                    }
                  ],
                  "model": "llama-3.1-8b-instant"
                }
                """;
        }

        public string? CapturedJson { get; private set; }
        public string? CapturedUri { get; private set; }
        public string? CapturedAuthorizationScheme { get; private set; }
        public string? CapturedAuthorizationParameter { get; private set; }
        public HashSet<string> CapturedHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri?.ToString();
            CapturedAuthorizationScheme = request.Headers.Authorization?.Scheme;
            CapturedAuthorizationParameter = request.Headers.Authorization?.Parameter;
            foreach (var header in request.Headers)
            {
                CapturedHeaders.Add(header.Key);
            }

            CapturedJson = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            };
        }
    }

    private sealed class FakeCredentialManager : ICredentialManager
    {
        public void SaveApiKey(string apiKey) => throw new NotImplementedException();
        public string? GetUserApiKey() => null;
        public void DeleteApiKey() => throw new NotImplementedException();
        public bool HasUserApiKey() => false;
        public string? GetActiveApiKey() => null;
        public ApiKeySource GetApiKeySource() => ApiKeySource.None;
        public bool IsUsingFallbackKey() => false;
        public void SaveOpenRouterApiKey(string apiKey) => throw new NotImplementedException();
        public string? GetUserOpenRouterApiKey() => null;
        public void DeleteOpenRouterApiKey() => throw new NotImplementedException();
        public bool HasUserOpenRouterApiKey() => false;
        public string? GetActiveOpenRouterApiKey() => null;
        public bool IsUsingOpenRouterFallbackKey() => false;
        public void SaveGroqApiKey(string apiKey) => throw new NotImplementedException();
        public string? GetUserGroqApiKey() => "test-groq-key";
        public void DeleteGroqApiKey() => throw new NotImplementedException();
        public bool HasUserGroqApiKey() => true;
        public string? GetActiveGroqApiKey() => "test-groq-key";
        public bool IsUsingGroqFallbackKey() => false;
        public void SaveActiveProvider(TranscriptionProvider provider) => throw new NotImplementedException();
        public TranscriptionProvider GetActiveProvider() => TranscriptionProvider.Groq;
        public string GetActiveModelId() => "whisper-large-v3";
        public string GetModelId(TranscriptionProvider provider) => "whisper-large-v3";
        public void SetActiveModel(TranscriptionProvider provider, string modelId) => throw new NotImplementedException();
        public TranscriptionProvider ToggleProvider() => throw new NotImplementedException();
        public string CycleModel() => throw new NotImplementedException();
        public string GetLocalModelId() => LocalAsrModelCatalog.FasterWhisperSmallEnInt8;
        public string GetGeminiModelId() => "gemini-3.1-flash-lite";
        public string GetOpenRouterModelId() => "openai/gpt-4o-mini-transcribe";
        public string GetGroqModelId() => "whisper-large-v3";
        public TextFormattingMode GetTextFormattingMode() => TextFormattingMode.PlainText;
        public void SetTextFormattingMode(TextFormattingMode mode) => throw new NotImplementedException();
        public CleanupContextMode GetCleanupContextMode() => CleanupContextMode.Auto;
        public void SetCleanupContextMode(CleanupContextMode mode) => throw new NotImplementedException();
        public CleanupIntensity GetCleanupIntensity() => CleanupIntensity.Standard;
        public void SetCleanupIntensity(CleanupIntensity intensity) => throw new NotImplementedException();
        public CleanupStyleOverrideKind GetCleanupStyleOverrideKind() => CleanupStyleOverrideKind.None;
        public int? GetCleanupStylePromptId() => null;
        public void SetCleanupStyleOverride(CleanupStyleOverrideKind kind, int? promptId) => throw new NotImplementedException();
        public int GetCleanupPromptMigrationVersion() => 1;
        public void MigrateLegacyCleanupPromptSettings(AirType.Services.Database.PromptDatabase promptDatabase) => throw new NotImplementedException();
        public void SaveActiveCleanupProvider(CleanupProvider provider) => throw new NotImplementedException();
        public CleanupProvider GetActiveCleanupProvider() => CleanupProvider.Groq;
        public string GetActiveCleanupModelId() => "llama-3.1-8b-instant";
        public string GetCleanupModelId(CleanupProvider provider) => ProviderModelCatalog.GetDefaultCleanupModelId(provider);
        public void SetActiveCleanupModel(CleanupProvider provider, string modelId) => throw new NotImplementedException();
        public bool IsTranscriptCleanupEnabled() => true;
        public void SetTranscriptCleanupEnabled(bool enabled) => throw new NotImplementedException();
    }
}
