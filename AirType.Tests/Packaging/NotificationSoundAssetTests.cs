using System.Security.Cryptography;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class NotificationSoundAssetTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Theory]
    [InlineData("start.wav", 151, "7d8b5a74ee54a7a5d2b93547ad6682de3bf2cd2a002208d7bea24525ec94eec0")]
    [InlineData("done.wav", 190, "b55bf39e4ea7aa0a0eea6c359a6deca04fdea8d814f6ecf70c834aee92a8621e")]
    [InlineData("cancel.wav", 198, "7bfa7b456e3213a76a2a8775f07c58565811b0faba93be20d7f678e3e8f68fae")]
    public void ApprovedSound_HasExpectedPcmContractAndHash(
        string fileName,
        int expectedDurationMilliseconds,
        string expectedHash)
    {
        string path = Path.Combine(RepoRoot, "AirType", "Sounds", fileName);
        byte[] bytes = File.ReadAllBytes(path);

        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

        WaveContract contract = ReadWaveContract(bytes);
        Assert.Equal(1, contract.AudioFormat);
        Assert.Equal(1, contract.Channels);
        Assert.Equal(44_100, contract.SampleRate);
        Assert.Equal(16, contract.BitsPerSample);
        Assert.Equal(expectedDurationMilliseconds, contract.DurationMilliseconds);
    }

    [Fact]
    public void SoundProvenance_RecordsFirstPartyGeneratorAndVerificationCommand()
    {
        string provenance = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType", "Sounds", "PROVENANCE.md"));

        Assert.Contains("first-party generated assets", provenance);
        Assert.Contains("generate-notification-sound-candidates.py --verify-production", provenance);
    }

    private static WaveContract ReadWaveContract(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        Assert.Equal("RIFF", new string(reader.ReadChars(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", new string(reader.ReadChars(4)));

        ushort? audioFormat = null;
        ushort? channels = null;
        int? sampleRate = null;
        ushort? bitsPerSample = null;
        uint? dataLength = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            uint chunkLength = reader.ReadUInt32();
            long nextChunk = stream.Position + chunkLength + (chunkLength % 2);

            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                dataLength = chunkLength;
            }

            stream.Position = nextChunk;
        }

        Assert.True(audioFormat.HasValue && channels.HasValue && sampleRate.HasValue);
        Assert.True(bitsPerSample.HasValue && dataLength.HasValue);

        int bytesPerSecond = sampleRate.Value * channels.Value * (bitsPerSample.Value / 8);
        int durationMilliseconds = (int)Math.Round(dataLength.Value * 1_000d / bytesPerSecond);
        return new WaveContract(
            audioFormat.Value,
            channels.Value,
            sampleRate.Value,
            bitsPerSample.Value,
            durationMilliseconds);
    }

    private sealed record WaveContract(
        ushort AudioFormat,
        ushort Channels,
        int SampleRate,
        ushort BitsPerSample,
        int DurationMilliseconds);
}
