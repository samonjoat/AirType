using System;
using System.Windows;
using AirType.Views;

namespace AirType.Services;

/// <summary>
/// Interface for widget notification services
/// </summary>
public interface IWidgetNotificationService
{
    void ShowInfo(string title, string message);
    void ShowWarning(string title, string message);
    void ShowRecordingError(string? message, bool includeStorageGuidance = false);
    void ShowAlreadyRecordingNotification(string currentMode, string stopInstruction);
}

/// <summary>
/// Service for displaying notifications from the widget.
/// Consolidates MessageBox display logic and storage guidance text.
/// </summary>
public class WidgetNotificationService : IWidgetNotificationService
{
    private const string Tag = "WidgetNotification";
    private const string DefaultRecordingErrorMessage = 
        "Unable to start recording. Check that your microphone is available and that AirType can write to %LOCALAPPDATA%\\AirType\\Audio_files.";

    private const string StorageGuidanceText = 
        "\n\nIf you use Windows Controlled Folder Access, allow AirType.exe (Virus & threat protection ▸ Ransomware protection) or pick another writable folder.";

    /// <summary>
    /// Show an informational notification
    /// </summary>
    public void ShowInfo(string title, string message)
    {
        ShowOnDispatcher(title, message, MessageBoxImage.Information);
    }

    /// <summary>
    /// Show a warning notification
    /// </summary>
    public void ShowWarning(string title, string message)
    {
        ShowOnDispatcher(title, message, MessageBoxImage.Warning);
    }

    /// <summary>
    /// Show a recording error with optional storage guidance
    /// </summary>
    public void ShowRecordingError(string? message, bool includeStorageGuidance = false)
    {
        string text = BuildRecordingErrorMessage(message, includeStorageGuidance);
        ShowWarning("AirType", text);
    }

    /// <summary>
    /// Show notification for silent audio detection
    /// </summary>
    public void ShowSilentAudioNotification()
    {
        ShowInfo("AirType - No Audio Detected",
            "No audio detected in recording.\n\nPlease check:\n• Microphone is not muted\n• Correct microphone is selected\n• Speak closer to the microphone");
    }

    /// <summary>
    /// Show notification for short recording rejection
    /// </summary>
    public void ShowShortRecordingNotification(TimeSpan minimumRequired)
    {
        var requiredSeconds = Math.Max(0.1, minimumRequired.TotalMilliseconds / 1000.0);
        var requiredText = requiredSeconds >= 1
            ? $"{requiredSeconds:0.0}s"
            : $"{minimumRequired.TotalMilliseconds:F0}ms";

        ShowInfo("Recording too short",
            $"Hold the hotkey for at least {requiredText} before releasing to capture audio.");
    }

    /// <summary>
    /// Show notification for microphone disconnection
    /// </summary>
    public void ShowMicrophoneDisconnectedNotification()
    {
        ShowInfo("Microphone disconnected",
            "Please check your microphone connection and try again.");
    }

    /// <summary>
    /// Show notification for max duration warning
    /// </summary>
    public void ShowMaxDurationWarningNotification(TimeSpan maxDuration, TimeSpan remainingTime)
    {
        var remainingSeconds = (int)Math.Ceiling(remainingTime.TotalSeconds);
        ShowInfo("Recording will stop soon",
            $"Maximum recording duration is {maxDuration.TotalMinutes:F0} minutes. Recording will stop in {remainingSeconds} seconds.");
    }

    /// <summary>
    /// Show notification for max duration reached
    /// </summary>
    public void ShowMaxDurationReachedNotification(TimeSpan maxDuration)
    {
        ShowInfo("Recording stopped",
            $"Maximum recording duration of {maxDuration.TotalMinutes:F0} minutes reached. Recording has been saved and will be transcribed.");
    }

    /// <summary>
    /// Show notification for quiet audio
    /// </summary>
    public void ShowQuietAudioNotification()
    {
        ShowInfo("Audio is very quiet",
            "The recording volume is low and may not transcribe well. Try speaking louder or adjusting your microphone settings.");
    }

    /// <summary>
    /// Show notification for audio file corruption
    /// </summary>
    public void ShowAudioFileCorruptedNotification(string errorMessage)
    {
        ShowInfo("Recording corrupted",
            $"The recording file is corrupted and cannot be used. Please try recording again.\n\nDetails: {errorMessage}");
    }

    /// <summary>
    /// Show notification for already recording in different mode
    /// </summary>
    public void ShowAlreadyRecordingNotification(string currentMode, string stopInstruction)
    {
        ShowInfo($"Already recording with {currentMode}", stopInstruction);
    }

    /// <summary>
    /// Check if a message should include storage guidance
    /// </summary>
    public static bool ShouldIncludeStorageGuidance(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Documents", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("folder", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("AirType", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ModernDictationApp", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRecordingErrorMessage(string? message, bool includeStorageGuidance)
    {
        string text = string.IsNullOrWhiteSpace(message)
            ? DefaultRecordingErrorMessage
            : message.Trim();

        if (includeStorageGuidance && !ShouldIncludeStorageGuidance(text))
        {
            text += StorageGuidanceText;
        }

        return text;
    }

    private static void ShowOnDispatcher(string title, string message, MessageBoxImage icon)
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var toastType = icon switch
                    {
                        MessageBoxImage.Error => ToastType.Error,
                        MessageBoxImage.Warning => ToastType.Warning,
                        _ => ToastType.Info
                    };

                    Logger.Info(Tag, $"Showing {toastType} toast: {title}");
                    ToastService.Instance.Show(message, toastType, title);
                }
                catch (Exception ex)
                {
                    Logger.Warn(Tag, $"Toast dispatch failed for '{title}': {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warn(Tag, $"Dispatcher unavailable for '{title}': {ex.Message}");
        }
    }
}
