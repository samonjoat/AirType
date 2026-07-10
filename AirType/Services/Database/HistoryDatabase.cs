using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Threading.Tasks;
using AirType.Models;

namespace AirType.Services.Database
{
    public class HistoryDatabase
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public event EventHandler? HistoryChanged;

        public HistoryDatabase()
            : this(new SqliteConnectionFactory())
        {
        }

        public HistoryDatabase(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

            EnsureSchemaCompatibility();
        }

        private void EnsureSchemaCompatibility()
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection();
                connection.Open();
                DatabaseInitializer.EnsureTranscriptionHistoryTable(connection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryDatabase] Schema check failed: {ex.Message}");
            }
        }

        public async Task AddEntryAsync(TranscriptionHistoryEntry entry)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string insertSql = @"
                    INSERT INTO TranscriptionHistory
                    (Id, Timestamp, TranscribedText, OriginalTranscribedText, EditedAt, WordCount, Provider, ModelUsed, AudioFilePath, AudioDuration, LatencySeconds, HasShownDictionaryPopup, SessionType,
                     RawTranscribedText, ProviderChain, RawProvider, RawModel, CleanupProvider, CleanupModel, CleanupStatus, AsrLatencySeconds, CleanupLatencySeconds, TotalLatencySeconds,
                     LocalFlushWaitMs, FallbackReason, WorkerSessionId, WorkerUtteranceCount, HotwordTokenCount, InitialPromptMaxTokenCount,
                     RequestedProvider, RequestedModel, ActualProvider, ActualModel, WorkflowStatus, WorkflowError, TranscriptDisplayMode)
                    VALUES (@Id, @Timestamp, @TranscribedText, @OriginalText, @EditedAt, @WordCount, @Provider, @Model, @Path, @Duration, @Latency, @HasShownPopup, @SessionType,
                            @RawTranscribedText, @ProviderChain, @RawProvider, @RawModel, @CleanupProvider, @CleanupModel, @CleanupStatus, @AsrLatencySeconds, @CleanupLatencySeconds,
                            @TotalLatencySeconds, @LocalFlushWaitMs, @FallbackReason, @WorkerSessionId, @WorkerUtteranceCount, @HotwordTokenCount, @InitialPromptMaxTokenCount,
                            @RequestedProvider, @RequestedModel, @ActualProvider, @ActualModel, @WorkflowStatus, @WorkflowError, @TranscriptDisplayMode);";

                using (var command = new SQLiteCommand(insertSql, connection))
                {
                    AddHistoryParameters(command, entry);
                    await command.ExecuteNonQueryAsync();
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task UpdateEntryAsync(TranscriptionHistoryEntry entry)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string updateSql = @"
                    UPDATE TranscriptionHistory
                    SET Timestamp = @Timestamp,
                        TranscribedText = @TranscribedText,
                        OriginalTranscribedText = @OriginalText,
                        EditedAt = @EditedAt,
                        WordCount = @WordCount,
                        Provider = @Provider,
                        ModelUsed = @Model,
                        AudioFilePath = @Path,
                        AudioDuration = @Duration,
                        LatencySeconds = @Latency,
                        HasShownDictionaryPopup = @HasShownPopup,
                        SessionType = @SessionType,
                        RawTranscribedText = @RawTranscribedText,
                        ProviderChain = @ProviderChain,
                        RawProvider = @RawProvider,
                        RawModel = @RawModel,
                        CleanupProvider = @CleanupProvider,
                        CleanupModel = @CleanupModel,
                        CleanupStatus = @CleanupStatus,
                        AsrLatencySeconds = @AsrLatencySeconds,
                        CleanupLatencySeconds = @CleanupLatencySeconds,
                        TotalLatencySeconds = @TotalLatencySeconds,
                        LocalFlushWaitMs = @LocalFlushWaitMs,
                        FallbackReason = @FallbackReason,
                        WorkerSessionId = @WorkerSessionId,
                        WorkerUtteranceCount = @WorkerUtteranceCount,
                        HotwordTokenCount = @HotwordTokenCount,
                        InitialPromptMaxTokenCount = @InitialPromptMaxTokenCount,
                        RequestedProvider = @RequestedProvider,
                        RequestedModel = @RequestedModel,
                        ActualProvider = @ActualProvider,
                        ActualModel = @ActualModel,
                        WorkflowStatus = @WorkflowStatus,
                        WorkflowError = @WorkflowError,
                        TranscriptDisplayMode = @TranscriptDisplayMode
                    WHERE Id = @Id;";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    AddHistoryParameters(command, entry);
                    await command.ExecuteNonQueryAsync();
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task UpdateTranscriptDisplayModeAsync(Guid id, string transcriptDisplayMode)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string updateSql = @"
                    UPDATE TranscriptionHistory
                    SET TranscriptDisplayMode = @TranscriptDisplayMode
                    WHERE Id = @Id;";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@TranscriptDisplayMode", transcriptDisplayMode);
                    await command.ExecuteNonQueryAsync();
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<List<TranscriptionHistoryEntry>> GetAllEntriesAsync()
        {
            var entries = new List<TranscriptionHistoryEntry>();
            try
            {
                using (var connection = _connectionFactory.CreateConnection())
                {
                    await connection.OpenAsync();
                    string selectSql = "SELECT * FROM TranscriptionHistory ORDER BY Timestamp DESC LIMIT 100;";

                    using (var command = new SQLiteCommand(selectSql, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            entries.Add(new TranscriptionHistoryEntry
                            {
                                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                                Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                                TranscribedText = ReadString(reader, "TranscribedText", string.Empty),
                                OriginalTranscribedText = ReadNullableString(reader, "OriginalTranscribedText"),
                                EditedAt = ReadNullableDateTime(reader, "EditedAt"),
                                WordCount = ReadInt32(reader, "WordCount", 0),
                                Provider = ReadString(reader, "Provider", "Unknown"),
                                ModelUsed = ReadString(reader, "ModelUsed", "Unknown"),
                                AudioFilePath = ReadNullableString(reader, "AudioFilePath"),
                                AudioDuration = TimeSpan.FromSeconds(ReadDouble(reader, "AudioDuration", 0)),
                                LatencySeconds = ReadDouble(reader, "LatencySeconds", 0),
                                HasShownDictionaryPopup = ReadInt32(reader, "HasShownDictionaryPopup", 0) == 1,
                                SessionType = ReadString(reader, "SessionType", "Hotkey"),
                                RawTranscribedText = ReadNullableString(reader, "RawTranscribedText"),
                                ProviderChain = ReadNullableString(reader, "ProviderChain"),
                                RawProvider = ReadNullableString(reader, "RawProvider"),
                                RawModel = ReadNullableString(reader, "RawModel"),
                                CleanupProvider = ReadNullableString(reader, "CleanupProvider"),
                                CleanupModel = ReadNullableString(reader, "CleanupModel"),
                                CleanupStatus = ReadNullableString(reader, "CleanupStatus"),
                                AsrLatencySeconds = ReadNullableDouble(reader, "AsrLatencySeconds"),
                                CleanupLatencySeconds = ReadNullableDouble(reader, "CleanupLatencySeconds"),
                                TotalLatencySeconds = ReadNullableDouble(reader, "TotalLatencySeconds"),
                                LocalFlushWaitMs = ReadNullableInt32(reader, "LocalFlushWaitMs"),
                                FallbackReason = ReadNullableString(reader, "FallbackReason"),
                                WorkerSessionId = ReadNullableString(reader, "WorkerSessionId"),
                                WorkerUtteranceCount = ReadNullableInt32(reader, "WorkerUtteranceCount"),
                                HotwordTokenCount = ReadNullableInt32(reader, "HotwordTokenCount"),
                                InitialPromptMaxTokenCount = ReadNullableInt32(reader, "InitialPromptMaxTokenCount"),
                                RequestedProvider = ReadNullableString(reader, "RequestedProvider"),
                                RequestedModel = ReadNullableString(reader, "RequestedModel"),
                                ActualProvider = ReadNullableString(reader, "ActualProvider"),
                                ActualModel = ReadNullableString(reader, "ActualModel"),
                                WorkflowStatus = ReadNullableString(reader, "WorkflowStatus"),
                                WorkflowError = ReadNullableString(reader, "WorkflowError"),
                                TranscriptDisplayMode = ReadString(reader, "TranscriptDisplayMode", TranscriptionHistoryEntry.TranscriptDisplayModeCleaner)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Database] Error loading history: {ex.Message}");
            }
            return entries;
        }

        public async Task DeleteEntryAsync(Guid id)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string deleteSql = "DELETE FROM TranscriptionHistory WHERE Id = @Id;";

                using (var command = new SQLiteCommand(deleteSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    await command.ExecuteNonQueryAsync();
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task ClearHistoryAsync()
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string clearSql = "DELETE FROM TranscriptionHistory;";

                using (var command = new SQLiteCommand(clearSql, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task PurgeEntriesBeforeAsync(DateTime cutoff)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string purgeSql = "DELETE FROM TranscriptionHistory WHERE Timestamp < @Cutoff;";

                using (var command = new SQLiteCommand(purgeSql, connection))
                {
                    command.Parameters.AddWithValue("@Cutoff", cutoff);
                    await command.ExecuteNonQueryAsync();
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Marks that the dictionary learning popup has been shown for this entry.
        /// This prevents the popup from showing on subsequent edits.
        /// </summary>
        public async Task MarkDictionaryPopupShownAsync(Guid id)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string updateSql = "UPDATE TranscriptionHistory SET HasShownDictionaryPopup = 1 WHERE Id = @Id;";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private static void AddHistoryParameters(SQLiteCommand command, TranscriptionHistoryEntry entry)
        {
            command.Parameters.AddWithValue("@Id", entry.Id);
            command.Parameters.AddWithValue("@Timestamp", entry.Timestamp);
            command.Parameters.AddWithValue("@TranscribedText", ToDbValue(entry.TranscribedText));
            command.Parameters.AddWithValue("@OriginalText", ToDbValue(entry.OriginalTranscribedText));
            command.Parameters.AddWithValue("@EditedAt", ToDbValue(entry.EditedAt));
            command.Parameters.AddWithValue("@WordCount", entry.WordCount);
            command.Parameters.AddWithValue("@Provider", ToDbValue(entry.Provider));
            command.Parameters.AddWithValue("@Model", ToDbValue(entry.ModelUsed));
            command.Parameters.AddWithValue("@Path", ToDbValue(entry.AudioFilePath));
            command.Parameters.AddWithValue("@Duration", entry.AudioDuration.TotalSeconds);
            command.Parameters.AddWithValue("@Latency", entry.LatencySeconds);
            command.Parameters.AddWithValue("@HasShownPopup", entry.HasShownDictionaryPopup ? 1 : 0);
            command.Parameters.AddWithValue("@SessionType", ToDbValue(entry.SessionType ?? "Hotkey"));
            command.Parameters.AddWithValue("@RawTranscribedText", ToDbValue(entry.RawTranscribedText));
            command.Parameters.AddWithValue("@ProviderChain", ToDbValue(entry.ProviderChain));
            command.Parameters.AddWithValue("@RawProvider", ToDbValue(entry.RawProvider));
            command.Parameters.AddWithValue("@RawModel", ToDbValue(entry.RawModel));
            command.Parameters.AddWithValue("@CleanupProvider", ToDbValue(entry.CleanupProvider));
            command.Parameters.AddWithValue("@CleanupModel", ToDbValue(entry.CleanupModel));
            command.Parameters.AddWithValue("@CleanupStatus", ToDbValue(entry.CleanupStatus));
            command.Parameters.AddWithValue("@AsrLatencySeconds", ToDbValue(entry.AsrLatencySeconds));
            command.Parameters.AddWithValue("@CleanupLatencySeconds", ToDbValue(entry.CleanupLatencySeconds));
            command.Parameters.AddWithValue("@TotalLatencySeconds", ToDbValue(entry.TotalLatencySeconds));
            command.Parameters.AddWithValue("@LocalFlushWaitMs", ToDbValue(entry.LocalFlushWaitMs));
            command.Parameters.AddWithValue("@FallbackReason", ToDbValue(entry.FallbackReason));
            command.Parameters.AddWithValue("@WorkerSessionId", ToDbValue(entry.WorkerSessionId));
            command.Parameters.AddWithValue("@WorkerUtteranceCount", ToDbValue(entry.WorkerUtteranceCount));
            command.Parameters.AddWithValue("@HotwordTokenCount", ToDbValue(entry.HotwordTokenCount));
            command.Parameters.AddWithValue("@InitialPromptMaxTokenCount", ToDbValue(entry.InitialPromptMaxTokenCount));
            command.Parameters.AddWithValue("@RequestedProvider", ToDbValue(entry.RequestedProvider));
            command.Parameters.AddWithValue("@RequestedModel", ToDbValue(entry.RequestedModel));
            command.Parameters.AddWithValue("@ActualProvider", ToDbValue(entry.ActualProvider));
            command.Parameters.AddWithValue("@ActualModel", ToDbValue(entry.ActualModel));
            command.Parameters.AddWithValue("@WorkflowStatus", ToDbValue(entry.WorkflowStatus));
            command.Parameters.AddWithValue("@WorkflowError", ToDbValue(entry.WorkflowError));
            command.Parameters.AddWithValue("@TranscriptDisplayMode", entry.TranscriptDisplayMode);
        }

        private static object ToDbValue(string? value) =>
            value is null ? DBNull.Value : value;

        private static object ToDbValue(DateTime? value) =>
            value.HasValue ? value.Value : DBNull.Value;

        private static object ToDbValue(double? value) =>
            value.HasValue ? value.Value : DBNull.Value;

        private static object ToDbValue(int? value) =>
            value.HasValue ? value.Value : DBNull.Value;

        private static string ReadString(DbDataReader reader, string columnName, string fallback)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? fallback : value?.ToString() ?? fallback;
        }

        private static string? ReadNullableString(DbDataReader reader, string columnName)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? null : value?.ToString();
        }

        private static DateTime? ReadNullableDateTime(DbDataReader reader, string columnName)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? null : Convert.ToDateTime(value);
        }

        private static double ReadDouble(DbDataReader reader, string columnName, double fallback)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? fallback : Convert.ToDouble(value);
        }

        private static double? ReadNullableDouble(DbDataReader reader, string columnName)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? null : Convert.ToDouble(value);
        }

        private static int ReadInt32(DbDataReader reader, string columnName, int fallback)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? fallback : Convert.ToInt32(value);
        }

        private static int? ReadNullableInt32(DbDataReader reader, string columnName)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? null : Convert.ToInt32(value);
        }
    }
}
