using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Manages widget states and transitions with proper event handling
/// </summary>
public class WidgetStateManager : INotifyPropertyChanged, IDisposable
{
    private WidgetState _currentState = WidgetState.IdleMinimal;
    private bool _isHotkeyRecording = false;
    private bool _isUnattendedRecording = false;
    private bool _isTranscribing = false;
    private bool _isCleaning = false;
    private bool _isFailureFlash = false;
    private WidgetConfiguration _configuration = new();
    private bool _disposed = false;

    /// <summary>
    /// Widget state enumeration
    /// </summary>
    public enum WidgetState
    {
        /// <summary>
        /// True idle state - barely visible capsule (40px x 10px)
        /// </summary>
        IdleMinimal,
        
        /// <summary>
        /// Hover affordance state - expanded capsule with flat waveform (100px x 30px)
        /// </summary>
        IdleHover,
        
        /// <summary>
        /// Recording state - full capsule with live waveform and controls when unattended (100px x 30px)
        /// </summary>
        Recording,

        /// <summary>
        /// Transcribing state - retains recording footprint while showing text animation
        /// </summary>
        Transcribing,

        /// <summary>
        /// Cleaning state - retains recording footprint while showing transcript cleanup animation
        /// </summary>
        Cleaning,

        /// <summary>
        /// Failure-only flash state - short active footprint warning before returning to idle
        /// </summary>
        FailureFlash
    }

    /// <summary>
    /// Current widget state
    /// </summary>
    public WidgetState CurrentState
    {
        get => _currentState;
        private set => SetField(ref _currentState, value);
    }

    /// <summary>
    /// Whether hotkey recording is active
    /// </summary>
    public bool IsHotkeyRecording
    {
        get => _isHotkeyRecording;
        private set => SetField(ref _isHotkeyRecording, value);
    }

    /// <summary>
    /// Whether unattended recording is active
    /// </summary>
    public bool IsUnattendedRecording
    {
        get => _isUnattendedRecording;
        private set => SetField(ref _isUnattendedRecording, value);
    }

    /// <summary>
    /// Widget configuration settings
    /// </summary>
    public WidgetConfiguration Configuration
    {
        get => _configuration;
        set
        {
            if (SetField(ref _configuration, value ?? new WidgetConfiguration()))
            {
                OnPropertyChanged(nameof(CurrentDimensions));
                OnPropertyChanged(nameof(CurrentOpacity));
            }
        }
    }

    /// <summary>
    /// Whether any recording is currently active
    /// </summary>
    public bool IsRecording => IsHotkeyRecording || IsUnattendedRecording;

    /// <summary>
    /// Whether control buttons should be visible
    /// </summary>
    public bool ShowControls => IsUnattendedRecording;

    /// <summary>
    /// Current widget dimensions based on state
    /// </summary>
    public WidgetDimensions CurrentDimensions => CurrentState switch
    {
        WidgetState.IdleMinimal => Configuration.MinimalState,
        WidgetState.IdleHover => Configuration.HoverState,
        WidgetState.Recording => Configuration.RecordingState,
        WidgetState.Transcribing => Configuration.RecordingState,
        WidgetState.Cleaning => Configuration.RecordingState,
        WidgetState.FailureFlash => Configuration.RecordingState,
        _ => Configuration.MinimalState
    };

    /// <summary>
    /// Current widget opacity based on state
    /// </summary>
    public double CurrentOpacity => GetOpacityForState(CurrentState);

    /// <summary>
    /// Whether the widget is currently showing the transcribing/loading state
    /// </summary>
    public bool IsTranscribing
    {
        get => _isTranscribing;
        private set => SetField(ref _isTranscribing, value);
    }

    /// <summary>
    /// Whether the widget is currently showing the transcript cleanup state
    /// </summary>
    public bool IsCleaning
    {
        get => _isCleaning;
        private set => SetField(ref _isCleaning, value);
    }

