using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using AirType.Services.Prompts;

namespace AirType.Services.Database
{
    public static class DatabaseInitializer
    {
        private sealed record ColumnDefinition(string Name, string Definition);

        public static readonly string DbPath = Path.Combine(
            AirTypeStoragePaths.CanonicalRoot,
            "dictation.db");

        public static readonly string BackupPath = DbPath + ".backup";

        public static string ConnectionString => $"Data Source={DbPath};Version=3;";

        private static void EnsureDatabaseDirectoryExists()
        {
            string? directory = Path.GetDirectoryName(DbPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"Could not determine database directory for path '{DbPath}'.");
            }

            Directory.CreateDirectory(directory);
        }

        private const string CreateTranscriptionHistoryTableSql = @"
                    CREATE TABLE IF NOT EXISTS TranscriptionHistory (
                        Id GUID PRIMARY KEY,
                        Timestamp DATETIME NOT NULL,
                        TranscribedText TEXT,
                        OriginalTranscribedText TEXT,
                        EditedAt DATETIME,
                        WordCount INTEGER,
                        Provider TEXT,
                        ModelUsed TEXT,
                        AudioFilePath TEXT,
                        AudioDuration DOUBLE DEFAULT 0,
                        LatencySeconds DOUBLE DEFAULT 0,
                        HasShownDictionaryPopup INTEGER DEFAULT 0,
                        SessionType TEXT DEFAULT 'Hotkey',
                        RawTranscribedText TEXT,
                        ProviderChain TEXT,
                        RawProvider TEXT,
                        RawModel TEXT,
                        CleanupProvider TEXT,
                        CleanupModel TEXT,
                        CleanupStatus TEXT,
                        AsrLatencySeconds DOUBLE,
                        CleanupLatencySeconds DOUBLE,
                        TotalLatencySeconds DOUBLE,
                        LocalFlushWaitMs INTEGER,
                        FallbackReason TEXT,
                        WorkerSessionId TEXT,
                        WorkerUtteranceCount INTEGER,
                        HotwordTokenCount INTEGER,
                        InitialPromptMaxTokenCount INTEGER,
                        RequestedProvider TEXT,
                        RequestedModel TEXT,
                        ActualProvider TEXT,
                        ActualModel TEXT,
                        WorkflowStatus TEXT,
                        WorkflowError TEXT,
                        TranscriptDisplayMode TEXT DEFAULT 'Cleaner'
                    );";

        private static readonly ColumnDefinition[] TranscriptionHistoryMigratedColumns =
        {
            new("ModelUsed", "TEXT"),
            new("AudioDuration", "DOUBLE DEFAULT 0"),
            new("LatencySeconds", "DOUBLE DEFAULT 0"),
            new("HasShownDictionaryPopup", "INTEGER DEFAULT 0"),
            new("SessionType", "TEXT DEFAULT 'Hotkey'"),
            new("RawTranscribedText", "TEXT"),
            new("ProviderChain", "TEXT"),
            new("RawProvider", "TEXT"),
            new("RawModel", "TEXT"),
            new("CleanupProvider", "TEXT"),
            new("CleanupModel", "TEXT"),
            new("CleanupStatus", "TEXT"),
            new("AsrLatencySeconds", "DOUBLE"),
            new("CleanupLatencySeconds", "DOUBLE"),
            new("TotalLatencySeconds", "DOUBLE"),
            new("LocalFlushWaitMs", "INTEGER"),
            new("FallbackReason", "TEXT"),
            new("WorkerSessionId", "TEXT"),
            new("WorkerUtteranceCount", "INTEGER"),
            new("HotwordTokenCount", "INTEGER"),
            new("InitialPromptMaxTokenCount", "INTEGER"),
            new("RequestedProvider", "TEXT"),
            new("RequestedModel", "TEXT"),
            new("ActualProvider", "TEXT"),
            new("ActualModel", "TEXT"),
            new("WorkflowStatus", "TEXT"),
            new("WorkflowError", "TEXT"),
            new("TranscriptDisplayMode", "TEXT DEFAULT 'Cleaner'")
        };

