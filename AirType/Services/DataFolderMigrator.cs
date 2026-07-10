using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AirType.Services;

public enum MigrationStatus
{
    AlreadyMigrated,
    ExistingTargetAdopted,
    NoSource,
    Completed,
    RepairedCanonicalDatabase,
    FailedCopyError
}

public readonly record struct MigrationResult(
    Environment.SpecialFolder Scope,
    MigrationStatus Status,
    string? Detail = null)
{
    public bool IsFailure => Status == MigrationStatus.FailedCopyError;
}

/// <summary>
public readonly record struct MigrationPaths(
    string CanonicalRoot,
    string LegacyLocalRoot,
    string LegacyRoamingRoot,
    string RoamingCompatibilityRoot);

/// <summary>
/// One-time reconciliation of legacy <c>ModernDictationApp</c> folders and any
/// pre-existing roaming <c>AirType</c> folder into the canonical
/// <c>%LOCALAPPDATA%\AirType</c> root. Must run before any service that reads
/// or writes user data. Uses Console/Debug output only — Logger would create a
/// new folder before migration can decide what to do with it.
/// </summary>
public static class DataFolderMigrator
{
    private const string CompletionMarker = ".airtype-migration-complete";
    private const string DatabaseFileName = "dictation.db";
    private const string DatabaseBackupFileName = "dictation.db.backup";

    private readonly record struct DatabaseProbe(
        string Path,
        bool Exists,
        int RowCount,
        DateTime? LatestTimestamp);

    public static MigrationPaths GetDefaultPaths()
    {
        return new MigrationPaths(
            AirTypeStoragePaths.CanonicalRoot,
            AirTypeStoragePaths.LegacyCanonicalRoot,
            AirTypeStoragePaths.LegacyRoamingRoot,
            AirTypeStoragePaths.RoamingCompatibilityRoot);
    }

    public static IReadOnlyList<MigrationResult> Run()
    {
        return Run(GetDefaultPaths());
    }

    public static IReadOnlyList<MigrationResult> Run(MigrationPaths paths)
    {
        return new[] { MigrateCanonicalRoot(paths) };
    }

    private static MigrationResult MigrateCanonicalRoot(MigrationPaths paths)
    {
        const Environment.SpecialFolder canonicalScope = Environment.SpecialFolder.LocalApplicationData;

        string markerPath = Path.Combine(paths.CanonicalRoot, CompletionMarker);
        bool canonicalExists = Directory.Exists(paths.CanonicalRoot);
        bool markerExists = canonicalExists && File.Exists(markerPath);

        string[] sourceRoots = GetSourceRoots(paths)
            .Where(HasMigratableContent)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DatabaseProbe canonicalDatabase = ProbeHistoryDatabase(paths.CanonicalRoot);
        DatabaseProbe? bestSourceDatabase = GetPreferredSourceDatabase(paths);

        if (markerExists)
        {
            if (NeedsCanonicalDatabaseRepair(canonicalDatabase, bestSourceDatabase))
            {
                try
                {
                    Directory.CreateDirectory(paths.CanonicalRoot);
                    RepairCanonicalDatabase(paths.CanonicalRoot, bestSourceDatabase!.Value);
                    WriteMarker(markerPath, MigrationStatus.RepairedCanonicalDatabase);
                    Trace(canonicalScope, $"Repaired canonical history database from '{bestSourceDatabase.Value.Path}'.");
                    return new MigrationResult(canonicalScope, MigrationStatus.RepairedCanonicalDatabase, bestSourceDatabase.Value.Path);
                }
                catch (Exception ex)
                {
                    string msg = $"Database repair failed for '{paths.CanonicalRoot}': {ex.Message}";
                    Trace(canonicalScope, msg);
                    return new MigrationResult(canonicalScope, MigrationStatus.FailedCopyError, msg);
                }
            }

            return new MigrationResult(canonicalScope, MigrationStatus.AlreadyMigrated);
        }

        bool canonicalHasContent = HasMigratableContent(paths.CanonicalRoot);

        if (sourceRoots.Length == 0)
        {
            if (!canonicalHasContent)
            {
                return new MigrationResult(canonicalScope, MigrationStatus.NoSource);
            }

            Directory.CreateDirectory(paths.CanonicalRoot);
            WriteMarker(markerPath, MigrationStatus.ExistingTargetAdopted);
            Trace(canonicalScope, $"Adopted existing canonical AirType folder at '{paths.CanonicalRoot}'.");
            return new MigrationResult(canonicalScope, MigrationStatus.ExistingTargetAdopted);
        }

        try
        {
            Directory.CreateDirectory(paths.CanonicalRoot);

            foreach (string sourceRoot in sourceRoots)
            {
                CopyDirectory(sourceRoot, paths.CanonicalRoot);
                Trace(canonicalScope, $"Merged '{sourceRoot}' -> '{paths.CanonicalRoot}'.");
            }

            if (bestSourceDatabase is { Exists: true, RowCount: > 0 })
            {
                RepairCanonicalDatabase(paths.CanonicalRoot, bestSourceDatabase!.Value);
                Trace(canonicalScope, $"Applied preferred canonical history database from '{bestSourceDatabase.Value.Path}' after merge.");
            }

            WriteMarker(markerPath, MigrationStatus.Completed);
            return new MigrationResult(canonicalScope, MigrationStatus.Completed);
        }
        catch (Exception ex)
        {
            string msg = $"Copy failed while reconciling into '{paths.CanonicalRoot}': {ex.Message}";
            Trace(canonicalScope, msg);
            return new MigrationResult(canonicalScope, MigrationStatus.FailedCopyError, msg);
        }
    }

