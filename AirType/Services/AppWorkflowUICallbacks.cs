using System;
using System.Windows;
using AirType.Services.Transcription;
using AirType.ViewModels;

namespace AirType.Services;

/// <summary>
/// Implementation of IWorkflowUICallbacks that bridges the workflow service to App UI.
/// </summary>
public sealed class AppWorkflowUICallbacks : IWorkflowUICallbacks
{
    private readonly Action<TranscriptionWorkflowStage> _updateWidgetWorkflowStage;
    private readonly Action<string, string> _showWorkflowError;
    private readonly Func<string, string, bool, bool>? _showBalloonTip;
    private readonly Func<string, string, bool>? _promptUserWithSettingsOption;
    private readonly Action? _refreshHistoryView;
    private readonly Action? _refreshPerformanceDiagnostics;

    public AppWorkflowUICallbacks(
        Action<TranscriptionWorkflowStage> updateWidgetWorkflowStage,
        Action<string, string> showWorkflowError,
        Func<string, string, bool, bool>? showBalloonTip = null,
        Func<string, string, bool>? promptUserWithSettingsOption = null,
        Action? refreshHistoryView = null,
        Action? refreshPerformanceDiagnostics = null)
    {
        _updateWidgetWorkflowStage = updateWidgetWorkflowStage ?? throw new ArgumentNullException(nameof(updateWidgetWorkflowStage));
        _showWorkflowError = showWorkflowError ?? throw new ArgumentNullException(nameof(showWorkflowError));
        _showBalloonTip = showBalloonTip;
        _promptUserWithSettingsOption = promptUserWithSettingsOption;
        _refreshHistoryView = refreshHistoryView;
        _refreshPerformanceDiagnostics = refreshPerformanceDiagnostics;
    }

    public void UpdateWidgetWorkflowStage(TranscriptionWorkflowStage stage)
    {
        _updateWidgetWorkflowStage(stage);
    }

    public void ShowWorkflowError(string title, string message)
    {
        _showWorkflowError(title, message);
    }

    public void ShowBalloonTip(string title, string message, bool isWarning = false)
    {
        _showBalloonTip?.Invoke(title, message, isWarning);
    }

    public bool PromptUserWithSettingsOption(string title, string message)
    {
        return _promptUserWithSettingsOption?.Invoke(title, message) ?? false;
    }

    public void RefreshHistoryView()
    {
        _refreshHistoryView?.Invoke();
    }

    public void RefreshPerformanceDiagnostics()
    {
        _refreshPerformanceDiagnostics?.Invoke();
    }
}
