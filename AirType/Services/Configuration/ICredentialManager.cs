using AirType.Models.Configuration;
using AirType.Services.Database;

namespace AirType.Services.Configuration;

/// <summary>
/// Manages API credentials with hybrid approach supporting both user-provided
/// and developer fallback API keys.
/// </summary>
public interface ICredentialManager
{
    /// <summary>
    /// Saves user's API key to Windows Credential Manager with encryption.
    /// </summary>
    /// <param name="apiKey">The API key to store securely</param>
    /// <exception cref="ArgumentException">Thrown when API key is null or empty</exception>
    /// <exception cref="InvalidOperationException">Thrown when credential storage fails</exception>
    void SaveApiKey(string apiKey);

    /// <summary>
    /// Retrieves user's API key from Windows Credential Manager.
    /// </summary>
    /// <returns>User's API key if stored, null otherwise</returns>
    string? GetUserApiKey();

    /// <summary>
    /// Deletes user's API key from Windows Credential Manager.
    /// </summary>
    void DeleteApiKey();

    /// <summary>
    /// Checks if user has configured their own API key.
    /// </summary>
    /// <returns>True if user key exists, false otherwise</returns>
    bool HasUserApiKey();

    /// <summary>
    /// Gets the active API key using hybrid priority:
    /// 1. User key (highest priority)
    /// 2. Developer fallback key (if enabled and quota available)
    /// 3. Null (no key available)
    /// </summary>
    /// <returns>Active API key or null</returns>
    string? GetActiveApiKey();

    /// <summary>
    /// Gets the source of the currently active API key.
    /// </summary>
    /// <returns>Source of the API key being used</returns>
    ApiKeySource GetApiKeySource();

    /// <summary>
    /// Checks if currently using developer fallback key.
    /// </summary>
    /// <returns>True if using fallback key, false otherwise</returns>
    bool IsUsingFallbackKey();

    // ============================================================
    // OpenRouter API Key Management (Task 2 - OpenRouter MVP)
    // ============================================================

    /// <summary>
    /// Saves user's OpenRouter API key to encrypted storage.
    /// </summary>
    /// <param name="apiKey">The OpenRouter API key to store securely</param>
    void SaveOpenRouterApiKey(string apiKey);

    /// <summary>
    /// Retrieves user's OpenRouter API key from encrypted storage.
    /// </summary>
    /// <returns>User's OpenRouter API key if stored, null otherwise</returns>
    string? GetUserOpenRouterApiKey();

    /// <summary>
    /// Deletes user's OpenRouter API key from encrypted storage.
    /// </summary>
    void DeleteOpenRouterApiKey();

    /// <summary>
    /// Checks if user has configured their own OpenRouter API key.
    /// </summary>
    /// <returns>True if user OpenRouter key exists, false otherwise</returns>
    bool HasUserOpenRouterApiKey();

    /// <summary>
    /// Gets the active OpenRouter API key using hybrid priority:
    /// 1. User key (highest priority)
    /// 2. Developer fallback key (if enabled and quota available)
    /// 3. Null (no key available)
    /// </summary>
    /// <returns>Active OpenRouter API key or null</returns>
    string? GetActiveOpenRouterApiKey();

    /// <summary>
    /// Checks if currently using OpenRouter developer fallback key.
    /// </summary>
    /// <returns>True if using OpenRouter fallback key, false otherwise</returns>
    bool IsUsingOpenRouterFallbackKey();

    // ============================================================
    // Groq API Key Management (Provider Expansion)
    // ============================================================

    void SaveGroqApiKey(string apiKey);
    string? GetUserGroqApiKey();
    void DeleteGroqApiKey();
    bool HasUserGroqApiKey();
    string? GetActiveGroqApiKey();
    bool IsUsingGroqFallbackKey();

    // ============================================================
    // Provider Configuration (Task 3 - Provider Selection)
    // ============================================================

    /// <summary>
    /// Saves the active transcription provider selection.
    /// </summary>
    /// <param name="provider">The provider to use (Gemini or OpenRouter)</param>
    void SaveActiveProvider(TranscriptionProvider provider);

