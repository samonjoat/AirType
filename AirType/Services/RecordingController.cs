using System;
using System.Threading.Tasks;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Controller for managing recording operations and mode transitions.
/// Encapsulates the logic for starting, stopping, and transitioning between recording modes.
/// </summary>
public class RecordingController
{
    private readonly WidgetStateManager _stateManager;
    private readonly IWidgetNotificationService _notifications;
    private IAudioInputManager? _audioInputManager;

    public RecordingController(WidgetStateManager stateManager, IWidgetNotificationService notifications)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>
    /// Gets or sets the audio input manager
    /// </summary>
    public IAudioInputManager? AudioInputManager
    {
        get => _audioInputManager;
        set => _audioInputManager = value;
    }

    /// <summary>
    /// Gets whether currently recording
    /// </summary>
    public bool IsRecording => _audioInputManager?.IsRecording ?? false;

    /// <summary>
    /// Gets the current recording mode, if any
    /// </summary>
    public RecordingMode? CurrentMode => _audioInputManager?.CurrentSession?.Mode;

    /// <summary>
    /// Gets the last error message from the audio manager
    /// </summary>
    public string? LastErrorMessage => _audioInputManager?.LastErrorMessage;

    /// <summary>
    /// Start recording in unattended mode (triggered by widget click)
    /// </summary>
    /// <returns>True if recording started successfully</returns>
    public async Task<bool> StartUnattendedRecordingAsync()
    {
        if (_audioInputManager == null)
            return false;

        // Edge Case #5: Check if already recording in a different mode
        if (IsRecording)
        {
            if (CurrentMode == RecordingMode.Hotkey)
            {
                System.Diagnostics.Debug.WriteLine("[RecordingController] Already recording with hotkey - ignoring click");
                _notifications.ShowAlreadyRecordingNotification("hotkey", "Release the hotkey to stop recording");
                return false;
            }
            else if (_stateManager.IsRecording)
            {
                // Already recording in unattended mode - this will be treated as stop
                return false;
            }
        }

        bool success = await TransitionToModeAsync(RecordingMode.Unattended);
        if (success && !_stateManager.IsUnattendedRecording)
        {
            _stateManager.StartUnattendedRecording();
        }
        else if (!success)
        {
            ShowRecordingError();
        }

        return success;
    }

    /// <summary>
    /// Start recording in hotkey mode
    /// </summary>
    /// <returns>True if recording started successfully</returns>
    public async Task<bool> StartHotkeyRecordingAsync()
    {
        if (_audioInputManager == null)
            return false;

        // Edge Case #5: Check if already recording in a different mode
        if (IsRecording)
        {
            if (CurrentMode == RecordingMode.Unattended)
            {
                System.Diagnostics.Debug.WriteLine("[RecordingController] Already recording in unattended mode - ignoring hotkey");
                _notifications.ShowAlreadyRecordingNotification("widget", "Click the widget to stop recording");
                return false;
            }
            // If already in hotkey mode, this is just holding the key - allow it
        }

        bool success = await TransitionToModeAsync(RecordingMode.Hotkey);
        if (success && !_stateManager.IsHotkeyRecording)
        {
            _stateManager.StartHotkeyRecording();
        }
        else if (!success)
        {
            ShowRecordingError();
        }

        return success;
    }

    /// <summary>
    /// Stop the current recording
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (_audioInputManager != null && IsRecording)
        {
            await _audioInputManager.StopRecordingAsync();
        }
        else
        {
            // Fallback to direct state reset
            if (_stateManager.IsHotkeyRecording)
                _stateManager.StopHotkeyRecording();
            else if (_stateManager.IsUnattendedRecording)
                _stateManager.StopUnattendedRecording();
        }
    }

    /// <summary>
    /// Stop hotkey recording specifically
    /// </summary>
    public async Task StopHotkeyRecordingAsync()
    {
        if (_audioInputManager != null && _stateManager.IsHotkeyRecording)
        {
            await _audioInputManager.StopRecordingAsync();
        }
        else
        {
            _stateManager.StopHotkeyRecording();
        }
    }

    /// <summary>
    /// Cancel the current recording
    /// </summary>
    public async Task CancelRecordingAsync()
    {
        if (_audioInputManager != null && IsRecording)
        {
            await _audioInputManager.CancelRecordingAsync();
        }
        
        _stateManager.CancelRecording();
    }

    /// <summary>
    /// Handle recording state changes from the audio input manager
    /// </summary>
    public void HandleRecordingStateChanged(RecordingStateEventArgs e)
    {
        if (e.IsRecording)
        {
            SynchronizeStartedState(e.Mode);
        }
        else
        {
            SynchronizeStoppedState();
        }
    }

    private void SynchronizeStartedState(RecordingMode mode)
    {
        if (mode == RecordingMode.Hotkey && !_stateManager.IsHotkeyRecording)
        {
            if (_stateManager.IsUnattendedRecording)
                _stateManager.StopUnattendedRecording();
            _stateManager.StartHotkeyRecording();
        }
        else if (mode == RecordingMode.Unattended && !_stateManager.IsUnattendedRecording)
        {
            if (_stateManager.IsHotkeyRecording)
                _stateManager.StopHotkeyRecording();
            _stateManager.StartUnattendedRecording();
        }
    }

    private void SynchronizeStoppedState()
    {
        var session = _audioInputManager?.CurrentSession;
        var sessionStatus = session?.Status;
        bool completedSession = sessionStatus == RecordingStatus.Completed;

        if (completedSession)
        {
            _stateManager.TransitionToTranscribing();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[RecordingController] Recording stopped without completion. Status: {sessionStatus?.ToString() ?? "null"}");
            
            if (_stateManager.IsHotkeyRecording)
                _stateManager.StopHotkeyRecording();
            else if (_stateManager.IsUnattendedRecording)
                _stateManager.StopUnattendedRecording();
        }
    }

    /// <summary>
    /// Transition to a specific recording mode, handling any existing recording
    /// </summary>
    private async Task<bool> TransitionToModeAsync(RecordingMode targetMode)
    {
        if (_audioInputManager == null)
            return false;

        try
        {
            // If already recording in a different mode, stop current recording first
            if (IsRecording)
            {
                var currentSession = _audioInputManager.CurrentSession;
                if (currentSession != null && currentSession.Mode != targetMode)
                {
                    await _audioInputManager.StopRecordingAsync();
                    await Task.Delay(50); // Brief delay for stop to complete
                }
            }

            // Start recording in the target mode
            if (!IsRecording)
            {
                return await _audioInputManager.StartRecordingAsync(targetMode);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error transitioning to recording mode {targetMode}: {ex.Message}");
            return false;
        }
    }

    private void ShowRecordingError()
    {
        bool includeGuidance = WidgetNotificationService.ShouldIncludeStorageGuidance(LastErrorMessage);
        _notifications.ShowRecordingError(LastErrorMessage, includeGuidance);
    }
}
