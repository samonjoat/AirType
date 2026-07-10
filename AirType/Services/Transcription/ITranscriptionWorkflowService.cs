using System;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Configuration;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Result of a transcription workflow execution.
/// </summary>
public sealed class TranscriptionWorkflowResult
{
    public bool Success { get; init; }
    public string? TranscribedText { get; init; }
    public string? ModelVersion { get; init; }
    public bool InjectionSucceeded { get; init; }
    public bool ClipboardFallbackUsed { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasCancelled { get; init; }
    public bool RequiresSettingsNavigation { get; init; }
}

public enum TranscriptionWorkflowStage
{
    Idle,
    LoadingAudio,
    PreparingPayload,
    Transcribing,
    Cleaning,
    Persisting,
    Injecting,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Callbacks for UI operations that must be performed on the UI thread.
/// </summary>
public interface IWorkflowUICallbacks
{
    /// <summary>
    /// Update the widget's workflow lifecycle state.
    /// </summary>
    void UpdateWidgetWorkflowStage(TranscriptionWorkflowStage stage);

    /// <summary>
    /// Show a workflow error message to the user.
    /// </summary>
    void ShowWorkflowError(string title, string message);

    /// <summary>
    /// Show a balloon tip notification.
    /// </summary>
    void ShowBalloonTip(string title, string message, bool isWarning = false);

    /// <summary>
    /// Prompt user with Yes/No dialog and optionally open settings.
    /// Returns true if user chose Yes.
    /// </summary>
    bool PromptUserWithSettingsOption(string title, string message);

    /// <summary>
    /// Refresh the history view if main window is open.
    /// </summary>
    void RefreshHistoryView();

    /// <summary>
    /// Refresh performance diagnostics if main window is open.
    /// </summary>
    void RefreshPerformanceDiagnostics();
}

/// <summary>
/// Service that orchestrates the complete transcription workflow:
/// audio preprocessing, API transcription, text injection, and clipboard fallback.
/// </summary>
public interface ITranscriptionWorkflowService
{
    /// <summary>
    /// Gets or sets the cancellation token source for the current operation.
    /// </summary>
    CancellationTokenSource? CancellationSource { get; }

    /// <summary>
    /// Gets whether a transcription is currently in progress.
    /// </summary>
    bool IsTranscribing { get; }

    /// <summary>
    /// Gets whether text injection is currently in progress.
    /// </summary>
    bool IsInjecting { get; }

    /// <summary>
    /// Gets whether the workflow is active (recording, transcribing, or injecting).
    /// </summary>
    bool IsWorkflowActive { get; }

    /// <summary>
    /// Gets the current lifecycle stage for the in-memory workflow.
    /// </summary>
    TranscriptionWorkflowStage CurrentStage { get; }

    /// <summary>
    /// Execute the complete transcription workflow for a recording session.
    /// </summary>
    /// <param name="session">The recording session to transcribe</param>
    /// <param name="provider">The transcription provider to use</param>
    /// <param name="capturedWindowContext">The window context captured when recording started</param>
    /// <param name="lastWindowContext">The last known window context</param>
    /// <param name="forceClipboardCopy">Whether to force clipboard copy instead of injection</param>
    /// <param name="isTestRun">Whether this is a test run</param>
    /// <param name="uiCallbacks">UI callbacks for notifications</param>
    /// <param name="preloadedAudioData">Optional pre-loaded audio data to avoid file read. If null, reads from session.FilePath.</param>
    Task<TranscriptionWorkflowResult> RunWorkflowAsync(
        RecordingSession session,
        TranscriptionProvider provider,
        WindowContext? capturedWindowContext,
        WindowContext? lastWindowContext,
        bool forceClipboardCopy,
        bool isTestRun,
        IWorkflowUICallbacks uiCallbacks,
        byte[]? preloadedAudioData = null,
        ILocalAsrSession? localAsrSession = null);

    /// <summary>
    /// Cancel the current transcription operation.
    /// </summary>
    void CancelCurrentOperation();

    /// <summary>
    /// Reset workflow state flags.
    /// </summary>
    void ResetWorkflowState();

    /// <summary>
    /// Update the last known external window context after successful injection.
    /// </summary>
    WindowContext? LastWindowContext { get; }
}
