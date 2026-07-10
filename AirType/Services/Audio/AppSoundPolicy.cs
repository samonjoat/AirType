namespace AirType.Services.Audio;

/// <summary>
/// Centralizes mapping from semantic application sound states to transport playback calls.
/// </summary>
public sealed class AppSoundPolicy : IAppSoundPolicy
{
    private readonly INotificationSoundService _notificationSoundService;

    public AppSoundPolicy(INotificationSoundService notificationSoundService)
    {
        ArgumentNullException.ThrowIfNull(notificationSoundService);
        _notificationSoundService = notificationSoundService;
    }

    public void Play(AppSoundRequest request)
    {
        if (!request.IsInitialized)
        {
            throw new ArgumentException("AppSoundRequest must be created through its factory methods.", nameof(request));
        }

        switch (request.State)
        {
            case AppSoundState.RecordingStarted:
                if (request.OnStartPlaybackCompleted is null)
                {
                    _notificationSoundService.PlayStart();
                }
                else
                {
                    _notificationSoundService.PlayStart(request.OnStartPlaybackCompleted);
                }
                break;

            case AppSoundState.RecordingCanceledByUser:
            case AppSoundState.WorkflowCanceled:
            case AppSoundState.WorkflowFailed:
                _notificationSoundService.PlayCancel();
                break;

            case AppSoundState.InjectionSucceeded:
                if (request.InjectionVerified && !request.ClipboardFallbackUsed)
                {
                    _notificationSoundService.PlayDone();
                }
                break;

            case AppSoundState.Silent:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.State, "Unknown app sound state.");
        }
    }
}
