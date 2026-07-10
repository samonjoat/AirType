using AirType.Services.Audio;
using Xunit;

namespace AirType.Tests.Services.Audio;

public sealed class AudioLevelAnalyzerTests
{
    [Fact]
    public void HasAudioSignal_WhenSampleExceedsThreshold_ReturnsTrue()
    {
        var analyzer = new AudioLevelAnalyzer();
        byte[] buffer = CreatePcm16Buffer(0, 101, 0);

        Assert.True(analyzer.HasAudioSignal(buffer, buffer.Length));
    }

    [Fact]
    public void HasAudioSignal_WhenSamplesStayBelowThreshold_ReturnsFalse()
    {
        var analyzer = new AudioLevelAnalyzer();
        byte[] buffer = CreatePcm16Buffer(0, 100, -100);

        Assert.False(analyzer.HasAudioSignal(buffer, buffer.Length));
    }

    [Fact]
    public void ProcessBuffer_ReturnsNormalizedAmplitudeAndTracksSampleCount()
    {
        var analyzer = new AudioLevelAnalyzer();
        byte[] buffer = CreatePcm16Buffer(0, 16384);

        double amplitude = analyzer.ProcessBuffer(buffer, buffer.Length);

        Assert.InRange(amplitude, 0.49, 0.51);
        Assert.Equal(2, analyzer.TotalSamplesProcessed);
    }

    private static byte[] CreatePcm16Buffer(params short[] samples)
    {
        var buffer = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            buffer[i * 2] = (byte)(samples[i] & 0xFF);
            buffer[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }

        return buffer;
    }
}

