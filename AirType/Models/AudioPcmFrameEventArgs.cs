using AirType.Models.Transcription;

namespace AirType.Models;

public sealed class AudioPcmFrameEventArgs : EventArgs
{
    public AudioPcmFrameEventArgs(AudioPcmFrame frame)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public AudioPcmFrame Frame { get; }
}
