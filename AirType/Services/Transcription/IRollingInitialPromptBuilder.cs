namespace AirType.Services.Transcription;

public interface IRollingInitialPromptBuilder
{
    string? BuildPrompt(Guid recordingId, int maxTokens);

    void AppendUtterance(Guid recordingId, string text);

    void Reset(Guid recordingId);
}
