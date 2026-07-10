using AirType.Models.Configuration;
using AirType.Services.Configuration;

namespace AirType.Services.Transcription;

public static class LocalAsrProviderAvailability
{
    public static TranscriptionProvider[] GetSelectableProviders(bool isLocalInstalled) =>
        isLocalInstalled
            ? ProviderModelCatalog.AsrProviderOrder.ToArray()
            : ProviderModelCatalog.AsrProviderOrder
                .Where(provider => provider != TranscriptionProvider.Local)
                .ToArray();

    public static bool IsLocalModelInstalled(IOfflineEngineManager offlineEngineManager, string? modelId) =>
        offlineEngineManager.GetStatus(modelId).IsInstalled;

    public static bool IsConfiguredLocalModelInstalled(
        ICredentialManager credentialManager,
        IOfflineEngineManager offlineEngineManager) =>
        IsLocalModelInstalled(
            offlineEngineManager,
            credentialManager.GetModelId(TranscriptionProvider.Local));

    public static TranscriptionProvider ResolveProvider(
        TranscriptionProvider requestedProvider,
        IOfflineEngineManager offlineEngineManager,
        string? localModelId)
    {
        var normalizedProvider = ProviderModelCatalog.NormalizeAsrProvider(requestedProvider);
        if (normalizedProvider == TranscriptionProvider.Local &&
            !IsLocalModelInstalled(offlineEngineManager, localModelId))
        {
            return TranscriptionProvider.Groq;
        }

        return normalizedProvider;
    }

    public static TranscriptionProvider ResolveActiveProvider(
        ICredentialManager credentialManager,
        IOfflineEngineManager offlineEngineManager,
        bool persistFallbackToCloud = false)
    {
        var activeProvider = ProviderModelCatalog.NormalizeAsrProvider(credentialManager.GetActiveProvider());
        var resolvedProvider = ResolveProvider(
            activeProvider,
            offlineEngineManager,
            credentialManager.GetModelId(TranscriptionProvider.Local));

        if (persistFallbackToCloud && resolvedProvider != activeProvider)
        {
            credentialManager.SaveActiveProvider(resolvedProvider);
        }

        return resolvedProvider;
    }
}
