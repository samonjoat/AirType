namespace AirType.Models;

/// <summary>
/// Represents an audio capture device exposed via the legacy WaveIn API.
/// </summary>
public record AudioDeviceInfo(int DeviceNumber, string DisplayName, int Channels)
{
    public override string ToString() => DisplayName;
}
