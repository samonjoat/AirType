using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using AirType.Models;

namespace AirType.Services.Database
{
    /// <summary>
    /// Manages daily aggregated statistics for fast dashboard queries.
    /// Updates the cache whenever a new transcription is added.
    /// </summary>
    public class DailyStatisticsManager
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public DailyStatisticsManager()
            : this(new SqliteConnectionFactory())
        {
        }

        public DailyStatisticsManager(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Gets statistics for a specific date.
        /// </summary>
        /// <param name="date">Date in YYYY-MM-DD format</param>
        /// <returns>DailyStatistics for the date, or null if no data exists</returns>
        public async Task<DailyStatistics?> GetStatsForDateAsync(string date)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                string selectSql = "SELECT * FROM DailyStatistics WHERE Date = @Date;";
                using var command = new SQLiteCommand(selectSql, connection);
                command.Parameters.AddWithValue("@Date", date);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new DailyStatistics
                    {
                        Date = reader["Date"]?.ToString() ?? date,
                        TotalWords = Convert.ToInt32(reader["TotalWords"]),
                        SuccessfulSessions = Convert.ToInt32(reader["SuccessfulSessions"]),
                        FailedSessions = reader["FailedSessions"] != DBNull.Value ? Convert.ToInt32(reader["FailedSessions"]) : 0,
                        TotalDurationSeconds = Convert.ToDouble(reader["TotalDurationSeconds"]),
                        UniqueProviders = reader["UniqueProviders"]?.ToString() ?? string.Empty,
                        UniqueModels = reader["UniqueModels"]?.ToString() ?? string.Empty,
                        ActiveDay = Convert.ToInt32(reader["ActiveDay"]) == 1,
                        ComputedAt = reader["ComputedAt"] != DBNull.Value
                            ? DateTime.Parse(reader["ComputedAt"].ToString()!)
                            : DateTime.Now
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Error getting stats for {date}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Updates the daily statistics cache with a new transcription entry.
        /// Uses UPSERT pattern: INSERT if new date, UPDATE if exists.
        /// </summary>
        /// <param name="date">Date in YYYY-MM-DD format</param>
        /// <param name="entry">The transcription entry to add to the stats</param>
        public async Task UpsertDailyStatsAsync(string date, TranscriptionHistoryEntry entry)
        {
            try
            {
                // Get existing stats for this date
                var existing = await GetStatsForDateAsync(date);

                // Calculate new values - separate successful vs failed sessions
                int totalWords = (existing?.TotalWords ?? 0) + entry.WordCount;
                int successfulSessions = existing?.SuccessfulSessions ?? 0;
                int failedSessions = existing?.FailedSessions ?? 0;
                double totalDuration = (existing?.TotalDurationSeconds ?? 0) + entry.AudioDuration.TotalSeconds;

                // Count as failed if WordCount is 0, otherwise successful
                if (entry.WordCount > 0)
                {
                    successfulSessions++;
                }
                else
                {
                    failedSessions++;
                }

                // Update unique providers list
                var providers = new HashSet<string>(
                    (existing?.UniqueProviders ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
                if (!string.IsNullOrEmpty(entry.Provider))
                    providers.Add(entry.Provider);
                string uniqueProviders = string.Join(",", providers.OrderBy(p => p));

                // Update unique models list
                var models = new HashSet<string>(
                    (existing?.UniqueModels ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
                if (!string.IsNullOrEmpty(entry.ModelUsed))
                    models.Add(entry.ModelUsed);
                string uniqueModels = string.Join(",", models.OrderBy(m => m));

                using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                // Use INSERT OR REPLACE for UPSERT behavior
                string upsertSql = @"
                    INSERT OR REPLACE INTO DailyStatistics
                    (Date, TotalWords, SuccessfulSessions, FailedSessions, TotalDurationSeconds, UniqueProviders, UniqueModels, ActiveDay, ComputedAt)
                    VALUES (@Date, @TotalWords, @SuccessfulSessions, @FailedSessions, @TotalDuration, @Providers, @Models, 1, @ComputedAt);";

                using var command = new SQLiteCommand(upsertSql, connection);
                command.Parameters.AddWithValue("@Date", date);
                command.Parameters.AddWithValue("@TotalWords", totalWords);
                command.Parameters.AddWithValue("@SuccessfulSessions", successfulSessions);
                command.Parameters.AddWithValue("@FailedSessions", failedSessions);
                command.Parameters.AddWithValue("@TotalDuration", totalDuration);
                command.Parameters.AddWithValue("@Providers", uniqueProviders);
                command.Parameters.AddWithValue("@Models", uniqueModels);
                command.Parameters.AddWithValue("@ComputedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Updated stats for {date}: {successfulSessions} successful, {failedSessions} failed, {totalWords} words");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Error updating stats for {date}: {ex.Message}");
            }
        }

        /// <summary>
        /// Increments SuccessfulSessions when a failed session is rerun successfully.
        /// FailedSessions is NOT decremented - it serves as a permanent record of initial failures.
        /// </summary>
        /// <param name="date">Date in YYYY-MM-DD format</param>
        /// <param name="entry">The updated transcription entry</param>
        public async Task IncrementSuccessfulOnRerunAsync(string date, TranscriptionHistoryEntry entry)
        {
            try
            {
                var existing = await GetStatsForDateAsync(date);
                if (existing == null) return; // Should not happen, but safety check

                int successfulSessions = existing.SuccessfulSessions + 1;
                int totalWords = existing.TotalWords + entry.WordCount;
                double totalDuration = existing.TotalDurationSeconds; // Duration already counted on first attempt

                using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                string updateSql = @"
                    UPDATE DailyStatistics
                    SET SuccessfulSessions = @SuccessfulSessions,
                        TotalWords = @TotalWords,
                        ComputedAt = @ComputedAt
                    WHERE Date = @Date;";

                using var command = new SQLiteCommand(updateSql, connection);
                command.Parameters.AddWithValue("@Date", date);
                command.Parameters.AddWithValue("@SuccessfulSessions", successfulSessions);
                command.Parameters.AddWithValue("@TotalWords", totalWords);
                command.Parameters.AddWithValue("@ComputedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Rerun success for {date}: now {successfulSessions} successful, +{entry.WordCount} words");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Error updating rerun stats for {date}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all daily statistics ordered by date descending.
        /// </summary>
        /// <param name="limit">Maximum number of days to return</param>
        public async Task<List<DailyStatistics>> GetAllStatsAsync(int limit = 365)
        {
            var stats = new List<DailyStatistics>();
            try
            {
                using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                string selectSql = "SELECT * FROM DailyStatistics ORDER BY Date DESC LIMIT @Limit;";
                using var command = new SQLiteCommand(selectSql, connection);
                command.Parameters.AddWithValue("@Limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stats.Add(new DailyStatistics
                    {
                        Date = reader["Date"]?.ToString() ?? string.Empty,
                        TotalWords = Convert.ToInt32(reader["TotalWords"]),
                        SuccessfulSessions = Convert.ToInt32(reader["SuccessfulSessions"]),
                        FailedSessions = reader["FailedSessions"] != DBNull.Value ? Convert.ToInt32(reader["FailedSessions"]) : 0,
                        TotalDurationSeconds = Convert.ToDouble(reader["TotalDurationSeconds"]),
                        UniqueProviders = reader["UniqueProviders"]?.ToString() ?? string.Empty,
                        UniqueModels = reader["UniqueModels"]?.ToString() ?? string.Empty,
                        ActiveDay = Convert.ToInt32(reader["ActiveDay"]) == 1,
                        ComputedAt = reader["ComputedAt"] != DBNull.Value
                            ? DateTime.Parse(reader["ComputedAt"].ToString()!)
                            : DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Error getting all stats: {ex.Message}");
            }
            return stats;
        }

        /// <summary>
        /// Backfills daily statistics from existing TranscriptionHistory data.
        /// Call this once when upgrading existing installations.
        /// </summary>
        public async Task BackfillFromHistoryAsync()
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                // Get aggregated data from history grouped by date
                string aggregateSql = @"
                    SELECT
                        DATE(Timestamp) as Date,
                        SUM(WordCount) as TotalWords,
                        COUNT(*) as TotalSessions,
                        SUM(AudioDuration) as TotalDurationSeconds,
                        GROUP_CONCAT(DISTINCT Provider) as UniqueProviders,
                        GROUP_CONCAT(DISTINCT ModelUsed) as UniqueModels
                    FROM TranscriptionHistory
                    GROUP BY DATE(Timestamp)
                    ORDER BY Date;";

                using var selectCmd = new SQLiteCommand(aggregateSql, connection);
                using var reader = await selectCmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string date = reader["Date"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(date)) continue;

                    // Check if stats already exist for this date
                    var existing = await GetStatsForDateAsync(date);
                    if (existing != null) continue; // Don't overwrite existing data

                    string upsertSql = @"
                        INSERT INTO DailyStatistics
                        (Date, TotalWords, TotalSessions, TotalDurationSeconds, UniqueProviders, UniqueModels, ActiveDay, ComputedAt)
                        VALUES (@Date, @TotalWords, @TotalSessions, @TotalDuration, @Providers, @Models, 1, @ComputedAt);";

                    using var insertCmd = new SQLiteCommand(upsertSql, connection);
                    insertCmd.Parameters.AddWithValue("@Date", date);
                    insertCmd.Parameters.AddWithValue("@TotalWords", Convert.ToInt32(reader["TotalWords"]));
                    insertCmd.Parameters.AddWithValue("@TotalSessions", Convert.ToInt32(reader["TotalSessions"]));
                    insertCmd.Parameters.AddWithValue("@TotalDuration", reader["TotalDurationSeconds"] != DBNull.Value ? Convert.ToDouble(reader["TotalDurationSeconds"]) : 0);
                    insertCmd.Parameters.AddWithValue("@Providers", reader["UniqueProviders"]?.ToString() ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@Models", reader["UniqueModels"]?.ToString() ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@ComputedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    await insertCmd.ExecuteNonQueryAsync();
                }

                System.Diagnostics.Debug.WriteLine("[DailyStats] Backfill complete.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyStats] Backfill error: {ex.Message}");
            }
        }
    }
}
