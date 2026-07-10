namespace AirType.Services.Audio;

/// <summary>
/// Controls temporary muting of the default Windows render endpoint during recording.
/// </summary>
public interface ISystemAudioMuteService
{
    /// <summary>
    /// Mutes the default playback endpoint and remembers its prior mute state.
    /// </summary>
    bool TryMuteForRecording();

    /// <summary>
    /// Restores the prior mute state if the app muted the endpoint for recording.
    /// </summary>
    void RestoreAfterRecording();
}
