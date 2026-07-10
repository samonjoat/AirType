namespace AirType.Services.Configuration;

/// <summary>
/// Tracks usage of developer fallback API key to enforce quota limits.
/// </summary>
public interface IUsageTracker
{
    /// <summary>
    /// Gets the current count of fallback API key usage.
    /// </summary>
    /// <returns>Number of times fallback key has been used</returns>
    int GetFallbackUsageCount();

    /// <summary>
    /// Increments the fallback usage counter by one.
    /// Should be called each time the fallback key is used for transcription.
    /// </summary>
    void IncrementFallbackUsage();

    /// <summary>
    /// Gets the remaining quota for fallback API key usage.
    /// </summary>
    /// <returns>Number of free transcriptions remaining</returns>
    int GetRemainingFallbackQuota();

    /// <summary>
    /// Resets fallback usage counter to zero.
    /// Used for testing purposes.
    /// </summary>
    void ResetFallbackUsage();

    // ============================================================
    // OpenRouter Fallback Tracking (Task 2 - OpenRouter MVP)
    // ============================================================

    /// <summary>
    /// Gets the current count of OpenRouter fallback API key usage.
    /// </summary>
    /// <returns>Number of times OpenRouter fallback key has been used</returns>
    int GetOpenRouterFallbackUsageCount();

    /// <summary>
    /// Increments the OpenRouter fallback usage counter by one.
    /// Should be called each time the OpenRouter fallback key is used for transcription.
    /// </summary>
    void IncrementOpenRouterFallbackUsage();

    /// <summary>
    /// Gets the remaining quota for OpenRouter fallback API key usage.
    /// </summary>
    /// <returns>Number of free OpenRouter transcriptions remaining</returns>
    int GetRemainingOpenRouterQuota();

    /// <summary>
    /// Resets OpenRouter fallback usage counter to zero.
    /// Used for testing purposes.
    /// </summary>
    void ResetOpenRouterFallbackUsage();

    // ============================================================
    // Groq Fallback Tracking (Provider Expansion)
    // ============================================================

    /// <summary>
    /// Gets the current count of Groq fallback API key usage.
    /// </summary>
    int GetGroqFallbackUsageCount();

    /// <summary>
    /// Increments Groq fallback usage counter and persists to disk.
    /// </summary>
    void IncrementGroqFallbackUsage();

    /// <summary>
    /// Gets the remaining quota for Groq fallback API key usage.
    /// </summary>
    int GetRemainingGroqQuota();

    /// <summary>
    /// Resets Groq fallback usage counter (testing only).
    /// </summary>
    void ResetGroqFallbackUsage();
}
