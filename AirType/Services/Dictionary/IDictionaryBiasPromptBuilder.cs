namespace AirType.Services.Dictionary;

/// <summary>
/// Builds compact transcription-bias prompts for STT endpoints that accept only short context hints.
/// </summary>
public interface IDictionaryBiasPromptBuilder
{
    /// <summary>
    /// Builds a compact prompt containing vocabulary and correction hints only.
    /// </summary>
    string BuildBiasPrompt(int maxTokens);
}
