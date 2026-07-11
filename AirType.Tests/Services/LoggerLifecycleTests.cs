using AirType.Services;
using Xunit;

namespace AirType.Tests.Services;

public sealed class LoggerLifecycleTests
{
    [Fact]
    public void DailyLogPath_FollowsTheSuppliedCalendarDate()
    {
        string directory = Path.Combine("root", "logs");

        string beforeMidnight = Logger.GetDailyLogFilePath(
            directory,
            new DateTime(2026, 7, 11, 23, 59, 59));
        string afterMidnight = Logger.GetDailyLogFilePath(
            directory,
            new DateTime(2026, 7, 12, 0, 0, 0));

        Assert.Equal(Path.Combine(directory, "App_20260711.log"), beforeMidnight);
        Assert.Equal(Path.Combine(directory, "App_20260712.log"), afterMidnight);
    }

    [Fact]
    public void RotatedLogPath_UsesAppPrefixAndAvoidsCollisions()
    {
        using var tempDirectory = new TempDirectory();
        var timestamp = new DateTime(2026, 7, 11, 14, 23, 45, 678);
        string expectedFirst = Path.Combine(
            tempDirectory.RootPath,
            "App_20260711_142345_678.log");
        File.WriteAllText(expectedFirst, "existing");

        string result = Logger.GetUniqueRotatedLogFilePath(tempDirectory.RootPath, timestamp);

        Assert.Equal(
            Path.Combine(tempDirectory.RootPath, "App_20260711_142345_678_1.log"),
            result);
    }

    [Fact]
    public void Cleanup_PrunesCurrentAndLegacyHistoryButPreservesActiveAndUnrelatedFiles()
    {
        using var tempDirectory = new TempDirectory();
        string active = CreateLog(tempDirectory.RootPath, "App_20260711.log", minutesAgo: 10);
        string newestCurrent = CreateLog(
            tempDirectory.RootPath,
            "App_20260710_120000_000.log",
            minutesAgo: 1);
        string newestLegacy = CreateLog(
            tempDirectory.RootPath,
            "DictationApp_20260710_110000.log",
            minutesAgo: 2);
        string oldCurrent = CreateLog(
            tempDirectory.RootPath,
            "App_20260709.log",
            minutesAgo: 3);
        string oldLegacy = CreateLog(
            tempDirectory.RootPath,
            "DictationApp_20260708_100000.log",
            minutesAgo: 4);
        string unrelated = CreateLog(tempDirectory.RootPath, "external.log", minutesAgo: 5);

        int deleted = Logger.CleanupOldLogFiles(
            tempDirectory.RootPath,
            active,
            maxHistoricalFiles: 2);

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(active));
        Assert.True(File.Exists(newestCurrent));
        Assert.True(File.Exists(newestLegacy));
        Assert.False(File.Exists(oldCurrent));
        Assert.False(File.Exists(oldLegacy));
        Assert.True(File.Exists(unrelated));
    }

    private static string CreateLog(string directory, string fileName, int minutesAgo)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, fileName);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"AirType.LoggerTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
