using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public interface ILocalAsrWorkerClient : IAsyncDisposable
{
    event EventHandler<LocalAsrWorkerEventArgs>? WorkerEventReceived;

    bool IsConnected { get; }

    Task ConnectAsync(Uri endpoint, string authToken, CancellationToken cancellationToken);

    Task LoadModelAsync(LocalAsrOptions options, CancellationToken cancellationToken);

    Task PrewarmAsync(CancellationToken cancellationToken);

    Task StartRecordingAsync(
        Guid recordingId,
        LocalAsrOptions options,
        string? hotwords,
        CancellationToken cancellationToken);

    ValueTask SendAudioFrameAsync(AudioPcmFrame frame, CancellationToken cancellationToken);

    Task StopRecordingAsync(Guid recordingId, long lastSequence, CancellationToken cancellationToken);

    Task CancelRecordingAsync(Guid recordingId, CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

public sealed class LocalAsrWorkerEventArgs : EventArgs
{
    public LocalAsrWorkerEventArgs(LocalAsrProtocol.WorkerEvent workerEvent)
    {
        WorkerEvent = workerEvent;
    }

    public LocalAsrProtocol.WorkerEvent WorkerEvent { get; }
}
