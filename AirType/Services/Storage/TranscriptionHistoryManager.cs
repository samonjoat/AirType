using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Services.Database;

namespace AirType.Services.Storage;

/// <summary>
/// Manages persistent storage of transcription history
/// </summary>
public class TranscriptionHistoryManager : IDisposable
{
    private const int MaxHistoryEntries = 100;
    private readonly HistoryDatabase _database;
    private readonly DailyStatisticsManager _dailyStatsManager;
    private readonly IFilePersistenceManager _fileManager;
    private readonly ITranscriptionLogger _logger;
    private List<TranscriptionHistoryEntry> _entries;
    private bool _disposed = false;

    public event EventHandler? HistoryChanged;

    public TranscriptionHistoryManager(IFilePersistenceManager fileManager, ITranscriptionLogger logger)
        : this(fileManager, logger, new HistoryDatabase(), new DailyStatisticsManager())
    {
    }

    public TranscriptionHistoryManager(
        IFilePersistenceManager fileManager,
        ITranscriptionLogger logger,
        HistoryDatabase database,
        DailyStatisticsManager dailyStatsManager)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _dailyStatsManager = dailyStatsManager ?? throw new ArgumentNullException(nameof(dailyStatsManager));
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entries = new List<TranscriptionHistoryEntry>();
        LoadHistory();
    }

    /// <summary>
    /// Gets all history entries (most recent first)
    /// </summary>
    public IReadOnlyList<TranscriptionHistoryEntry> GetAllEntries()
    {
        return _entries.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// Adds a new transcription to history
    /// </summary>
    public async Task AddEntryAsync(TranscriptionHistoryEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        var existing = _entries.FirstOrDefault(e => e.Id == entry.Id);
        if (existing != null)
        {
            if (!ShouldReplaceExistingEntry(existing, entry))
            {
                Logger.Info("HistoryManager", $"Preserved existing successful history entry {entry.Id} instead of replacing it with a failed rerun.");
                return;
            }

            // Check if this is a rerun that changed from failed to successful
            bool wasFailedNowSuccessful = existing.WordCount == 0 && entry.WordCount > 0;

            int index = _entries.IndexOf(existing);
            _entries[index] = entry;
            await _database.UpdateEntryAsync(entry);

            // Update stats if rerun recovered a failed session
            if (wasFailedNowSuccessful)
            {
                var date = entry.Timestamp.ToString("yyyy-MM-dd");
                await _dailyStatsManager.IncrementSuccessfulOnRerunAsync(date, entry);
            }
        }
        else
        {
            await _database.AddEntryAsync(entry);
            _entries.Add(entry);

            // Update daily statistics cache
            var date = entry.Timestamp.ToString("yyyy-MM-dd");
            await _dailyStatsManager.UpsertDailyStatsAsync(date, entry);

            // Capture first transcription date (only once)
            try
            {
                if (System.Windows.Application.Current is App app && app.Services?.AppSettingsManager != null)
                {
                    var appSettings = app.Services.AppSettingsManager;
                    if (appSettings.FirstTranscriptionDate == null)
                    {
                        appSettings.FirstTranscriptionDate = entry.Timestamp;
                        Logger.Info("TranscriptionHistoryManager", "First transcription date recorded");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryManager] Error capturing first transcription date: {ex.Message}");
            }
        }

        if (_entries.Count > MaxHistoryEntries)
        {
            _entries = _entries
                .OrderByDescending(e => e.Timestamp)
                .Take(MaxHistoryEntries)
                .ToList();
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates an existing entry in the cache and database.
    /// </summary>
    public async Task AddOrUpdateEntryAsync(TranscriptionHistoryEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        var existing = _entries.FirstOrDefault(e => e.Id == entry.Id);
        if (existing != null)
        {
            if (!ShouldReplaceExistingEntry(existing, entry))
            {
                Logger.Info("HistoryManager", $"Skipped update for {entry.Id} because the incoming entry would downgrade a successful transcript to a failed one.");
                return;
            }

            // Replace in cache
            int index = _entries.IndexOf(existing);
            _entries[index] = entry;
            
            // Update in DB
            await _database.UpdateEntryAsync(entry);
        }
        else
        {
            // New entry
            await AddEntryAsync(entry);
        }
        
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates an existing entry's transcribed text.
    /// </summary>
    public async Task<bool> UpdateEntryAsync(Guid id, string newText)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry == null)
            return false;

        // Note: HistoryDatabase currently lacks Update. 
        // I should probably add Update to HistoryDatabase or just re-insert/delete.
        // For now, I'll update the cache.
        
        if (entry.OriginalTranscribedText == null)
        {
            entry.OriginalTranscribedText = entry.TranscribedText;
        }

        entry.TranscribedText = newText;
        entry.EditedAt = DateTime.Now;
        entry.WordCount = string.IsNullOrWhiteSpace(newText) 
            ? 0 
            : newText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

        // Since HistoryDatabase doesn't have update yet, we will just delete and re-add or add Update.
        // I will add UpdateEntryAsync to HistoryDatabase in the next step.
        // For now, let's keep it simple.
        
        await _database.UpdateEntryAsync(entry);

        return true;
    }

    public async Task<bool> UpdateTranscriptDisplayModeAsync(Guid id, string transcriptDisplayMode)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry == null)
            return false;

        entry.TranscriptDisplayMode = transcriptDisplayMode;
        await _database.UpdateTranscriptDisplayModeAsync(id, entry.TranscriptDisplayMode);
        HistoryChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Gets an entry by its ID.
    /// </summary>
    public TranscriptionHistoryEntry? GetEntryById(Guid id)
    {
        return _entries.FirstOrDefault(e => e.Id == id);
    }

    /// <summary>
    /// Removes an entry from history and deletes associated files
    /// </summary>
    public async Task RemoveEntryAsync(Guid id)
    {
        // 1. Delete associated files first (audio + log)
        try
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                // Delete audio file
                if (!string.IsNullOrEmpty(entry.AudioFilePath))
                {
                    _fileManager.DeleteRecordingFile(entry.AudioFilePath);
                }
                
                // Delete log file
                _logger.DeleteLogFile(id);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("HistoryManager", $"Error cleaning up files for {id}: {ex.Message}");
        }

        // 2. Delete from database
        await _database.DeleteEntryAsync(id);

        // 3. Remove from cache
        var cachedEntry = _entries.FirstOrDefault(e => e.Id == id);
        if (cachedEntry != null)
        {
            _entries.Remove(cachedEntry);
        }
        
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all history and deletes all associated files
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        // 1. Delete all associated files
        foreach (var entry in _entries)
        {
            try
            {
                if (!string.IsNullOrEmpty(entry.AudioFilePath))
                {
                    _fileManager.DeleteRecordingFile(entry.AudioFilePath);
                }
                _logger.DeleteLogFile(entry.Id);
            }
            catch (Exception ex)
            {
                Logger.Warn("HistoryManager", $"Error cleaning up files for {entry.Id} during clear: {ex.Message}");
            }
        }

        // 2. Clear database
        await _database.ClearHistoryAsync();

        // 3. Clear cache
        _entries.Clear();
        
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the most recent entry
    /// </summary>
    public TranscriptionHistoryEntry? GetMostRecent()
    {
        return _entries.OrderByDescending(e => e.Timestamp).FirstOrDefault();
    }

    /// <summary>
    /// Searches history by text content
    /// </summary>
    public IReadOnlyList<TranscriptionHistoryEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAllEntries();

        return _entries
            .Where(e => e.TranscribedText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Marks that the dictionary learning popup has been shown for this entry.
    /// This prevents the popup from showing on subsequent edits.
    /// </summary>
    public async Task MarkDictionaryPopupShownAsync(Guid id)
    {
        // Update database
        await _database.MarkDictionaryPopupShownAsync(id);

        // Update cache
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry != null)
        {
            entry.HasShownDictionaryPopup = true;
        }
    }

    private void LoadHistory()
    {
        try
        {
            // Sync call inside constructor
            var entries = _database.GetAllEntriesAsync().GetAwaiter().GetResult();
            _entries = entries ?? new List<TranscriptionHistoryEntry>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load transcription history from database: {ex.Message}");
            _entries = new List<TranscriptionHistoryEntry>();
        }
    }

    private static bool ShouldReplaceExistingEntry(TranscriptionHistoryEntry existing, TranscriptionHistoryEntry incoming)
    {
        if (existing == null) throw new ArgumentNullException(nameof(existing));
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));

        bool existingSucceeded = existing.WordCount > 0;
        bool incomingFailed = incoming.WordCount == 0;

        return !(existingSucceeded && incomingFailed);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
        }
    }
}
