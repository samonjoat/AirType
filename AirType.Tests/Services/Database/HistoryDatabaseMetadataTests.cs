using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Services.Database;
using Xunit;

namespace AirType.Tests.Services.Database;

public sealed class HistoryDatabaseMetadataTests
{
    private static readonly string[] NewMetadataColumns =
    {
        "RawTranscribedText",
        "ProviderChain",
        "RawProvider",
        "RawModel",
        "CleanupProvider",
        "CleanupModel",
        "CleanupStatus",
        "AsrLatencySeconds",
        "CleanupLatencySeconds",
        "TotalLatencySeconds",
        "LocalFlushWaitMs",
        "FallbackReason",
        "WorkerSessionId",
        "WorkerUtteranceCount",
        "HotwordTokenCount",
        "InitialPromptMaxTokenCount",
        "RequestedProvider",
        "RequestedModel",
        "ActualProvider",
        "ActualModel",
        "WorkflowStatus",
        "WorkflowError",
        "TranscriptDisplayMode"
    };

    [Fact]
    public void EnsureTranscriptionHistoryTable_WhenCreatingNewTable_IncludesNullableMetadataColumns()
    {
        using var database = TempDatabase.Create();
        using var connection = database.OpenConnection();

        DatabaseInitializer.EnsureTranscriptionHistoryTable(connection);

        var columns = ReadTranscriptionHistoryColumns(connection);
        foreach (string columnName in NewMetadataColumns)
        {
            Assert.True(columns.TryGetValue(columnName, out var column), $"Missing column {columnName}.");
            Assert.False(column.NotNull, $"{columnName} should be nullable.");
        }
    }

    [Fact]
    public async Task Constructor_WhenExistingSchemaIsMissingMetadataColumns_MigratesAndLoadsExistingRows()
    {
        using var database = TempDatabase.Create();
        Guid id = Guid.NewGuid();
        DateTime timestamp = new(2026, 6, 22, 9, 15, 0);

        using (var connection = database.OpenConnection())
        {
            CreateLegacyTranscriptionHistoryTable(connection);
            InsertLegacyHistoryRow(connection, id, timestamp, "legacy final transcript");
        }

        var historyDatabase = new HistoryDatabase(database.CreateFactory());

        using (var connection = database.OpenConnection())
        {
            var columns = ReadTranscriptionHistoryColumns(connection);
            foreach (string columnName in NewMetadataColumns)
            {
                Assert.True(columns.ContainsKey(columnName), $"Migration did not add {columnName}.");
            }
        }

        var entries = await historyDatabase.GetAllEntriesAsync();
        var entry = Assert.Single(entries);

        Assert.Equal(id, entry.Id);
        Assert.Equal(timestamp, entry.Timestamp);
        Assert.Equal("legacy final transcript", entry.TranscribedText);
        Assert.Null(entry.RawTranscribedText);
        Assert.Null(entry.ProviderChain);
        Assert.Null(entry.CleanupStatus);
        Assert.Equal(TranscriptionHistoryEntry.TranscriptDisplayModeCleaner, entry.TranscriptDisplayMode);
    }

    [Fact]
    public async Task AddEntryAsync_WhenCleanupSucceeds_PersistsRawTextSeparatelyFromFinalText()
    {
        using var database = TempDatabase.Create();
        var historyDatabase = new HistoryDatabase(database.CreateFactory());
        var entry = CreateMetadataEntry();

        await historyDatabase.AddEntryAsync(entry);

        var saved = Assert.Single(await historyDatabase.GetAllEntriesAsync());
        Assert.Equal("final cleaned transcript", saved.TranscribedText);
        Assert.Equal("raw asr transcript", saved.RawTranscribedText);
        Assert.Equal("Succeeded", saved.CleanupStatus);
        Assert.Equal("Local", saved.RawProvider);
        Assert.Equal("faster-whisper-small-en-int8", saved.RawModel);
        Assert.Equal("Gemini", saved.CleanupProvider);
        Assert.Equal("gemini-flash-lite-latest", saved.CleanupModel);
        Assert.Equal(1.25, saved.AsrLatencySeconds);
        Assert.Equal(0.5, saved.CleanupLatencySeconds);
        Assert.Equal(1.75, saved.TotalLatencySeconds);
        Assert.Equal("Local", saved.RequestedProvider);
        Assert.Equal("faster-whisper-small-en-int8", saved.RequestedModel);
        Assert.Equal("Local", saved.ActualProvider);
        Assert.Equal("faster-whisper-small-en-int8", saved.ActualModel);
        Assert.Equal("Completed", saved.WorkflowStatus);
        Assert.Null(saved.WorkflowError);
        Assert.Equal(TranscriptionHistoryEntry.TranscriptDisplayModeCleaner, saved.TranscriptDisplayMode);
    }

