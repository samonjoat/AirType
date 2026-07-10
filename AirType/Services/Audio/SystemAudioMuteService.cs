using NAudio.CoreAudioApi;

namespace AirType.Services.Audio;

/// <summary>
/// Temporarily mutes the default Windows output device for the duration of a recording.
/// </summary>
public sealed class SystemAudioMuteService : ISystemAudioMuteService
{
    private readonly object _lock = new();
    private string? _mutedEndpointId;
    private bool _originalMuteState;
    private bool _hasActiveMuteSession;

    public bool TryMuteForRecording()
    {
        lock (_lock)
        {
            if (_hasActiveMuteSession)
            {
                return true;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                _mutedEndpointId = endpoint.ID;
                _originalMuteState = endpoint.AudioEndpointVolume.Mute;
                _hasActiveMuteSession = true;

                if (!_originalMuteState)
                {
                    endpoint.AudioEndpointVolume.Mute = true;
                    Logger.Info("SystemAudioMuteService", $"Muted '{endpoint.FriendlyName}' for recording.");
                }
                else
                {
                    Logger.Debug("SystemAudioMuteService", $"'{endpoint.FriendlyName}' was already muted before recording.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _mutedEndpointId = null;
                _originalMuteState = false;
                _hasActiveMuteSession = false;
                Logger.Error("SystemAudioMuteService", "Failed to mute the default playback device.", ex);
                return false;
            }
        }
    }

    public void RestoreAfterRecording()
    {
        lock (_lock)
        {
            if (!_hasActiveMuteSession)
            {
                return;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var endpoint = ResolveEndpoint(enumerator);
                endpoint.AudioEndpointVolume.Mute = _originalMuteState;

                Logger.Info(
                    "SystemAudioMuteService",
                    $"Restored '{endpoint.FriendlyName}' mute state to {_originalMuteState} after recording.");
            }
            catch (Exception ex)
            {
                Logger.Error("SystemAudioMuteService", "Failed to restore playback mute state after recording.", ex);
            }
            finally
            {
                _mutedEndpointId = null;
                _originalMuteState = false;
                _hasActiveMuteSession = false;
            }
        }
    }

    private MMDevice ResolveEndpoint(MMDeviceEnumerator enumerator)
    {
        if (!string.IsNullOrWhiteSpace(_mutedEndpointId))
        {
            try
            {
                return enumerator.GetDevice(_mutedEndpointId);
            }
            catch
            {
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
