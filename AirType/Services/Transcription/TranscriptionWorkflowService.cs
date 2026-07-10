using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Configuration;

using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Database;
using AirType.Services.Dictionary;
using AirType.Services.Prompts;
using AirType.Services.Storage;

namespace AirType.Services.Transcription;

/// <summary>
/// Service that orchestrates the complete transcription workflow.
/// Extracted from App.xaml.cs to improve maintainability and testability.
/// </summary>
public sealed class TranscriptionWorkflowService : ITranscriptionWorkflowService
{
    // Dependencies
    private readonly ICredentialManager? _credentialManager;
    private readonly IGeminiApiClient? _geminiApiClient;
    private readonly IOpenRouterClient? _openRouterClient;
    private readonly IOpenRouterSttClient? _openRouterSttClient;
    private readonly IGroqApiClient? _groqApiClient;
    private readonly ITranscriptionLogger? _transcriptionLogger;
    private readonly IClipboardManager? _clipboardManager;
    private readonly IWindowManager? _windowManager;
    private readonly ITextInjectionService? _textInjectionService;
    private readonly TranscriptionHistoryManager? _historyManager;
    private readonly IPromptManager? _promptManager;
    private readonly IOpusEncodingService? _opusEncodingService;
    private readonly IAudioPreprocessor? _audioPreprocessor;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private readonly IDictionaryBiasPromptBuilder? _dictionaryBiasPromptBuilder;
    private readonly IDictionaryPromptBuilder? _dictionaryPromptBuilder;
    private readonly ITranscriptCleanupClient? _geminiTranscriptCleanupClient;
    private readonly ITranscriptCleanupClient? _openRouterTranscriptCleanupClient;
    private readonly ITranscriptCleanupClient? _groqTranscriptCleanupClient;
    private readonly ICloudFallbackPolicy? _cloudFallbackPolicy;
    private readonly PromptDatabase? _promptDatabase;