    /// <summary>
    /// Whether the widget is currently showing the failure-only flash state
    /// </summary>
    public bool IsFailureFlash
    {
        get => _isFailureFlash;
        private set => SetField(ref _isFailureFlash, value);
    }

    /// <summary>
    /// Event fired when widget state changes
    /// </summary>
    public event EventHandler<WidgetStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event fired when recording mode changes
    /// </summary>
    public event EventHandler<RecordingModeChangedEventArgs>? RecordingModeChanged;

    /// <summary>
    /// Event fired when widget dimensions should be updated
    /// </summary>
    public event EventHandler<WidgetDimensionsChangedEventArgs>? DimensionsChanged;

    /// <summary>
    /// PropertyChanged event for INotifyPropertyChanged
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Transition to a new widget state
    /// </summary>
    /// <param name="newState">Target state</param>
    /// <param name="recordingMode">Recording mode if transitioning to recording state</param>
    public void TransitionTo(WidgetState newState, RecordingMode recordingMode = RecordingMode.Hotkey)
    {
        if (_disposed)
            return;

        var previousState = CurrentState;
        var previousRecordingMode = GetCurrentRecordingMode();

        // Update state
        CurrentState = newState;

        // Update recording flags based on new state and mode
        UpdateRecordingFlags(newState, recordingMode);
        UpdateProcessingFlags(newState);

        // Fire events
        StateChanged?.Invoke(this, new WidgetStateChangedEventArgs(previousState, newState));
        
        var currentRecordingMode = GetCurrentRecordingMode();
        if (previousRecordingMode != currentRecordingMode)
        {
            RecordingModeChanged?.Invoke(this, new RecordingModeChangedEventArgs(previousRecordingMode, currentRecordingMode));
        }

        DimensionsChanged?.Invoke(this, new WidgetDimensionsChangedEventArgs(CurrentDimensions));

        // Notify property changes
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(ShowControls));
        OnPropertyChanged(nameof(CurrentDimensions));
        OnPropertyChanged(nameof(CurrentOpacity));
    }

    /// <summary>
    /// Start hotkey recording
    /// </summary>
    public void StartHotkeyRecording()
    {
        TransitionTo(WidgetState.Recording, RecordingMode.Hotkey);
    }

    /// <summary>
    /// Stop hotkey recording
    /// </summary>
    public void StopHotkeyRecording()
    {
        TransitionTo(WidgetState.IdleMinimal);
    }

    /// <summary>
    /// Start unattended recording
    /// </summary>
    public void StartUnattendedRecording()
    {
        TransitionTo(WidgetState.Recording, RecordingMode.Unattended);
    }

    /// <summary>
    /// Stop unattended recording
    /// </summary>
    public void StopUnattendedRecording()
    {
        TransitionTo(WidgetState.IdleHover);
    }

    /// <summary>
    /// Enter the transcribing state (after recording completes)
    /// </summary>
    public void TransitionToTranscribing()
    {
        if (CurrentState == WidgetState.Transcribing)
        {
            return;
        }

        TransitionTo(WidgetState.Transcribing);
    }

    /// <summary>
    /// Enter the cleaning state after raw transcription completes and cleanup begins.
    /// </summary>
    public void TransitionToCleaning()
    {
        if (CurrentState == WidgetState.Cleaning)
        {
            return;
        }

        TransitionTo(WidgetState.Cleaning);
    }

    /// <summary>
    /// Enter the failure-only flash state.
    /// </summary>
    public void TransitionToFailureFlash()
    {
        if (CurrentState == WidgetState.FailureFlash)
        {
            return;
        }

        TransitionTo(WidgetState.FailureFlash);
    }

    /// <summary>
    /// Exit any workflow processing state and return to minimal footprint.
    /// </summary>
    public void TransitionFromWorkflow()
    {
        if (CurrentState is WidgetState.Transcribing or WidgetState.Cleaning or WidgetState.FailureFlash)
        {
            TransitionTo(WidgetState.IdleMinimal);
        }
    }

    /// <summary>
    /// Cancel current recording
    /// </summary>
    public void CancelRecording()
    {
        if (IsHotkeyRecording)
        {
            TransitionTo(WidgetState.IdleMinimal);
        }
        else if (IsUnattendedRecording)
        {
            TransitionTo(WidgetState.IdleHover);
        }
    }

    /// <summary>
    /// Transition to hover state (typically on mouse enter)
    /// </summary>
    public void EnterHoverState()
    {
        if (CurrentState == WidgetState.IdleMinimal && !IsRecording)
        {
            TransitionTo(WidgetState.IdleHover);
        }
    }

    /// <summary>
    /// Transition to minimal state (typically on mouse leave)
    /// </summary>
    public void ExitHoverState()
    {
        if (CurrentState == WidgetState.IdleHover && !IsRecording)
        {
            TransitionTo(WidgetState.IdleMinimal);
        }
    }

    /// <summary>
    /// Reset to initial state
    /// </summary>
    public void Reset()
    {
        TransitionTo(WidgetState.IdleMinimal);
    }

    private void UpdateRecordingFlags(WidgetState state, RecordingMode mode)
    {
        if (state == WidgetState.Recording)
        {
            IsHotkeyRecording = mode == RecordingMode.Hotkey;
            IsUnattendedRecording = mode == RecordingMode.Unattended;
        }
        else
        {
            IsHotkeyRecording = false;
            IsUnattendedRecording = false;
        }
    }

    private RecordingMode? GetCurrentRecordingMode()
    {
        if (IsHotkeyRecording) return RecordingMode.Hotkey;
        if (IsUnattendedRecording) return RecordingMode.Unattended;
        return null;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Clean up any resources if needed
            _disposed = true;
        }
    }

    /// <summary>
    /// Resolve opacity for the supplied widget state using configuration defaults.
    /// </summary>
    public double GetOpacityForState(WidgetState state)
    {
        return state switch
        {
            WidgetState.IdleMinimal => Configuration.MinimalOpacity,
            WidgetState.IdleHover => Configuration.HoverOpacity,
            WidgetState.Recording => Configuration.RecordingOpacity,
            WidgetState.Transcribing => Configuration.RecordingOpacity,
            WidgetState.Cleaning => Configuration.RecordingOpacity,
            WidgetState.FailureFlash => Configuration.RecordingOpacity,
            _ => Configuration.HoverOpacity
        };
    }

    private void UpdateProcessingFlags(WidgetState state)
    {
        IsTranscribing = state == WidgetState.Transcribing;
        IsCleaning = state == WidgetState.Cleaning;
        IsFailureFlash = state == WidgetState.FailureFlash;
    }
}

/// <summary>
/// Event arguments for widget state changes
/// </summary>
public class WidgetStateChangedEventArgs : EventArgs
{
    public WidgetStateManager.WidgetState PreviousState { get; }
    public WidgetStateManager.WidgetState NewState { get; }

    public WidgetStateChangedEventArgs(WidgetStateManager.WidgetState previousState, WidgetStateManager.WidgetState newState)
    {
        PreviousState = previousState;
        NewState = newState;
    }
}

/// <summary>
/// Event arguments for recording mode changes
/// </summary>
public class RecordingModeChangedEventArgs : EventArgs
{
    public RecordingMode? PreviousMode { get; }
    public RecordingMode? NewMode { get; }

    public RecordingModeChangedEventArgs(RecordingMode? previousMode, RecordingMode? newMode)
    {
        PreviousMode = previousMode;
        NewMode = newMode;
    }
}

/// <summary>
/// Event arguments for widget dimensions changes
/// </summary>
public class WidgetDimensionsChangedEventArgs : EventArgs
{
    public WidgetDimensions Dimensions { get; }

    public WidgetDimensionsChangedEventArgs(WidgetDimensions dimensions)
    {
        Dimensions = dimensions;
    }
}
