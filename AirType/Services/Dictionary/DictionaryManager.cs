using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AirType.Models.Dictionary;
using AirType.Services.Database;

namespace AirType.Services.Dictionary;

/// <summary>
/// Manages persistent storage and operations for the user dictionary.
/// Stores entries in JSON format at %LOCALAPPDATA%\AirType\user_dictionary.json.
/// </summary>
public class DictionaryManager : IDictionaryManager, IDisposable
{
    private readonly DictionaryDatabase _database;
    private readonly object _lock = new();
    private List<DictionaryEntry> _entries;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<DictionaryChangedEventArgs>? DictionaryChanged;

    public DictionaryManager()
        : this(new DictionaryDatabase())
    {
    }

    public DictionaryManager(DictionaryDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _entries = new List<DictionaryEntry>();
        LoadDictionary();
    }

    #region Query Methods

    /// <inheritdoc />
    public IReadOnlyList<DictionaryEntry> GetAllEntries()
    {
        lock (_lock)
        {
            return _entries.OrderByDescending(e => e.ModifiedAt).ToList();
        }
    }

    /// <inheritdoc />
    public DictionaryEntry? GetEntryById(Guid id)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.Id == id);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DictionaryEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAllEntries();

        lock (_lock)
        {
            return _entries
                .Where(e => MatchesSearch(e, query))
                .OrderByDescending(e => e.ModifiedAt)
                .ToList();
        }
    }

    private static bool MatchesSearch(DictionaryEntry entry, string query)
    {
        if (entry.EntryType == DictionaryEntryType.VocabularyWord)
        {
            return entry.Word?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        }
        else
        {
            return entry.OriginalText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                   || entry.CorrectedText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    #endregion

    #region CRUD Methods

    /// <inheritdoc />
    public async Task<DictionaryEntry> AddVocabularyWordAsync(string word)
    {
        var entry = DictionaryEntry.CreateVocabularyWord(word);
        var validation = ValidateEntry(entry);

        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.ErrorMessage);
        }

        if (IsDuplicateWord(entry.Word!))
        {
            throw new ArgumentException($"The word '{entry.Word}' already exists in the dictionary.");
        }

        await _database.AddEntryAsync(entry);
        
        lock (_lock)
        {
            _entries.Add(entry);
        }

        OnDictionaryChanged(DictionaryChangedEventArgs.ForEntry(DictionaryChangeType.Added, entry));

        Logger.Info("Dictionary", $"Added vocabulary word: {entry.Word}");
        return entry;
    }

    /// <inheritdoc />
    public async Task<DictionaryEntry> AddCorrectionPairAsync(string originalText, string correctedText)
    {
        var entry = DictionaryEntry.CreateCorrectionPair(originalText, correctedText);
        var validation = ValidateEntry(entry);

        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.ErrorMessage);
        }

        if (IsDuplicateCorrection(entry.OriginalText!, entry.CorrectedText!))
        {
            throw new ArgumentException($"A correction from '{entry.OriginalText}' to '{entry.CorrectedText}' already exists.");
        }

        await _database.AddEntryAsync(entry);

        lock (_lock)
        {
            _entries.Add(entry);
        }

        OnDictionaryChanged(DictionaryChangedEventArgs.ForEntry(DictionaryChangeType.Added, entry));

        Logger.Info("Dictionary", $"Added correction pair: {entry.OriginalText} → {entry.CorrectedText}");
        return entry;
    }

    /// <inheritdoc />
    public async Task<DictionaryEntry> UpdateEntryAsync(DictionaryEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        var validation = ValidateEntry(entry);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.ErrorMessage);
        }

        lock (_lock)
        {
            var existingIndex = _entries.FindIndex(e => e.Id == entry.Id);
            if (existingIndex < 0)
            {
                throw new KeyNotFoundException($"Entry with ID '{entry.Id}' not found.");
            }

            // Check for duplicates, excluding this entry
            if (entry.EntryType == DictionaryEntryType.VocabularyWord)
            {
                var duplicate = _entries.FirstOrDefault(e =>
                    e.Id != entry.Id &&
                    e.EntryType == DictionaryEntryType.VocabularyWord &&
                    string.Equals(e.Word, entry.Word, StringComparison.OrdinalIgnoreCase));

                if (duplicate != null)
                {
                    throw new ArgumentException($"The word '{entry.Word}' already exists in the dictionary.");
                }
            }
            else
            {
                var duplicate = _entries.FirstOrDefault(e =>
                    e.Id != entry.Id &&
                    e.EntryType == DictionaryEntryType.CorrectionPair &&
                    string.Equals(e.OriginalText, entry.OriginalText, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.CorrectedText, entry.CorrectedText, StringComparison.OrdinalIgnoreCase));

                if (duplicate != null)
                {
                    throw new ArgumentException($"A correction from '{entry.OriginalText}' to '{entry.CorrectedText}' already exists.");
                }
            }

            entry.ModifiedAt = DateTime.Now;
            _entries[existingIndex] = entry;
        }

        await _database.UpdateEntryAsync(entry);
        OnDictionaryChanged(DictionaryChangedEventArgs.ForEntry(DictionaryChangeType.Updated, entry));

        Logger.Info("Dictionary", $"Updated entry: {entry.DisplayText}");
        return entry;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEntryAsync(Guid id)
    {
        DictionaryEntry? removedEntry;

        lock (_lock)
        {
            removedEntry = _entries.FirstOrDefault(e => e.Id == id);
            if (removedEntry == null)
            {
                return false;
            }

            _entries.Remove(removedEntry);
        }

        await _database.DeleteEntryAsync(id);
        OnDictionaryChanged(DictionaryChangedEventArgs.ForEntry(DictionaryChangeType.Deleted, removedEntry));

        Logger.Info("Dictionary", $"Deleted entry: {removedEntry.DisplayText}");
        return true;
    }

    #endregion

    #region Validation Methods

    /// <inheritdoc />
    public ValidationResult ValidateEntry(DictionaryEntry entry)
    {
        if (entry == null)
        {
            return ValidationResult.Failure("Entry cannot be null.");
        }

        if (entry.EntryType == DictionaryEntryType.VocabularyWord)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
            {
                return ValidationResult.Failure("Vocabulary word cannot be empty.");
            }
        }
        else // CorrectionPair
        {
            if (string.IsNullOrWhiteSpace(entry.OriginalText))
            {
                return ValidationResult.Failure("Original text cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(entry.CorrectedText))
            {
                return ValidationResult.Failure("Corrected text cannot be empty.");
            }

            if (string.Equals(entry.OriginalText.Trim(), entry.CorrectedText.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Failure("Original and corrected text cannot be the same.");
            }
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public bool IsDuplicateWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        lock (_lock)
        {
            return _entries.Any(e =>
                e.EntryType == DictionaryEntryType.VocabularyWord &&
                string.Equals(e.Word, word.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc />
    public bool IsDuplicateCorrection(string originalText, string correctedText)
    {
        if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(correctedText))
            return false;

        lock (_lock)
        {
            return _entries.Any(e =>
                e.EntryType == DictionaryEntryType.CorrectionPair &&
                string.Equals(e.OriginalText, originalText.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.CorrectedText, correctedText.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    #endregion

    #region Import/Export Methods

    /// <inheritdoc />
    public async Task ExportAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        List<DictionaryEntry> entriesToExport;
        lock (_lock)
        {
            entriesToExport = _entries.OrderByDescending(e => e.ModifiedAt).ToList();
        }

        var exportOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        string json = JsonSerializer.Serialize(entriesToExport, exportOptions);
        await File.WriteAllTextAsync(filePath, json);

        Logger.Info("Dictionary", $"Exported {entriesToExport.Count} entries to {filePath}");
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        if (!File.Exists(filePath))
        {
            return new ImportResult
            {
                EntriesAdded = 0,
                EntriesSkipped = 0,
                Errors = { $"File not found: {filePath}" }
            };
        }

        var result = new ImportResult();

        try
        {
            string json = await File.ReadAllTextAsync(filePath);
            var importedEntries = JsonSerializer.Deserialize<List<DictionaryEntry>>(json);

            if (importedEntries == null || importedEntries.Count == 0)
            {
                result.Errors.Add("No valid entries found in the file.");
                return result;
            }

            foreach (var importedEntry in importedEntries)
            {
                var validation = ValidateEntry(importedEntry);
                if (!validation.IsValid)
                {
                    result.EntriesSkipped++;
                    continue;
                }

                bool isDuplicate = importedEntry.EntryType == DictionaryEntryType.VocabularyWord
                    ? IsDuplicateWord(importedEntry.Word!)
                    : IsDuplicateCorrection(importedEntry.OriginalText!, importedEntry.CorrectedText!);

                if (isDuplicate)
                {
                    result.EntriesSkipped++;
                    continue;
                }

                // Create new entry with fresh ID and timestamps
                var newEntry = importedEntry.EntryType == DictionaryEntryType.VocabularyWord
                    ? DictionaryEntry.CreateVocabularyWord(importedEntry.Word!)
                    : DictionaryEntry.CreateCorrectionPair(importedEntry.OriginalText!, importedEntry.CorrectedText!);

                await _database.AddEntryAsync(newEntry);

                lock (_lock)
                {
                    _entries.Add(newEntry);
                }

                result.EntriesAdded++;
            }

            if (result.EntriesAdded > 0)
            {
                OnDictionaryChanged(DictionaryChangedEventArgs.ForBulk(DictionaryChangeType.Imported));
            }

            Logger.Info("Dictionary", $"Imported {result.EntriesAdded} entries, skipped {result.EntriesSkipped}");
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Invalid JSON format: {ex.Message}");
            Logger.Error("Dictionary", $"Import failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Import failed: {ex.Message}");
            Logger.Error("Dictionary", $"Import failed: {ex.Message}");
        }

        return result;
    }

    #endregion

    #region Private Methods

    private void LoadDictionary()
    {
        try
        {
            _entries = _database.GetAllEntriesAsync().GetAwaiter().GetResult();
            Logger.Info("Dictionary", $"Loaded {_entries.Count} dictionary entries from database");
        }
        catch (Exception ex)
        {
            Logger.Error("Dictionary", $"Failed to load dictionary from database: {ex.Message}");
            _entries = new List<DictionaryEntry>();
        }
    }

    #endregion

    #region IDisposable

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

    /// <summary>
    /// Safely raises the DictionaryChanged event
    /// </summary>
    protected virtual void OnDictionaryChanged(DictionaryChangedEventArgs e)
    {
        DictionaryChanged?.Invoke(this, e);
    }

    #endregion
}
