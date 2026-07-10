namespace AirType.Models.Configuration;

/// <summary>
/// Indicates the source of the API key being used for transcription.
/// </summary>
public enum ApiKeySource
{
    /// <summary>
    /// No API key is available. Transcription features are disabled.
    /// </summary>
    None,

    /// <summary>
    /// User provided their own API key stored in Windows Credential Manager.
    /// Unlimited usage (user pays for API calls).
    /// </summary>
    UserProvided,

    /// <summary>
    /// Using developer's fallback API key with limited quota.
    /// Limited to 100 free transcriptions for trial purposes.
    /// </summary>
    DeveloperFallback
}
