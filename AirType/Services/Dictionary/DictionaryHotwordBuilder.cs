using AirType.Models.Dictionary;

namespace AirType.Services.Dictionary;

public sealed class DictionaryHotwordBuilder : IDictionaryHotwordBuilder
{
    private readonly IDictionaryManager _dictionaryManager;

    public DictionaryHotwordBuilder(IDictionaryManager dictionaryManager)
    {
        _dictionaryManager = dictionaryManager ?? throw new ArgumentNullException(nameof(dictionaryManager));
    }

    public DictionaryHotwordResult BuildHotwords(int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return new DictionaryHotwordResult(string.Empty, 0, Array.Empty<string>());
        }

        var candidates = _dictionaryManager
            .GetAllEntries()
            .Where(entry => entry.EntryType == DictionaryEntryType.VocabularyWord)
            .Select(entry => new
            {
                Term = (entry.Word ?? string.Empty).Trim(),
                entry.ModifiedAt,
                Score = ScoreTerm(entry.Word ?? string.Empty)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Term))
            .GroupBy(entry => entry.Term, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.Score)
                .ThenByDescending(entry => entry.ModifiedAt)
                .First())
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.ModifiedAt)
            .ToList();

        var terms = new List<string>();
        int tokenCount = 0;

        foreach (var candidate in candidates)
        {
            int candidateTokens = CountApproximateTokens(candidate.Term);
            if (candidateTokens == 0)
            {
                continue;
            }

            if (tokenCount + candidateTokens > maxTokens)
            {
                continue;
            }

            terms.Add(candidate.Term);
            tokenCount += candidateTokens;
        }

        return new DictionaryHotwordResult(string.Join(", ", terms), tokenCount, terms);
    }

    private static int ScoreTerm(string term)
    {
        int score = 0;

        if (term.Any(char.IsUpper) && term.Any(char.IsLower))
        {
            score += 3;
        }

        if (term.Any(char.IsDigit))
        {
            score += 2;
        }

        if (term.Any(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch)))
        {
            score += 2;
        }

        if (term.Length <= 4 && term.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
        {
            score += 2;
        }

        return score;
    }

    private static int CountApproximateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return text
            .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '.', ':', '/', '\\', '-', '_' },
                StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }
}