        /// <summary>
        /// Whether the main database file exists.
        /// </summary>
        public static bool DatabaseExists => File.Exists(DbPath);

        /// <summary>
        /// Whether a backup file exists.
        /// </summary>
        public static bool BackupExists => File.Exists(BackupPath);

        /// <summary>
        /// Gets the last write time of the latest backup file.
        /// </summary>
        public static DateTime? GetLatestBackupTime()
        {
            if (File.Exists(BackupPath))
            {
                return File.GetLastWriteTime(BackupPath);
            }
            return null;
        }

        /// <summary>
        /// Verifies the integrity of the database using SQLite's PRAGMA integrity_check.
        /// </summary>
        public static bool VerifyIntegrity()
        {
            if (!File.Exists(DbPath)) return true;

            try
            {
                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand("PRAGMA integrity_check;", connection))
                    {
                        var result = command.ExecuteScalar() as string;
                        return result == "ok";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Integrity Check Failed: {ex.Message}");
                // If we can't open it to check it, it's definitely not healthy for backup
                return false;
            }
        }

        /// <summary>
        /// Creates a backup of the database. Called on app exit.
        /// Implements rolling backups (keeps last 5).
        /// </summary>
        public static void CreateBackup()
        {
            if (!File.Exists(DbPath)) return;

            // 1. Verify integrity before backing up
            // We don't want to replace a good backup with a corrupted one.
            if (!VerifyIntegrity())
            {
                System.Diagnostics.Debug.WriteLine("[Backup] Aborted: Database integrity check failed.");
                return;
            }

            try 
            {
                SQLiteConnection.ClearAllPools();

                // 2. Rotate existing backups
                // Strategy: 
                // .backup -> .backup.1
                // .backup.1 -> .backup.2
                // ...
                // .backup.5 implies deletion
                
                int maxBackups = 5;

                for (int i = maxBackups - 1; i >= 1; i--)
                {
                    string source = $"{BackupPath}.{i}";
                    string target = $"{BackupPath}.{i + 1}";
                    
                    // Special case for the first rotation
                    if (i == 1) source = BackupPath;

                    if (File.Exists(source))
                    {
                        // If checking the last one, we can just overwrite the target or delete it first.
                        // File.Move with overwrite handles it.
                        File.Move(source, target, overwrite: true);
                    }
                }

                // 3. Create new backup
                File.Copy(DbPath, BackupPath, overwrite: true);
                System.Diagnostics.Debug.WriteLine("[Backup] Success: Database backed up.");
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"[Backup] Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the database from backup. Called when user chooses to restore.
        /// </summary>
        public static void RestoreFromBackup()
        {
            if (File.Exists(BackupPath))
            {
                EnsureDatabaseDirectoryExists();
                File.Copy(BackupPath, DbPath, overwrite: true);
            }
        }

        public static void Initialize()
        {
            EnsureDatabaseDirectoryExists();

            if (!File.Exists(DbPath))
            {
                SQLiteConnection.CreateFile(DbPath);
            }

            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();

                // Dictionary Table
                string createDictionaryTable = @"
                    CREATE TABLE IF NOT EXISTS UserDictionary (
                        Id GUID PRIMARY KEY,
                        EntryType INTEGER NOT NULL,
                        Word TEXT,
                        OriginalText TEXT,
                        CorrectedText TEXT,
                        CreatedAt DATETIME NOT NULL,
                        ModifiedAt DATETIME NOT NULL
                    );";

                // Notes Table
                string createNotesTable = @"
                    CREATE TABLE IF NOT EXISTS Notes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Date DATETIME NOT NULL,
                        Content TEXT NOT NULL
                    );";

                // Prompts Table
                string createPromptsTable = @"
                    CREATE TABLE IF NOT EXISTS Prompts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        Content TEXT NOT NULL,
                        IsBuiltIn INTEGER DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0
                    );";

                // DailyStatistics Table (aggregate cache for future dashboard)
                string createDailyStatisticsTable = @"
                    CREATE TABLE IF NOT EXISTS DailyStatistics (
                        Date TEXT PRIMARY KEY,
                        TotalWords INTEGER DEFAULT 0,
                        SuccessfulSessions INTEGER DEFAULT 0,
                        FailedSessions INTEGER DEFAULT 0,
                        TotalDurationSeconds REAL DEFAULT 0.0,
                        UniqueProviders TEXT,
                        UniqueModels TEXT,
                        ActiveDay INTEGER DEFAULT 1,
                        ComputedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";

                using (var command = new SQLiteCommand(connection))
                {
                    EnsureTranscriptionHistoryTable(connection);

                    command.CommandText = createDictionaryTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createNotesTable;
                    command.ExecuteNonQuery();

                    // Migration: Check for EditedAt column in Notes
                    command.CommandText = "PRAGMA table_info(Notes);";
                    bool hasNotesEditedAt = false;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "EditedAt") hasNotesEditedAt = true;
                        }
                    }

