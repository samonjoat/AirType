using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AirType.Models.Dictionary;

namespace AirType.Services.Dictionary;

/// <summary>
/// Builds short provider-bias prompts for speech-to-text endpoints.
/// </summary>
public sealed class DictionaryBiasPromptBuilder : IDictionaryBiasPromptBuilder
{
    private const int CharactersPerToken = 4;

    private readonly IDictionaryManager _dictionaryManager;

    public DictionaryBiasPromptBuilder(IDictionaryManager dictionaryManager)
    {
        _dictionaryManager = dictionaryManager ?? throw new ArgumentNullException(nameof(dictionaryManager));
    }

    public string BuildBiasPrompt(int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return string.Empty;
        }

        int maxCharacters = Math.Max(1, maxTokens * CharactersPerToken);
        var entries = _dictionaryManager.GetAllEntries();
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var vocabularyWords = entries
            .Where(e => e.EntryType == DictionaryEntryType.VocabularyWord && !string.IsNullOrWhiteSpace(e.Word))
            .OrderByDescending(e => e.ModifiedAt)
            .Select(e => e.Word!.Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var correctionPairs = entries
            .Where(e => e.EntryType == DictionaryEntryType.CorrectionPair &&
                        !string.IsNullOrWhiteSpace(e.OriginalText) &&
                        !string.IsNullOrWhiteSpace(e.CorrectedText))
            .OrderByDescending(e => e.ModifiedAt)
            .Select(e => $"\"{e.OriginalText!.Trim()}\" -> \"{e.CorrectedText!.Trim()}\"")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var prompt = new StringBuilder(maxCharacters);
        AppendDelimitedSection(prompt, "Vocabulary: ", vocabularyWords, ", ", ".", maxCharacters);
        AppendDelimitedSection(prompt, "Corrections: ", correctionPairs, "; ", ".", maxCharacters);

        string result = prompt.ToString().Trim();
        Logger.Debug("Dictionary", $"Built STT dictionary bias prompt ({result.Length} chars)");
        return result;
    }

    private static void AppendDelimitedSection(
        StringBuilder prompt,
        string prefix,
        IReadOnlyList<string> items,
        string separator,
        string suffix,
        int maxCharacters)
    {
        if (items.Count == 0)
        {
            return;
        }

        int sectionStart = prompt.Length;
        if (prompt.Length > 0)
        {
            if (!TryAppend(prompt, " ", maxCharacters))
            {
                return;
            }
        }

        if (!TryAppend(prompt, prefix, maxCharacters))
        {
            prompt.Length = sectionStart;
            return;
        }

        int included = 0;
        foreach (string item in items)
        {
            string text = included == 0 ? item : separator + item;
            if (!TryAppend(prompt, text, maxCharacters))
            {
                break;
            }

            included++;
        }

        if (included == 0)
        {
            prompt.Length = sectionStart;
            return;
        }

        TryAppend(prompt, suffix, maxCharacters);
    }

    private static bool TryAppend(StringBuilder builder, string value, int maxCharacters)
    {
        if (builder.Length + value.Length > maxCharacters)
        {
            return false;
        }

        builder.Append(value);
        return true;
    }
}
