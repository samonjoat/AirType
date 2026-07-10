using System;
using AirType.Services.Audio;
using Xunit;

namespace AirType.Tests.Services.Audio;

public sealed class AppSoundPolicyTests
{
    [Fact]
    public void Play_WhenRecordingStarted_ForwardsStartCueAndMuteCallback()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);
        var muteCallbackRan = false;

        sut.Play(AppSoundRequest.CreateRecordingStarted(() => muteCallbackRan = true));

        Assert.Equal(1, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
        Assert.NotNull(transport.LastStartCallback);

        transport.LastStartCallback!.Invoke();

        Assert.True(muteCallbackRan);
    }

    [Fact]
    public void Play_WhenRecordingStartedWithoutCallback_ForwardsStartCueWithoutCallbackOverload()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        sut.Play(AppSoundRequest.Create(AppSoundState.RecordingStarted));

        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(1, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
        Assert.Null(transport.LastStartCallback);
    }

    [Theory]
    [InlineData(AppSoundState.RecordingCanceledByUser)]
    [InlineData(AppSoundState.WorkflowCanceled)]
    [InlineData(AppSoundState.WorkflowFailed)]
    public void Play_WhenStateMapsToCancel_ForwardsCancelCue(AppSoundState state)
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        sut.Play(AppSoundRequest.Create(state));

        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(1, transport.PlayCancelCallCount);
    }

    [Fact]
    public void Play_WhenInjectionSucceededWithoutFallbackOrSkippedVerification_ForwardsDoneCue()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        sut.Play(AppSoundRequest.CreateInjectionSucceeded(injectionVerified: true, clipboardFallbackUsed: false));

        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(1, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
    }

    [Fact]
    public void Play_WhenStateIsSilent_DoesNotCallTransport()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        sut.Play(AppSoundRequest.Create(AppSoundState.Silent));

        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
    }

    [Fact]
    public void Play_WhenInjectionSucceededButClipboardFallbackWasUsed_StaysSilent()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        sut.Play(AppSoundRequest.CreateInjectionSucceeded(injectionVerified: true, clipboardFallbackUsed: true));

        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
    }

    [Fact]
    public void Play_WhenInjectionSucceededButVerificationWasSkipped_StaysSilent()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        sut.Play(AppSoundRequest.CreateInjectionSucceeded(injectionVerified: false, clipboardFallbackUsed: false));

        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
    }

    [Fact]
    public void Create_WhenStateIsOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => AppSoundRequest.Create((AppSoundState)999));

        Assert.Equal("state", exception.ParamName);
    }

    [Fact]
    public void Play_WhenRequestIsDefault_ThrowsArgumentException()
    {
        var transport = new FakeNotificationSoundService();
        IAppSoundPolicy sut = new AppSoundPolicy(transport);

        var exception = Assert.Throws<ArgumentException>(() => sut.Play(default));

        Assert.Equal("request", exception.ParamName);
        Assert.Equal(0, transport.PlayStartCallCount);
        Assert.Equal(0, transport.PlayStartWithCallbackCallCount);
        Assert.Equal(0, transport.PlayDoneCallCount);
        Assert.Equal(0, transport.PlayCancelCallCount);
    }

    private sealed class FakeNotificationSoundService : INotificationSoundService
    {
        public int PlayStartCallCount { get; private set; }

        public int PlayStartWithCallbackCallCount { get; private set; }

        public int PlayDoneCallCount { get; private set; }

        public int PlayCancelCallCount { get; private set; }

        public Action? LastStartCallback { get; private set; }

        public void PlayStart()
        {
            PlayStartCallCount++;
        }

        public void PlayStart(Action? onCompleted)
        {
            PlayStartWithCallbackCallCount++;
            LastStartCallback = onCompleted;
        }

        public void PlayDone()
        {
            PlayDoneCallCount++;
        }

        public void PlayCancel()
        {
            PlayCancelCallCount++;
        }
    }
}