    private static IEnumerable<string> GetSourceRoots(MigrationPaths paths)
    {
        yield return paths.LegacyLocalRoot;
        yield return paths.LegacyRoamingRoot;
        yield return paths.RoamingCompatibilityRoot;
    }

    private static bool HasMigratableContent(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(directory)
                .Any(path => IsMigratableArtifact(Path.GetRelativePath(directory, path)));
        }
        catch
        {
            return true;
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, sourceFile);
            if (IsDatabaseArtifact(relative))
            {
                continue;
            }

            string destFile = Path.Combine(destDir, relative);
            string? destFileDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destFileDir))
            {
                Directory.CreateDirectory(destFileDir);
            }

            if (!File.Exists(destFile))
            {
                File.Copy(sourceFile, destFile, overwrite: false);
            }
        }
    }

    private static bool NeedsCanonicalDatabaseRepair(DatabaseProbe canonicalDatabase, DatabaseProbe? bestSourceDatabase)
    {
        if (bestSourceDatabase is not { Exists: true, RowCount: > 0 } source)
        {
            return false;
        }

        if (!canonicalDatabase.Exists || canonicalDatabase.RowCount == 0)
        {
            return true;
        }

        return source.RowCount > canonicalDatabase.RowCount
            && (source.LatestTimestamp ?? DateTime.MinValue) > (canonicalDatabase.LatestTimestamp ?? DateTime.MinValue);
    }

    private static DatabaseProbe? GetPreferredSourceDatabase(MigrationPaths paths)
    {
        return new[]
            {
                ProbeHistoryDatabase(paths.RoamingCompatibilityRoot),
                ProbeHistoryDatabase(paths.LegacyRoamingRoot),
                ProbeHistoryDatabase(paths.LegacyLocalRoot)
            }
            .Where(probe => probe.Exists && probe.RowCount > 0)
            .OrderByDescending(probe => probe.RowCount)
            .ThenByDescending(probe => probe.LatestTimestamp ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    private static bool IsDatabaseArtifact(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        return string.Equals(fileName, DatabaseFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, DatabaseBackupFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMigratableArtifact(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (string.Equals(fileName, CompletionMarker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(fileName, DatabaseBackupFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static DatabaseProbe ProbeHistoryDatabase(string rootPath)
    {
        string dbPath = Path.Combine(rootPath, DatabaseFileName);
        if (!File.Exists(dbPath))
        {
            return new DatabaseProbe(dbPath, false, 0, null);
        }

        try
        {
            using var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            connection.Open();

            using var tableCommand = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='TranscriptionHistory';", connection);
            bool hasHistoryTable = tableCommand.ExecuteScalar() != null;
            if (!hasHistoryTable)
            {
                return new DatabaseProbe(dbPath, true, 0, null);
            }

            using var countCommand = new SQLiteCommand("SELECT COUNT(*) FROM TranscriptionHistory;", connection);
            int rowCount = Convert.ToInt32(countCommand.ExecuteScalar());

            using var latestCommand = new SQLiteCommand("SELECT MAX(Timestamp) FROM TranscriptionHistory;", connection);
            object? latestValue = latestCommand.ExecuteScalar();
            DateTime? latestTimestamp = latestValue is DBNull or null
                ? null
                : Convert.ToDateTime(latestValue);

            return new DatabaseProbe(dbPath, true, rowCount, latestTimestamp);
        }
        catch
        {
            return new DatabaseProbe(dbPath, true, 0, null);
        }
    }

    private static void RepairCanonicalDatabase(string canonicalRoot, DatabaseProbe sourceDatabase)
    {
        string targetDatabase = Path.Combine(canonicalRoot, DatabaseFileName);
        File.Copy(sourceDatabase.Path, targetDatabase, overwrite: true);

        string sourceBackup = Path.Combine(Path.GetDirectoryName(sourceDatabase.Path)!, DatabaseBackupFileName);
        string targetBackup = Path.Combine(canonicalRoot, DatabaseBackupFileName);
        if (File.Exists(sourceBackup))
        {
            File.Copy(sourceBackup, targetBackup, overwrite: true);
        }
    }

    private static void WriteMarker(string markerPath, MigrationStatus status)
    {
        string content = $"{status}\n{DateTime.UtcNow:O}\n";
        File.WriteAllText(markerPath, content);
    }

    private static void Trace(Environment.SpecialFolder scope, string message)
    {
        string line = $"[DataFolderMigrator:{scope}] {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);
    }
}