                    if (!hasNotesEditedAt)
                    {
                        command.CommandText = "ALTER TABLE Notes ADD COLUMN EditedAt DATETIME;";
                        command.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[Database] Migration: Added EditedAt column to Notes.");
                    }

                    command.CommandText = createPromptsTable;
                    command.ExecuteNonQuery();

                    // Migration: Check for SortOrder in Prompts
                    command.CommandText = "PRAGMA table_info(Prompts);";
                    bool hasSortOrder = false;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "SortOrder") hasSortOrder = true;
                        }
                    }

                    if (!hasSortOrder)
                    {
                        command.CommandText = "ALTER TABLE Prompts ADD COLUMN SortOrder INTEGER DEFAULT 0;";
                        command.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[Database] Migration: Added SortOrder column to Prompts.");
                    }

                    // Create DailyStatistics table
                    command.CommandText = createDailyStatisticsTable;
                    command.ExecuteNonQuery();

                    // Migration: Check for DailyStatistics columns
                    command.CommandText = "PRAGMA table_info(DailyStatistics);";
                    bool hasSuccessfulSessions = false;
                    bool hasTotalSessions = false;
                    bool hasFailedSessions = false;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string colName = reader["name"].ToString() ?? "";
                            if (colName == "SuccessfulSessions") hasSuccessfulSessions = true;
                            if (colName == "TotalSessions") hasTotalSessions = true;
                            if (colName == "FailedSessions") hasFailedSessions = true;
                        }
                    }

                    // Rename TotalSessions → SuccessfulSessions if old name exists
                    if (hasTotalSessions && !hasSuccessfulSessions)
                    {
                        command.CommandText = "ALTER TABLE DailyStatistics RENAME COLUMN TotalSessions TO SuccessfulSessions;";
                        command.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[Database] Migration: Renamed TotalSessions to SuccessfulSessions in DailyStatistics.");
                    }
                    else if (!hasSuccessfulSessions)
                    {
                        command.CommandText = "ALTER TABLE DailyStatistics ADD COLUMN SuccessfulSessions INTEGER DEFAULT 0;";
                        command.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[Database] Migration: Added SuccessfulSessions column to DailyStatistics.");
                    }

                    if (!hasFailedSessions)
                    {
                        command.CommandText = "ALTER TABLE DailyStatistics ADD COLUMN FailedSessions INTEGER DEFAULT 0;";
                        command.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[Database] Migration: Added FailedSessions column to DailyStatistics.");
                    }
                }

                EnsureBuiltInData(connection);
                BackfillFirstTranscriptionDate(connection);
            }
        }

        public static void EnsureTranscriptionHistoryTable(SQLiteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            using var createCommand = new SQLiteCommand(CreateTranscriptionHistoryTableSql, connection);
            createCommand.ExecuteNonQuery();

            EnsureTranscriptionHistoryColumns(connection);
        }

        public static void EnsureTranscriptionHistoryColumns(SQLiteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (!TableExists(connection, "TranscriptionHistory")) return;

            var existingColumns = GetTableColumns(connection, "TranscriptionHistory");

            foreach (var column in TranscriptionHistoryMigratedColumns)
            {
                if (existingColumns.Contains(column.Name)) continue;

                using var alterCommand = new SQLiteCommand(
                    $"ALTER TABLE TranscriptionHistory ADD COLUMN {column.Name} {column.Definition};",
                    connection);
                alterCommand.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine($"[Database] Migration: Added {column.Name} column to TranscriptionHistory.");
            }
        }

        private static bool TableExists(SQLiteConnection connection, string tableName)
        {
            using var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @TableName;",
                connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static HashSet<string> GetTableColumns(SQLiteConnection connection, string tableName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = new SQLiteCommand($"PRAGMA table_info({tableName});", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string? columnName = reader["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    columns.Add(columnName);
                }
            }

            return columns;
        }

        /// <summary>
        /// Backfills the FirstTranscriptionDate setting for existing users.
        /// Only runs if the setting is not already set.
        /// </summary>
        private static void BackfillFirstTranscriptionDate(SQLiteConnection connection)
        {
            try
            {
                // Check if AppSettingsManager is available and FirstTranscriptionDate is not set
                if (System.Windows.Application.Current is not App app) return;
                if (app.Services?.AppSettingsManager == null) return;
                if (app.Services.AppSettingsManager.FirstTranscriptionDate != null) return;

                // Query the minimum timestamp from TranscriptionHistory
                using var command = new SQLiteCommand(
                    "SELECT MIN(Timestamp) FROM TranscriptionHistory", connection);
                var result = command.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                {
                    var firstDate = DateTime.Parse(result.ToString()!);
                    app.Services.AppSettingsManager.FirstTranscriptionDate = firstDate;
                    System.Diagnostics.Debug.WriteLine($"[DatabaseInitializer] Backfilled FirstTranscriptionDate: {firstDate}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseInitializer] Backfill error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures built-in data exists (prompts only). No demo/sample data is seeded.
        /// </summary>
        private static void EnsureBuiltInData(SQLiteConnection connection)
        {
            // Only ensure built-in prompts exist - no demo data
            EnsureBuiltInPrompts(connection);
        }

        private static void EnsureBuiltInPrompts(SQLiteConnection connection)
        {
            string checkSql = "SELECT Id, IsBuiltIn FROM Prompts WHERE Name = @Name LIMIT 1";
            string cleanupSql = "DELETE FROM Prompts WHERE Name IN ('Groq Prompt (Short)', 'Groq (Optimized)', '1. General', '2. Email', '3. Chat', '4. Code', '5. Classic', '6. Groq (Optimized)') AND IsBuiltIn = 1";
            using (var cleanupCmd = new SQLiteCommand(cleanupSql, connection))
            {
                cleanupCmd.ExecuteNonQuery();
            }

            string insertSql = "INSERT INTO Prompts (Name, Content, IsBuiltIn, SortOrder) VALUES (@Name, @Content, @IsBuiltIn, @SortOrder)";
            string updateSql = "UPDATE Prompts SET Content = @Content, SortOrder = @SortOrder WHERE Id = @Id AND IsBuiltIn = 1";

            var sampleData = BuiltInPrompts.DatabaseSeedOrder
                .Select((profile, index) => (profile.Name, profile.Content, 1, index + 1))
                .ToArray();

            foreach (var (name, content, isBuiltIn, sortOrder) in sampleData)
            {
                bool exists = false;
                bool isExistingBuiltIn = false;
                int existingId = 0;
                using (var checkCmd = new SQLiteCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@Name", name);
                    using var reader = checkCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        exists = true;
                        existingId = reader.GetInt32(0);
                        isExistingBuiltIn = reader.GetInt32(1) == 1;
                    }
                }

                if (exists && isExistingBuiltIn)
                {
                    using (var command = new SQLiteCommand(updateSql, connection))
                    {
                        command.Parameters.AddWithValue("@Id", existingId);
                        command.Parameters.AddWithValue("@Content", content);
                        command.Parameters.AddWithValue("@SortOrder", sortOrder);
                        command.ExecuteNonQuery();
                    }
                }
                else if (!exists)
                {
                    using (var command = new SQLiteCommand(insertSql, connection))
                    {
                        command.Parameters.AddWithValue("@Name", name);
                        command.Parameters.AddWithValue("@Content", content);
                        command.Parameters.AddWithValue("@IsBuiltIn", isBuiltIn);
                        command.Parameters.AddWithValue("@SortOrder", sortOrder);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
