namespace AirType.Services.Transcription;

public sealed class RollingInitialPromptBuilder : IRollingInitialPromptBuilder
{
    private readonly object _lockObject = new();
    private readonly Dictionary<Guid, List<string>> _recordingText = new();

    public string? BuildPrompt(Guid recordingId, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return null;
        }

        lock (_lockObject)
        {
            if (!_recordingText.TryGetValue(recordingId, out var utterances) || utterances.Count == 0)
            {
                return null;
            }

            var tokens = utterances
                .SelectMany(SplitTokens)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToList();

            if (tokens.Count == 0)
            {
                return null;
            }

            return string.Join(" ", tokens.TakeLast(maxTokens));
        }
    }

    public void AppendUtterance(Guid recordingId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_lockObject)
        {
            if (!_recordingText.TryGetValue(recordingId, out var utterances))
            {
                utterances = new List<string>();
                _recordingText[recordingId] = utterances;
            }

            utterances.Add(text.Trim());
        }
    }

    public void Reset(Guid recordingId)
    {
        lock (_lockObject)
        {
            _recordingText.Remove(recordingId);
        }
    }

    private static IEnumerable<string> SplitTokens(string text) =>
        text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
}
