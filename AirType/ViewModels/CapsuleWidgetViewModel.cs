using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Threading;
using AirType.Models;
using AirType.Services;

namespace AirType.ViewModels;

/// <summary>
/// ViewModel for the CapsuleWidget that manages state and UI bindings.
/// Refactored to delegate responsibilities to specialized helper classes.
/// </summary>
public class CapsuleWidgetViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WidgetStateManager _stateManager;
    private readonly AudioEventWiring _eventWiring;
    private readonly WidgetNotificationService _notifications;
    private readonly RecordingController _recordingController;
    private WidgetAnimationManager? _animationManager;
    private WaveformRenderer? _waveformRenderer;
    private DispatcherTimer? _failureFlashTimer;
    private bool _disposed;

    public CapsuleWidgetViewModel()
    {
        _stateManager = new WidgetStateManager();
        _notifications = new WidgetNotificationService();
        _eventWiring = new AudioEventWiring();
        _recordingController = new RecordingController(_stateManager, _notifications);
        _waveformRenderer = new WaveformRenderer();

        // Subscribe to state manager events
        _stateManager.StateChanged += OnStateChanged;
        _stateManager.RecordingModeChanged += OnRecordingModeChanged;
        _stateManager.DimensionsChanged += OnDimensionsChanged;
        _stateManager.PropertyChanged += OnStateManagerPropertyChanged;

        // Subscribe to audio/hotkey events via wiring helper
        _eventWiring.WaveformDataAvailable += OnWaveformDataAvailable;
        _eventWiring.RecordingStateChanged += OnAudioRecordingStateChanged;
        _eventWiring.SilentAudioDetected += OnSilentAudioDetected;
        _eventWiring.ShortRecordingRejected += OnShortRecordingRejected;
        _eventWiring.MicrophoneDisconnected += OnMicrophoneDisconnected;
        _eventWiring.MaxDurationWarning += OnMaxDurationWarning;
        _eventWiring.MaxDurationReached += OnMaxDurationReached;
        _eventWiring.QuietAudioDetected += OnQuietAudioDetected;
        _eventWiring.AudioFileCorrupted += OnAudioFileCorrupted;
        _eventWiring.HotkeyPressed += OnHotkeyPressed;
        _eventWiring.HotkeyReleased += OnHotkeyReleased;
        _eventWiring.HotkeyRegistrationFailed += OnHotkeyRegistrationFailed;
    }

    #region Properties

    public WidgetStateManager StateManager => _stateManager;
    public double Width => 100;
    public double Height => 30;
    public CornerRadius CornerRadius => new(15);
    public Visibility ControlsVisibility => _stateManager.ShowControls ? Visibility.Visible : Visibility.Collapsed;
    public bool IsIdleMinimal => _stateManager.CurrentState == WidgetStateManager.WidgetState.IdleMinimal;
    public bool IsIdleHover => _stateManager.CurrentState == WidgetStateManager.WidgetState.IdleHover;
    public bool IsRecording => _stateManager.CurrentState == WidgetStateManager.WidgetState.Recording;
    public bool IsTranscribing => _stateManager.CurrentState == WidgetStateManager.WidgetState.Transcribing;
    public bool IsCleaning => _stateManager.CurrentState == WidgetStateManager.WidgetState.Cleaning;
    public bool IsFailureFlash => _stateManager.CurrentState == WidgetStateManager.WidgetState.FailureFlash;
    public bool IsProcessing => IsTranscribing || IsCleaning || IsFailureFlash;
    public bool IsWaveformVisible => !IsProcessing;
    public bool IsHotkeyRecording => _stateManager.IsHotkeyRecording;
    public bool IsUnattendedRecording => _stateManager.IsUnattendedRecording;
    public Thickness WaveformMargin => _stateManager.ShowControls 
        ? new Thickness(25, 0, 25, 0) 
        : new Thickness(10, 0, 10, 0);
    public double Opacity => _stateManager.CurrentOpacity;

    #endregion

    #region Events

    public event EventHandler<WidgetDimensionsChangedEventArgs>? DimensionsChanged;
    public event EventHandler<WidgetStateChangedEventArgs>? StateChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Mouse/Click Handlers

    public void OnMouseEnter() => _stateManager.EnterHoverState();
    public void OnMouseLeave() => _stateManager.ExitHoverState();
    public void OnWidgetUnclick() { } // Mouse release no longer stops unattended recording

    public async void OnWidgetClick()
    {
        if (_stateManager.CurrentState == WidgetStateManager.WidgetState.IdleHover && !_stateManager.IsUnattendedRecording)
        {
            await _recordingController.StartUnattendedRecordingAsync();
        }
        else if (_stateManager.IsRecording)
        {
            OnStopClick();
        }
    }

    public async void OnCancelClick() => await _recordingController.CancelRecordingAsync();
    public async void OnStopClick() => await _recordingController.StopRecordingAsync();
    public async void StartHotkeyRecording() => await _recordingController.StartHotkeyRecordingAsync();
    public async void StopHotkeyRecording() => await _recordingController.StopHotkeyRecordingAsync();
    public Task CancelRecordingAsync() => _recordingController.CancelRecordingAsync();

    /// <summary>
    /// Toggle unattended recording from external trigger (e.g., Notes page mic button).
    /// Bypasses hover state requirement.
    /// </summary>
    public async void ToggleUnattendedRecordingExternal()
    {
        if (_stateManager.IsRecording)
        {
            await _recordingController.StopRecordingAsync();
        }
        else
        {
            await _recordingController.StartUnattendedRecordingAsync();
        }
    }

    #endregion

    #region Configuration and Dependency Injection

    public void UpdateConfiguration(WidgetConfiguration configuration) 
        => _stateManager.Configuration = configuration;

    public void SetAnimationManager(WidgetAnimationManager animationManager) 
        => _animationManager = animationManager;

    public void SetWaveformPath(Path waveformPath)
    {
        _waveformRenderer?.SetWaveformPath(waveformPath);
        UpdateWaveformDisplayMode();
    }

    public void SetAudioInputManager(IAudioInputManager audioInputManager)
    {
        _eventWiring.WireAudioInputManager(audioInputManager);
        _recordingController.AudioInputManager = audioInputManager;
    }

    public void SetHotkeyManager(IHotkeyManager hotkeyManager) 
        => _eventWiring.WireHotkeyManager(hotkeyManager);

    public void UpdateWaveformCanvasSize(double width, double height) 
        => _waveformRenderer?.UpdateCanvasSize(width, height);

    #endregion

    #region Visual Feedback

    public enum VisualFeedbackType { Click, HoverEnter, HoverExit }

    public void TriggerVisualFeedback(VisualFeedbackType feedbackType)
    {
        if (_animationManager == null) return;

        switch (feedbackType)
        {
            case VisualFeedbackType.Click: _animationManager.AnimateScaleFeedback(); break;
            case VisualFeedbackType.HoverEnter: _animationManager.AnimateHoverEnter(); break;
            case VisualFeedbackType.HoverExit: _animationManager.AnimateHoverExit(); break;
        }
    }

    #endregion

    #region State Manager Event Handlers

    private void OnStateChanged(object? sender, WidgetStateChangedEventArgs e)
    {
        TriggerStateAnimation(e.NewState);
        UpdateWaveformDisplayMode();
        UpdateFailureFlashTimer(e.NewState);
        NotifyStateProperties();
        StateChanged?.Invoke(this, e);
    }

    private void OnRecordingModeChanged(object? sender, RecordingModeChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsHotkeyRecording));
        OnPropertyChanged(nameof(IsUnattendedRecording));
        OnPropertyChanged(nameof(ControlsVisibility));
        OnPropertyChanged(nameof(WaveformMargin));
    }

    private void OnDimensionsChanged(object? sender, WidgetDimensionsChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(CornerRadius));
        DimensionsChanged?.Invoke(this, e);
    }

    private void OnStateManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WidgetStateManager.Configuration):
                OnPropertyChanged(nameof(Opacity));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(CornerRadius));
                break;
            case nameof(WidgetStateManager.CurrentOpacity):
                OnPropertyChanged(nameof(Opacity));
                break;
        }
    }

    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(IsIdleMinimal));
        OnPropertyChanged(nameof(IsIdleHover));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsTranscribing));
        OnPropertyChanged(nameof(IsCleaning));
        OnPropertyChanged(nameof(IsFailureFlash));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsWaveformVisible));
        OnPropertyChanged(nameof(ControlsVisibility));
        OnPropertyChanged(nameof(WaveformMargin));
        OnPropertyChanged(nameof(Opacity));
    }

    #endregion

    #region Audio Event Handlers

    private void OnWaveformDataAvailable(object? sender, WaveformDataEventArgs e) 
        => _waveformRenderer?.UpdateWaveform(e.Amplitude);

    private void OnAudioRecordingStateChanged(object? sender, RecordingStateEventArgs e)
    {
        // Ignore recording events from Notes page - they have their own UI
        if (e.Source == RecordingSource.Notes)
            return;

        _recordingController.HandleRecordingStateChanged(e);
        UpdateWaveformDisplayMode();
    }

    private void OnSilentAudioDetected(object? sender, SilentAudioDetectedEventArgs e)
    {
        Logger.Warn("ViewModel", $"Silent audio detected - RMS: {e.RmsLevelDb:F2} dB");
        _notifications.ShowSilentAudioNotification();
    }

    private void OnShortRecordingRejected(object? sender, ShortRecordingRejectedEventArgs e)
    {
        if (e.Mode == RecordingMode.Hotkey)
            _notifications.ShowShortRecordingNotification(e.MinimumRequired);
    }

    private void OnMicrophoneDisconnected(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[AudioMonitor] Microphone disconnected - showing notification");
        _notifications.ShowMicrophoneDisconnectedNotification();
    }

    private void OnMaxDurationWarning(object? sender, MaxDurationWarningEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioRecorder] Max duration warning - {e.RemainingTime.TotalSeconds:F0}s remaining");
        _notifications.ShowMaxDurationWarningNotification(e.MaxDuration, e.RemainingTime);
    }

    private void OnMaxDurationReached(object? sender, MaxDurationReachedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioRecorder] Max duration reached - recording auto-stopped at {e.MaxDuration.TotalMinutes:F0} minutes");
        _notifications.ShowMaxDurationReachedNotification(e.MaxDuration);
    }

    private void OnQuietAudioDetected(object? sender, QuietAudioDetectedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioMonitor] Quiet audio detected - RMS: {e.RmsLevelDb:F2} dB");
        _notifications.ShowQuietAudioNotification();
    }

    private void OnAudioFileCorrupted(object? sender, AudioFileCorruptedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioValidator] Audio file corrupted: {e.ErrorMessage}");
        _notifications.ShowAudioFileCorruptedNotification(e.ErrorMessage);
    }

    #endregion

    #region Hotkey Event Handlers

    private async void OnHotkeyPressed(object? sender, EventArgs e) 
        => await _recordingController.StartHotkeyRecordingAsync();

    private async void OnHotkeyReleased(object? sender, EventArgs e) 
        => await _recordingController.StopHotkeyRecordingAsync();

    private void OnHotkeyRegistrationFailed(object? sender, HotkeyRegistrationFailedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Hotkey registration failed: {e.ErrorMessage}");
        _notifications.ShowRecordingError($"Hotkey registration failed: {e.ErrorMessage}", includeStorageGuidance: false);
    }

    #endregion

    #region Animation Helpers

    private void TriggerStateAnimation(WidgetStateManager.WidgetState newState)
    {
        if (_animationManager == null) return;

        var targetDimensions = _stateManager.CurrentDimensions;
        var targetOpacity = _stateManager.GetOpacityForState(newState);

        switch (newState)
        {
            case WidgetStateManager.WidgetState.IdleMinimal:
                _animationManager.AnimateToMinimal(targetDimensions, targetOpacity);
                break;
            case WidgetStateManager.WidgetState.IdleHover:
                _animationManager.AnimateToHover(targetDimensions, targetOpacity);
                break;
            case WidgetStateManager.WidgetState.Recording:
                _animationManager.AnimateToRecording(targetDimensions, targetOpacity, _stateManager.ShowControls);
                break;
            case WidgetStateManager.WidgetState.Transcribing:
            case WidgetStateManager.WidgetState.Cleaning:
            case WidgetStateManager.WidgetState.FailureFlash:
                _animationManager.AnimateToRecording(targetDimensions, targetOpacity, showControls: false);
                break;
        }
    }

    private void UpdateFailureFlashTimer(WidgetStateManager.WidgetState newState)
    {
        _failureFlashTimer?.Stop();

        if (newState != WidgetStateManager.WidgetState.FailureFlash)
        {
            return;
        }

        _failureFlashTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };

        _failureFlashTimer.Tick -= OnFailureFlashTimerTick;
        _failureFlashTimer.Tick += OnFailureFlashTimerTick;
        _failureFlashTimer.Start();
    }

    private void OnFailureFlashTimerTick(object? sender, EventArgs e)
    {
        _failureFlashTimer?.Stop();
        if (_stateManager.CurrentState == WidgetStateManager.WidgetState.FailureFlash)
        {
            _stateManager.TransitionFromWorkflow();
        }
    }

    private void UpdateWaveformDisplayMode()
    {
        if (_waveformRenderer == null) return;

        if (IsTranscribing)
        {
            _waveformRenderer.SetDisplayModeForState(isRecording: false, isHover: false, isTranscribing: true);
        }
        else if (IsCleaning)
        {
            _waveformRenderer.SetDisplayModeForState(isRecording: false, isHover: false, isCleaning: true);
        }
        else if (IsFailureFlash)
        {
            _waveformRenderer.SetDisplayModeForState(isRecording: false, isHover: false);
        }
        else if (IsRecording)
        {
            _waveformRenderer.SetDisplayModeForState(isRecording: true, isHover: false);
        }
        else if (IsIdleHover)
        {
            _waveformRenderer.SetDisplayModeForState(isRecording: false, isHover: true);
        }
        else
        {
            _waveformRenderer.SetDisplayModeForState(isRecording: false, isHover: false);
        }
    }

    #endregion

    #region INotifyPropertyChanged

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) 
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing) return;

        // Unsubscribe from state manager events
        _stateManager.StateChanged -= OnStateChanged;
        _stateManager.RecordingModeChanged -= OnRecordingModeChanged;
        _stateManager.DimensionsChanged -= OnDimensionsChanged;
        _stateManager.PropertyChanged -= OnStateManagerPropertyChanged;

        // Unsubscribe from audio/hotkey events
        _eventWiring.WaveformDataAvailable -= OnWaveformDataAvailable;
        _eventWiring.RecordingStateChanged -= OnAudioRecordingStateChanged;
        _eventWiring.SilentAudioDetected -= OnSilentAudioDetected;
        _eventWiring.ShortRecordingRejected -= OnShortRecordingRejected;
        _eventWiring.MicrophoneDisconnected -= OnMicrophoneDisconnected;
        _eventWiring.MaxDurationWarning -= OnMaxDurationWarning;
        _eventWiring.MaxDurationReached -= OnMaxDurationReached;
        _eventWiring.QuietAudioDetected -= OnQuietAudioDetected;
        _eventWiring.AudioFileCorrupted -= OnAudioFileCorrupted;
        _eventWiring.HotkeyPressed -= OnHotkeyPressed;
        _eventWiring.HotkeyReleased -= OnHotkeyReleased;
        _eventWiring.HotkeyRegistrationFailed -= OnHotkeyRegistrationFailed;

        if (_failureFlashTimer != null)
        {
            _failureFlashTimer.Stop();
            _failureFlashTimer.Tick -= OnFailureFlashTimerTick;
            _failureFlashTimer = null;
        }

        // Dispose components
        _eventWiring.Dispose();
        _stateManager.Dispose();
        _waveformRenderer?.Dispose();

        _disposed = true;
    }

    #endregion
}
