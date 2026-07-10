using AirType.Models;

namespace AirType.Services.Prompts;

/// <summary>
/// Provides access to built-in and custom prompt profiles plus persistence for the active selection.
/// </summary>
public interface IPromptManager
{
    /// <summary>
    /// Returns all available prompt profiles (built-in plus custom when present).
    /// </summary>
    IReadOnlyList<PromptProfile> GetAllProfiles();

    /// <summary>
    /// Returns the profile with the provided name or <c>null</c> when not found.
    /// </summary>
    PromptProfile? GetProfile(string profileName);

    /// <summary>
    /// Gets the currently active profile, falling back to Classic if the stored selection is invalid.
    /// </summary>
    PromptProfile GetActiveProfile();

    /// <summary>
    /// Sets the active profile. Returns <c>true</c> if the profile exists; otherwise <c>false</c>.
    /// </summary>
    bool SetActiveProfile(string profileName);

    /// <summary>
    /// Saves or updates the custom prompt text, marks it as active, and returns the stored profile.
    /// </summary>
    PromptProfile SaveCustomPrompt(string systemInstruction);

    /// <summary>
    /// Indicates whether a custom prompt is currently available on disk.
    /// </summary>
    bool HasCustomPrompt();
}
