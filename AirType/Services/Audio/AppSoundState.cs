namespace AirType.Services.Audio;

/// <summary>
/// Describes the semantic application states that may request a notification sound.
/// </summary>
public enum AppSoundState
{
    RecordingStarted,
    RecordingCanceledByUser,
    WorkflowCanceled,
    WorkflowFailed,
    InjectionSucceeded,
    Silent
}
