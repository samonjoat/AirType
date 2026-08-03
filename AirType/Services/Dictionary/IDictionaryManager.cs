using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AirType.Models.Dictionary;

namespace AirType.Services.Dictionary;

/// <summary>
/// Service responsible for loading, saving, querying, and validating dictionary entries.
/// </summary>
public interface IDictionaryManager
{
    #region Query Methods

    /// <summary>
    /// Gets all dictionary entries.
    /// </summary>
    /// <returns>A read-only list of all entries.</returns>
    IReadOnlyList<DictionaryEntry> GetAllEntries();

    /// <summary>
    /// Gets a dictionary entry by its unique identifier.
    /// </summary>
    /// <param name="id">The entry ID.</param>
    /// <returns>The entry if found, null otherwise.</returns>
    DictionaryEntry? GetEntryById(Guid id);

    /// <summary>
    /// Searches dictionary entries by text content.
    /// </summary>
    /// <param name="query">The search query (case-insensitive).</param>
    /// <returns>Entries matching the search query.</returns>
    IReadOnlyList<DictionaryEntry> Search(string query);

    #endregion

    #region CRUD Methods

    /// <summary>
    /// Adds a new vocabulary word to the dictionary.
    /// </summary>
    /// <param name="word">The word to add (e.g., "Contoso", "NAudio", "Kubernetes").</param>
    /// <returns>The created entry.</returns>
    /// <exception cref="ArgumentException">If the word is invalid or a duplicate.</exception>
    Task<DictionaryEntry> AddVocabularyWordAsync(string word);

    /// <summary>
    /// Adds a new correction pair to the dictionary.
    /// </summary>
    /// <param name="originalText">The incorrect transcription (e.g., "deleet", "teh").</param>
    /// <param name="correctedText">The correct spelling (e.g., "delete", "the").</param>
    /// <returns>The created entry.</returns>
    /// <exception cref="ArgumentException">If the correction is invalid or a duplicate.</exception>
    Task<DictionaryEntry> AddCorrectionPairAsync(string originalText, string correctedText);

    /// <summary>
    /// Updates an existing dictionary entry.
    /// </summary>
    /// <param name="entry">The entry with updated values.</param>
    /// <returns>The updated entry.</returns>
    /// <exception cref="KeyNotFoundException">If the entry does not exist.</exception>
    /// <exception cref="ArgumentException">If the updated values are invalid.</exception>
    Task<DictionaryEntry> UpdateEntryAsync(DictionaryEntry entry);

    /// <summary>
    /// Deletes a dictionary entry.
    /// </summary>
    /// <param name="id">The ID of the entry to delete.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteEntryAsync(Guid id);

    #endregion

    #region Validation Methods

    /// <summary>
    /// Validates a dictionary entry.
    /// </summary>
    /// <param name="entry">The entry to validate.</param>
    /// <returns>Validation result with success/failure and error message.</returns>
    ValidationResult ValidateEntry(DictionaryEntry entry);

    /// <summary>
    /// Checks if a vocabulary word already exists (case-insensitive).
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>True if duplicate exists.</returns>
    bool IsDuplicateWord(string word);

    /// <summary>
    /// Checks if a correction pair already exists (case-insensitive).
    /// </summary>
    /// <param name="originalText">The original text to check.</param>
    /// <param name="correctedText">The corrected text to check.</param>
    /// <returns>True if duplicate exists.</returns>
    bool IsDuplicateCorrection(string originalText, string correctedText);

    #endregion

    #region Import/Export Methods

    /// <summary>
    /// Exports the dictionary to a JSON file.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    Task ExportAsync(string filePath);

    /// <summary>
    /// Imports entries from a JSON file, merging with existing entries.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>Import result with counts of entries added and skipped.</returns>
    Task<ImportResult> ImportAsync(string filePath);

    #endregion

    #region Events

    /// <summary>
    /// Raised when the dictionary is modified (add, update, delete, import).
    /// </summary>
    event EventHandler<DictionaryChangedEventArgs>? DictionaryChanged;

    #endregion
}

/// <summary>
/// Result of a validation operation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Error message if validation failed, null otherwise.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public static ValidationResult Failure(string errorMessage) => new() { IsValid = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Result of an import operation.
/// </summary>
public class ImportResult
{
    /// <summary>
    /// Number of entries successfully added.
    /// </summary>
    public int EntriesAdded { get; set; }

    /// <summary>
    /// Number of entries skipped (duplicates or invalid).
    /// </summary>
    public int EntriesSkipped { get; set; }

    /// <summary>
    /// List of error messages encountered during import.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Whether the import completed successfully (may still have skipped entries).
    /// </summary>
    public bool Success => Errors.Count == 0 || EntriesAdded > 0;
}

/// <summary>
/// Event arguments for dictionary change events.
/// </summary>
public class DictionaryChangedEventArgs : EventArgs
{
    /// <summary>
    /// The type of change that occurred.
    /// </summary>
    public DictionaryChangeType ChangeType { get; set; }

    /// <summary>
    /// The affected entry (null for bulk operations like Import).
    /// </summary>
    public DictionaryEntry? Entry { get; set; }

    /// <summary>
    /// Creates event args for a single entry change.
    /// </summary>
    public static DictionaryChangedEventArgs ForEntry(DictionaryChangeType changeType, DictionaryEntry entry)
        => new() { ChangeType = changeType, Entry = entry };

    /// <summary>
    /// Creates event args for a bulk operation.
    /// </summary>
    public static DictionaryChangedEventArgs ForBulk(DictionaryChangeType changeType)
        => new() { ChangeType = changeType, Entry = null };
}

/// <summary>
/// Types of dictionary changes.
/// </summary>
public enum DictionaryChangeType
{
    /// <summary>
    /// A new entry was added.
    /// </summary>
    Added,

    /// <summary>
    /// An existing entry was updated.
    /// </summary>
    Updated,

    /// <summary>
    /// An entry was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// Entries were imported from a file.
    /// </summary>
    Imported
}
