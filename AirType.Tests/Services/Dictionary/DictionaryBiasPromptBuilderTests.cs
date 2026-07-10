using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AirType.Models.Dictionary;
using AirType.Services.Dictionary;
using Xunit;

namespace AirType.Tests.Services.Dictionary;

public sealed class DictionaryBiasPromptBuilderTests
{
    [Fact]
    public void BuildBiasPrompt_WhenDictionaryIsEmpty_ReturnsEmpty()
    {
        var builder = new DictionaryBiasPromptBuilder(new FakeDictionaryManager(Array.Empty<DictionaryEntry>()));

        Assert.Equal(string.Empty, builder.BuildBiasPrompt(maxTokens: 224));
    }

    [Fact]
    public void BuildBiasPrompt_IncludesVocabularyAndCorrectionPairs()
    {
        var entries = new[]
        {
            NewVocabulary("NAudio", DateTime.UtcNow.AddMinutes(-1)),
            NewVocabulary("AirType", DateTime.UtcNow),
            NewCorrection("air type", "AirType", DateTime.UtcNow),
            NewCorrection("open router", "OpenRouter", DateTime.UtcNow.AddMinutes(-1))
        };
        var builder = new DictionaryBiasPromptBuilder(new FakeDictionaryManager(entries));

        string prompt = builder.BuildBiasPrompt(maxTokens: 224);

        Assert.Contains("Vocabulary: AirType, NAudio.", prompt);
        Assert.Contains("Corrections: \"air type\" -> \"AirType\"; \"open router\" -> \"OpenRouter\".", prompt);
    }

    [Fact]
    public void BuildBiasPrompt_DoesNotIncludeFormattingOrWorkflowInstructions()
    {
        var entries = new[]
        {
            NewVocabulary("AirType", DateTime.UtcNow),
            NewCorrection("open router", "OpenRouter", DateTime.UtcNow)
        };
        var builder = new DictionaryBiasPromptBuilder(new FakeDictionaryManager(entries));

        string prompt = builder.BuildBiasPrompt(maxTokens: 224);

        Assert.DoesNotContain("Markdown", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paragraph", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bullet", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("format", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBiasPrompt_RespectsApproximateTokenBudget()
    {
        var entries = new List<DictionaryEntry>();
        for (int i = 0; i < 20; i++)
        {
            entries.Add(NewVocabulary($"DomainTerm{i:00}", DateTime.UtcNow.AddMinutes(-i)));
        }

        var builder = new DictionaryBiasPromptBuilder(new FakeDictionaryManager(entries));

        string prompt = builder.BuildBiasPrompt(maxTokens: 12);

        Assert.True(prompt.Length <= 48, $"Prompt length was {prompt.Length}: {prompt}");
        Assert.Contains("DomainTerm00", prompt);
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
