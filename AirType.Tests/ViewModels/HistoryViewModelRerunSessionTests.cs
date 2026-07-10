using AirType.Models;
using AirType.ViewModels;
using NAudio.Wave;
using Xunit;

namespace AirType.Tests.ViewModels;

public sealed class HistoryViewModelRerunSessionTests
{
    [Fact]
    public void CreateRerunRecordingSession_UsesStoredAudioDuration()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = new DateTime(2026, 7, 8, 9, 30, 0, DateTimeKind.Unspecified),
            AudioDuration = TimeSpan.FromSeconds(12.345),
            AudioFilePath = Path.Combine(Path.GetTempPath(), "missing-airtype-rerun-audio.wav")
        };

        RecordingSession session = HistoryViewModel.CreateRerunRecordingSession(entry);

        Assert.Equal(entry.Id, session.Id);
        Assert.Equal(RecordingStatus.Completed, session.Status);
        Assert.Equal(RecordingMode.Unattended, session.Mode);
        Assert.Equal(entry.AudioDuration, session.Duration);
    }

    [Fact]
    public void CreateRerunRecordingSession_WhenStoredDurationIsZero_UsesWavDuration()
    {
        string audioPath = CreateSilentWav(TimeSpan.FromMilliseconds(750));
        try
        {
            var entry = new TranscriptionHistoryEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = new DateTime(2026, 7, 8, 9, 30, 0, DateTimeKind.Unspecified),
                AudioDuration = TimeSpan.Zero,
                AudioFilePath = audioPath
            };

            RecordingSession session = HistoryViewModel.CreateRerunRecordingSession(entry);

            Assert.InRange(session.Duration.TotalMilliseconds, 740, 760);
        }
        finally
        {
            File.Delete(audioPath);
        }
    }

    [Fact]
    public void CreateRerunRecordingSession_MarksHistoryTimestampAsLocalToAvoidWorkflowShift()
    {
        var timestamp = new DateTime(2026, 7, 8, 9, 30, 0, DateTimeKind.Unspecified);
        var entry = new TranscriptionHistoryEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            AudioDuration = TimeSpan.FromSeconds(2)
        };

        RecordingSession session = HistoryViewModel.CreateRerunRecordingSession(entry);

        Assert.Equal(DateTimeKind.Local, session.StartTime.Kind);
        Assert.Equal(DateTime.SpecifyKind(timestamp, DateTimeKind.Local), session.StartTime);
        Assert.Equal(session.StartTime, session.StartTime.ToLocalTime());
    }

    private static string CreateSilentWav(TimeSpan duration)
    {
        string directory = Path.Combine(Path.GetTempPath(), "AirType.Tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.wav");

        var format = new WaveFormat(16000, 16, 1);
        int byteCount = (int)(format.AverageBytesPerSecond * duration.TotalSeconds);
        var audioBytes = new byte[byteCount];

        using (var writer = new WaveFileWriter(path, format))
        {
            writer.Write(audioBytes, 0, audioBytes.Length);
        }

        return path;
    }
}
