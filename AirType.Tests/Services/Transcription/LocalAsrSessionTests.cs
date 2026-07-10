using System;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class LocalAsrSessionTests
{
    [Fact]
    public async Task FinalFlushTimeout_AllowsSmallCt2ToFinishBeyondOldTwoSecondCutoff()
    {
        var recordingId = Guid.NewGuid();
        var client = new FakeLocalAsrWorkerClient();
        client.OnStop = id =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2200));
                client.Emit(new LocalAsrProtocol.WorkerEvent(
                    "recording_complete",
                    RecordingId: id,
                    Text: "local result",
                    UtteranceCount: 1,
                    FinalFlushMilliseconds: 2200,
                    AsrTotalMilliseconds: 2200));
            });
        };
        await using var session = new LocalAsrSession(
            recordingId,
            client,
            LocalAsrOptions.Default,
            new RollingInitialPromptBuilder());

        Assert.True(session.FinalFlushTimeout >= TimeSpan.FromSeconds(15));

        var result = await session.StopAndWaitAsync(session.FinalFlushTimeout, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal("local result", result.Text);
        Assert.Equal(LocalAsrFallbackReason.None, result.FallbackReason);
        Assert.Equal(2200, result.Diagnostics.FinalFlushMilliseconds);
    }

    [Fact]
    public async Task StopAndWaitAsync_WhenWorkerCompletes_ReturnsAssembledUtteranceText()
    {
        var recordingId = Guid.NewGuid();
        var client = new FakeLocalAsrWorkerClient();
        client.OnStop = id =>
        {
            client.Emit(new LocalAsrProtocol.WorkerEvent(
                "utterance_final",
                RecordingId: id,
                Text: "hello world",
                Index: 1,
                StartMilliseconds: 0,
                EndMilliseconds: 1000,
                AsrMilliseconds: 120));
            client.Emit(new LocalAsrProtocol.WorkerEvent(
                "recording_complete",
                RecordingId: id,
                UtteranceCount: 1,
                FinalFlushMilliseconds: 25,
                AsrTotalMilliseconds: 120));
        };
        await using var session = new LocalAsrSession(
            recordingId,
            client,
            LocalAsrOptions.Default,
            new RollingInitialPromptBuilder());

        var result = await session.StopAndWaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal("hello world", result.Text);
        Assert.Single(result.Utterances);
        Assert.Equal(25, result.Diagnostics.FinalFlushMilliseconds);
    }

    [Fact]
    public async Task StopAndWaitAsync_WhenWorkerDoesNotComplete_ReturnsTimeout()
    {
        var recordingId = Guid.NewGuid();
        var client = new FakeLocalAsrWorkerClient();
        await using var session = new LocalAsrSession(
            recordingId,
            client,
            LocalAsrOptions.Default,
            new RollingInitialPromptBuilder());

        var result = await session.StopAndWaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Equal(LocalAsrFallbackReason.LocalFlushTimeout, result.FallbackReason);
    }

    [Fact]
    public async Task StopAndWaitAsync_IgnoresStaleRecordingEvents()
    {
        var recordingId = Guid.NewGuid();
        var staleRecordingId = Guid.NewGuid();
        var client = new FakeLocalAsrWorkerClient();
        client.OnStop = id =>
        {
            client.Emit(new LocalAsrProtocol.WorkerEvent(
                "recording_complete",
                RecordingId: staleRecordingId,
                Text: "wrong session"));
        };
        await using var session = new LocalAsrSession(
            recordingId,
            client,
            LocalAsrOptions.Default,
            new RollingInitialPromptBuilder());

        var result = await session.StopAndWaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Equal(LocalAsrFallbackReason.LocalFlushTimeout, result.FallbackReason);
    }

    private sealed class FakeLocalAsrWorkerClient : ILocalAsrWorkerClient
    {
        public event EventHandler<LocalAsrWorkerEventArgs>? WorkerEventReceived;

        public bool IsConnected => true;

        public Action<Guid>? OnStop { get; set; }

        public void Emit(LocalAsrProtocol.WorkerEvent workerEvent) =>
            WorkerEventReceived?.Invoke(this, new LocalAsrWorkerEventArgs(workerEvent));

        public Task ConnectAsync(Uri endpoint, string authToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadModelAsync(LocalAsrOptions options, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrewarmAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartRecordingAsync(Guid recordingId, LocalAsrOptions options, string? hotwords, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask SendAudioFrameAsync(AudioPcmFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Task StopRecordingAsync(Guid recordingId, long lastSequence, CancellationToken cancellationToken)
        {
            OnStop?.Invoke(recordingId);
            return Task.CompletedTask;
        }

        public Task CancelRecordingAsync(Guid recordingId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
