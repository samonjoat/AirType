using System;

namespace AirType.Models.Dictionary;

/// <summary>
/// Represents a single entry in the user dictionary.
/// Each entry is exactly ONE of two types: VocabularyWord or CorrectionPair.
/// </summary>
public class DictionaryEntry
{
    /// <summary>
    /// Unique identifier for this entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// When this entry was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// When this entry was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// The type of this dictionary entry.
    /// </summary>
    public DictionaryEntryType EntryType { get; set; }

    /// <summary>
    /// For VocabularyWord entries ONLY: the word to recognize.
    /// Examples: "Contoso", "NAudio", "Kubernetes"
    /// For CorrectionPair entries: null.
    /// </summary>
    public string? Word { get; set; }

    /// <summary>
    /// For CorrectionPair entries ONLY: the incorrect transcription.
    /// Examples: "deleet", "teh", "recieve"
    /// For VocabularyWord entries: null.
    /// </summary>
    public string? OriginalText { get; set; }

    /// <summary>
    /// For CorrectionPair entries ONLY: the correct spelling.
    /// Examples: "delete", "the", "receive"
    /// For VocabularyWord entries: null.
    /// </summary>
    public string? CorrectedText { get; set; }

    /// <summary>
    /// Display text for UI rendering.
    /// VocabularyWord: returns the word itself.
    /// CorrectionPair: returns "original → corrected" format.
    /// </summary>
    public string DisplayText => EntryType == DictionaryEntryType.VocabularyWord
        ? Word ?? string.Empty
        : $"{OriginalText} → {CorrectedText}";

    public string HeardText => EntryType == DictionaryEntryType.VocabularyWord
        ? Word ?? string.Empty
        : OriginalText ?? string.Empty;

    public string ReplacementText => EntryType == DictionaryEntryType.VocabularyWord
        ? "Vocabulary"
        : CorrectedText ?? string.Empty;

    /// <summary>
    /// Type label for UI display.
    /// Returns "WORD" for VocabularyWord, "CORR" for CorrectionPair.
    /// </summary>
    public string TypeLabel => EntryType == DictionaryEntryType.VocabularyWord
        ? "WORD"
        : "CORR";

    /// <summary>
    /// Creates a new VocabularyWord entry.
    /// </summary>
    /// <param name="word">The vocabulary word to add.</param>
    /// <returns>A new DictionaryEntry of type VocabularyWord.</returns>
    public static DictionaryEntry CreateVocabularyWord(string word)
    {
        return new DictionaryEntry
        {
            EntryType = DictionaryEntryType.VocabularyWord,
            Word = word?.Trim(),
            OriginalText = null,
            CorrectedText = null
        };
    }

    /// <summary>
    /// Creates a new CorrectionPair entry.
    /// </summary>
    /// <param name="originalText">The incorrect transcription.</param>
    /// <param name="correctedText">The correct spelling.</param>
    /// <returns>A new DictionaryEntry of type CorrectionPair.</returns>
    public static DictionaryEntry CreateCorrectionPair(string originalText, string correctedText)
    {
        return new DictionaryEntry
        {
            EntryType = DictionaryEntryType.CorrectionPair,
            Word = null,
            OriginalText = originalText?.Trim(),
            CorrectedText = correctedText?.Trim()
        };
    }
}

/// <summary>
/// Defines the types of dictionary entries.
/// Each entry is exactly one type - the types are mutually exclusive.
/// </summary>
public enum DictionaryEntryType
{
    /// <summary>
    /// A vocabulary word the user wants the API to recognize.
    /// Examples: "Contoso", "NAudio", "Kubernetes", "gRPC", "INCOSE"
    /// Uses the Word property only. OriginalText and CorrectedText are null.
    /// </summary>
    VocabularyWord,

    /// <summary>
    /// A correction mapping from wrong transcription to correct spelling.
    /// Examples: "deleet" → "delete", "teh" → "the", "recieve" → "receive"
    /// Uses OriginalText and CorrectedText properties only. Word is null.
    /// </summary>
    CorrectionPair
}
