namespace AirType.Models.Configuration;

/// <summary>
/// Selects how transcript cleanup chooses destination-specific guidance.
/// </summary>
public enum CleanupContextMode
{
    Auto = 0,
    Terminal = 1,
    Editor = 2,
    Email = 3,
    Chat = 4,
    Generic = 5
}
