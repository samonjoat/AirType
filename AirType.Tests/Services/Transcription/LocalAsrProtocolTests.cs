using System.Text;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class LocalAsrProtocolTests
{
    [Fact]
    public void BuildPcmFrame_UsesWorkerHeaderContract()
    {
        byte[] payload = { 1, 0, 2, 0 };

        byte[] frame = LocalAsrProtocol.BuildPcmFrame(sequence: 7, firstSample: 800, payload);

        Assert.Equal((byte)'A', frame[0]);
        Assert.Equal((byte)'T', frame[1]);
        Assert.Equal((byte)'P', frame[2]);
        Assert.Equal((byte)'C', frame[3]);
        Assert.Equal(1, frame[4]);
        Assert.Equal(24, frame[5]);
        Assert.Equal(payload, frame.Skip(LocalAsrProtocol.PcmHeaderLength).ToArray());
    }

    [Fact]
    public void ParseWorkerEvent_ForMetrics_CapturesHotwordTokenCount()
    {
        var json = """
            {"type":"metrics","protocolVersion":1,"hotwordTokenCount":42,"utterancesFinal":3}
            """;

        var workerEvent = LocalAsrProtocol.ParseWorkerEvent(json);

        Assert.Equal("metrics", workerEvent.Type);
        Assert.Equal(42, workerEvent.HotwordTokenCount);
        Assert.Equal(3, workerEvent.UtteranceCount);
    }

    [Fact]
    public void Serialize_UsesCamelCaseProperties()
    {
        string json = LocalAsrProtocol.Serialize(new { Type = "hello", ProtocolVersion = 1 });

        Assert.Contains("\"type\"", json);
        Assert.Contains("\"protocolVersion\"", json);
        Assert.DoesNotContain("Type", json);
    }
}