    // State
    private CancellationTokenSource? _cancellationSource;
    private bool _isTranscribing;
    private bool _isInjecting;
    private bool _isWorkflowActive;
    private TranscriptionWorkflowStage _currentStage = TranscriptionWorkflowStage.Idle;
    private WindowContext? _lastWindowContext;

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    private static class WorkflowStatus
    {
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    // Process name for checking if window belongs to this app
    private static readonly string CurrentProcessName = Process.GetCurrentProcess().ProcessName;

    public TranscriptionWorkflowService(
        ICredentialManager? credentialManager,
        IGeminiApiClient? geminiApiClient,
        IOpenRouterClient? openRouterClient,
        IGroqApiClient? groqApiClient,
        ITranscriptionLogger? transcriptionLogger,
        IClipboardManager? clipboardManager,
        IWindowManager? windowManager,
        ITextInjectionService? textInjectionService,
        TranscriptionHistoryManager? historyManager,
        IPromptManager? promptManager,
        IOpusEncodingService? opusEncodingService,
        IAudioPreprocessor? audioPreprocessor,
        IPerformanceMonitor? performanceMonitor,
        IOpenRouterSttClient? openRouterSttClient = null,
        IDictionaryBiasPromptBuilder? dictionaryBiasPromptBuilder = null,
        IDictionaryPromptBuilder? dictionaryPromptBuilder = null,
        ITranscriptCleanupClient? geminiTranscriptCleanupClient = null,
        ITranscriptCleanupClient? openRouterTranscriptCleanupClient = null,
        ITranscriptCleanupClient? groqTranscriptCleanupClient = null,
        ICloudFallbackPolicy? cloudFallbackPolicy = null,
        PromptDatabase? promptDatabase = null)
    {
        _credentialManager = credentialManager;
        _geminiApiClient = geminiApiClient;
        _openRouterClient = openRouterClient;
        _openRouterSttClient = openRouterSttClient;
        _groqApiClient = groqApiClient;
        _transcriptionLogger = transcriptionLogger;
        _clipboardManager = clipboardManager;
        _windowManager = windowManager;
        _textInjectionService = textInjectionService;
        _historyManager = historyManager;
        _promptManager = promptManager;
        _opusEncodingService = opusEncodingService;
        _audioPreprocessor = audioPreprocessor;
        _performanceMonitor = performanceMonitor;
        _dictionaryBiasPromptBuilder = dictionaryBiasPromptBuilder;
        _dictionaryPromptBuilder = dictionaryPromptBuilder;
        _geminiTranscriptCleanupClient = geminiTranscriptCleanupClient;
        _openRouterTranscriptCleanupClient = openRouterTranscriptCleanupClient;
        _groqTranscriptCleanupClient = groqTranscriptCleanupClient;
        _cloudFallbackPolicy = cloudFallbackPolicy;
        _promptDatabase = promptDatabase;
    }

    public CancellationTokenSource? CancellationSource => _cancellationSource;
    public bool IsTranscribing => _isTranscribing;
    public bool IsInjecting => _isInjecting;
    public bool IsWorkflowActive => _isWorkflowActive;
    public TranscriptionWorkflowStage CurrentStage => _currentStage;
    public WindowContext? LastWindowContext => _lastWindowContext;

    public void CancelCurrentOperation()
    {
        Logger.Info("Workflow", "Cancellation requested for active workflow");
        _cancellationSource?.Cancel();
    }

    public void ResetWorkflowState()
    {
        _isTranscribing = false;
        _isInjecting = false;
        _isWorkflowActive = false;
        SetStage(TranscriptionWorkflowStage.Idle);
        _cancellationSource?.Dispose();
        _cancellationSource = null;
    }

    private void SetStage(TranscriptionWorkflowStage stage, IWorkflowUICallbacks? uiCallbacks = null)
    {
        if (_currentStage == stage)
        {
            return;
        }

        _currentStage = stage;
        Logger.Debug("Workflow", $"Stage changed to {stage}.");
        uiCallbacks?.UpdateWidgetWorkflowStage(stage);
    }

    public async Task<TranscriptionWorkflowResult> RunWorkflowAsync(
        RecordingSession session,
        TranscriptionProvider provider,
        WindowContext? capturedWindowContext,
        WindowContext? lastWindowContext,
        bool forceClipboardCopy,
        bool isTestRun,
        IWorkflowUICallbacks uiCallbacks,
        byte[]? preloadedAudioData = null,
        ILocalAsrSession? localAsrSession = null)
    {
        _lastWindowContext = lastWindowContext;
        string requestId = session.Id.ToString();
        bool trackingStarted = false;
        string transcribedText = string.Empty;
        
        // Pre-initialize modelVersion based on current settings so failure path has it
        string requestedModelVersion = GetConfiguredModelId(provider);
        string modelVersion = requestedModelVersion;
        var cleanupContext = CleanupContextResolver.Resolve(
            capturedWindowContext,
            _credentialManager?.GetCleanupContextMode() ?? CleanupContextMode.Auto);
        var effectiveFormatting = CleanupContextResolver.ResolveEffectiveFormatting(
            cleanupContext,
            _credentialManager?.GetTextFormattingMode() ?? TextFormattingMode.PlainText);

        bool injectionSucceeded = false;
        bool clipboardFallbackUsed = false;
        bool historySaved = false;
        bool widgetFailureFlashRequested = false;
        TranscriptionHistoryEntry? persistedHistoryEntry = null;

        try
        {
            _isWorkflowActive = true;
            _isTranscribing = true;
            SetStage(TranscriptionWorkflowStage.LoadingAudio, uiCallbacks);
            Debug.WriteLine("[Workflow] Transcription phase started.");

            // Create cancellation token for this transcription
            _cancellationSource?.Cancel();
            _cancellationSource?.Dispose();
            _cancellationSource = new CancellationTokenSource();
            var cancellationToken = _cancellationSource.Token;

            // Phase 3 optimization: Use preloaded audio data if available, otherwise read from file
            byte[] audioData;
            if (preloadedAudioData != null && preloadedAudioData.Length > 0)
            {
                audioData = preloadedAudioData;
                Debug.WriteLine($"[Workflow] [+] Using preloaded audio data: {audioData.Length:N0} bytes (skipped file read)");
            }
            else
            {
                Debug.WriteLine("[Workflow] Reading WAV file from disk...");
                Debug.WriteLine($"[Workflow] File path: {session.FilePath}");
                audioData = await File.ReadAllBytesAsync(session.FilePath, cancellationToken);
                Debug.WriteLine($"[Workflow] [+] Read {audioData.Length:N0} bytes from {Path.GetFileName(session.FilePath)}");
            }

            _performanceMonitor?.StartTracking(requestId, audioData.Length, session.Duration);
            trackingStarted = true;

            if (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("[KeyboardHook] Transcription cancelled after audio load");
                return new TranscriptionWorkflowResult { WasCancelled = true };
            }

            RawTranscriptionResult transcriptionResult;
            if (provider == TranscriptionProvider.Local)
            {
                SetStage(TranscriptionWorkflowStage.Transcribing, uiCallbacks);
                transcriptionResult = await TranscribeWithLocalStreamingAsync(
                    session,
                    audioData,
                    cancellationToken,
                    requestId,
                    localAsrSession,
                    uiCallbacks);
            }
            else
            {
                SetStage(TranscriptionWorkflowStage.PreparingPayload, uiCallbacks);
                var payload = await PrepareTranscriptionPayloadAsync(session, audioData, cancellationToken, provider, requestId);
                Logger.Info("Workflow", $"Prepared payload: {payload.MimeType} ({payload.Data.Length:N0} bytes)");
                if (payload.CompressionRatio.HasValue)
                {
                    Logger.Info("Workflow", $"Opus compression ratio: {payload.CompressionRatio.Value:F2}x (encoded in {payload.EncodeMilliseconds?.ToString("F0") ?? "n/a"} ms)");
                }
                _performanceMonitor?.RecordCheckpoint(requestId, "Preprocess+Encode");

                SetStage(TranscriptionWorkflowStage.Transcribing, uiCallbacks);
                transcriptionResult = await ExecuteRawTranscriptionAsync(
                    provider, payload, cancellationToken, requestId, localAsrSession, uiCallbacks);
            }

            transcribedText = transcriptionResult.Text;
            modelVersion = transcriptionResult.ModelVersion;
            CleanupResult cleanupResult = CleanupResult.Skipped(transcribedText, modelVersion);

            if (!string.IsNullOrWhiteSpace(transcribedText))
            {
                if (CanAttemptCleanup(provider))
                {
                    SetStage(TranscriptionWorkflowStage.Cleaning, uiCallbacks);
                }

                cleanupResult = await CleanupTranscriptIfNeededAsync(
                    provider,
                    modelVersion,
                    transcribedText,
                    cleanupContext,
                    effectiveFormatting,
                    cancellationToken);
                transcribedText = cleanupResult.Text;
                modelVersion = cleanupResult.ModelVersion;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("[KeyboardHook] Transcription cancelled after API call");
                return new TranscriptionWorkflowResult { WasCancelled = true };
            }

            Debug.WriteLine($"[Transcription] Result Text: {transcribedText}");
            Debug.WriteLine($"[Transcription] Model: {modelVersion}");

            _isTranscribing = false;
            Debug.WriteLine("[Workflow] Transcription phase completed.");

            // Stop performance tracking BEFORE saving to history
            if (trackingStarted)
            {
                _performanceMonitor?.CompleteTracking(requestId, success: !string.IsNullOrEmpty(transcribedText));
                trackingStarted = false; // Mark as done for finally block
                uiCallbacks.RefreshPerformanceDiagnostics();
            }

            // Save raw STT provider trace before any formatting/history persistence.
            await SaveTranscriptionLogAsync(session, transcriptionResult);

            // Process and validate transcription text
            string plainText = string.Empty;
            bool isInvalidTranscription = false;

            if (!string.IsNullOrEmpty(transcribedText))
            {
                plainText = ApplyFormattingPreference(transcribedText, cleanupContext, effectiveFormatting);
                Debug.WriteLine($"[Transcription] Decoded {transcribedText.Length} -> {plainText.Length} characters");

                if (IsInvalidTranscription(plainText))
                {
                    Debug.WriteLine($"[WARNING] Transcription contains invalid/placeholder text: '{plainText}'");
                    isInvalidTranscription = true;
                    plainText = string.Empty;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Save to history (SUCCESS OR FAILURE - as long as not cancelled)
            if (!isTestRun)
            {
                SetStage(TranscriptionWorkflowStage.Persisting, uiCallbacks);
                // Capture final latency if possible
                double latency = 0;
                if (_performanceMonitor != null)
                {
                    var lastMetrics = _performanceMonitor.GetHistory(1).FirstOrDefault();
                    if (lastMetrics != null && lastMetrics.RequestId == requestId)
                    {
                        latency = lastMetrics.TotalTime.TotalSeconds;
                    }
                }

                persistedHistoryEntry = await SaveToHistoryAsync(
                    session,
                    provider,
                    requestedModelVersion,
                    modelVersion,
                    plainText,
                    injectionSucceeded,
                    capturedWindowContext,
                    uiCallbacks,
                    latency,
                    transcriptionResult,
                    cleanupResult,
                    WorkflowStatus.Completed);
                historySaved = persistedHistoryEntry != null;
            }

            if (string.IsNullOrEmpty(plainText) || isInvalidTranscription)
            {
                string reason = isInvalidTranscription
                    ? "The transcription contained only placeholder text (e.g., [inaudible])."
                    : "The audio was too short, too quiet, or contained no intelligible speech.";

                Debug.WriteLine($"[WARNING] Transcription invalid - {reason}");
                uiCallbacks.ShowWorkflowError("AirType - No Speech Detected",
                    $"No speech detected in recording.\n\n{reason}\n\nPlease try:\n• Speaking louder and clearer\n• Recording for longer\n• Checking microphone settings");
                SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
                widgetFailureFlashRequested = true;
                return new TranscriptionWorkflowResult
                {
                    Success = false,
                    TranscribedText = string.Empty,
                    ModelVersion = modelVersion,
                    ErrorMessage = reason,
                    InjectionSucceeded = false,
                    ClipboardFallbackUsed = false
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Handle text injection
            SetStage(TranscriptionWorkflowStage.Injecting, uiCallbacks);
            var injectionResult = await HandleTextInjectionAsync(
                plainText, capturedWindowContext, forceClipboardCopy, uiCallbacks, cancellationToken);

            injectionSucceeded = injectionResult.InjectionSucceeded;
            clipboardFallbackUsed = injectionResult.ClipboardFallbackAttempted && injectionResult.ClipboardFallbackSucceeded;

            if (!isTestRun && persistedHistoryEntry != null)
            {
                await UpdateHistoryInjectionStatusAsync(
                    persistedHistoryEntry,
                    injectionSucceeded,
                    clipboardFallbackUsed,
                    plainText,
                    uiCallbacks);
            }

            bool unexpectedWriteFailure = !forceClipboardCopy &&
                (injectionResult.ClipboardFallbackAttempted || (!injectionSucceeded && !clipboardFallbackUsed));
            bool expectedClipboardFailure = forceClipboardCopy &&
                injectionResult.ClipboardFallbackAttempted &&
                !injectionResult.ClipboardFallbackSucceeded;

            if (unexpectedWriteFailure || expectedClipboardFailure)
            {
                SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
                widgetFailureFlashRequested = true;
            }
            else
            {
                SetStage(TranscriptionWorkflowStage.Completed, uiCallbacks);
            }

            return new TranscriptionWorkflowResult
            {
                Success = !string.IsNullOrEmpty(plainText),
                TranscribedText = plainText,
                ModelVersion = modelVersion,
                InjectionSucceeded = injectionSucceeded,
                ClipboardFallbackUsed = clipboardFallbackUsed
            };
        }
        catch (TaskCanceledException)
        {
            SetStage(TranscriptionWorkflowStage.Cancelled, uiCallbacks);
            Logger.Info("Workflow", "Workflow cancelled by user (TaskCanceledException)");
            Debug.WriteLine("[KeyboardHook] Transcription cancelled by user (TaskCanceledException)");
            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Cancelled, workflowError: "Workflow cancelled by user.");
            }
            return new TranscriptionWorkflowResult { WasCancelled = true };
        }
        catch (OperationCanceledException)
        {
            SetStage(TranscriptionWorkflowStage.Cancelled, uiCallbacks);
            Logger.Info("Workflow", "Workflow cancelled by user (OperationCanceledException)");
            Debug.WriteLine("[KeyboardHook] Transcription cancelled by user (OperationCanceledException)");
            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Cancelled, workflowError: "Workflow cancelled by user.");
            }
            return new TranscriptionWorkflowResult { WasCancelled = true };
        }
        catch (InvalidOperationException ex)
        {
            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Failed, workflowError: ex.Message);
            }
            SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
            widgetFailureFlashRequested = true;
            return HandleInvalidOperationException(ex, uiCallbacks);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Transcription", "API authentication failed - invalid API key", ex);
            
            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Failed, workflowError: ex.Message);
            }
            SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
            widgetFailureFlashRequested = true;

            bool openSettings = uiCallbacks.PromptUserWithSettingsOption(
                "AirType - Invalid API Key",
                ex.Message + "\n\nWould you like to open Settings to configure your API key?");

            return new TranscriptionWorkflowResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RequiresSettingsNavigation = openSettings
            };
        }
        catch (TimeoutException ex)
        {
            Logger.Error("Transcription", "API request timed out", ex);

            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Failed, workflowError: ex.Message);
            }
            SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
            widgetFailureFlashRequested = true;

            bool retry = uiCallbacks.PromptUserWithSettingsOption(
                "AirType - Request Timeout",
                ex.Message + "\n\nWould you like to retry the transcription?");

            return new TranscriptionWorkflowResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RequiresSettingsNavigation = false // Retry, not settings
            };
        }
        catch (HttpRequestException ex)
        {
            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Failed, workflowError: ex.Message);
            }
            SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
            widgetFailureFlashRequested = true;
            return HandleHttpRequestException(ex, uiCallbacks);
        }
        catch (Exception ex)
        {
            Logger.Error("Transcription", "Unexpected error during transcription workflow", ex);

            if (!isTestRun && !historySaved)
            {
                await SaveToHistoryAsync(session, provider, requestedModelVersion, modelVersion, string.Empty, false, capturedWindowContext, uiCallbacks, workflowStatus: WorkflowStatus.Failed, workflowError: ex.Message);
            }
            SetStage(TranscriptionWorkflowStage.Failed, uiCallbacks);
            widgetFailureFlashRequested = true;

            uiCallbacks.ShowWorkflowError("Unexpected Error",
                $"An unexpected error occurred during transcription:\n\n{ex.Message}\n\n" +
                $"Error type: {ex.GetType().Name}\n\n" +
                "The audio file has been saved. You can try recording again.");

            return new TranscriptionWorkflowResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            if (trackingStarted)
            {
                _performanceMonitor?.CompleteTracking(requestId, success: !string.IsNullOrEmpty(transcribedText));
                uiCallbacks.RefreshPerformanceDiagnostics();
            }

            if (_isTranscribing)
                Debug.WriteLine("[Workflow] Transcription phase aborted; resetting state.");

            if (_isInjecting)
                Debug.WriteLine("[Workflow] Injection phase aborted; resetting state.");

            if (!widgetFailureFlashRequested)
            {
                uiCallbacks.UpdateWidgetWorkflowStage(TranscriptionWorkflowStage.Idle);
            }
            _isTranscribing = false;
            _isInjecting = false;
            _isWorkflowActive = false;
            if (_currentStage is TranscriptionWorkflowStage.Completed or TranscriptionWorkflowStage.Failed or TranscriptionWorkflowStage.Cancelled)
            {
                SetStage(TranscriptionWorkflowStage.Idle);
            }

            _cancellationSource?.Dispose();
            _cancellationSource = null;

            Debug.WriteLine("[Workflow] Workflow marked inactive.");
        }
    }

    #region Provider-Specific Transcription

    private string GetConfiguredModelId(TranscriptionProvider provider) =>
        _credentialManager?.GetModelId(provider) ?? ProviderModelCatalog.GetDefaultModelId(provider);

    private sealed record ProviderTranscriptionResult(
        string Text,
        string ModelVersion,
        object? RequestObject,
        object? ResponseObject);

    private sealed record RawTranscriptionResult(
        string Text,
        string ModelVersion,
        TranscriptionProvider RawProvider,
        string RawModelVersion,
        object? RequestObject,
        object? ResponseObject,
        string? ProviderChain = null,
        string? FallbackReason = null,
        double? AsrLatencySeconds = null,
        int? LocalFlushWaitMs = null,
        string? WorkerSessionId = null,
        int? WorkerUtteranceCount = null,
        int? HotwordTokenCount = null,
        int? InitialPromptMaxTokenCount = null);

    private sealed record CleanupResult(
        string Text,
        string ModelVersion,
        string Status,
        string? Provider,
        string? Model,
        double? LatencySeconds)
    {
        public static CleanupResult Skipped(string text, string modelVersion) =>
            new(text, modelVersion, "Skipped", null, null, null);
    }

    private async Task<RawTranscriptionResult> ExecuteRawTranscriptionAsync(
        TranscriptionProvider provider,
        TranscriptionPayload payload,
        CancellationToken cancellationToken,
        string requestId,
        ILocalAsrSession? localAsrSession,
        IWorkflowUICallbacks uiCallbacks)
    {
        if (provider == TranscriptionProvider.Local)
        {
            throw new InvalidOperationException("Local transcription must be stopped before preparing cloud payloads.");
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteProviderTranscriptionAsync(provider, payload, cancellationToken, requestId);
        stopwatch.Stop();
        return new RawTranscriptionResult(
            result.Text,
            result.ModelVersion,
            provider,
            result.ModelVersion,
            result.RequestObject,
            result.ResponseObject,
            ProviderChain: $"{ProviderModelCatalog.GetDisplayName(provider)} {result.ModelVersion}",
            AsrLatencySeconds: stopwatch.Elapsed.TotalSeconds);
    }

    private async Task<RawTranscriptionResult> TranscribeWithLocalStreamingAsync(
        RecordingSession session,
        byte[] rawWavBytes,
        CancellationToken cancellationToken,
        string requestId,
        ILocalAsrSession? localAsrSession,
        IWorkflowUICallbacks uiCallbacks)
    {
        string localModelId = GetConfiguredModelId(TranscriptionProvider.Local);

        if (localAsrSession == null)
        {
            var unavailable = LocalAsrResult.Incomplete(
                Guid.Empty,
                localModelId,
                LocalAsrFallbackReason.WorkerUnavailable);
            return await FallbackFromLocalAsync(
                session,
                rawWavBytes,
                cancellationToken,
                requestId,
                unavailable,
                LocalAsrFallbackReason.WorkerUnavailable,
                localFlushWaitMs: null);
        }

        var flushStopwatch = Stopwatch.StartNew();
        LocalAsrResult localResult = await localAsrSession.StopAndWaitAsync(localAsrSession.FinalFlushTimeout, cancellationToken);
        flushStopwatch.Stop();
        int flushWaitMs = (int)Math.Min(int.MaxValue, flushStopwatch.ElapsedMilliseconds);

        if (localResult.IsComplete && !string.IsNullOrWhiteSpace(localResult.Text))
        {
            return BuildLocalRawResult(localResult, flushWaitMs);
        }

        bool canFallbackToCloud = _cloudFallbackPolicy?.TryGetFallbackProvider(out _) == true;
        if (LocalAsrFallbackDecision.ShouldFallback(localResult.FallbackReason, canFallbackToCloud))
        {
            try
            {
                await localAsrSession.CancelAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.Warn("Workflow", $"Local ASR cancel after fallback failed: {ex.Message}");
            }

            return await FallbackFromLocalAsync(
                session,
                rawWavBytes,
                cancellationToken,
                requestId,
                localResult,
                localResult.FallbackReason,
                flushWaitMs);
        }

        uiCallbacks.ShowBalloonTip(
            "AirType Local Transcription",
            "Local transcription is still finishing. No cloud fallback is configured.",
            isWarning: true);

        localResult = await localAsrSession.WaitUntilCompleteAsync(cancellationToken);
        if (localResult.IsComplete && !string.IsNullOrWhiteSpace(localResult.Text))
        {
            return BuildLocalRawResult(localResult, flushWaitMs);
        }

        throw new InvalidOperationException("Local transcription did not produce text, and no cloud fallback is configured.");
    }

    private RawTranscriptionResult BuildLocalRawResult(LocalAsrResult localResult, int? localFlushWaitMs)
    {
        var diagnostics = localResult.Diagnostics;
        return new RawTranscriptionResult(
            localResult.Text,
            localResult.ModelUsed,
            TranscriptionProvider.Local,
            localResult.ModelUsed,
            localResult,
            localResult,
            ProviderChain: $"Local {localResult.ModelUsed}",
            FallbackReason: null,
            AsrLatencySeconds: diagnostics.AsrTotalMilliseconds.HasValue
                ? diagnostics.AsrTotalMilliseconds.Value / 1000d
                : null,
            LocalFlushWaitMs: localFlushWaitMs,
            WorkerSessionId: diagnostics.WorkerSessionId,
            WorkerUtteranceCount: localResult.Utterances.Count,
            HotwordTokenCount: diagnostics.HotwordTokenCount,
            InitialPromptMaxTokenCount: diagnostics.InitialPromptMaxTokenCount);
    }

    private async Task<RawTranscriptionResult> FallbackFromLocalAsync(
        RecordingSession session,
        byte[] rawWavBytes,
        CancellationToken cancellationToken,
        string requestId,
        LocalAsrResult localResult,
        LocalAsrFallbackReason reason,
        int? localFlushWaitMs)
    {
        if (_cloudFallbackPolicy?.TryGetFallbackProvider(out var fallbackProvider) != true)
        {
            throw new InvalidOperationException("Local transcription worker is unavailable, and no cloud fallback is configured.");
        }

        Logger.Warn("Workflow", $"Local ASR fallback triggered: {reason}. Falling back to {fallbackProvider}.");
        var payload = new TranscriptionPayload("audio/wav", rawWavBytes, audioDuration: session.Duration);
        var stopwatch = Stopwatch.StartNew();
        var fallbackResult = await ExecuteProviderTranscriptionAsync(fallbackProvider, payload, cancellationToken, requestId);
        stopwatch.Stop();

        string localModel = string.IsNullOrWhiteSpace(localResult.ModelUsed)
            ? GetConfiguredModelId(TranscriptionProvider.Local)
            : localResult.ModelUsed;
        string chainModel = $"{localModel} unavailable -> {fallbackResult.ModelVersion}";

        return new RawTranscriptionResult(
            fallbackResult.Text,
            chainModel,
            fallbackProvider,
            fallbackResult.ModelVersion,
            fallbackResult.RequestObject,
            fallbackResult.ResponseObject,
            ProviderChain: $"Local {localModel} ({reason}) -> {ProviderModelCatalog.GetDisplayName(fallbackProvider)} {fallbackResult.ModelVersion}",
            FallbackReason: reason.ToString(),
            AsrLatencySeconds: stopwatch.Elapsed.TotalSeconds,
            LocalFlushWaitMs: localFlushWaitMs,
            WorkerSessionId: localResult.Diagnostics.WorkerSessionId,
            WorkerUtteranceCount: localResult.Utterances.Count,
            HotwordTokenCount: localResult.Diagnostics.HotwordTokenCount,
            InitialPromptMaxTokenCount: localResult.Diagnostics.InitialPromptMaxTokenCount);
    }

    private async Task<ProviderTranscriptionResult> ExecuteProviderTranscriptionAsync(
        TranscriptionProvider provider,
        TranscriptionPayload payload,
        CancellationToken cancellationToken,
        string requestId)
    {
        var startTime = DateTime.Now;
        var activePromptProfile = _promptManager?.GetActiveProfile();
        
        // Pre-initialize modelVersion based on current settings so failure path has it
        string modelVersion = GetConfiguredModelId(provider);

        if (activePromptProfile != null)
        {
            Logger.Info("PromptManager", $"Using prompt profile '{activePromptProfile.Name}' for this transcription.");
        }

        string transcribedText = string.Empty;
        object? requestObject = null;
        object? responseObject = null;

        try
        {
            switch (provider)
            {
                case TranscriptionProvider.Local:
                    throw new InvalidOperationException("Local transcription requires an active streaming ASR session.");

                case TranscriptionProvider.Gemini:
                    (transcribedText, modelVersion, requestObject, responseObject) =
                        await TranscribeWithGeminiAsync(payload, activePromptProfile, cancellationToken, requestId);
                    break;

                case TranscriptionProvider.OpenRouter:
                    (transcribedText, modelVersion, requestObject, responseObject) =
                        await TranscribeWithOpenRouterAsync(payload, activePromptProfile, cancellationToken, requestId);
                    break;

                case TranscriptionProvider.Groq:
                    (transcribedText, modelVersion, requestObject, responseObject) =
                        await TranscribeWithGroqAsync(payload, cancellationToken, requestId);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported transcription provider: {provider}");
            }
        }
        catch (Exception)
        {
            // Re-throw but we've ensured modelVersion was set above
            throw;
        }

        var elapsed = (DateTime.Now - startTime).TotalSeconds;
        Debug.WriteLine($"[Workflow] [+] API call completed in {elapsed:F2}s");

        return new ProviderTranscriptionResult(transcribedText, modelVersion, requestObject, responseObject);
    }

    private async Task<(string Text, string ModelVersion, object Request, object Response)> TranscribeWithGeminiAsync(
        TranscriptionPayload payload,
        PromptProfile? activePromptProfile,
        CancellationToken cancellationToken,
        string requestId)
    {
        Debug.WriteLine("[Transcription] Sending to Gemini API for transcription...");

        string modelId = GetConfiguredModelId(TranscriptionProvider.Gemini);
        string modelVersion = modelId; // Initialize for failure path
        Debug.WriteLine($"[Workflow] Using Gemini model: {modelId}");

        var request = new GeminiTranscriptionRequest { ModelId = modelId };
        if (activePromptProfile != null)
        {
            request.SystemInstruction = activePromptProfile.Content;
        }

        Debug.WriteLine("[Workflow] Gemini request settings:");
        Debug.WriteLine($"[Workflow]   - ModelId: {request.ModelId}");
        Debug.WriteLine($"[Workflow]   - Temperature: {request.Temperature}");
        Debug.WriteLine($"[Workflow]   - MaxOutputTokens: {request.MaxOutputTokens}");

        _performanceMonitor?.RecordCheckpoint(requestId, "BeforeApi");
        var response = await _geminiApiClient!.TranscribeAudioAsync(payload, request, cancellationToken);
        _performanceMonitor?.RecordCheckpoint(requestId, "AfterApi");

        return (response.Text ?? string.Empty, response.ModelVersion ?? modelId, request, response);
    }

    private async Task<(string Text, string ModelVersion, object Request, object Response)> TranscribeWithOpenRouterAsync(
        TranscriptionPayload payload,
        PromptProfile? activePromptProfile,
        CancellationToken cancellationToken,
        string requestId)
    {
        Debug.WriteLine("[Transcription] Sending to OpenRouter API for transcription...");

        string modelId = GetConfiguredModelId(TranscriptionProvider.OpenRouter);
        string modelVersion = modelId; // Initialize for failure path
        Debug.WriteLine($"[Workflow] Using OpenRouter model: {modelId}");

        var modelInfo = ProviderModelCatalog.FindModel(TranscriptionProvider.OpenRouter, modelId);
        if (modelInfo?.EndpointKind == ModelEndpointKind.SpeechToText)
        {
            return await TranscribeWithOpenRouterSttAsync(payload, modelInfo, cancellationToken, requestId);
        }

        var request = new OpenRouterTranscriptionRequest { ModelId = modelId };
        if (activePromptProfile != null)
        {
            request.SystemInstruction = activePromptProfile.Content;
        }

        Debug.WriteLine("[Workflow] OpenRouter request settings:");
        Debug.WriteLine($"[Workflow]   - ModelId: {request.ModelId}");
        Debug.WriteLine($"[Workflow]   - Temperature: {request.Temperature}");
        Debug.WriteLine($"[Workflow]   - MaxOutputTokens: {request.MaxOutputTokens}");

        _performanceMonitor?.RecordCheckpoint(requestId, "BeforeApi");
        var response = await _openRouterClient!.TranscribeAudioAsync(payload, request, cancellationToken);
        _performanceMonitor?.RecordCheckpoint(requestId, "AfterApi");

        return (response.Text ?? string.Empty, response.ModelUsed ?? modelId, request, response);
    }

    private async Task<(string Text, string ModelVersion, object Request, object Response)> TranscribeWithOpenRouterSttAsync(
        TranscriptionPayload payload,
        ModelInfo modelInfo,
        CancellationToken cancellationToken,
        string requestId)
    {
        Debug.WriteLine("[Transcription] Sending to OpenRouter STT API for transcription...");

        string modelId = modelInfo.Id;
        var request = TranscriptionRequestFactory.CreateOpenRouterSttRawRequest(modelId);

        Debug.WriteLine("[Workflow] OpenRouter STT request settings:");
        Debug.WriteLine($"[Workflow]   - ModelId: {request.ModelId}");
        Debug.WriteLine($"[Workflow]   - Temperature: {request.Temperature}");
        Debug.WriteLine($"[Workflow]   - Has dictionary bias: {!string.IsNullOrWhiteSpace(request.DictionaryBiasPrompt)}");

        if (_openRouterSttClient == null)
        {
            throw new InvalidOperationException("OpenRouter STT client is not configured.");
        }

        _performanceMonitor?.RecordCheckpoint(requestId, "BeforeApi");
        var response = await _openRouterSttClient.TranscribeAudioAsync(payload, request, cancellationToken);
        _performanceMonitor?.RecordCheckpoint(requestId, "AfterApi");

        return (response.Text, response.ModelUsed ?? modelId, request, response);
    }

    private async Task<(string Text, string ModelVersion, object Request, object Response)> TranscribeWithGroqAsync(
        TranscriptionPayload payload,
        CancellationToken cancellationToken,
        string requestId)
    {
        Debug.WriteLine("[Transcription] Sending to Groq API for transcription...");

        string modelId = GetConfiguredModelId(TranscriptionProvider.Groq);
        string modelVersion = modelId; // Initialize for failure path
        Debug.WriteLine($"[Workflow] Using Groq model: {modelId}");

        var request = TranscriptionRequestFactory.CreateGroqRawRequest(modelId);

        Debug.WriteLine("[Workflow] Groq request settings:");
        Debug.WriteLine($"[Workflow]   - ModelId: {request.ModelId}");
        Debug.WriteLine($"[Workflow]   - Temperature: {request.Temperature}");

        _performanceMonitor?.RecordCheckpoint(requestId, "BeforeApi");
        var response = await _groqApiClient!.TranscribeAudioAsync(payload, request, cancellationToken);
        _performanceMonitor?.RecordCheckpoint(requestId, "AfterApi");

        return (response.Text ?? string.Empty, response.ModelUsed ?? modelId, request, response);
    }

    private async Task<CleanupResult> CleanupTranscriptIfNeededAsync(
        TranscriptionProvider provider,
        string rawModelVersion,
        string rawTranscript,
        CleanupContextResolution cleanupContext,
        TextFormattingMode effectiveFormatting,
        CancellationToken cancellationToken)
    {
        if (!ShouldCleanupAfterRawTranscription(provider))
        {
            return CleanupResult.Skipped(rawTranscript, rawModelVersion);
        }

        if (_credentialManager == null)
        {
            return CleanupResult.Skipped(rawTranscript, rawModelVersion);
        }

        CleanupProvider cleanupProvider = _credentialManager.GetActiveCleanupProvider();
        string cleanupModelId = _credentialManager.GetCleanupModelId(cleanupProvider);
        string dictionarySection = _dictionaryPromptBuilder?.BuildDictionarySection() ?? string.Empty;
        var cleanupIntensity = _credentialManager.GetCleanupIntensity();
        string? styleOverrideGuidance = ResolveCleanupStyleOverrideGuidance();
        var cleanupRequest = TranscriptCleanupRequestFactory.Create(
            cleanupProvider,
            cleanupModelId,
            rawTranscript,
            cleanupContext,
            cleanupIntensity,
            effectiveFormatting,
            dictionarySection,
            styleOverrideGuidance);

        ITranscriptCleanupClient? cleanupClient = cleanupProvider switch
        {
            CleanupProvider.Gemini => _geminiTranscriptCleanupClient,
            CleanupProvider.OpenRouter => _openRouterTranscriptCleanupClient,
            CleanupProvider.Groq => _groqTranscriptCleanupClient,
            _ => null
        };

        if (cleanupClient == null)
        {
            Logger.Warn("Workflow", $"Cleanup client for {cleanupProvider} is not configured; using raw transcript.");
            return new CleanupResult(rawTranscript, rawModelVersion, "Failed", cleanupProvider.ToString(), cleanupModelId, null);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            Logger.Info("Workflow", $"Running transcript cleanup with {cleanupProvider} model {cleanupModelId}.");
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(CleanupTimeout);
            var cleanupResponse = await cleanupClient.CleanupTranscriptAsync(cleanupRequest, timeoutSource.Token);
            stopwatch.Stop();
            if (string.IsNullOrWhiteSpace(cleanupResponse.Text))
            {
                Logger.Warn("Workflow", "Cleanup returned empty text; using raw transcript.");
                return new CleanupResult(rawTranscript, rawModelVersion, "Failed", cleanupProvider.ToString(), cleanupModelId, stopwatch.Elapsed.TotalSeconds);
            }

            string cleanupModelVersion = cleanupResponse.ModelUsed ?? cleanupModelId;
            return new CleanupResult(
                cleanupResponse.Text,
                $"{rawModelVersion} -> {cleanupModelVersion}",
                "Succeeded",
                cleanupProvider.ToString(),
                cleanupModelVersion,
                stopwatch.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            Logger.Warn("Workflow", "Transcript cleanup timed out; using raw transcript.");
            return new CleanupResult(rawTranscript, rawModelVersion, "TimedOut", cleanupProvider.ToString(), cleanupModelId, stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            Logger.Warn("Workflow", $"Transcript cleanup failed; using raw transcript. {ex.Message}");
            return new CleanupResult(rawTranscript, rawModelVersion, "Failed", cleanupProvider.ToString(), cleanupModelId, stopwatch.Elapsed.TotalSeconds);
        }
    }

    private string? ResolveCleanupStyleOverrideGuidance()
    {
        if (_credentialManager == null)
        {
            return null;
        }

        return _credentialManager.GetCleanupStyleOverrideKind() switch
        {
            CleanupStyleOverrideKind.Classic => BuiltInPrompts.ClassicStyleOverrideGuidance,
            CleanupStyleOverrideKind.CustomPrompt => ResolveCustomStyleOverrideGuidance(
                _credentialManager.GetCleanupStylePromptId()),
            _ => null
        };
    }

    private string? ResolveCustomStyleOverrideGuidance(int? promptId)
    {
        if (!promptId.HasValue || _promptDatabase == null)
        {
            return null;
        }

        var prompt = _promptDatabase.GetPromptById(promptId.Value);
        return prompt is { IsBuiltIn: false } && !string.IsNullOrWhiteSpace(prompt.Content)
            ? prompt.Content.Trim()
            : null;
    }

    private bool ShouldCleanupAfterRawTranscription(TranscriptionProvider provider)
    {
        if (_credentialManager?.IsTranscriptCleanupEnabled() != true)
        {
            return false;
        }

        if (provider == TranscriptionProvider.Local || provider == TranscriptionProvider.Groq)
        {
            return true;
        }

        if (provider == TranscriptionProvider.OpenRouter)
        {
            string modelId = GetConfiguredModelId(provider);
            var modelInfo = ProviderModelCatalog.FindModel(provider, modelId);
            return modelInfo?.EndpointKind == ModelEndpointKind.SpeechToText;
        }

        return false;
    }

    private bool CanAttemptCleanup(TranscriptionProvider provider)
    {
        if (!ShouldCleanupAfterRawTranscription(provider) || _credentialManager == null)
        {
            return false;
        }

        CleanupProvider cleanupProvider = _credentialManager.GetActiveCleanupProvider();
        return cleanupProvider switch
        {
            CleanupProvider.Gemini => _geminiTranscriptCleanupClient != null,
            CleanupProvider.OpenRouter => _openRouterTranscriptCleanupClient != null,
            CleanupProvider.Groq => _groqTranscriptCleanupClient != null,
            _ => false
        };
    }

    private string? BuildDictionaryBiasPrompt(ModelInfo modelInfo)
    {
        if (!modelInfo.SupportsDictionaryBiasPrompt || _dictionaryBiasPromptBuilder == null)
        {
            return null;
        }

        int maxTokens = modelInfo.BiasPromptTokenLimit ?? 224;
        string prompt = _dictionaryBiasPromptBuilder.BuildBiasPrompt(maxTokens);
        return string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }

    #endregion

    #region Text Injection

    private sealed record InjectionResult(
        bool InjectionSucceeded,
        bool ClipboardFallbackAttempted,
        bool ClipboardFallbackSucceeded,
        bool ClipboardFallbackExpected);

    private async Task<InjectionResult> HandleTextInjectionAsync(
        string plainText,
        WindowContext? capturedWindowContext,
        bool forceClipboardCopy,
        IWorkflowUICallbacks uiCallbacks,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(plainText))
        {
            return new InjectionResult(false, false, false, false);
        }

        WindowContext? currentForegroundContext = null;
        bool requiresClipboardFallback = forceClipboardCopy;
        bool injectionAttempted = false;
        bool injectionSucceeded = false;
        bool skipDueToWindowChange = false;

        if (!forceClipboardCopy && _textInjectionService != null)
        {
            WindowContext? targetContext = DetermineTargetWindow(
                capturedWindowContext, out currentForegroundContext, out skipDueToWindowChange);

            if (skipDueToWindowChange)
            {
                Debug.WriteLine("[TextInjection] Skipping text injection - active window changed; clipboard result preserved.");
                requiresClipboardFallback = true;
            }
            else if (targetContext != null && targetContext.IsValid())
            {
                injectionAttempted = true;
                _isInjecting = true;
                Debug.WriteLine("[Workflow] Injection phase started.");

                try
                {
                    bool preserveExistingFocus = currentForegroundContext != null &&
                                                 targetContext.WindowHandle == currentForegroundContext.WindowHandle;

                    Logger.Info("Workflow",
                        $"Injection target selected: {targetContext.ProcessName} | " +
                        $"PreserveFocus={preserveExistingFocus} | " +
                        $"TargetWindow='{targetContext.WindowTitle}'");

                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await _textInjectionService.InjectTextAsync(targetContext, plainText, preserveExistingFocus);
                    if (result.ClipboardFallbackUsed)
                    {
                        Logger.Info("Workflow", $"Text injection fell back to clipboard only: {result.Message}");
                        ShowClipboardFallbackNotice(uiCallbacks, result.Message);
                        return new InjectionResult(false, true, result.Success, result.ClipboardFallbackExpected);
                    }

                    if (result.IsProvisional)
                    {
                        Logger.Warn("Workflow", $"Text injection is provisional via {result.Method}: {result.Message}");
                        return new InjectionResult(true, false, false, false);
                    }

                    if (result.Success)
                    {
                        Logger.Info("Workflow",
                            $"Text injection completed via {result.Method}. Outcome={result.Outcome}; TargetVerified={result.TargetVerified}");
                        if (!IsApplicationWindow(targetContext))
                        {
                            _lastWindowContext = targetContext;
                        }
                        injectionSucceeded = true;
                    }
                    else
                    {
                        Debug.WriteLine($"[TextInjection] Text injection failed: {result.Message}");
                        requiresClipboardFallback = true;
                    }
                }
                finally
                {
                    _isInjecting = false;
                    Debug.WriteLine("[Workflow] Injection phase completed.");
                }
            }
            else
            {
                Debug.WriteLine("[TextInjection] Skipped text injection - no valid window target.");
                requiresClipboardFallback = true;
            }
        }
        else if (forceClipboardCopy)
        {
            Debug.WriteLine("[TextInjection] Clipboard-only mode - skipping injection.");
        }
        else if (_textInjectionService == null)
        {
            Debug.WriteLine("[TextInjection] TextInjectionService not initialized - injection skipped.");
            requiresClipboardFallback = true;
        }

        if (!injectionAttempted && !skipDueToWindowChange && !requiresClipboardFallback)
        {
            requiresClipboardFallback = true;
        }

        // Clipboard fallback
        if (requiresClipboardFallback)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool clipboardFallbackSucceeded = await HandleClipboardFallbackAsync(plainText, skipDueToWindowChange, uiCallbacks);
            return new InjectionResult(false, true, clipboardFallbackSucceeded, forceClipboardCopy);
        }

        if (injectionSucceeded)
        {
            Logger.Info("Workflow", "Text injection succeeded - clipboard copy skipped");
        }

        return new InjectionResult(injectionSucceeded, false, false, false);
    }

    private static void ShowClipboardFallbackNotice(IWorkflowUICallbacks uiCallbacks, string reason)
    {
        uiCallbacks.ShowBalloonTip(
            "AirType",
            "Transcription copied to clipboard — press Ctrl+V to paste.");
    }

    private WindowContext? DetermineTargetWindow(
        WindowContext? capturedWindowContext,
        out WindowContext? currentForegroundContext,
        out bool skipDueToWindowChange)
    {
        currentForegroundContext = null;
        skipDueToWindowChange = false;
        WindowContext? targetContext = null;

        // Default to current foreground window (Smart Targeting)
        if (_windowManager != null)
        {
            currentForegroundContext = _windowManager.CaptureCurrentWindow();
            if (currentForegroundContext != null &&
                currentForegroundContext.IsValid() &&
                !IsApplicationWindow(currentForegroundContext))
            {
                targetContext = currentForegroundContext;
                Debug.WriteLine("[TextInjection] Using current external foreground window for injection.");
            }
        }

        // If the current foreground is application-owned (for example the capsule), prefer the captured external target.
        if ((targetContext == null || !targetContext.IsValid()) &&
            capturedWindowContext != null &&
            capturedWindowContext.IsValid() &&
            !IsApplicationWindow(capturedWindowContext))
        {
            Debug.WriteLine("[TextInjection] Using captured external window context for injection.");
            targetContext = capturedWindowContext;
        }

        // Final fallback to last known window
        if ((targetContext == null || !targetContext.IsValid()) &&
            _lastWindowContext != null &&
            _lastWindowContext.IsValid() &&
            !IsApplicationWindow(_lastWindowContext))
        {
            Debug.WriteLine("[TextInjection] Falling back to last known external window context.");
            targetContext = _lastWindowContext;
        }

        return targetContext;
    }

    private async Task<bool> HandleClipboardFallbackAsync(string plainText, bool skipDueToWindowChange, IWorkflowUICallbacks uiCallbacks)
    {
        if (_clipboardManager == null)
        {
            Logger.Warn("Workflow", "ClipboardManager unavailable - unable to copy fallback text");
            uiCallbacks.ShowWorkflowError("Service Unavailable",
                "Clipboard service is unavailable.\n\n" +
                "The transcription is saved in History.\n" +
                "You can copy it from there.");
            return false;
        }

        try
        {
            Logger.Info("Workflow", "Falling back to clipboard-only mode");
            bool clipboardSuccess = await _clipboardManager.SetTextAsync(plainText);

            if (clipboardSuccess)
            {
                Logger.Info("Workflow", $"Clipboard fallback succeeded ({plainText.Length} characters)");

                string reason = skipDueToWindowChange
                    ? "Active window changed during transcription."
                    : "Text injection failed.";

                ShowClipboardFallbackNotice(uiCallbacks, reason);
                return true;
            }
            else
            {
                Logger.Error("Workflow", "Clipboard fallback failed after retry", null);
                uiCallbacks.ShowWorkflowError("Clipboard Error",
                    "Failed to copy transcription to clipboard.\n\n" +
                    "The transcription is saved in History.\n" +
                    "You can copy it from there.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Workflow", "Clipboard fallback threw exception", ex);
            uiCallbacks.ShowWorkflowError("Clipboard Error",
                $"Failed to copy transcription to clipboard: {ex.Message}\n\n" +
                "The transcription is saved in History.");
            return false;
        }
    }

    #endregion

    #region History and Logging

    private async Task SaveTranscriptionLogAsync(
        RecordingSession session,
        RawTranscriptionResult rawResult)
    {
        if (_transcriptionLogger == null) return;

        try
        {
            Debug.WriteLine("[Workflow] Saving raw transcription trace to log file...");
            string logFilePath = await _transcriptionLogger.SaveTranscriptionAsync(
                session,
                rawResult.RawProvider,
                rawResult.RawModelVersion,
                rawResult.Text,
                rawResult.RequestObject,
                rawResult.ResponseObject);
            Debug.WriteLine($"[Workflow] [+] Transcription log saved: {Path.GetFileName(logFilePath)}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARNING] Failed to save transcription log: {ex.Message}");
        }
    }

    private async Task<TranscriptionHistoryEntry?> SaveToHistoryAsync(
        RecordingSession session,
        TranscriptionProvider provider,
        string requestedModelVersion,
        string modelVersion,
        string plainText,
        bool injectionSucceeded,
        WindowContext? capturedWindowContext,
        IWorkflowUICallbacks uiCallbacks,
        double latencySeconds = 0,
        RawTranscriptionResult? rawResult = null,
        CleanupResult? cleanupResult = null,
        string workflowStatus = WorkflowStatus.Completed,
        string? workflowError = null)
    {
        if (_historyManager == null) return null;

        try
        {
            string requestedProvider = ProviderModelCatalog.GetDisplayName(provider);
            string? actualProvider = rawResult != null
                ? ProviderModelCatalog.GetDisplayName(rawResult.RawProvider)
                : null;
            string? actualModel = rawResult?.RawModelVersion;

            var historyEntry = new TranscriptionHistoryEntry
            {
                Id = session.Id, // UNIFIED ID
                Timestamp = session.StartTime.ToLocalTime(), // Use original capture time
                TranscribedText = plainText,
                WordCount = string.IsNullOrEmpty(plainText) ? 0 : plainText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
                AudioDuration = session.Duration,
                LatencySeconds = latencySeconds,
                Provider = requestedProvider,
                ModelUsed = modelVersion,
                RawTranscribedText = rawResult?.Text,
                ProviderChain = rawResult?.ProviderChain,
                RawProvider = rawResult != null ? ProviderModelCatalog.GetDisplayName(rawResult.RawProvider) : null,
                RawModel = rawResult?.RawModelVersion,
                CleanupProvider = cleanupResult?.Provider,
                CleanupModel = cleanupResult?.Model,
                CleanupStatus = cleanupResult?.Status,
                AsrLatencySeconds = rawResult?.AsrLatencySeconds,
                CleanupLatencySeconds = cleanupResult?.LatencySeconds,
                TotalLatencySeconds = latencySeconds > 0 ? latencySeconds : rawResult?.AsrLatencySeconds,
                LocalFlushWaitMs = rawResult?.LocalFlushWaitMs,
                FallbackReason = rawResult?.FallbackReason,
                WorkerSessionId = rawResult?.WorkerSessionId,
                WorkerUtteranceCount = rawResult?.WorkerUtteranceCount,
                HotwordTokenCount = rawResult?.HotwordTokenCount,
                InitialPromptMaxTokenCount = rawResult?.InitialPromptMaxTokenCount,
                RequestedProvider = requestedProvider,
                RequestedModel = requestedModelVersion,
                ActualProvider = actualProvider,
                ActualModel = actualModel,
                WorkflowStatus = workflowStatus,
                WorkflowError = workflowError,
                InjectionSucceeded = injectionSucceeded,
                InjectionErrorMessage = injectionSucceeded ? null : (string.IsNullOrEmpty(plainText) ? "Transcription failed" : "Injection failed - copied to clipboard"),
                TargetWindowTitle = capturedWindowContext?.WindowTitle,
                AudioFilePath = session.FilePath, // Store the path for Rerun/Download
                SessionType = session.Mode == RecordingMode.Hotkey ? "Hotkey" : "Unattended"
            };

            await _historyManager.AddEntryAsync(historyEntry);
            Debug.WriteLine($"[History] Transcription saved to history ({historyEntry.WordCount} words)");

            uiCallbacks.RefreshHistoryView();
            return historyEntry;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARNING] Failed to save transcription to history: {ex.Message}");
            return null;
        }
    }

    private async Task UpdateHistoryInjectionStatusAsync(
        TranscriptionHistoryEntry historyEntry,
        bool injectionSucceeded,
        bool clipboardFallbackUsed,
        string plainText,
        IWorkflowUICallbacks uiCallbacks)
    {
        if (_historyManager == null)
        {
            return;
        }

        try
        {
            historyEntry.InjectionSucceeded = injectionSucceeded;
            historyEntry.InjectionErrorMessage = BuildInjectionStatusMessage(injectionSucceeded, clipboardFallbackUsed, plainText);
            await _historyManager.AddOrUpdateEntryAsync(historyEntry);
            uiCallbacks.RefreshHistoryView();
        }
        catch (Exception ex)
        {
            Logger.Warn("Workflow", $"Failed to update history injection status for {historyEntry.Id}: {ex.Message}");
        }
    }

    private static string? BuildInjectionStatusMessage(bool injectionSucceeded, bool clipboardFallbackUsed, string plainText)
    {
        if (injectionSucceeded)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(plainText))
        {
            return "Transcription failed";
        }

        return clipboardFallbackUsed
            ? "Copied to clipboard"
            : "Injection failed";
    }

    #endregion

    #region Audio Preprocessing

    // Phase 2 optimization: Skip preprocessing for very short clips where overhead isn't worth it
    private static readonly TimeSpan MinDurationForPreprocessing = TimeSpan.FromSeconds(5);

    private async Task<TranscriptionPayload> PrepareTranscriptionPayloadAsync(
        RecordingSession session,
        byte[] wavBytes,
        CancellationToken cancellationToken,
        TranscriptionProvider provider,
        string? requestId = null)
    {
        byte[] processedWav = wavBytes;
        TimeSpan effectiveDuration = session.Duration;

        // Phase 2 optimization: Skip preprocessing for very short clips (< 2 seconds)
        // The overhead of silence detection isn't worth it for brief recordings
        bool shouldPreprocess = _audioPreprocessor != null && session.Duration >= MinDurationForPreprocessing;

        if (shouldPreprocess)
        {
            try
            {
                if (requestId != null) _performanceMonitor?.RecordCheckpoint(requestId, "PreprocessStart");
                var preprocessed = await _audioPreprocessor!.PreprocessAsync(wavBytes, session, cancellationToken);
                processedWav = preprocessed.ProcessedAudioData.Length > 0 ? preprocessed.ProcessedAudioData : wavBytes;
                effectiveDuration = preprocessed.TrimmedDuration;

                if (requestId != null) _performanceMonitor?.RecordCheckpoint(requestId, "PreprocessEnd");
                Logger.Info("Preprocess", $"Audio trimmed: original {preprocessed.OriginalDuration.TotalSeconds:F2}s/{preprocessed.OriginalSize:N0} bytes -> {preprocessed.TrimmedDuration.TotalSeconds:F2}s/{preprocessed.ProcessedSize:N0} bytes (lead trim {preprocessed.LeadingSilenceTrimmed.TotalMilliseconds:F0}ms, trail trim {preprocessed.TrailingSilenceTrimmed.TotalMilliseconds:F0}ms)");
                _performanceMonitor?.RecordPreprocessing(preprocessed);

                if (!preprocessed.IsValid && !string.IsNullOrEmpty(preprocessed.ValidationMessage))
                {
                    Logger.Warn("Preprocess", preprocessed.ValidationMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Preprocess", $"Preprocessing failed; using original audio. {ex.Message}");
                processedWav = wavBytes;
                effectiveDuration = session.Duration;
            }
        }
        else if (_audioPreprocessor != null)
        {
            Logger.Debug("Preprocess", $"Skipped preprocessing for short clip ({session.Duration.TotalSeconds:F2}s < {MinDurationForPreprocessing.TotalSeconds}s threshold)");
        }

        // Determine if Opus encoding should be used
        bool shouldUseOpus = ShouldUseOpusEncoding(provider);

        if (!shouldUseOpus)
        {
            return new TranscriptionPayload("audio/wav", processedWav, audioDuration: effectiveDuration);
        }

        if (requestId != null) _performanceMonitor?.RecordCheckpoint(requestId, "EncodeStart");
        return await EncodeToOpusAsync(session.FilePath, processedWav, effectiveDuration, cancellationToken);
    }

    private bool ShouldUseOpusEncoding(TranscriptionProvider provider)
    {
        ModelInfo? activeModel = null;
        try
        {
            string modelId = GetConfiguredModelId(provider);
            activeModel = ProviderModelCatalog.FindModel(provider, modelId);
        }
        catch
        {
            activeModel = null;
        }

        bool providerSupportsOpus = provider switch
        {
            TranscriptionProvider.Local => false,
            TranscriptionProvider.OpenRouter => false,
            TranscriptionProvider.Groq => true,
            _ => true
        };

        if (activeModel != null && !activeModel.SupportsOpus)
        {
            providerSupportsOpus = false;
        }

        // Always use Opus if supported (automatic optimization)
        bool shouldUseOpus = providerSupportsOpus && _opusEncodingService != null;

        if (!providerSupportsOpus)
        {
            Logger.Debug("OpusEncoding", $"Provider {provider} does not accept Opus uploads; falling back to WAV.");
        }

        return shouldUseOpus;
    }

    private async Task<TranscriptionPayload> EncodeToOpusAsync(
        string originalFilePath,
        byte[] processedWav,
        TimeSpan effectiveDuration,
        CancellationToken cancellationToken)
    {
        try
        {
            // Phase 2 optimization: Encode directly from memory instead of writing to temp file.
            // This eliminates disk I/O overhead (write temp file + read temp file + delete).
            var result = await _opusEncodingService!.EncodeFromBytesAsync(processedWav, cancellationToken);
            Logger.Info("OpusEncoding", $"Encoded WAV {processedWav.Length:N0} bytes -> Opus {result.OggBytes.Length:N0} bytes ({result.CompressionRatio:F2}x) in {result.DurationMilliseconds:F0}ms");

            return new TranscriptionPayload("audio/ogg", result.OggBytes, result.CompressionRatio, result.DurationMilliseconds, effectiveDuration, processedWav);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("OpusEncoding", "Failed to encode Opus payload. Falling back to WAV.", ex);
            return new TranscriptionPayload("audio/wav", processedWav, audioDuration: effectiveDuration);
        }
    }

    #endregion

    #region Text Processing Helpers

    private static string ApplyFormattingPreference(
        string transcribedText,
        CleanupContextResolution cleanupContext,
        TextFormattingMode effectiveFormatting)
    {
        return CleanupTextPostProcessor.Apply(transcribedText, cleanupContext, effectiveFormatting);
    }

    private static bool IsInvalidTranscription(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        string normalized = text.Trim().ToLowerInvariant();

        string[] invalidPatterns = new[]
        {
            "[inaudible]",
            "[unintelligible]",
            "[silence]",
            "[no audio]",
            "[background noise]",
            "inaudible",
            "unintelligible",
            "...",
            "…",
            "---",
            "___"
        };

        if (invalidPatterns.Contains(normalized))
            return true;

        if (normalized.All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c)))
            return true;

        if (normalized.Length < 2)
            return true;

        return false;
    }

    private static bool IsApplicationWindow(WindowContext? context)
    {
        if (context == null)
            return false;

        return !string.IsNullOrWhiteSpace(context.ProcessName) &&
               string.Equals(context.ProcessName, CurrentProcessName, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Error Handling

    private TranscriptionWorkflowResult HandleInvalidOperationException(
        InvalidOperationException ex,
        IWorkflowUICallbacks uiCallbacks)
    {
        Logger.Error("Transcription", "Transcription operation failed", ex);

        bool isMalformedResponse = ex.Message.Contains("Invalid API response", StringComparison.OrdinalIgnoreCase) ||
                                  ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase);
        bool isConfigError = ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase);

        if (isConfigError)
        {
            bool openSettings = uiCallbacks.PromptUserWithSettingsOption(
                "AirType - Configuration Error",
                ex.Message + "\n\nWould you like to open Settings to fix this?");

            return new TranscriptionWorkflowResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RequiresSettingsNavigation = openSettings
            };
        }
        else if (isMalformedResponse)
        {
            // Could trigger retry here, but for now just report error
            uiCallbacks.ShowWorkflowError("AirType - Invalid Response",
                ex.Message + "\n\nThis may be a temporary API issue. Please try again.");
        }
        else
        {
            uiCallbacks.ShowWorkflowError("Transcription Error", $"Transcription failed:\n\n{ex.Message}");
        }

        return new TranscriptionWorkflowResult
        {
            Success = false,
            ErrorMessage = ex.Message
        };
    }

    private TranscriptionWorkflowResult HandleHttpRequestException(HttpRequestException ex, IWorkflowUICallbacks uiCallbacks)
    {
        Logger.Error("Transcription", "Network error during transcription", ex);

        bool isRateLimitError = ex.Message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) ||
                               ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase);
        bool isQuotaError = ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                           ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase);

        if (isRateLimitError)
        {
            uiCallbacks.ShowWorkflowError("Rate Limit Exceeded", ex.Message);
        }
        else if (isQuotaError)
        {
            uiCallbacks.ShowWorkflowError("API Quota Exceeded", ex.Message);
        }
        else
        {
            uiCallbacks.ShowWorkflowError("Network Error",
                ex.Message + "\n\nPlease check your internet connection and try again.");
        }

        return new TranscriptionWorkflowResult
        {
            Success = false,
            ErrorMessage = ex.Message
        };
    }

    #endregion
}
