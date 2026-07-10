using System;

namespace AirType.Services.Audio;

/// <summary>
/// Describes a single semantic sound request with only the context needed for that request.
/// </summary>
public readonly struct AppSoundRequest
{
    private readonly bool _isInitialized;

    private AppSoundRequest(
        AppSoundState state,
        Action? onStartPlaybackCompleted,
        bool injectionVerified,
        bool clipboardFallbackUsed)
    {
        _isInitialized = true;
        State = state;
        OnStartPlaybackCompleted = onStartPlaybackCompleted;
        InjectionVerified = injectionVerified;
        ClipboardFallbackUsed = clipboardFallbackUsed;
    }

    internal bool IsInitialized => _isInitialized;

    public AppSoundState State { get; }

    public Action? OnStartPlaybackCompleted { get; }

    public bool InjectionVerified { get; }

    public bool ClipboardFallbackUsed { get; }

    public static AppSoundRequest Create(AppSoundState state)
    {
        return state switch
        {
            AppSoundState.RecordingStarted => new(state, onStartPlaybackCompleted: null, injectionVerified: true, clipboardFallbackUsed: false),
            AppSoundState.RecordingCanceledByUser => new(state, onStartPlaybackCompleted: null, injectionVerified: true, clipboardFallbackUsed: false),
            AppSoundState.WorkflowCanceled => new(state, onStartPlaybackCompleted: null, injectionVerified: true, clipboardFallbackUsed: false),
            AppSoundState.WorkflowFailed => new(state, onStartPlaybackCompleted: null, injectionVerified: true, clipboardFallbackUsed: false),
            AppSoundState.Silent => new(state, onStartPlaybackCompleted: null, injectionVerified: true, clipboardFallbackUsed: false),
            AppSoundState.InjectionSucceeded => throw new ArgumentException("Use CreateInjectionSucceeded for injection result routing.", nameof(state)),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown app sound state.")
        };
    }

    public static AppSoundRequest CreateRecordingStarted(Action? onStartPlaybackCompleted = null) =>
        new(AppSoundState.RecordingStarted, onStartPlaybackCompleted, injectionVerified: true, clipboardFallbackUsed: false);

    public static AppSoundRequest CreateInjectionSucceeded(bool injectionVerified, bool clipboardFallbackUsed) =>
        new(AppSoundState.InjectionSucceeded, onStartPlaybackCompleted: null, injectionVerified, clipboardFallbackUsed);
}
