using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AirType.Models.Dictionary;
using AirType.Services.Dictionary;
using Xunit;

namespace AirType.Tests.Services.Dictionary;

public sealed class DictionaryHotwordBuilderTests
{
    [Fact]
    public void BuildHotwords_UsesVocabularyOnly()
    {
        var entries = new[]
        {
            NewVocabulary("AirType", DateTime.UtcNow),
            NewCorrection("cloud", "Claude", DateTime.UtcNow),
            NewVocabulary("OAuth", DateTime.UtcNow.AddMinutes(-1))
        };
        var builder = new DictionaryHotwordBuilder(new FakeDictionaryManager(entries));

        var result = builder.BuildHotwords(maxTokens: 100);

        Assert.Contains("AirType", result.Text);
        Assert.Contains("OAuth", result.Text);
        Assert.DoesNotContain("cloud", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Claude", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildHotwords_RespectsTokenCap()
    {
        var entries = new List<DictionaryEntry>();
        for (int i = 0; i < 20; i++)
        {
            entries.Add(NewVocabulary($"DomainTerm{i:00}", DateTime.UtcNow.AddMinutes(-i)));
        }
        var builder = new DictionaryHotwordBuilder(new FakeDictionaryManager(entries));

        var result = builder.BuildHotwords(maxTokens: 5);

        Assert.True(result.TokenCount <= 5);
        Assert.Equal(result.TokenCount, result.Terms.Count);
    }

    private static DictionaryEntry NewVocabulary(string word, DateTime modifiedAt)
    {
        var entry = DictionaryEntry.CreateVocabularyWord(word);
        entry.ModifiedAt = modifiedAt;
        return entry;
    }

    private static DictionaryEntry NewCorrection(string original, string corrected, DateTime modifiedAt)
    {
        var entry = DictionaryEntry.CreateCorrectionPair(original, corrected);
        entry.ModifiedAt = modifiedAt;
        return entry;
    }

    private sealed class FakeDictionaryManager : IDictionaryManager
    {
        private readonly IReadOnlyList<DictionaryEntry> _entries;

        public FakeDictionaryManager(IReadOnlyList<DictionaryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<DictionaryEntry> GetAllEntries() => _entries;
        public DictionaryEntry? GetEntryById(Guid id) => throw new NotImplementedException();
        public IReadOnlyList<DictionaryEntry> Search(string query) => throw new NotImplementedException();
        public Task<DictionaryEntry> AddVocabularyWordAsync(string word) => throw new NotImplementedException();
        public Task<DictionaryEntry> AddCorrectionPairAsync(string originalText, string correctedText) => throw new NotImplementedException();
        public Task<DictionaryEntry> UpdateEntryAsync(DictionaryEntry entry) => throw new NotImplementedException();
        public Task<bool> DeleteEntryAsync(Guid id) => throw new NotImplementedException();
        public ValidationResult ValidateEntry(DictionaryEntry entry) => throw new NotImplementedException();
        public bool IsDuplicateWord(string word) => throw new NotImplementedException();
        public bool IsDuplicateCorrection(string originalText, string correctedText) => throw new NotImplementedException();
        public Task ExportAsync(string filePath) => throw new NotImplementedException();
        public Task<ImportResult> ImportAsync(string filePath) => throw new NotImplementedException();
        public event EventHandler<DictionaryChangedEventArgs>? DictionaryChanged
        {
            add { }
            remove { }
        }
    }
}
