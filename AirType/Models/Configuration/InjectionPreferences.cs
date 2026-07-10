using System;
using System.Collections.Generic;

namespace AirType.Models.Configuration;

/// <summary>
/// Stores per-application injection method preferences.
/// </summary>
public sealed class InjectionPreferences
{
    /// <summary>
    /// Maps process name to preferred injection method and failure count.
    /// </summary>
    public Dictionary<string, AppInjectionPreference> AppPreferences { get; set; } = new();
}

/// <summary>
/// Represents injection preferences for a specific application.
/// </summary>
public sealed class AppInjectionPreference
{
    /// <summary>
    /// Preferred injection method for this application (the last one that succeeded).
    /// Valid values: "UIAutomation", "Win32Message", "ClipboardPaste", "ClipboardOnly"
    /// </summary>
    public string PreferredMethod { get; set; } = "UIAutomation";

    /// <summary>
    /// Number of consecutive failures with the preferred method.
    /// </summary>
    public int ConsecutiveFailures { get; set; } = 0;

    /// <summary>
    /// Tracks consecutive failure counts per injection method.
    /// Methods with 3+ failures will be skipped.
    /// </summary>
    public Dictionary<string, int> MethodFailures { get; set; } = new();

    /// <summary>
    /// Last successful injection method.
    /// </summary>
    public string? LastSuccessfulMethod { get; set; }

    /// <summary>
    /// Timestamp of last injection attempt.
    /// </summary>
    public DateTime LastAttempt { get; set; } = DateTime.UtcNow;
}
