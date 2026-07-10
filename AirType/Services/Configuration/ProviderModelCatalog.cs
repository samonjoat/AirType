using System;
using System.Linq;
using AirType.Models.Configuration;

namespace AirType.Services.Configuration;

/// <summary>
/// Central metadata for supported transcription providers and their models.
/// </summary>
public static class ProviderModelCatalog
{
    public static readonly TranscriptionProvider[] AsrProviderOrder = new[]
    {
        TranscriptionProvider.Local,
        TranscriptionProvider.Groq
    };

    public static readonly TranscriptionProvider[] ProviderOrder = AsrProviderOrder;

    public static readonly CleanupProvider[] CleanupProviderOrder = new[]
    {
        CleanupProvider.Gemini,
        CleanupProvider.OpenRouter,
        CleanupProvider.Groq
    };

    public static string GetDisplayName(TranscriptionProvider provider) => provider switch
    {
        TranscriptionProvider.Local => "Local",
        TranscriptionProvider.Gemini => "Gemini",
        TranscriptionProvider.OpenRouter => "OpenRouter",
        TranscriptionProvider.Groq => "Groq",
        _ => provider.ToString()
    };

    public static string GetAsrDisplayName(TranscriptionProvider provider) => provider switch
    {
        TranscriptionProvider.Local => "Local",
        TranscriptionProvider.Groq => "Cloud",
        _ => GetDisplayName(provider)
    };

    public static string GetDisplayName(CleanupProvider provider) => provider switch
    {
        CleanupProvider.Gemini => "Gemini",
        CleanupProvider.OpenRouter => "OpenRouter",
        CleanupProvider.Groq => "Groq",
        _ => provider.ToString()
    };

    public static bool TryParseProvider(string? value, out TranscriptionProvider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            provider = TranscriptionProvider.Groq;
            return true;
        }

        foreach (var candidate in Enum.GetValues<TranscriptionProvider>())
        {
            if (string.Equals(value, GetDisplayName(candidate), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, GetAsrDisplayName(candidate), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                provider = candidate;
                return true;
            }
        }

        return Enum.TryParse(value, ignoreCase: true, out provider) &&
               Enum.IsDefined(typeof(TranscriptionProvider), provider);
    }

    public static bool TryParseAsrProvider(string? value, out TranscriptionProvider provider)
    {
        provider = default;
        if (!TryParseProvider(value, out var parsedProvider))
        {
            return false;
        }

        provider = NormalizeAsrProvider(parsedProvider);
        return AsrProviderOrder.Contains(provider) &&
               (parsedProvider == provider || string.Equals(value, "Cloud", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSelectableAsrProvider(TranscriptionProvider provider) =>
        AsrProviderOrder.Contains(provider);

    public static TranscriptionProvider NormalizeAsrProvider(TranscriptionProvider provider) =>
        IsSelectableAsrProvider(provider) ? provider : TranscriptionProvider.Groq;

    public static bool TryParseCleanupProvider(string? value, out CleanupProvider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in CleanupProviderOrder)
        {
            if (string.Equals(value, GetDisplayName(candidate), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                provider = candidate;
                return true;
            }
        }

        return Enum.TryParse(value, ignoreCase: true, out provider) &&
               CleanupProviderOrder.Contains(provider);
    }

    public static ModelInfo[] GetModels(TranscriptionProvider provider) => provider switch
    {
        TranscriptionProvider.Local => ModelConfig.LocalModels,
        TranscriptionProvider.Gemini => ModelConfig.GeminiModels,
        TranscriptionProvider.OpenRouter => ModelConfig.OpenRouterModels,
        TranscriptionProvider.Groq => ModelConfig.GroqModels,
        _ => Array.Empty<ModelInfo>()
    };

    public static ModelInfo[] GetCleanupModels(CleanupProvider provider) => provider switch
    {
        CleanupProvider.Gemini => ModelConfig.GeminiModels,
        CleanupProvider.OpenRouter => ModelConfig.OpenRouterCleanupModels,
        CleanupProvider.Groq => ModelConfig.GroqCleanupModels,
        _ => Array.Empty<ModelInfo>()
    };

    public static string GetDefaultModelId(TranscriptionProvider provider)
    {
        var models = GetModels(provider);
        return provider switch
        {
            TranscriptionProvider.Local => new ModelConfig().ActiveLocalModel,
            TranscriptionProvider.Gemini => new ModelConfig().ActiveGeminiModel,
            TranscriptionProvider.OpenRouter => new ModelConfig().ActiveOpenRouterModel,
            TranscriptionProvider.Groq => new ModelConfig().ActiveGroqModel,
            _ => models.Length > 0 ? models[0].Id : "unknown"
        };
    }

    public static string GetDefaultCleanupModelId(CleanupProvider provider)
    {
        var models = GetCleanupModels(provider);
        return models.Length > 0 ? models[0].Id : "unknown";
    }

    public static bool ContainsModel(TranscriptionProvider provider, string modelId) =>
        GetModels(provider).Any(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));

    public static bool ContainsCleanupModel(CleanupProvider provider, string modelId) =>
        GetCleanupModels(provider).Any(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));

    public static ModelInfo? FindModel(TranscriptionProvider provider, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return GetModels(provider).FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
    }

    public static ModelInfo? FindCleanupModel(CleanupProvider provider, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return GetCleanupModels(provider).FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
    }

    public static string GetActiveModelId(ModelConfig modelConfig, TranscriptionProvider provider) => provider switch
    {
        TranscriptionProvider.Local => modelConfig.ActiveLocalModel,
        TranscriptionProvider.Gemini => modelConfig.ActiveGeminiModel,
        TranscriptionProvider.OpenRouter => modelConfig.ActiveOpenRouterModel,
        TranscriptionProvider.Groq => modelConfig.ActiveGroqModel,
        _ => GetDefaultModelId(provider)
    };

    public static void SetActiveModelId(ModelConfig modelConfig, TranscriptionProvider provider, string modelId)
    {
        switch (provider)
        {
            case TranscriptionProvider.Local:
                modelConfig.ActiveLocalModel = modelId;
                break;
            case TranscriptionProvider.Gemini:
                modelConfig.ActiveGeminiModel = modelId;
                break;
            case TranscriptionProvider.OpenRouter:
                modelConfig.ActiveOpenRouterModel = modelId;
                break;
            case TranscriptionProvider.Groq:
                modelConfig.ActiveGroqModel = modelId;
                break;
        }
    }
}
