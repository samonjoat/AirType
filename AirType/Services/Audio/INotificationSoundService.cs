using System;

namespace AirType.Services.Audio;

/// <summary>
/// Provides playback transport for the app's notification sound assets.
/// </summary>
public interface INotificationSoundService
{
    /// <summary>
    /// Plays the recording-started cue.
    /// </summary>
    void PlayStart();

    /// <summary>
    /// Plays the recording-started cue and invokes a callback after playback drains.
    /// </summary>
    void PlayStart(Action? onCompleted);

    /// <summary>
    /// Plays the completion cue.
    /// </summary>
    void PlayDone();

    /// <summary>
    /// Plays the cancel or failure cue.
    /// </summary>
    void PlayCancel();
}
