using System;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class RollingInitialPromptBuilderTests
{
    [Fact]
    public void BuildPrompt_ForFirstChunk_ReturnsNull()
    {
        var builder = new RollingInitialPromptBuilder();

        Assert.Null(builder.BuildPrompt(Guid.NewGuid(), maxTokens: 180));
    }

    [Fact]
    public void BuildPrompt_UsesOnlyCurrentRecordingTail()
    {
        var builder = new RollingInitialPromptBuilder();
        var firstRecording = Guid.NewGuid();
        var secondRecording = Guid.NewGuid();

        builder.AppendUtterance(firstRecording, "alpha beta gamma delta");
        builder.AppendUtterance(secondRecording, "separate session");

        Assert.Equal("gamma delta", builder.BuildPrompt(firstRecording, maxTokens: 2));
        Assert.Equal("separate session", builder.BuildPrompt(secondRecording, maxTokens: 10));
    }

    [Fact]
    public void Reset_ClearsRecordingPrompt()
    {
        var builder = new RollingInitialPromptBuilder();
        var recordingId = Guid.NewGuid();

        builder.AppendUtterance(recordingId, "AirType OAuth");
        builder.Reset(recordingId);

        Assert.Null(builder.BuildPrompt(recordingId, maxTokens: 180));
    }
}