    [Fact]
    public async Task UpdateTranscriptDisplayModeAsync_PersistsModeWithoutChangingTranscriptText()
    {
        using var database = TempDatabase.Create();
        var historyDatabase = new HistoryDatabase(database.CreateFactory());
        var entry = CreateMetadataEntry();

        await historyDatabase.AddEntryAsync(entry);
        await historyDatabase.UpdateTranscriptDisplayModeAsync(entry.Id, TranscriptionHistoryEntry.TranscriptDisplayModeAsr);

        var saved = Assert.Single(await historyDatabase.GetAllEntriesAsync());
        Assert.Equal("final cleaned transcript", saved.TranscribedText);
        Assert.Equal("raw asr transcript", saved.RawTranscribedText);
        Assert.Equal(TranscriptionHistoryEntry.TranscriptDisplayModeAsr, saved.TranscriptDisplayMode);
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenTextIsEdited_PreservesDiagnosticMetadata()
    {
        using var database = TempDatabase.Create();
        var historyDatabase = new HistoryDatabase(database.CreateFactory());
        var entry = CreateMetadataEntry();

        await historyDatabase.AddEntryAsync(entry);

        var loaded = Assert.Single(await historyDatabase.GetAllEntriesAsync());
        loaded.OriginalTranscribedText = loaded.TranscribedText;
        loaded.TranscribedText = "user edited final transcript";
        loaded.EditedAt = new DateTime(2026, 6, 22, 10, 30, 0);
        loaded.WordCount = 4;

        await historyDatabase.UpdateEntryAsync(loaded);

        var saved = Assert.Single(await historyDatabase.GetAllEntriesAsync());
        Assert.Equal("user edited final transcript", saved.TranscribedText);
        Assert.Equal("final cleaned transcript", saved.OriginalTranscribedText);
        Assert.Equal("raw asr transcript", saved.RawTranscribedText);
        Assert.Equal("Local -> Gemini", saved.ProviderChain);
        Assert.Equal("Succeeded", saved.CleanupStatus);
        Assert.Equal("worker-session-1", saved.WorkerSessionId);
        Assert.Equal(3, saved.WorkerUtteranceCount);
        Assert.Equal(2, saved.HotwordTokenCount);
        Assert.Equal(224, saved.InitialPromptMaxTokenCount);
    }

    private static TranscriptionHistoryEntry CreateMetadataEntry() =>
        new()
        {
            Id = Guid.NewGuid(),
            Timestamp = new DateTime(2026, 6, 22, 10, 0, 0),
            TranscribedText = "final cleaned transcript",
            RawTranscribedText = "raw asr transcript",
            WordCount = 3,
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8",
            AudioFilePath = @"C:\Temp\sample.wav",
            AudioDuration = TimeSpan.FromSeconds(4.2),
            LatencySeconds = 1.75,
            SessionType = "Hotkey",
            ProviderChain = "Local -> Gemini",
            RawProvider = "Local",
            RawModel = "faster-whisper-small-en-int8",
            CleanupProvider = "Gemini",
            CleanupModel = "gemini-flash-lite-latest",
            CleanupStatus = "Succeeded",
            AsrLatencySeconds = 1.25,
            CleanupLatencySeconds = 0.5,
            TotalLatencySeconds = 1.75,
            LocalFlushWaitMs = 80,
            WorkerSessionId = "worker-session-1",
            WorkerUtteranceCount = 3,
            HotwordTokenCount = 2,
            InitialPromptMaxTokenCount = 224,
            RequestedProvider = "Local",
            RequestedModel = "faster-whisper-small-en-int8",
            ActualProvider = "Local",
            ActualModel = "faster-whisper-small-en-int8",
            WorkflowStatus = "Completed",
            TranscriptDisplayMode = TranscriptionHistoryEntry.TranscriptDisplayModeCleaner
        };

    private static void CreateLegacyTranscriptionHistoryTable(SQLiteConnection connection)
    {
        using var command = new SQLiteCommand(
            """
            CREATE TABLE TranscriptionHistory (
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
                SessionType TEXT DEFAULT 'Hotkey'
            );
            """,
            connection);
        command.ExecuteNonQuery();
    }

    private static void InsertLegacyHistoryRow(SQLiteConnection connection, Guid id, DateTime timestamp, string text)
    {
        using var command = new SQLiteCommand(
            """
            INSERT INTO TranscriptionHistory
                (Id, Timestamp, TranscribedText, WordCount, Provider, ModelUsed, SessionType)
            VALUES
                (@Id, @Timestamp, @TranscribedText, @WordCount, @Provider, @ModelUsed, @SessionType);
            """,
            connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Timestamp", timestamp);
        command.Parameters.AddWithValue("@TranscribedText", text);
        command.Parameters.AddWithValue("@WordCount", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        command.Parameters.AddWithValue("@Provider", "Groq");
        command.Parameters.AddWithValue("@ModelUsed", "whisper-large-v3");
        command.Parameters.AddWithValue("@SessionType", "Hotkey");
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, ColumnInfo> ReadTranscriptionHistoryColumns(SQLiteConnection connection)
    {
        var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        using var command = new SQLiteCommand("PRAGMA table_info(TranscriptionHistory);", connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string name = reader["name"].ToString()!;
            string type = reader["type"].ToString()!;
            bool notNull = Convert.ToInt32(reader["notnull"]) == 1;
            columns[name] = new ColumnInfo(name, type, notNull);
        }

        return columns;
    }

    private sealed record ColumnInfo(string Name, string Type, bool NotNull);

    private sealed class TempDatabase : IDisposable
    {
        private TempDatabase(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        public string ConnectionString => $"Data Source={FilePath};Version=3;";

        public static TempDatabase Create()
        {
            string filePath = Path.Combine(Path.GetTempPath(), $"airtype-history-{Guid.NewGuid():N}.db");
            SQLiteConnection.CreateFile(filePath);
            return new TempDatabase(filePath);
        }

        public SqliteConnectionFactory CreateFactory() => new(ConnectionString);

        public SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();

            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }
}
