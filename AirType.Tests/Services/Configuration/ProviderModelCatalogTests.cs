using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using Xunit;

namespace AirType.Tests.Services.Configuration;

public sealed class ProviderModelCatalogTests
{
    [Theory]
    [InlineData("Gemini", TranscriptionProvider.Gemini)]
    [InlineData("OpenRouter", TranscriptionProvider.OpenRouter)]
    [InlineData("Groq", TranscriptionProvider.Groq)]
    [InlineData("Local", TranscriptionProvider.Local)]
    [InlineData("Cloud", TranscriptionProvider.Groq)]
    [InlineData("groq", TranscriptionProvider.Groq)]
    public void TryParseProvider_AcceptsProviderNamesUsedBySettingsAndApiKeys(string value, TranscriptionProvider expected)
    {
        bool parsed = ProviderModelCatalog.TryParseProvider(value, out var provider);

        Assert.True(parsed);
        Assert.Equal(expected, provider);
    }

    [Fact]
    public void AsrProviderOrder_OnlyExposesLocalAndCloudChoices()
    {
        Assert.Equal(
            new[] { TranscriptionProvider.Local, TranscriptionProvider.Groq },
            ProviderModelCatalog.AsrProviderOrder);
        Assert.Equal(
            new[] { "Local", "Cloud" },
            ProviderModelCatalog.AsrProviderOrder.Select(ProviderModelCatalog.GetAsrDisplayName).ToArray());
    }

