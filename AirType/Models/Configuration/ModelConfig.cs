using AirType.Models.Transcription;

namespace AirType.Models.Configuration;

public enum ModelEndpointKind
{
    ChatAudio,
    SpeechToText,
    ChatText
}

public enum ModelStability
{
    Stable,
    Preview
}

/// <summary>
/// Information about a specific transcription model.
/// </summary>
public class ModelInfo
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public string Pricing { get; set; }
    public string SpeedTier { get; set; }
    public bool SupportsTranslation { get; set; }
    public bool SupportsOpus { get; set; }
    public ModelEndpointKind EndpointKind { get; set; }
    public ModelStability Stability { get; set; }
    public string[] SupportedAudioFormats { get; set; }
    public bool SupportsDictionaryBiasPrompt { get; set; }
    public int? BiasPromptTokenLimit { get; set; }
    public bool RequiresPostProcessing { get; set; }
    public bool SupportsTextCleanup { get; set; }

    public ModelInfo(
        string id,
        string displayName,
        string pricing,
        string speedTier = "",
        bool supportsTranslation = false,
        bool supportsOpus = false,
        ModelEndpointKind endpointKind = ModelEndpointKind.ChatAudio,
        ModelStability stability = ModelStability.Stable,
        string[]? supportedAudioFormats = null,
        bool supportsDictionaryBiasPrompt = false,
        int? biasPromptTokenLimit = null,
        bool requiresPostProcessing = false,
        bool supportsTextCleanup = false)
    {
        Id = id;
        DisplayName = displayName;
        Pricing = pricing;
        SpeedTier = speedTier;
        SupportsTranslation = supportsTranslation;
        SupportsOpus = supportsOpus;
        EndpointKind = endpointKind;
        Stability = stability;
        SupportedAudioFormats = supportedAudioFormats ?? new[] { "wav" };
        SupportsDictionaryBiasPrompt = supportsDictionaryBiasPrompt;
        BiasPromptTokenLimit = biasPromptTokenLimit;
        RequiresPostProcessing = requiresPostProcessing;
        SupportsTextCleanup = supportsTextCleanup;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Configuration for available transcription models per provider.
/// Defines all supported models for Gemini API and OpenRouter API.
/// </summary>
public class ModelConfig
{
    /// <summary>
    /// Available Gemini API models.
    /// Model IDs are stored WITHOUT "models/" prefix (added dynamically by API client).
    /// </summary>
    public static readonly ModelInfo[] GeminiModels = new[]
    {
        new ModelInfo("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite", "", "fast", supportsTextCleanup: true),
        new ModelInfo("gemini-3.5-flash", "Gemini 3.5 Flash", "", "balanced", supportsTextCleanup: true),
        new ModelInfo("gemini-3.1-pro-preview", "Gemini 3.1 Pro Preview", "", "accurate", stability: ModelStability.Preview, supportsTextCleanup: true)
    };

    /// <summary>
    /// Local ASR model choices. Models are managed inside AirType's local model folder.
    /// </summary>
    public static readonly ModelInfo[] LocalModels = LocalAsrModelCatalog.All
        .Select(model => new ModelInfo(
            model.Id,
            model.DisplayName,
            "",
            "accurate",
            supportsTranslation: true,
            supportedAudioFormats: new[] { "wav" },
            supportsDictionaryBiasPrompt: model.SupportsPromptBias,
            biasPromptTokenLimit: model.SupportsHotwords ? 100 : null,
            requiresPostProcessing: true,
            endpointKind: ModelEndpointKind.SpeechToText))
        .ToArray();

    /// <summary>
    /// Available OpenRouter API models for audio transcription.
    /// Model IDs include provider prefix (used as-is in API requests).
    /// </summary>
    public static readonly ModelInfo[] OpenRouterModels = new[]
    {
        new ModelInfo(
            "openai/gpt-4o-mini-transcribe",
            "GPT-4o Mini Transcribe",
            "",
            "fast",
            supportedAudioFormats: new[] { "wav", "mp3", "flac", "m4a", "ogg", "webm", "aac" },
            supportsDictionaryBiasPrompt: true,
            biasPromptTokenLimit: 224,
            requiresPostProcessing: true,
            endpointKind: ModelEndpointKind.SpeechToText),
        new ModelInfo(
            "mistralai/voxtral-mini-transcribe",
            "Voxtral Mini Transcribe",
            "",
            "fast",
            supportedAudioFormats: new[] { "wav", "mp3", "flac", "m4a", "ogg", "webm", "aac" },
            supportsDictionaryBiasPrompt: true,
            biasPromptTokenLimit: 224,
            requiresPostProcessing: true,
            endpointKind: ModelEndpointKind.SpeechToText),
        new ModelInfo(
            "openai/whisper-1",
            "Whisper 1",
            "",
            "baseline",
            supportsTranslation: true,
            supportedAudioFormats: new[] { "wav", "mp3", "flac", "m4a", "ogg", "webm", "aac" },
            supportsDictionaryBiasPrompt: true,
            biasPromptTokenLimit: 224,
            requiresPostProcessing: true,
            endpointKind: ModelEndpointKind.SpeechToText)
    };

    /// <summary>
    /// OpenRouter text models available for transcript cleanup after raw STT.
    /// </summary>
    public static readonly ModelInfo[] OpenRouterCleanupModels = new[]
    {
        new ModelInfo(
            "openai/gpt-5.4-mini",
            "GPT-5.4 Mini",
            "",
            "balanced",
            endpointKind: ModelEndpointKind.ChatText,
            supportsTextCleanup: true),
        new ModelInfo(
            "anthropic/claude-haiku-4.5",
            "Claude Haiku 4.5",
            "",
            "balanced",
            endpointKind: ModelEndpointKind.ChatText,
            supportsTextCleanup: true),
        new ModelInfo(
            "mistralai/mistral-small-2603",
            "Mistral Small 4",
            "",
            "fast",
            endpointKind: ModelEndpointKind.ChatText,
            supportsTextCleanup: true),
        new ModelInfo(
            "google/gemini-3.1-flash-lite",
            "Gemini 3.1 Flash-Lite",
            "",
            "fast",
            endpointKind: ModelEndpointKind.ChatText,
            supportsTextCleanup: true)
    };

    /// <summary>
    /// Groq text models available for transcript cleanup after raw STT.
    /// </summary>
    public static readonly ModelInfo[] GroqCleanupModels = new[]
    {
        new ModelInfo(
            "llama-3.1-8b-instant",
            "Llama 3.1 8B Instant",
            "",
            "fast",
            endpointKind: ModelEndpointKind.ChatText,
            supportsTextCleanup: true),
        new ModelInfo(
            "groq/compound-mini",
            "Groq Compound Mini",
            "",
            "fast",
            endpointKind: ModelEndpointKind.ChatText,
            supportsTextCleanup: true)
    };

    /// <summary>
    /// Available Groq API models (Whisper via Groq).
    /// </summary>
    public static readonly ModelInfo[] GroqModels = new[]
    {
        new ModelInfo(
            "whisper-large-v3",
            "Whisper Large V3",
            "",
            "accurate",
            supportsTranslation: true,
            supportedAudioFormats: new[] { "wav", "mp3", "flac", "m4a", "ogg", "webm" },
            supportsDictionaryBiasPrompt: true,
            biasPromptTokenLimit: 224,
            requiresPostProcessing: true,
            endpointKind: ModelEndpointKind.SpeechToText)
    };

    /// <summary>
    /// Currently active model ID for local faster-whisper.
    /// </summary>
    public string ActiveLocalModel { get; set; } = LocalModels[0].Id;

    /// <summary>
    /// Currently active model ID for Gemini API.
    /// </summary>
    public string ActiveGeminiModel { get; set; } = GeminiModels[0].Id;

    /// <summary>
    /// Currently active model ID for OpenRouter API.
    /// </summary>
    public string ActiveOpenRouterModel { get; set; } = OpenRouterModels[0].Id;

    /// <summary>
    /// Currently active model ID for Groq API.
    /// </summary>
    public string ActiveGroqModel { get; set; } = GroqModels[0].Id;
}
