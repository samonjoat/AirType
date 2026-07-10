using System;

namespace AirType.Services.Dictionary;

/// <summary>
/// Service responsible for building dictionary sections for inclusion in transcription API prompts.
/// </summary>
public interface IDictionaryPromptBuilder
{
    /// <summary>
    /// Builds a formatted dictionary section for inclusion in the system instruction.
    /// Formats VocabularyWord entries as a comma-separated list and CorrectionPair entries as correction hints.
    /// </summary>
    /// <returns>
    /// A formatted string containing the dictionary section, or an empty string if the dictionary is empty.
    /// Output is capped at 2000 characters.
    /// </returns>
    string BuildDictionarySection();

    /// <summary>
    /// Gets the estimated token count of the dictionary section.
    /// Useful for monitoring prompt size.
    /// </summary>
    /// <returns>Estimated token count (approximately 1 token per 4 characters).</returns>
    int GetEstimatedTokenCount();
}