    [Theory]
    [InlineData("Local", TranscriptionProvider.Local)]
    [InlineData("Cloud", TranscriptionProvider.Groq)]
    [InlineData("Groq", TranscriptionProvider.Groq)]
    public void TryParseAsrProvider_AcceptsOnlySelectableAsrChoices(string value, TranscriptionProvider expected)
    {
        bool parsed = ProviderModelCatalog.TryParseAsrProvider(value, out var provider);

        Assert.True(parsed);
        Assert.Equal(expected, provider);
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenRouter")]
    public void TryParseAsrProvider_RejectsCleanupOnlyProviders(string value)
    {
        bool parsed = ProviderModelCatalog.TryParseAsrProvider(value, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void GetModels_ReturnsProviderSpecificModelSet()
    {
        Assert.Same(ModelConfig.LocalModels, ProviderModelCatalog.GetModels(TranscriptionProvider.Local));
        Assert.Same(ModelConfig.GeminiModels, ProviderModelCatalog.GetModels(TranscriptionProvider.Gemini));
        Assert.Same(ModelConfig.OpenRouterModels, ProviderModelCatalog.GetModels(TranscriptionProvider.OpenRouter));
        Assert.Same(ModelConfig.GroqModels, ProviderModelCatalog.GetModels(TranscriptionProvider.Groq));
    }

    [Fact]
    public void ActiveModelHelpers_ReadAndWriteProviderSpecificModel()
    {
        var config = new ModelConfig();
        string groqModel = ModelConfig.GroqModels[0].Id;

        ProviderModelCatalog.SetActiveModelId(config, TranscriptionProvider.Groq, groqModel);

        Assert.Equal(groqModel, ProviderModelCatalog.GetActiveModelId(config, TranscriptionProvider.Groq));
        Assert.Equal("gemini-3.1-flash-lite", ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.Gemini));
    }

    [Fact]
    public void Catalog_DoesNotExposeKnownRetiredOrUnavailableModelIds()
    {
        var retiredOrUnavailableIds = new[]
        {
            "gemini-3.1-flash-lite-preview",
            "google/gemini-2.0-flash-lite-001",
            "google/gemini-2.0-flash-001",
            "openai/gpt-4o-audio-preview"
        };

        var allModelIds = ProviderModelCatalog.AsrProviderOrder
            .SelectMany(ProviderModelCatalog.GetModels)
            .Select(model => model.Id)
            .ToHashSet();

        foreach (string retiredId in retiredOrUnavailableIds)
        {
            Assert.DoesNotContain(retiredId, allModelIds);
        }
    }

    [Fact]
    public void OpenRouterCatalog_IncludesCuratedSpeechToTextModels()
    {
        var expectedSttModels = new[]
        {
            "openai/gpt-4o-mini-transcribe",
            "mistralai/voxtral-mini-transcribe",
            "openai/whisper-1"
        };

        foreach (string modelId in expectedSttModels)
        {
            var model = ProviderModelCatalog.FindModel(TranscriptionProvider.OpenRouter, modelId);

            Assert.NotNull(model);
            Assert.Equal(ModelEndpointKind.SpeechToText, model!.EndpointKind);
            Assert.True(model.RequiresPostProcessing);
            Assert.True(model.SupportsDictionaryBiasPrompt);
            Assert.Contains("wav", model.SupportedAudioFormats);
        }
    }

    [Fact]
    public void GeminiCatalog_OnlyExposesCurrentCleanAliases()
    {
        var models = ProviderModelCatalog.GetModels(TranscriptionProvider.Gemini);

        Assert.Equal(
            new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-3.1-pro-preview" },
            models.Select(model => model.Id).ToArray());
        Assert.Equal(
            new[] { "Gemini 3.1 Flash-Lite", "Gemini 3.5 Flash", "Gemini 3.1 Pro Preview" },
            models.Select(model => model.DisplayName).ToArray());
    }

    [Fact]
    public void OpenRouterTranscriptionCatalog_OnlyExposesCuratedSttModelsWithCleanLabels()
    {
        var models = ProviderModelCatalog.GetModels(TranscriptionProvider.OpenRouter);

        Assert.Equal(
            new[]
            {
                "openai/gpt-4o-mini-transcribe",
                "mistralai/voxtral-mini-transcribe",
                "openai/whisper-1"
            },
            models.Select(model => model.Id).ToArray());

        foreach (var model in models)
        {
            Assert.DoesNotContain("via OpenRouter", model.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OpenAI", model.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mistral", model.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Google", model.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/", model.DisplayName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CleanupCatalog_SeparatesTextModelsFromSpeechToTextModels()
    {
        var geminiCleanupModels = ProviderModelCatalog.GetCleanupModels(CleanupProvider.Gemini);
        var openRouterCleanupModels = ProviderModelCatalog.GetCleanupModels(CleanupProvider.OpenRouter);
        var groqCleanupModels = ProviderModelCatalog.GetCleanupModels(CleanupProvider.Groq);

        Assert.Equal(
            new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-3.1-pro-preview" },
            geminiCleanupModels.Select(model => model.Id).ToArray());
        Assert.Equal(
            new[]
            {
                "openai/gpt-5.4-mini",
                "anthropic/claude-haiku-4.5",
                "mistralai/mistral-small-2603",
                "google/gemini-3.1-flash-lite"
            },
            openRouterCleanupModels.Select(model => model.Id).ToArray());
        Assert.All(openRouterCleanupModels, model =>
        {
            Assert.True(model.SupportsTextCleanup);
            Assert.NotEqual(ModelEndpointKind.SpeechToText, model.EndpointKind);
            Assert.DoesNotContain("via OpenRouter", model.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(
            new[]
            {
                "llama-3.1-8b-instant",
                "groq/compound-mini"
            },
            groqCleanupModels.Select(model => model.Id).ToArray());
        Assert.All(groqCleanupModels, model =>
        {
            Assert.True(model.SupportsTextCleanup);
            Assert.Equal(ModelEndpointKind.ChatText, model.EndpointKind);
        });
        Assert.Equal("llama-3.1-8b-instant", ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.Groq));
    }

    [Fact]
    public void GeminiCatalog_UsesChatAudioEndpointAndConservativeAudioFormats()
    {
        foreach (var model in ProviderModelCatalog.GetModels(TranscriptionProvider.Gemini))
        {
            Assert.Equal(ModelEndpointKind.ChatAudio, model.EndpointKind);
            Assert.False(model.SupportsOpus);
            Assert.Contains("wav", model.SupportedAudioFormats);
        }
    }

    [Fact]
    public void DefaultOpenRouterModel_UsesModernSpeechToTextModel()
    {
        Assert.Equal("openai/gpt-4o-mini-transcribe", ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.OpenRouter));
    }

    [Fact]
    public void DefaultLocalModel_UsesWhisperSmallEnglish()
    {
        Assert.Equal(LocalAsrModelCatalog.FasterWhisperSmallEnInt8, ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.Local));
    }

    [Fact]
    public void AsrCatalog_UsesApprovedCt2LocalModelAndOneCloudEngine()
    {
        Assert.Equal(
            new[]
            {
                LocalAsrModelCatalog.FasterWhisperSmallEnInt8
            },
            ModelConfig.LocalModels.Select(model => model.Id).ToArray());
        Assert.DoesNotContain(ModelConfig.LocalModels, model => model.DisplayName.Contains("Base.en", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ModelConfig.LocalModels, model => model.Id == LocalAsrModelCatalog.WhisperCppSmallEnQ8);
        Assert.Equal(new[] { "whisper-large-v3" }, ModelConfig.GroqModels.Select(model => model.Id).ToArray());
    }

    [Fact]
    public void FindModel_WhenModelExists_ReturnsModelInfo()
    {
        var model = ProviderModelCatalog.FindModel(TranscriptionProvider.Groq, "whisper-large-v3");

        Assert.NotNull(model);
        Assert.Equal("Whisper Large V3", model!.DisplayName);
    }
}
