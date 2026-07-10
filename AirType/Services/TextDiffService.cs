using System;
using System.Collections.Generic;
using System.Linq;
using AirType.Models;
using Phonix;

namespace AirType.Services;

/// <summary>
/// Service for detecting text differences between original and edited transcriptions.
/// Uses word-by-word comparison with Longest Common Subsequence (LCS) algorithm.
/// </summary>
public interface ITextDiffService
{
    /// <summary>
    /// Compares original and edited text and returns a list of differences.
    /// </summary>
    /// <param name="original">The original transcribed text.</param>
    /// <param name="edited">The user-edited text.</param>
    /// <returns>List of text differences found.</returns>
    IReadOnlyList<TextDiff> GetDifferences(string original, string edited);
    
    /// <summary>
    /// Gets only meaningful changes that could be added to the dictionary.
    /// Filters out trivial changes like whitespace and punctuation.
    /// </summary>
    IReadOnlyList<TextDiff> GetMeaningfulDifferences(string original, string edited);
}

/// <summary>
/// Implementation of ITextDiffService using word-level diff with LCS algorithm.
/// </summary>
public class TextDiffService : ITextDiffService
{
    /// <summary>
    /// Characters considered word separators.
    /// </summary>
    private static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '(', ')', '"' };

    /// <summary>
    /// Common word endings that indicate grammar changes rather than ASR errors.
    /// </summary>
    private static readonly string[] GrammarSuffixes = { "s", "es", "ed", "ing", "ly", "er", "est", "tion", "sion" };

    /// <summary>
    /// Double Metaphone encoder for phonetic matching.
    /// Used to match words that sound alike (e.g., Jude → Joat).
    /// </summary>
    private static readonly DoubleMetaphone _metaphone = new DoubleMetaphone();

    /// <summary>
    /// Checks if two words sound alike using Double Metaphone phonetic algorithm.
    /// This is the primary matching method for ASR corrections since transcription
    /// errors are typically phonetic (words that sound similar).
    /// </summary>
    private static bool SoundsAlike(string word1, string word2)
    {
        if (string.IsNullOrEmpty(word1) || string.IsNullOrEmpty(word2))
            return false;

        // Get phonetic codes for both words
        var keys1 = _metaphone.BuildKeys(word1);
        var keys2 = _metaphone.BuildKeys(word2);

        // Match if any primary or alternate keys match
        // Double Metaphone generates primary and alternate pronunciations
        return keys1.Any(k1 => keys2.Any(k2 =>
            !string.IsNullOrEmpty(k1) && !string.IsNullOrEmpty(k2) &&
            k1.Equals(k2, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Checks if the change is just a grammar variation (pluralization, tense, etc.)
    /// rather than an ASR transcription error.
    /// Example: "word" → "words" is grammar, not ASR error.
    /// </summary>
    private static bool IsGrammarOnlyChange(string original, string corrected)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(corrected))
            return false;

        var origLower = original.ToLowerInvariant();
        var corrLower = corrected.ToLowerInvariant();

        // Check if one is a prefix of the other with a grammar suffix
        // e.g., "word" → "words", "walk" → "walking"
        if (corrLower.StartsWith(origLower))
        {
            var suffix = corrLower.Substring(origLower.Length);
            if (GrammarSuffixes.Any(s => suffix == s))
                return true;
        }
        else if (origLower.StartsWith(corrLower))
        {
            var suffix = origLower.Substring(corrLower.Length);
            if (GrammarSuffixes.Any(s => suffix == s))
                return true;
        }

        // Check for common tense changes (e.g., "run" → "ran", "go" → "went")
        // These are harder to detect programmatically, so we use a simple length check
        // If words are very similar length and share the same root, likely grammar
        if (Math.Abs(original.Length - corrected.Length) <= 2)
        {
            // Check if they share a common prefix of at least 60% of the shorter word
            int minLen = Math.Min(origLower.Length, corrLower.Length);
            int commonPrefix = 0;
            for (int i = 0; i < minLen; i++)
            {
                if (origLower[i] == corrLower[i])
                    commonPrefix++;
                else
                    break;
            }
            // If they share 60%+ prefix and only differ by 1-2 chars at end, likely grammar
            if (commonPrefix >= minLen * 0.6 && Math.Abs(original.Length - corrected.Length) <= 2)
            {
                // Additional check: if the remaining chars are just suffix changes
                var origSuffix = origLower.Substring(commonPrefix);
                var corrSuffix = corrLower.Substring(commonPrefix);
                if ((origSuffix.Length <= 3 && corrSuffix.Length <= 3) &&
                    (GrammarSuffixes.Any(s => origSuffix.EndsWith(s)) ||
                     GrammarSuffixes.Any(s => corrSuffix.EndsWith(s))))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Calculates confidence score based on match type and edit distance.
    /// </summary>
    private static double CalculateConfidence(string original, string corrected, bool isPhoneticMatch, int editDistance)
    {
        // Grammar-only changes get 0 confidence (filtered out)
        if (IsGrammarOnlyChange(original, corrected))
            return 0.0;

        // Phonetic match = highest confidence
        if (isPhoneticMatch)
            return 0.95;

        // Calculate confidence based on edit distance ratio
        int maxLen = Math.Max(original.Length, corrected.Length);
        if (maxLen == 0) return 0.0;

        double distanceRatio = (double)editDistance / maxLen;

        // Edit distance < 30% = 0.85
        if (distanceRatio < 0.30)
            return 0.85;
        // Edit distance 30-50% = 0.70
        if (distanceRatio < 0.50)
            return 0.70;
        // Edit distance 50-80% = 0.50
        if (distanceRatio < 0.80)
            return 0.50;

        // Beyond 80% = too different, likely not ASR error
        return 0.30;
    }

    /// <summary>
    /// Compares original and edited text and returns a list of differences.
    /// </summary>
    public IReadOnlyList<TextDiff> GetDifferences(string original, string edited)
    {
        if (string.IsNullOrEmpty(original) && string.IsNullOrEmpty(edited))
            return Array.Empty<TextDiff>();
        
        var originalWords = TokenizeText(original ?? string.Empty);
        var editedWords = TokenizeText(edited ?? string.Empty);
        
        return ComputeDiff(originalWords, editedWords);
    }
    
    /// <summary>
    /// Gets only meaningful changes that could be added to the dictionary.
    /// </summary>
    public IReadOnlyList<TextDiff> GetMeaningfulDifferences(string original, string edited)
    {
        var allDiffs = GetDifferences(original, edited);
        return allDiffs.Where(d => d.IsMeaningfulChange).ToList();
    }
    
    /// <summary>
    /// Tokenizes text into words by splitting on whitespace and punctuation.
    /// Punctuation characters are treated as separators and are not returned as tokens.
    /// </summary>
    private static string[] TokenizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();
        
        return text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
    }
    
    /// <summary>
    /// Computes differences between two word arrays using LCS-based algorithm.
    /// Uses positional matching when arrays have equal length and no exact positional matches.
    /// </summary>
    private static List<TextDiff> ComputeDiff(string[] original, string[] edited)
    {
        // Special case: equal length arrays with no exact positional matches
        // Use positional pairing (e.g., "Samuel Jutes" → "Samon Joat" becomes 2 changes, not 3)
        if (original.Length == edited.Length && original.Length > 0)
        {
            bool hasAnyExactMatch = false;
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i].Equals(edited[i], StringComparison.Ordinal))
                {
                    hasAnyExactMatch = true;
                    break;
                }
            }

            // If same length and no exact positional matches, use positional pairing
            if (!hasAnyExactMatch)
            {
                return CreatePositionalDiffs(original, edited);
            }
        }

        // Fall back to LCS algorithm for other cases
        var diffs = new List<TextDiff>();

        // Compute LCS table
        var lcs = ComputeLcsTable(original, edited);

        // Backtrack to find differences
        BacktrackDiff(original, edited, lcs, original.Length, edited.Length, diffs);

        // Merge consecutive changes where possible
        return MergeConsecutiveChanges(diffs);
    }

    /// <summary>
    /// Creates diffs by pairing words at the same position.
    /// Used when arrays have equal length and no exact matches.
    /// </summary>
    private static List<TextDiff> CreatePositionalDiffs(string[] original, string[] edited)
    {
        var diffs = new List<TextDiff>();

        for (int i = 0; i < original.Length; i++)
        {
            if (!original[i].Equals(edited[i], StringComparison.Ordinal))
            {
                bool isPhonetic = SoundsAlike(original[i], edited[i]);
                int editDist = LevenshteinDistance(original[i], edited[i]);
                double confidence = CalculateConfidence(original[i], edited[i], isPhonetic, editDist);

                diffs.Add(new TextDiff
                {
                    OriginalText = original[i],
                    CorrectedText = edited[i],
                    Type = DiffType.Modified,
                    Confidence = confidence
                });
            }
        }

        return diffs;
    }

    /// <summary>
    /// Calculates the Levenshtein (edit) distance between two strings.
    /// Used for matching similar words when pairing removes and adds.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int[,] dp = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                // Case-insensitive comparison for better matching
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }

    /// <summary>
    /// Builds the LCS (Longest Common Subsequence) table.
    /// </summary>
    private static int[,] ComputeLcsTable(string[] original, string[] edited)
    {
        int m = original.Length;
        int n = edited.Length;
        var lcs = new int[m + 1, n + 1];
        
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (original[i - 1].Equals(edited[j - 1], StringComparison.Ordinal))
                {
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                }
                else
                {
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }
        }
        
        return lcs;
    }
    
    /// <summary>
    /// Backtracks through LCS table to identify differences.
    /// </summary>
    private static void BacktrackDiff(string[] original, string[] edited, int[,] lcs, int i, int j, List<TextDiff> diffs)
    {
        if (i == 0 && j == 0)
            return;
        
        if (i > 0 && j > 0 && original[i - 1].Equals(edited[j - 1], StringComparison.Ordinal))
        {
            // Words match - no difference
            BacktrackDiff(original, edited, lcs, i - 1, j - 1, diffs);
        }
        else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
        {
            // Word was added in edited version
            BacktrackDiff(original, edited, lcs, i, j - 1, diffs);
            diffs.Add(new TextDiff
            {
                OriginalText = string.Empty,
                CorrectedText = edited[j - 1],
                Type = DiffType.Added,
                Confidence = 0.60  // Pure additions: moderate confidence (may be merged later)
            });
        }
        else if (i > 0 && (j == 0 || lcs[i - 1, j] > lcs[i, j - 1]))
        {
            // Word was removed from original
            BacktrackDiff(original, edited, lcs, i - 1, j, diffs);
            diffs.Add(new TextDiff
            {
                OriginalText = original[i - 1],
                CorrectedText = string.Empty,
                Type = DiffType.Removed,
                Confidence = 0.00  // Pure deletions: no confidence (filtered out)
            });
        }
    }
    
    /// <summary>
    /// Merges consecutive Removes followed by consecutive Adds into Modified changes.
    /// Uses similarity-based pairing (Levenshtein distance) instead of positional pairing.
    /// This correctly handles cases where words are inserted alongside modifications.
    /// Example: [Remove Solomon, Remove Jude, Add The, Add Samon, Add Joat]
    ///       → [(add)The, Solomon→Samon, Jude→Joat]  (not Solomon→The, Jude→Samon, (add)Joat)
    /// </summary>
    private static List<TextDiff> MergeConsecutiveChanges(List<TextDiff> diffs)
    {
        var merged = new List<TextDiff>();
        int i = 0;

        while (i < diffs.Count)
        {
            // Collect consecutive Removes
            var removes = new List<TextDiff>();
            while (i < diffs.Count && diffs[i].Type == DiffType.Removed)
            {
                removes.Add(diffs[i]);
                i++;
            }

            // Collect consecutive Adds
            var adds = new List<TextDiff>();
            while (i < diffs.Count && diffs[i].Type == DiffType.Added)
            {
                adds.Add(diffs[i]);
                i++;
            }

            // Use similarity-based pairing instead of positional pairing
            if (removes.Count > 0 && adds.Count > 0)
            {
                var usedAdds = new HashSet<int>();
                var pairs = new List<(TextDiff Remove, TextDiff Add, bool IsPhonetic, int EditDistance)>();

                // Find best match for each remove using phonetic matching (priority) + edit distance (fallback)
                foreach (var remove in removes)
                {
                    int bestAddIndex = -1;
                    int bestDistance = int.MaxValue;
                    bool bestIsPhonetic = false;

                    for (int a = 0; a < adds.Count; a++)
                    {
                        if (usedAdds.Contains(a)) continue;

                        // PRIORITY 1: Phonetic match (words that sound alike)
                        // This is the primary matching method for ASR corrections
                        if (SoundsAlike(remove.OriginalText, adds[a].CorrectedText))
                        {
                            bestAddIndex = a;
                            bestIsPhonetic = true;
                            bestDistance = LevenshteinDistance(remove.OriginalText, adds[a].CorrectedText);
                            break; // Phonetic match is definitive - stop searching
                        }

                        // PRIORITY 2: Edit distance fallback for non-phonetic matches
                        int distance = LevenshteinDistance(remove.OriginalText, adds[a].CorrectedText);

                        // Only consider as match if distance is reasonable (< 80% of longer word length)
                        // 80% threshold handles short words better (e.g., Jude→Joat = distance 3, threshold 3.2)
                        int maxLen = Math.Max(remove.OriginalText.Length, adds[a].CorrectedText.Length);
                        if (distance < maxLen * 0.8 && distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestAddIndex = a;
                            bestIsPhonetic = false;
                        }
                    }

                    if (bestAddIndex >= 0)
                    {
                        pairs.Add((remove, adds[bestAddIndex], bestIsPhonetic, bestDistance));
                        usedAdds.Add(bestAddIndex);
                    }
                    else
                    {
                        // No good match - this is a pure deletion (0 confidence)
                        merged.Add(remove);
                    }
                }

                // Create modifications from pairs with confidence scores
                foreach (var (remove, add, isPhonetic, editDist) in pairs)
                {
                    double confidence = CalculateConfidence(remove.OriginalText, add.CorrectedText, isPhonetic, editDist);
                    merged.Add(new TextDiff
                    {
                        OriginalText = remove.OriginalText,
                        CorrectedText = add.CorrectedText,
                        Type = DiffType.Modified,
                        Confidence = confidence
                    });
                }

                // Leftover adds are pure insertions (keep their 0.60 confidence)
                for (int a = 0; a < adds.Count; a++)
                {
                    if (!usedAdds.Contains(a))
                    {
                        merged.Add(adds[a]);
                    }
                }
            }
            else
            {
                // Only removes or only adds - add them directly
                foreach (var remove in removes)
                {
                    merged.Add(remove);
                }
                foreach (var add in adds)
                {
                    merged.Add(add);
                }
            }

            // If we didn't process any removes or adds, move to next item
            if (removes.Count == 0 && adds.Count == 0 && i < diffs.Count)
            {
                merged.Add(diffs[i]);
                i++;
            }
        }

        return merged;
    }
}
