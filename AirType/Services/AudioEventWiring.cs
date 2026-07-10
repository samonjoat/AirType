using System;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Handles event subscription/unsubscription for audio-related managers.
/// Centralizes event wiring to reduce code duplication in ViewModels.
/// </summary>
public class AudioEventWiring : IDisposable
{
    private IAudioInputManager? _audioInputManager;
    private IHotkeyManager? _hotkeyManager;
    private bool _disposed;

    // Audio input manager event handlers
    public event EventHandler<WaveformDataEventArgs>? WaveformDataAvailable;
    public event EventHandler<RecordingStateEventArgs>? RecordingStateChanged;
    public event EventHandler<SilentAudioDetectedEventArgs>? SilentAudioDetected;
    public event EventHandler<ShortRecordingRejectedEventArgs>? ShortRecordingRejected;
    public event EventHandler? MicrophoneDisconnected;
    public event EventHandler<MaxDurationWarningEventArgs>? MaxDurationWarning;
    public event EventHandler<MaxDurationReachedEventArgs>? MaxDurationReached;
    public event EventHandler<QuietAudioDetectedEventArgs>? QuietAudioDetected;
    public event EventHandler<AudioFileCorruptedEventArgs>? AudioFileCorrupted;

    // Hotkey manager event handlers
    public event EventHandler? HotkeyPressed;
    public event EventHandler? HotkeyReleased;
    public event EventHandler<HotkeyRegistrationFailedEventArgs>? HotkeyRegistrationFailed;

    /// <summary>
    /// Gets the currently wired audio input manager
    /// </summary>
    public IAudioInputManager? AudioInputManager => _audioInputManager;

    /// <summary>
    /// Gets the currently wired hotkey manager
    /// </summary>
    public IHotkeyManager? HotkeyManager => _hotkeyManager;

    /// <summary>
    /// Wire up a new audio input manager, unsubscribing from any previous one
    /// </summary>
    public void WireAudioInputManager(IAudioInputManager? manager)
    {
        if (_audioInputManager != null)
            UnsubscribeAudioInputManager();

        _audioInputManager = manager;

        if (_audioInputManager != null)
            SubscribeAudioInputManager();
    }

    /// <summary>
    /// Wire up a new hotkey manager, unsubscribing from any previous one
    /// </summary>
    public void WireHotkeyManager(IHotkeyManager? manager)
    {
        if (_hotkeyManager != null)
            UnsubscribeHotkeyManager();

        _hotkeyManager = manager;

        if (_hotkeyManager != null)
            SubscribeHotkeyManager();
    }

    private void SubscribeAudioInputManager()
    {
        if (_audioInputManager == null) return;

        _audioInputManager.WaveformDataAvailable += OnWaveformDataAvailable;
        _audioInputManager.RecordingStateChanged += OnRecordingStateChanged;
        _audioInputManager.SilentAudioDetected += OnSilentAudioDetected;
        _audioInputManager.ShortRecordingRejected += OnShortRecordingRejected;
        _audioInputManager.MicrophoneDisconnected += OnMicrophoneDisconnected;
        _audioInputManager.MaxDurationWarning += OnMaxDurationWarning;
        _audioInputManager.MaxDurationReached += OnMaxDurationReached;
        _audioInputManager.QuietAudioDetected += OnQuietAudioDetected;
        _audioInputManager.AudioFileCorrupted += OnAudioFileCorrupted;
    }

    private void UnsubscribeAudioInputManager()
    {
        if (_audioInputManager == null) return;

        _audioInputManager.WaveformDataAvailable -= OnWaveformDataAvailable;
        _audioInputManager.RecordingStateChanged -= OnRecordingStateChanged;
        _audioInputManager.SilentAudioDetected -= OnSilentAudioDetected;
        _audioInputManager.ShortRecordingRejected -= OnShortRecordingRejected;
        _audioInputManager.MicrophoneDisconnected -= OnMicrophoneDisconnected;
        _audioInputManager.MaxDurationWarning -= OnMaxDurationWarning;
        _audioInputManager.MaxDurationReached -= OnMaxDurationReached;
        _audioInputManager.QuietAudioDetected -= OnQuietAudioDetected;
        _audioInputManager.AudioFileCorrupted -= OnAudioFileCorrupted;
    }

    private void SubscribeHotkeyManager()
    {
        if (_hotkeyManager == null) return;

        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        _hotkeyManager.HotkeyReleased += OnHotkeyReleased;
        _hotkeyManager.HotkeyRegistrationFailed += OnHotkeyRegistrationFailed;
    }

    private void UnsubscribeHotkeyManager()
    {
        if (_hotkeyManager == null) return;

        _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyManager.HotkeyReleased -= OnHotkeyReleased;
        _hotkeyManager.HotkeyRegistrationFailed -= OnHotkeyRegistrationFailed;
    }

    // Forward events from audio input manager
    private void OnWaveformDataAvailable(object? sender, WaveformDataEventArgs e) 
        => WaveformDataAvailable?.Invoke(sender, e);
    
    private void OnRecordingStateChanged(object? sender, RecordingStateEventArgs e) 
        => RecordingStateChanged?.Invoke(sender, e);
    
    private void OnSilentAudioDetected(object? sender, SilentAudioDetectedEventArgs e) 
        => SilentAudioDetected?.Invoke(sender, e);
    
    private void OnShortRecordingRejected(object? sender, ShortRecordingRejectedEventArgs e) 
        => ShortRecordingRejected?.Invoke(sender, e);
    
    private void OnMicrophoneDisconnected(object? sender, EventArgs e) 
        => MicrophoneDisconnected?.Invoke(sender, e);
    
    private void OnMaxDurationWarning(object? sender, MaxDurationWarningEventArgs e) 
        => MaxDurationWarning?.Invoke(sender, e);
    
    private void OnMaxDurationReached(object? sender, MaxDurationReachedEventArgs e) 
        => MaxDurationReached?.Invoke(sender, e);
    
    private void OnQuietAudioDetected(object? sender, QuietAudioDetectedEventArgs e) 
        => QuietAudioDetected?.Invoke(sender, e);
    
    private void OnAudioFileCorrupted(object? sender, AudioFileCorruptedEventArgs e) 
        => AudioFileCorrupted?.Invoke(sender, e);

    // Forward events from hotkey manager
    private void OnHotkeyPressed(object? sender, EventArgs e) 
        => HotkeyPressed?.Invoke(sender, e);
    
    private void OnHotkeyReleased(object? sender, EventArgs e) 
        => HotkeyReleased?.Invoke(sender, e);
    
    private void OnHotkeyRegistrationFailed(object? sender, HotkeyRegistrationFailedEventArgs e) 
        => HotkeyRegistrationFailed?.Invoke(sender, e);

    public void Dispose()
    {
        if (_disposed) return;

        UnsubscribeAudioInputManager();
        UnsubscribeHotkeyManager();
        _disposed = true;
    }
}
