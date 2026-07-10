namespace AirType.Services.Dictionary;

public interface IDictionaryHotwordBuilder
{
    DictionaryHotwordResult BuildHotwords(int maxTokens);
}

public sealed record DictionaryHotwordResult(string Text, int TokenCount, IReadOnlyList<string> Terms);
