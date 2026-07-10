using System;

namespace AirType.Models
{
    /// <summary>
    /// Represents aggregated statistics for a single day.
    /// Used as a cache for fast dashboard queries.
    /// </summary>
    public class DailyStatistics
    {
        /// <summary>
        /// Date in YYYY-MM-DD format (primary key).
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// Total words transcribed on this day.
        /// </summary>
        public int TotalWords { get; set; }

        /// <summary>
        /// Successful transcription sessions on this day (WordCount > 0).
        /// </summary>
        public int SuccessfulSessions { get; set; }

        /// <summary>
        /// Failed transcription attempts (WordCount = 0).
        /// </summary>
        public int FailedSessions { get; set; }

        /// <summary>
        /// Total audio duration in seconds for all sessions.
        /// </summary>
        public double TotalDurationSeconds { get; set; }

        /// <summary>
        /// Comma-separated list of unique providers used (e.g., "Gemini,OpenRouter").
        /// </summary>
        public string UniqueProviders { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated list of unique models used.
        /// </summary>
        public string UniqueModels { get; set; } = string.Empty;

        /// <summary>
        /// Whether this day had any activity (for streak calculation).
        /// </summary>
        public bool ActiveDay { get; set; } = true;

        /// <summary>
        /// When these statistics were last computed/updated.
        /// </summary>
        public DateTime ComputedAt { get; set; } = DateTime.Now;
    }
}
