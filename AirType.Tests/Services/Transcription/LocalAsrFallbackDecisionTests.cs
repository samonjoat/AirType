using AirType.Models.Transcription;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class LocalAsrFallbackDecisionTests
{
    [Theory]
    [InlineData(LocalAsrFallbackReason.WorkerUnavailable)]
    [InlineData(LocalAsrFallbackReason.WorkerWarming)]
    [InlineData(LocalAsrFallbackReason.WorkerError)]
    [InlineData(LocalAsrFallbackReason.LocalFlushTimeout)]
    [InlineData(LocalAsrFallbackReason.ModelMissing)]
    [InlineData(LocalAsrFallbackReason.EmptyResult)]
    [InlineData(LocalAsrFallbackReason.SessionRejected)]
    public void ShouldFallback_WhenCloudFallbackConfigured_AllowsRecoverableLocalFailures(LocalAsrFallbackReason reason)
    {
        Assert.True(LocalAsrFallbackDecision.ShouldFallback(reason, cloudFallbackConfigured: true));
    }

    [Fact]
    public void ShouldFallback_WhenReasonIsNone_NeverFallsBack()
    {
        Assert.False(LocalAsrFallbackDecision.ShouldFallback(LocalAsrFallbackReason.None, cloudFallbackConfigured: true));
    }

    [Theory]
    [InlineData(LocalAsrFallbackReason.WorkerUnavailable)]
    [InlineData(LocalAsrFallbackReason.LocalFlushTimeout)]
    [InlineData(LocalAsrFallbackReason.EmptyResult)]
    public void ShouldFallback_WhenCloudFallbackIsNotConfigured_DoesNotFallback(LocalAsrFallbackReason reason)
    {
        Assert.False(LocalAsrFallbackDecision.ShouldFallback(reason, cloudFallbackConfigured: false));
    }
}
