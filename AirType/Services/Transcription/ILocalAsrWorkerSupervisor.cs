using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public interface ILocalAsrWorkerSupervisor : IAsyncDisposable
{
    LocalAsrWorkerState State { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task EnsureStartedAsync(CancellationToken cancellationToken);

    Task PrewarmActiveModelAsync(CancellationToken cancellationToken);

    Task<ILocalAsrSession?> TryStartSessionAsync(Guid recordingId, CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

public interface ILocalAsrSession : IAsyncDisposable
{
    Guid RecordingId { get; }

    LocalAsrSessionState State { get; }

    TimeSpan FinalFlushTimeout { get; }

    ValueTask EnqueueFrameAsync(AudioPcmFrame frame, CancellationToken cancellationToken);

    Task<LocalAsrResult> StopAndWaitAsync(TimeSpan flushTimeout, CancellationToken cancellationToken);

    Task<LocalAsrResult> WaitUntilCompleteAsync(CancellationToken cancellationToken);

    Task CancelAsync(CancellationToken cancellationToken);
}
