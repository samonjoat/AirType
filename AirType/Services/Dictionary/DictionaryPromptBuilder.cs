using System;
using System.Linq;
using System.Text;
using AirType.Models.Dictionary;

namespace AirType.Services.Dictionary;

/// <summary>
/// Builds formatted dictionary sections for inclusion in transcription API prompts.
/// </summary>
public class DictionaryPromptBuilder : IDictionaryPromptBuilder
{
    private const double TokensPerCharacter = 0.25; // Approximately 1 token per 4 characters

    private readonly IDictionaryManager _dictionaryManager;

    public DictionaryPromptBuilder(IDictionaryManager dictionaryManager)
    {
        _dictionaryManager = dictionaryManager ?? throw new ArgumentNullException(nameof(dictionaryManager));
    }

    /// <inheritdoc />
    public string BuildDictionarySection()
    {
        var entries = _dictionaryManager.GetAllEntries();

        if (entries.Count == 0)
        {
            return string.Empty;
        }

        // Separate entries by type, ordered by most recently modified
        var vocabularyWords = entries
            .Where(e => e.EntryType == DictionaryEntryType.VocabularyWord && !string.IsNullOrWhiteSpace(e.Word))
            .OrderByDescending(e => e.ModifiedAt)
            .Select(e => e.Word!)
            .ToList();

        var correctionPairs = entries
            .Where(e => e.EntryType == DictionaryEntryType.CorrectionPair &&
                        !string.IsNullOrWhiteSpace(e.OriginalText) &&
                        !string.IsNullOrWhiteSpace(e.CorrectedText))
            .OrderByDescending(e => e.ModifiedAt)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("USER VOCABULARY GUIDE:");

        // Build vocabulary word section
        if (vocabularyWords.Count > 0)
        {
            sb.Append("Known vocabulary: ");
            sb.AppendLine(string.Join(", ", vocabularyWords));
        }

        // Build correction pairs section
        if (correctionPairs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Likely corrections (apply when the context fits):");
            foreach (var pair in correctionPairs)
            {
                sb.AppendLine($"- \"{pair.OriginalText}\" -> correct to \"{pair.CorrectedText}\" when the context fits");
            }
        }

        sb.Append("---");

        string result = sb.ToString();

        Logger.Debug("Dictionary", $"Built dictionary section with {vocabularyWords.Count} words and {correctionPairs.Count} corrections ({result.Length} chars)");

        return result;
    }

    /// <inheritdoc />
    public int GetEstimatedTokenCount()
    {
        string section = BuildDictionarySection();
        return (int)(section.Length * TokensPerCharacter);
    }
}
