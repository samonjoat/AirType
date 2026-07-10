using System;
using System.Net.Http;
using AirType.Services.Audio;
using AirType.Services.Configuration;
using AirType.Services.Dictionary;
using AirType.Services.Prompts;
using AirType.Services.Storage;
using AirType.Services.Transcription;
using AirType.Services.Database;
using AirType.Services.Injection;

namespace AirType.Services;

/// <summary>
/// Container for all application services. Centralizes service creation and lifetime management.
/// </summary>
public sealed class ServiceContainer : IDisposable
{
    // Core services
    public IFilePersistenceManager FilePersistenceManager { get; }
    public IRecordingValidityGuard RecordingValidityGuard { get; }
    public IAudioInputManager AudioInputManager { get; }
    public IHotkeyManager HotkeyManager { get; }

    // Configuration services
    public AppSettingsManager AppSettingsManager { get; }

    // Transcription services
    public ICredentialManager CredentialManager { get; }
    public IGeminiApiClient GeminiApiClient { get; }
    public IOpenRouterClient OpenRouterClient { get; }
    public IOpenRouterSttClient OpenRouterSttClient { get; }
    public IGroqApiClient GroqApiClient { get; }
    public IOfflineEngineManager OfflineEngineManager { get; }
    public ILocalAsrWorkerClient LocalAsrWorkerClient { get; }
    public ILocalAsrWorkerSupervisor LocalAsrWorkerSupervisor { get; }
    public ICloudFallbackPolicy CloudFallbackPolicy { get; }
    public ITranscriptCleanupClient GeminiTranscriptCleanupClient { get; }
    public ITranscriptCleanupClient OpenRouterTranscriptCleanupClient { get; }
    public ITranscriptCleanupClient GroqTranscriptCleanupClient { get; }
    public ITranscriptionLogger TranscriptionLogger { get; }

    // UI and integration services
    public IClipboardManager ClipboardManager { get; }
    public IWindowManager WindowManager { get; }
    public ITextInjectionService TextInjectionService { get; }
    public TranscriptionHistoryManager HistoryManager { get; }

    // Prompt and encoding services
    public IPromptManager? PromptManager { get; }
    public IOpusEncodingService OpusEncodingService { get; }
    public IAudioPreprocessor AudioPreprocessor { get; }
    public IPerformanceMonitor PerformanceMonitor { get; }
    public NotificationSoundService NotificationSoundService { get; }
    public IAppSoundPolicy AppSoundPolicy { get; }
    public ISystemAudioMuteService SystemAudioMuteService { get; }

    // Dictionary services
    public IDictionaryManager DictionaryManager { get; }
    public IDictionaryPromptBuilder DictionaryPromptBuilder { get; }
    public IDictionaryBiasPromptBuilder DictionaryBiasPromptBuilder { get; }
    public IDictionaryHotwordBuilder DictionaryHotwordBuilder { get; }
    public IRollingInitialPromptBuilder RollingInitialPromptBuilder { get; }

    // Database services
    public ISqliteConnectionFactory SqliteConnectionFactory { get; }
    public HistoryDatabase HistoryDatabase { get; }
    public DictionaryDatabase DictionaryDatabase { get; }
    public NotesDatabase NotesDatabase { get; }
    public PromptDatabase PromptDatabase { get; }
    public DailyStatisticsManager DailyStatisticsManager { get; }

    // Workflow orchestration
    public ITranscriptionWorkflowService TranscriptionWorkflowService { get; }

    private bool _disposed;

