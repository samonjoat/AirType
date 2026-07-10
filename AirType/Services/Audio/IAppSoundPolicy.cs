namespace AirType.Services.Audio;

/// <summary>
/// Maps semantic application sound states to transport-level notification cues.
/// </summary>
public interface IAppSoundPolicy
{
    /// <summary>
    /// Requests the appropriate sound for the specified semantic sound request.
    /// </summary>
    void Play(AppSoundRequest request);
}
