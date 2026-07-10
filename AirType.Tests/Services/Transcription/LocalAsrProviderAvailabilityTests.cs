using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class LocalAsrProviderAvailabilityTests
{
    [Fact]
    public void GetSelectableProviders_WhenLocalIsMissing_ReturnsCloudOnly()
    {
        var providers = LocalAsrProviderAvailability.GetSelectableProviders(isLocalInstalled: false);

        Assert.Equal(new[] { TranscriptionProvider.Groq }, providers);
    }

    [Fact]
    public void GetSelectableProviders_WhenLocalIsInstalled_ReturnsFullAsrOrder()
    {
        var providers = LocalAsrProviderAvailability.GetSelectableProviders(isLocalInstalled: true);

        Assert.Equal(ProviderModelCatalog.AsrProviderOrder, providers);
    }

    [Fact]
    public void ResolveProvider_WhenLocalIsMissing_ReturnsCloud()
    {
        var offlineEngine = new FakeOfflineEngineManager(isInstalled: false);

        var provider = LocalAsrProviderAvailability.ResolveProvider(
            TranscriptionProvider.Local,
            offlineEngine,
            LocalAsrModelCatalog.FasterWhisperSmallEnInt8);

        Assert.Equal(TranscriptionProvider.Groq, provider);
    }

    [Fact]
    public void ResolveProvider_WhenLocalIsInstalled_ReturnsLocal()
    {
        var offlineEngine = new FakeOfflineEngineManager(isInstalled: true);

        var provider = LocalAsrProviderAvailability.ResolveProvider(
            TranscriptionProvider.Local,
            offlineEngine,
            LocalAsrModelCatalog.FasterWhisperSmallEnInt8);

        Assert.Equal(TranscriptionProvider.Local, provider);
    }

    [Fact]
    public void ResolveActiveProvider_WhenLocalIsMissingAndPersistIsRequested_SavesCloud()
    {
        var credentials = new FakeCredentialManager(TranscriptionProvider.Local);
        var offlineEngine = new FakeOfflineEngineManager(isInstalled: false);

        var provider = LocalAsrProviderAvailability.ResolveActiveProvider(
            credentials,
            offlineEngine,
            persistFallbackToCloud: true);

        Assert.Equal(TranscriptionProvider.Groq, provider);
        Assert.Equal(TranscriptionProvider.Groq, credentials.SavedProvider);
    }

    private sealed class FakeOfflineEngineManager : IOfflineEngineManager
    {
        private readonly bool _isInstalled;

        public FakeOfflineEngineManager(bool isInstalled)
        {
            _isInstalled = isInstalled;
        }

        public string InstallDirectory => "install";

        public string WorkerPath => "worker";

        public OfflineEngineStatus GetStatus() => GetStatus(null);

        public OfflineEngineStatus GetStatus(string? modelId) => new(
            _isInstalled,
            "install",
            "worker",
            string.Empty,
            null,
            CanInstall: true,
            _isInstalled ? "Installed" : "Not installed",
            "model",
            "Bundled offline engine",
            modelId ?? LocalAsrModelCatalog.FasterWhisperSmallEnInt8,
            "Small.en CT2",
            LocalAsrBackend.FasterWhisperCt2,
            "python.exe");

        public Task<OfflineEngineInstallResult> InstallAsync(
            IProgress<OfflineEngineInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OfflineEngineInstallResult(false, "not implemented"));

        public Task<OfflineEngineInstallResult> InstallModelAsync(
            string? modelId,
            IProgress<OfflineEngineInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OfflineEngineInstallResult(false, "not implemented"));

        public Task RemoveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveModelAsync(string? modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string GetModelFolderPath(string? modelId) => "model";
    }

    private sealed class FakeCredentialManager : ICredentialManager
    {
        private readonly TranscriptionProvider _activeProvider;

        public FakeCredentialManager(TranscriptionProvider activeProvider)
        {
            _activeProvider = activeProvider;
        }

        public TranscriptionProvider? SavedProvider { get; private set; }

        public void SaveActiveProvider(TranscriptionProvider provider) => SavedProvider = provider;

        public TranscriptionProvider GetActiveProvider() => _activeProvider;

        public string GetModelId(TranscriptionProvider provider) =>
            provider == TranscriptionProvider.Local
                ? LocalAsrModelCatalog.FasterWhisperSmallEnInt8
                : ProviderModelCatalog.GetDefaultModelId(provider);

        public string GetActiveModelId() => GetModelId(_activeProvider);

        public string GetLocalModelId() => LocalAsrModelCatalog.FasterWhisperSmallEnInt8;

        public string GetGeminiModelId() => ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.Gemini);

        public string GetOpenRouterModelId() => ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.OpenRouter);

        public string GetGroqModelId() => ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.Groq);

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
        public string? GetUserGroqApiKey() => null;
        public void DeleteGroqApiKey() => throw new NotImplementedException();
        public bool HasUserGroqApiKey() => false;
        public string? GetActiveGroqApiKey() => null;
        public bool IsUsingGroqFallbackKey() => false;
        public void SetActiveModel(TranscriptionProvider provider, string modelId) => throw new NotImplementedException();
        public TranscriptionProvider ToggleProvider() => throw new NotImplementedException();
        public string CycleModel() => throw new NotImplementedException();
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
        public string GetActiveCleanupModelId() => ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.Gemini);
        public string GetCleanupModelId(CleanupProvider provider) => ProviderModelCatalog.GetDefaultCleanupModelId(provider);
        public void SetActiveCleanupModel(CleanupProvider provider, string modelId) => throw new NotImplementedException();
        public bool IsTranscriptCleanupEnabled() => true;
        public void SetTranscriptCleanupEnabled(bool enabled) => throw new NotImplementedException();
    }
}