    public ServiceContainer()
    {
        // Core services
        FilePersistenceManager = new FilePersistenceManager();
        RecordingValidityGuard = new RecordingValidityGuard();
        
        // Configuration services
        AppSettingsManager = new AppSettingsManager();
        
        HotkeyManager = new HotkeyManager();
        
        // Cleanup old audio files based on retention setting
        FilePersistenceManager.CleanupOldAudioFiles(AppSettingsManager.AudioRetentionDays);
        
        var audioLevelAnalyzer = new AudioLevelAnalyzer();
        var audioFileValidator = new AudioFileValidator();
        var recordingMonitor = new RecordingMonitor();
        AudioInputManager = new AudioInputManager(
            FilePersistenceManager,
            RecordingValidityGuard,
            audioLevelAnalyzer,
            audioFileValidator,
            recordingMonitor);
        SystemAudioMuteService = new SystemAudioMuteService();

        // Transcription services
        CredentialManager = new CredentialManager();

        // Database services
        SqliteConnectionFactory = new SqliteConnectionFactory();
        HistoryDatabase = new HistoryDatabase(SqliteConnectionFactory);
        DictionaryDatabase = new DictionaryDatabase(SqliteConnectionFactory);
        NotesDatabase = new NotesDatabase(SqliteConnectionFactory);
        PromptDatabase = new PromptDatabase(SqliteConnectionFactory);
        DailyStatisticsManager = new DailyStatisticsManager(SqliteConnectionFactory);
        CredentialManager.MigrateLegacyCleanupPromptSettings(PromptDatabase);
        
        // Dictionary services (must be created before API clients)
        DictionaryManager = new DictionaryManager(DictionaryDatabase);
        DictionaryPromptBuilder = new DictionaryPromptBuilder(DictionaryManager);
        DictionaryBiasPromptBuilder = new DictionaryBiasPromptBuilder(DictionaryManager);
        DictionaryHotwordBuilder = new DictionaryHotwordBuilder(DictionaryManager);
        RollingInitialPromptBuilder = new RollingInitialPromptBuilder();
        
        // API clients with dictionary support
        GeminiApiClient = new GeminiApiClient(CredentialManager, DictionaryPromptBuilder);
        OpenRouterClient = new OpenRouterClient(CredentialManager, DictionaryPromptBuilder);
        OpenRouterSttClient = new OpenRouterSttClient(CredentialManager);
        GroqApiClient = new GroqApiClient(CredentialManager);
        OfflineEngineManager = new OfflineEngineManager();
        LocalAsrWorkerClient = new LocalAsrWorkerClient();
        CloudFallbackPolicy = new CloudFallbackPolicy(CredentialManager);
        LocalAsrWorkerSupervisor = new LocalAsrWorkerSupervisor(
            OfflineEngineManager,
            CredentialManager,
            LocalAsrWorkerClient,
            DictionaryHotwordBuilder,
            RollingInitialPromptBuilder);
        GeminiTranscriptCleanupClient = new GeminiTranscriptCleanupClient(CredentialManager);
        OpenRouterTranscriptCleanupClient = new OpenRouterTranscriptCleanupClient(CredentialManager);
        GroqTranscriptCleanupClient = new GroqTranscriptCleanupClient(CredentialManager);
        TranscriptionLogger = new TranscriptionLogger();

        // UI and integration services
        ClipboardManager = new ClipboardManager();
        WindowManager = new WindowManager();
        TextInjectionService = new TextInjectionService(
            ClipboardManager,
            new UIAutomationCaretContextReader(),
            nativeTypingEnabled: () => AppSettingsManager.EnableNativeTypingInjection,
            smartInsertionEnabled: () => AppSettingsManager.EnableSmartInsertion);
        HistoryManager = new TranscriptionHistoryManager(
            FilePersistenceManager,
            TranscriptionLogger,
            HistoryDatabase,
            DailyStatisticsManager);

        // Prompt manager (optional, can fail gracefully)
        try
        {
            PromptManager = new PromptManager();
            Logger.Info("PromptManager", "Prompt manager initialized.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warning: Prompt manager failed to initialize: {ex.Message}");
            PromptManager = null;
        }

        // Encoding and preprocessing services
        OpusEncodingService = new OpusEncodingService();
        AudioPreprocessor = new AudioPreprocessor();
        PerformanceMonitor = new PerformanceMonitor();
        NotificationSoundService = new NotificationSoundService();
        AppSoundPolicy = new AppSoundPolicy(NotificationSoundService);

        // Workflow orchestration (depends on all other services)
        TranscriptionWorkflowService = new TranscriptionWorkflowService(
            CredentialManager,
            GeminiApiClient,
            OpenRouterClient,
            GroqApiClient,
            TranscriptionLogger,
            ClipboardManager,
            WindowManager,
            TextInjectionService,
            HistoryManager,
            PromptManager,
            OpusEncodingService,
            AudioPreprocessor,
            PerformanceMonitor,
            OpenRouterSttClient,
            DictionaryBiasPromptBuilder,
            DictionaryPromptBuilder,
            GeminiTranscriptCleanupClient,
            OpenRouterTranscriptCleanupClient,
            GroqTranscriptCleanupClient,
            CloudFallbackPolicy,
            PromptDatabase);

        Logger.Info("ServiceContainer", "All services initialized successfully");
    }

    /// <summary>
    /// Warms up API connections in parallel for faster first transcription.
    /// </summary>
    public async System.Threading.Tasks.Task WarmupConnectionsAsync()
    {
        var warmupTasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>
        {
            GeminiApiClient.WarmupConnectionAsync(),
            OpenRouterClient.WarmupConnectionAsync(),
            OpenRouterSttClient.WarmupConnectionAsync(),
            GroqApiClient.WarmupConnectionAsync()
        };

        if (LocalAsrProviderAvailability.ResolveActiveProvider(
                CredentialManager,
                OfflineEngineManager) == Models.Configuration.TranscriptionProvider.Local)
        {
            warmupTasks.Add(LocalAsrWorkerSupervisor.StartAsync(System.Threading.CancellationToken.None));
        }

        await System.Threading.Tasks.Task.WhenAll(warmupTasks);
        Logger.Info("ServiceContainer", "Connection warmup completed");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose in reverse order of creation
        NotificationSoundService?.Dispose();
        LocalAsrWorkerSupervisor?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        HotkeyManager?.Dispose();
        HistoryManager?.Dispose();
        (DictionaryManager as IDisposable)?.Dispose();
        (AudioInputManager as IDisposable)?.Dispose();
        (FilePersistenceManager as IDisposable)?.Dispose();

        Logger.Info("ServiceContainer", "Services disposed");
    }
}
