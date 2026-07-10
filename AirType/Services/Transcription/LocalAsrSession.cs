using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public sealed class LocalAsrSession : ILocalAsrSession
{
    private readonly ILocalAsrWorkerClient _client;
    private readonly LocalAsrOptions _options;
    private readonly IRollingInitialPromptBuilder _promptBuilder;
    private readonly TaskCompletionSource<LocalAsrResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<LocalAsrUtterance> _utterances = new();
    private readonly object _lockObject = new();
    private long _lastSequence = -1;
    private int? _finalFlushMilliseconds;
    private int? _asrTotalMilliseconds;
    private int? _hotwordTokenCount;
    private string? _lastErrorCode;
    private string? _lastErrorMessage;
    private bool _disposed;

    public LocalAsrSession(
        Guid recordingId,
        ILocalAsrWorkerClient client,
        LocalAsrOptions options,
        IRollingInitialPromptBuilder promptBuilder)
    {
        RecordingId = recordingId;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options;
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _client.WorkerEventReceived += OnWorkerEventReceived;
    }

    public Guid RecordingId { get; }

    public LocalAsrSessionState State { get; private set; } = LocalAsrSessionState.Created;

    public TimeSpan FinalFlushTimeout { get; } = TimeSpan.FromSeconds(15);

    internal async Task StartAsync(string? hotwords, int hotwordTokenCount, CancellationToken cancellationToken)
    {
        State = LocalAsrSessionState.Starting;
        _hotwordTokenCount = hotwordTokenCount;
        _promptBuilder.Reset(RecordingId);
        await _client.StartRecordingAsync(RecordingId, _options, hotwords, cancellationToken);
        State = LocalAsrSessionState.Active;
    }

    public async ValueTask EnqueueFrameAsync(AudioPcmFrame frame, CancellationToken cancellationToken)
    {
        if (frame.RecordingId != RecordingId)
        {
            return;
        }

        if (State is not LocalAsrSessionState.Active and not LocalAsrSessionState.Starting)
        {
            return;
        }

        _lastSequence = Math.Max(_lastSequence, frame.Sequence);
        try
        {
            await _client.SendAudioFrameAsync(frame, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastErrorCode = "SEND_FAILED";
            _lastErrorMessage = ex.Message;
            State = LocalAsrSessionState.Failed;
            _completion.TrySetResult(BuildIncomplete(LocalAsrFallbackReason.WorkerError));
        }
    }

    public async Task<LocalAsrResult> StopAndWaitAsync(TimeSpan flushTimeout, CancellationToken cancellationToken)
    {
        if (State is LocalAsrSessionState.Complete)
        {
            return await _completion.Task;
        }

        State = LocalAsrSessionState.Stopping;
        try
        {
            await _client.StopRecordingAsync(RecordingId, _lastSequence, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastErrorCode = "STOP_FAILED";
            _lastErrorMessage = ex.Message;
            State = LocalAsrSessionState.Failed;
            return BuildIncomplete(LocalAsrFallbackReason.WorkerError);
        }

        try
        {
            return await _completion.Task.WaitAsync(flushTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return BuildIncomplete(LocalAsrFallbackReason.LocalFlushTimeout);
        }
    }

    public async Task<LocalAsrResult> WaitUntilCompleteAsync(CancellationToken cancellationToken)
    {
        if (State is LocalAsrSessionState.Active)
        {
            State = LocalAsrSessionState.Stopping;
            await _client.StopRecordingAsync(RecordingId, _lastSequence, cancellationToken);
        }

        return await _completion.Task.WaitAsync(cancellationToken);
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        if (State is LocalAsrSessionState.Cancelled or LocalAsrSessionState.Complete)
        {
            return;
        }

        State = LocalAsrSessionState.Cancelled;
        await _client.CancelRecordingAsync(RecordingId, cancellationToken);
        _completion.TrySetResult(BuildIncomplete(LocalAsrFallbackReason.WorkerError));
    }

    private void OnWorkerEventReceived(object? sender, LocalAsrWorkerEventArgs e)
    {
        var workerEvent = e.WorkerEvent;
        if (workerEvent.RecordingId.HasValue && workerEvent.RecordingId.Value != RecordingId)
        {
            return;
        }

        switch (workerEvent.Type)
        {
            case "utterance_final":
                AddUtterance(workerEvent);
                break;
            case "recording_complete":
                Complete(workerEvent);
                break;
            case "metrics":
                CaptureMetric(workerEvent);
                break;
            case "error":
                if (IsWarning(workerEvent))
                {
                    Logger.Warn("LocalAsrSession", $"Worker warning for {RecordingId}: {workerEvent.Code} {workerEvent.Message}");
                    break;
                }

                _lastErrorCode = workerEvent.Code;
                _lastErrorMessage = workerEvent.Message;
                State = LocalAsrSessionState.Failed;
                _completion.TrySetResult(BuildIncomplete(LocalAsrFallbackReason.WorkerError));
                break;
        }
    }

    private static bool IsWarning(LocalAsrProtocol.WorkerEvent workerEvent) =>
        string.Equals(workerEvent.Severity, "warning", StringComparison.OrdinalIgnoreCase);

    private void AddUtterance(LocalAsrProtocol.WorkerEvent workerEvent)
    {
        string text = workerEvent.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var utterance = new LocalAsrUtterance(
            workerEvent.Index ?? (_utterances.Count + 1),
            text,
            workerEvent.StartMilliseconds ?? 0,
            workerEvent.EndMilliseconds ?? 0,
            workerEvent.AsrMilliseconds ?? 0);

        lock (_lockObject)
        {
            _utterances.Add(utterance);
        }

        _promptBuilder.AppendUtterance(RecordingId, text);
    }

    private void Complete(LocalAsrProtocol.WorkerEvent workerEvent)
    {
        _finalFlushMilliseconds = workerEvent.FinalFlushMilliseconds;
        _asrTotalMilliseconds = workerEvent.AsrTotalMilliseconds;
        State = LocalAsrSessionState.Complete;

        string text = workerEvent.Text ?? BuildTextFromUtterances();
        _completion.TrySetResult(new LocalAsrResult(
            RecordingId,
            IsComplete: !string.IsNullOrWhiteSpace(text),
            Text: text,
            ModelUsed: _options.ModelId,
            FallbackReason: string.IsNullOrWhiteSpace(text) ? LocalAsrFallbackReason.EmptyResult : LocalAsrFallbackReason.None,
            Utterances: SnapshotUtterances(),
            Diagnostics: BuildDiagnostics()));
    }

    private void CaptureMetric(LocalAsrProtocol.WorkerEvent workerEvent)
    {
        if (string.Equals(workerEvent.Name, "hotwordTokenCount", StringComparison.OrdinalIgnoreCase))
        {
            _hotwordTokenCount = workerEvent.Value;
        }
    }

    private LocalAsrResult BuildIncomplete(LocalAsrFallbackReason reason) =>
        LocalAsrResult.Incomplete(RecordingId, _options.ModelId, reason, BuildDiagnostics());

    private LocalAsrDiagnostics BuildDiagnostics() => new(
        FinalFlushMilliseconds: _finalFlushMilliseconds,
        AsrTotalMilliseconds: _asrTotalMilliseconds,
        HotwordTokenCount: _hotwordTokenCount,
        InitialPromptMaxTokenCount: _options.InitialPromptTokenCap,
        WorkerSessionId: RecordingId.ToString(),
        ErrorCode: _lastErrorCode,
        ErrorMessage: _lastErrorMessage);

    private string BuildTextFromUtterances() =>
        string.Join(" ", SnapshotUtterances().Select(utterance => utterance.Text)).Trim();

    private IReadOnlyList<LocalAsrUtterance> SnapshotUtterances()
    {
        lock (_lockObject)
        {
            return _utterances.ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.WorkerEventReceived -= OnWorkerEventReceived;

        if (State is LocalAsrSessionState.Active or LocalAsrSessionState.Starting or LocalAsrSessionState.Stopping)
        {
            try
            {
                await CancelAsync(CancellationToken.None);
            }
            catch
            {
            }
        }
    }
}