    /// <summary>
    /// Gets the currently active transcription provider.
    /// </summary>
    /// <returns>The active provider (default: Gemini)</returns>
    TranscriptionProvider GetActiveProvider();

    // ============================================================
    // Model Configuration (MVP - Provider & Model Selection UI)
    // ============================================================

    /// <summary>
    /// Gets the active model ID for the current provider.
    /// </summary>
    /// <returns>Model ID string (e.g., "gemini-3.1-flash-lite" or "openai/gpt-4o-mini-transcribe")</returns>
    string GetActiveModelId();

    /// <summary>
    /// Gets the active model ID for a specific provider.
    /// </summary>
    string GetModelId(TranscriptionProvider provider);

    /// <summary>
    /// Sets the active model for a specific provider.
    /// </summary>
    /// <param name="provider">The provider to configure</param>
    /// <param name="modelId">The model ID to use</param>
    void SetActiveModel(TranscriptionProvider provider, string modelId);

    /// <summary>
    /// Toggles between Gemini and OpenRouter providers.
    /// </summary>
    /// <returns>The new active provider after toggling</returns>
    TranscriptionProvider ToggleProvider();

    /// <summary>
    /// Cycles to the next available model for the current provider.
    /// </summary>
    /// <returns>Display name of the new active model</returns>
    string CycleModel();

    /// <summary>
    /// Gets the active model ID for local faster-whisper.
    /// </summary>
    /// <returns>Local model ID</returns>
    string GetLocalModelId();

    /// <summary>
    /// Gets the active model ID for Gemini Direct.
    /// </summary>
    /// <returns>Gemini model ID</returns>
    string GetGeminiModelId();

    /// <summary>
    /// Gets the active model ID for OpenRouter.
    /// </summary>
    /// <returns>OpenRouter model ID</returns>
    string GetOpenRouterModelId();

    /// <returns>Groq model ID</returns>
    string GetGroqModelId();

    /// <summary>
    /// Gets the preferred text formatting mode for transcription output.
    /// </summary>
    TextFormattingMode GetTextFormattingMode();

    /// <summary>
    /// Sets the preferred text formatting mode for transcription output.
    /// </summary>
    void SetTextFormattingMode(TextFormattingMode mode);

    CleanupContextMode GetCleanupContextMode();
    void SetCleanupContextMode(CleanupContextMode mode);
    CleanupIntensity GetCleanupIntensity();
    void SetCleanupIntensity(CleanupIntensity intensity);
    CleanupStyleOverrideKind GetCleanupStyleOverrideKind();
    int? GetCleanupStylePromptId();
    void SetCleanupStyleOverride(CleanupStyleOverrideKind kind, int? promptId);
    int GetCleanupPromptMigrationVersion();
    void MigrateLegacyCleanupPromptSettings(PromptDatabase promptDatabase);

    /// <summary>
    /// Saves the active transcript cleanup provider.
    /// </summary>
    void SaveActiveCleanupProvider(CleanupProvider provider);

    /// <summary>
    /// Gets the active transcript cleanup provider.
    /// </summary>
    CleanupProvider GetActiveCleanupProvider();

    /// <summary>
    /// Gets the active cleanup model ID for the current cleanup provider.
    /// </summary>
    string GetActiveCleanupModelId();

    /// <summary>
    /// Gets the cleanup model ID for a specific cleanup provider.
    /// </summary>
    string GetCleanupModelId(CleanupProvider provider);

    /// <summary>
    /// Sets the cleanup model for a specific cleanup provider.
    /// </summary>
    void SetActiveCleanupModel(CleanupProvider provider, string modelId);

    /// <summary>
    /// Gets whether post-transcription cleanup is enabled.
    /// </summary>
    bool IsTranscriptCleanupEnabled();

    /// <summary>
    /// Enables or disables post-transcription cleanup.
    /// </summary>
    void SetTranscriptCleanupEnabled(bool enabled);
}
