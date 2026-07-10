using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class OpenRouterSttClientTests
{
    [Fact]
    public async Task TranscribeAudioAsync_PostsToDedicatedTranscriptionsEndpoint()
    {
        using var handler = new CapturingHandler("""
        {
          "text": "Hello AirType.",
          "usage": {
            "cost": 0.0001,
            "input_tokens": 12,
            "output_tokens": 3,
            "total_tokens": 15,
            "seconds": 2.4
          }
        }
        """);
        var client = new OpenRouterSttClient(new FakeCredentialManager("or-test-key"), handler);
        var payload = new TranscriptionPayload("audio/wav", new byte[] { 1, 2, 3 }, audioDuration: TimeSpan.FromSeconds(2));
        var request = new OpenRouterSttTranscriptionRequest
        {
            ModelId = "openai/gpt-4o-mini-transcribe",
            DictionaryBiasPrompt = "Vocabulary: AirType.",
            Language = "en",
            Temperature = 0
        };

        OpenRouterSttTranscriptionResponse response = await client.TranscribeAudioAsync(payload, request);

        Assert.Equal(HttpMethod.Post, handler.CapturedRequest?.Method);
        Assert.Equal("https://openrouter.ai/api/v1/audio/transcriptions", handler.CapturedRequest?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.CapturedRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("or-test-key", handler.CapturedRequest?.Headers.Authorization?.Parameter);
        Assert.Equal("Hello AirType.", response.Text);
        Assert.Equal("openai/gpt-4o-mini-transcribe", response.ModelUsed);
        Assert.NotNull(response.UsageMetadata);
        Assert.Equal(15, response.UsageMetadata.TotalTokens);
    }

    [Fact]
    public async Task TranscribeAudioAsync_SendsAudioFormatAndProviderBiasOptions()
    {
        using var handler = new CapturingHandler("""{"text":"Hello AirType."}""");
        var client = new OpenRouterSttClient(new FakeCredentialManager("or-test-key"), handler);
        var payload = new TranscriptionPayload("audio/wav", new byte[] { 1, 2, 3 });
        var request = new OpenRouterSttTranscriptionRequest
        {
            ModelId = "openai/gpt-4o-mini-transcribe",
            DictionaryBiasPrompt = "Vocabulary: AirType.",
            Language = "en",
            Temperature = 0
        };

        await client.TranscribeAudioAsync(payload, request);

        using var doc = JsonDocument.Parse(handler.CapturedBody);
        JsonElement root = doc.RootElement;

        Assert.Equal("openai/gpt-4o-mini-transcribe", root.GetProperty("model").GetString());
        Assert.Equal("AQID", root.GetProperty("input_audio").GetProperty("data").GetString());
        Assert.Equal("wav", root.GetProperty("input_audio").GetProperty("format").GetString());
        Assert.Equal("en", root.GetProperty("language").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetDouble());
        Assert.Equal(
            "Vocabulary: AirType.",
            root.GetProperty("provider")
                .GetProperty("options")
                .GetProperty("openai")
                .GetProperty("prompt")
                .GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string CapturedBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            CapturedBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeCredentialManager : ICredentialManager
    {
        private readonly string? _openRouterApiKey;

        public FakeCredentialManager(string? openRouterApiKey)
        {
            _openRouterApiKey = openRouterApiKey;
        }

        public void SaveApiKey(string apiKey) => throw new NotImplementedException();
        public string? GetUserApiKey() => null;
        public void DeleteApiKey() => throw new NotImplementedException();
        public bool HasUserApiKey() => false;
        public string? GetActiveApiKey() => null;
        public ApiKeySource GetApiKeySource() => ApiKeySource.None;
        public bool IsUsingFallbackKey() => false;
        public void SaveOpenRouterApiKey(string apiKey) => throw new NotImplementedException();
        public string? GetUserOpenRouterApiKey() => _openRouterApiKey;
        public void DeleteOpenRouterApiKey() => throw new NotImplementedException();
        public bool HasUserOpenRouterApiKey() => !string.IsNullOrEmpty(_openRouterApiKey);
        public string? GetActiveOpenRouterApiKey() => _openRouterApiKey;
        public bool IsUsingOpenRouterFallbackKey() => false;
        public void SaveGroqApiKey(string apiKey) => throw new NotImplementedException();
        public string? GetUserGroqApiKey() => null;
        public void DeleteGroqApiKey() => throw new NotImplementedException();
        public bool HasUserGroqApiKey() => false;
        public string? GetActiveGroqApiKey() => null;
        public bool IsUsingGroqFallbackKey() => false;
        public void SaveActiveProvider(TranscriptionProvider provider) => throw new NotImplementedException();
        public TranscriptionProvider GetActiveProvider() => TranscriptionProvider.OpenRouter;
        public string GetActiveModelId() => "openai/gpt-4o-mini-transcribe";
        public string GetModelId(TranscriptionProvider provider) => "openai/gpt-4o-mini-transcribe";
        public void SetActiveModel(TranscriptionProvider provider, string modelId) => throw new NotImplementedException();
        public TranscriptionProvider ToggleProvider() => throw new NotImplementedException();
        public string CycleModel() => throw new NotImplementedException();
        public string GetLocalModelId() => "faster-whisper-small-en-int8";
        public string GetGeminiModelId() => "gemini-3.5-flash";
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
        public CleanupProvider GetActiveCleanupProvider() => CleanupProvider.Gemini;
        public string GetActiveCleanupModelId() => "gemini-3.1-flash-lite";
        public string GetCleanupModelId(CleanupProvider provider) => provider == CleanupProvider.Gemini
            ? "gemini-3.1-flash-lite"
            : "openai/gpt-5.4-mini";
        public void SetActiveCleanupModel(CleanupProvider provider, string modelId) => throw new NotImplementedException();
        public bool IsTranscriptCleanupEnabled() => true;
        public void SetTranscriptCleanupEnabled(bool enabled) => throw new NotImplementedException();
    }
}
