using AirType.Models.Dictionary;
using AirType.Services.Dictionary;
using Xunit;

namespace AirType.Tests.Services.Dictionary;

public sealed class DictionaryPromptBuilderTests
{
    [Fact]
    public void BuildDictionarySection_UsesContextAwareCorrectionWording()
    {
        var entries = new[]
        {
            NewVocabulary("AirType", DateTime.UtcNow),
            NewCorrection("air type", "AirType", DateTime.UtcNow)
        };
        var builder = new DictionaryPromptBuilder(new FakeDictionaryManager(entries));

        string result = builder.BuildDictionarySection();

        Assert.Contains("Known vocabulary: AirType", result);
        Assert.Contains("Likely corrections (apply when the context fits):", result);
        Assert.Contains("- \"air type\" -> correct to \"AirType\" when the context fits", result);
        Assert.DoesNotContain("should be written as", result);
    }

    [Fact]
    public void BuildDictionarySection_DoesNotTruncateCleanupDictionary()
    {
        var entries = new List<DictionaryEntry>();
        for (int i = 0; i < 260; i++)
        {
            entries.Add(NewVocabulary($"DomainTerm{i:000}", DateTime.UtcNow.AddMinutes(-i)));
        }

        var builder = new DictionaryPromptBuilder(new FakeDictionaryManager(entries));

        string result = builder.BuildDictionarySection();

        Assert.True(result.Length > 2000, $"Expected untruncated dictionary section, got {result.Length} chars.");
        Assert.Contains("DomainTerm259", result);
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
