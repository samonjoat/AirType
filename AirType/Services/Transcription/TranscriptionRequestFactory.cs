using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Creates raw STT requests without cleanup or dictionary rewrite instructions.
/// </summary>
public static class TranscriptionRequestFactory
{
    public static GroqTranscriptionRequest CreateGroqRawRequest(string modelId) =>
        new()
        {
            ModelId = modelId,
            Prompt = null
        };

    public static OpenRouterSttTranscriptionRequest CreateOpenRouterSttRawRequest(string modelId) =>
        new()
        {
            ModelId = modelId,
            DictionaryBiasPrompt = null
        };
}
