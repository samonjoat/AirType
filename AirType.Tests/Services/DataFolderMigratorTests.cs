using System;
using System.IO;
using System.Linq;
using System.Data.SQLite;
using AirType.Services;
using Xunit;

namespace AirType.Tests.Services;

public class DataFolderMigratorTests
{
    private const string CompletionMarker = ".airtype-migration-complete";

    [Fact]
    public void Run_without_sources_does_not_create_marker_or_target()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.NoSource, result.Status);
        Assert.False(Directory.Exists(paths.CanonicalRoot));
        Assert.False(File.Exists(Path.Combine(paths.CanonicalRoot, CompletionMarker)));
    }

    [Fact]
    public void Run_adopts_existing_canonical_root_when_no_sources_exist()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);
        string existingFile = Path.Combine(paths.CanonicalRoot, "theme.txt");

        Directory.CreateDirectory(paths.CanonicalRoot);
        File.WriteAllText(existingFile, "dark");

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.ExistingTargetAdopted, result.Status);
        string markerPath = Path.Combine(paths.CanonicalRoot, CompletionMarker);
        Assert.True(File.Exists(markerPath));
        Assert.Contains(MigrationStatus.ExistingTargetAdopted.ToString(), File.ReadAllText(markerPath));
        Assert.True(File.Exists(existingFile));
    }

    [Fact]
    public void Run_merges_legacy_and_wrong_root_sources_into_canonical_root()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        CreateHistoryDatabase(Path.Combine(paths.LegacyLocalRoot, "dictation.db"), rowCount: 1);
        WriteSourceFile(paths.LegacyRoamingRoot, "app_settings.json", "legacy-roaming-settings");
        WriteSourceFile(paths.RoamingCompatibilityRoot, Path.Combine("Logs", "App", "App_20260417.log"), "roaming-airtype-log");

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.Completed, result.Status);
        Assert.True(File.Exists(Path.Combine(paths.CanonicalRoot, "dictation.db")));
        Assert.True(File.Exists(Path.Combine(paths.CanonicalRoot, "app_settings.json")));
        Assert.True(File.Exists(Path.Combine(paths.CanonicalRoot, "Logs", "App", "App_20260417.log")));

        string markerPath = Path.Combine(paths.CanonicalRoot, CompletionMarker);
        Assert.True(File.Exists(markerPath));
        Assert.Contains(MigrationStatus.Completed.ToString(), File.ReadAllText(markerPath));
    }

    [Fact]
    public void Run_repairs_empty_canonical_db_from_populated_roaming_source_when_marker_exists()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        Directory.CreateDirectory(paths.CanonicalRoot);
        File.WriteAllText(Path.Combine(paths.CanonicalRoot, CompletionMarker), "Completed");
        CreateHistoryDatabase(Path.Combine(paths.CanonicalRoot, "dictation.db"), rowCount: 0);
        CreateHistoryDatabase(Path.Combine(paths.RoamingCompatibilityRoot, "dictation.db"), rowCount: 3);

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.RepairedCanonicalDatabase, result.Status);
        Assert.Equal(3, GetHistoryRowCount(Path.Combine(paths.CanonicalRoot, "dictation.db")));
        Assert.Contains(MigrationStatus.RepairedCanonicalDatabase.ToString(), File.ReadAllText(Path.Combine(paths.CanonicalRoot, CompletionMarker)));
    }

    [Fact]
    public void Run_prefers_database_with_more_history_rows_during_initial_merge()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        CreateHistoryDatabase(Path.Combine(paths.LegacyLocalRoot, "dictation.db"), rowCount: 10);
        CreateHistoryDatabase(Path.Combine(paths.RoamingCompatibilityRoot, "dictation.db"), rowCount: 5);

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.Completed, result.Status);
        Assert.Equal(10, GetHistoryRowCount(Path.Combine(paths.CanonicalRoot, "dictation.db")));
    }

    [Fact]
    public void Run_prefers_newer_database_when_sources_have_same_row_count()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        CreateHistoryDatabase(
            Path.Combine(paths.LegacyRoamingRoot, "dictation.db"),
            rowCount: 4,
            baseTimestampUtc: new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc));
        CreateHistoryDatabase(
            Path.Combine(paths.LegacyLocalRoot, "dictation.db"),
            rowCount: 4,
            baseTimestampUtc: new DateTime(2026, 4, 17, 12, 0, 0, DateTimeKind.Utc));

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.Completed, result.Status);
        Assert.Equal(
            new DateTime(2026, 4, 17, 12, 0, 0, DateTimeKind.Utc),
            GetLatestHistoryTimestamp(Path.Combine(paths.CanonicalRoot, "dictation.db")).ToUniversalTime());
    }

    [Fact]
    public void Run_ignores_backup_only_source_folders()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        WriteSourceFile(paths.RoamingCompatibilityRoot, "dictation.db.backup", "backup-only");

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.NoSource, result.Status);
        Assert.False(Directory.Exists(paths.CanonicalRoot));
    }

    [Fact]
    public void Run_repairs_stale_non_empty_canonical_db_when_preferred_source_is_newer_and_has_more_rows()
    {
        using var tempDirectory = new TempDirectory();
        var paths = CreatePaths(tempDirectory.RootPath);

        Directory.CreateDirectory(paths.CanonicalRoot);
        File.WriteAllText(Path.Combine(paths.CanonicalRoot, CompletionMarker), "Completed");
        CreateHistoryDatabase(Path.Combine(paths.CanonicalRoot, "dictation.db"), rowCount: 2, baseTimestampUtc: new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));
        CreateHistoryDatabase(Path.Combine(paths.RoamingCompatibilityRoot, "dictation.db"), rowCount: 5, baseTimestampUtc: new DateTime(2026, 4, 16, 18, 33, 31, DateTimeKind.Utc));

        MigrationResult result = DataFolderMigrator.Run(paths).Single();

        Assert.Equal(MigrationStatus.RepairedCanonicalDatabase, result.Status);
        Assert.Equal(5, GetHistoryRowCount(Path.Combine(paths.CanonicalRoot, "dictation.db")));
    }

    private static MigrationPaths CreatePaths(string rootPath)
    {
        return new MigrationPaths(
            Path.Combine(rootPath, "local", AirTypeStoragePaths.AppFolderName),
            Path.Combine(rootPath, "local", AirTypeStoragePaths.LegacyAppFolderName),
            Path.Combine(rootPath, "roaming", AirTypeStoragePaths.LegacyAppFolderName),
            Path.Combine(rootPath, "roaming", AirTypeStoragePaths.AppFolderName));
    }

    private static void WriteSourceFile(string rootPath, string relativePath, string content)
    {
        string filePath = Path.Combine(rootPath, relativePath);
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content);
    }

    private static void CreateHistoryDatabase(string dbPath, int rowCount, DateTime? baseTimestampUtc = null)
    {
        string? directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SQLiteConnection.CreateFile(dbPath);
        using var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        connection.Open();

        using (var create = new SQLiteCommand("CREATE TABLE TranscriptionHistory (Id TEXT PRIMARY KEY, Timestamp DATETIME NOT NULL);", connection))
        {
            create.ExecuteNonQuery();
        }

        DateTime seedTimestamp = baseTimestampUtc ?? DateTime.UtcNow;

        for (int i = 0; i < rowCount; i++)
        {
            using var insert = new SQLiteCommand("INSERT INTO TranscriptionHistory (Id, Timestamp) VALUES (@Id, @Timestamp);", connection);
            insert.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@Timestamp", seedTimestamp.AddMinutes(-i));
            insert.ExecuteNonQuery();
        }
    }

    private static int GetHistoryRowCount(string dbPath)
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand("SELECT COUNT(*) FROM TranscriptionHistory;", connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static DateTime GetLatestHistoryTimestamp(string dbPath)
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand("SELECT MAX(Timestamp) FROM TranscriptionHistory;", connection);
        return Convert.ToDateTime(command.ExecuteScalar());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"AirTypeTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
