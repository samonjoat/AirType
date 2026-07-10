using System.IO;
using System.Net.WebSockets;
using System.Text;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public sealed class LocalAsrWorkerClient : ILocalAsrWorkerClient
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private ClientWebSocket? _webSocket;
    private Task? _receiveTask;

    public event EventHandler<LocalAsrWorkerEventArgs>? WorkerEventReceived;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public async Task ConnectAsync(Uri endpoint, string authToken, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(endpoint, cancellationToken);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_disposeCts.Token));

        await SendJsonAsync(new
        {
            type = "hello",
            protocolVersion = LocalAsrProtocol.ProtocolVersion,
            authToken,
            client = "AirType"
        }, cancellationToken);
    }

    public Task LoadModelAsync(LocalAsrOptions options, CancellationToken cancellationToken) =>
        SendJsonAsync(new
        {
            type = "load_model",
            model = options.ModelName,
            device = options.Device,
            computeType = options.ComputeType,
            cpuThreads = options.CpuThreads
        }, cancellationToken);

    public Task PrewarmAsync(CancellationToken cancellationToken) =>
        SendJsonAsync(new { type = "prewarm" }, cancellationToken);

    public Task StartRecordingAsync(
        Guid recordingId,
        LocalAsrOptions options,
        string? hotwords,
        CancellationToken cancellationToken) =>
        SendJsonAsync(new
        {
            type = "start_recording",
            recordingId,
            sampleRate = options.SampleRate,
            channels = options.Channels,
            sampleFormat = "pcm_s16le",
            frameMs = options.FrameMilliseconds,
            hotwords = string.IsNullOrWhiteSpace(hotwords) ? null : hotwords,
            initialPromptTokenCap = options.InitialPromptTokenCap,
            hotwordTokenCap = options.HotwordTokenCap
        }, cancellationToken);

    public async ValueTask SendAudioFrameAsync(AudioPcmFrame frame, CancellationToken cancellationToken)
    {
        if (!IsConnected || _webSocket == null)
        {
            throw new InvalidOperationException("Local ASR worker is not connected.");
        }

        var payload = new byte[frame.BytesRecorded];
        Buffer.BlockCopy(frame.Pcm16Mono16Khz, 0, payload, 0, frame.BytesRecorded);
        long firstSample = frame.Sequence * (frame.BytesRecorded / 2);
        byte[] websocketFrame = LocalAsrProtocol.BuildPcmFrame(frame.Sequence, firstSample, payload);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _webSocket.SendAsync(
                websocketFrame,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task StopRecordingAsync(Guid recordingId, long lastSequence, CancellationToken cancellationToken) =>
        SendJsonAsync(new
        {
            type = "stop_recording",
            recordingId,
            lastSequence
        }, cancellationToken);

    public Task CancelRecordingAsync(Guid recordingId, CancellationToken cancellationToken) =>
        SendJsonAsync(new
        {
            type = "cancel_recording",
            recordingId
        }, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        IsConnected
            ? SendJsonAsync(new { type = "shutdown" }, cancellationToken)
            : Task.CompletedTask;

    private async Task SendJsonAsync(object message, CancellationToken cancellationToken)
    {
        if (!IsConnected || _webSocket == null)
        {
            throw new InvalidOperationException("Local ASR worker is not connected.");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(LocalAsrProtocol.Serialize(message));

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _webSocket.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null)
        {
            return;
        }

        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    RaiseWorkerDisconnected("WebSocket closed by worker.");
                    return;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            string json = Encoding.UTF8.GetString(message.ToArray());
            try
            {
                var workerEvent = LocalAsrProtocol.ParseWorkerEvent(json);
                WorkerEventReceived?.Invoke(this, new LocalAsrWorkerEventArgs(workerEvent));
            }
            catch (Exception ex)
            {
                Logger.Warn("LocalAsrWorkerClient", $"Failed to parse worker event: {ex.Message}");
            }
        }
        RaiseWorkerDisconnected("Worker receive loop exited.");
    }

    private void RaiseWorkerDisconnected(string message)
    {
        WorkerEventReceived?.Invoke(
            this,
            new LocalAsrWorkerEventArgs(new LocalAsrProtocol.WorkerEvent(
                "error",
                Code: "WORKER_DISCONNECTED",
                Message: message,
                Severity: "error")));
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();

        if (_webSocket is { State: WebSocketState.Open })
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            }
            catch
            {
                _webSocket.Abort();
            }
        }

        try
        {
            if (_receiveTask != null)
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }
        catch
        {
        }

        _webSocket?.Dispose();
        _disposeCts.Dispose();
        _sendLock.Dispose();
    }
}
