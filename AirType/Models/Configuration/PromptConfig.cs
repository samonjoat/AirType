namespace AirType.Models.Configuration;

/// <summary>
/// Stores which prompt profile should be active across application sessions.
/// </summary>
public class PromptConfig
{
    public const string DefaultProfileName = "Classic";

    /// <summary>
    /// Name of the currently selected profile. Defaults to the Classic prompt.
    /// </summary>
    public string ActiveProfile { get; set; } = DefaultProfileName;

    /// <summary>
    /// Provides an easy way to get a copy of the defaults.
    /// </summary>
    public static PromptConfig Default => new();
}
