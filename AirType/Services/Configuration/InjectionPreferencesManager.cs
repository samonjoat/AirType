using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AirType.Models.Configuration;

namespace AirType.Services.Configuration;

/// <summary>
/// Manages per-application text injection method preferences.
/// </summary>
public sealed class InjectionPreferencesManager
{
    private static readonly string PreferencesFilePath = Path.Combine(
        AirTypeStoragePaths.CanonicalRoot,
        "injection_preferences.json");

    // Skip method after just 1 failure (fast learning)
    private static readonly int MaxConsecutiveFailures = 1;
    
    // Reset failure counts after 24 hours (self-healing)
    private static readonly TimeSpan FailureResetPeriod = TimeSpan.FromHours(24);
    private InjectionPreferences _preferences;
    private readonly object _lock = new();

    public InjectionPreferencesManager()
    {
        _preferences = LoadPreferences();
    }

    /// <summary>
    /// Gets the preferred injection method for the specified application.
    /// Returns null if no preference is set or if the method has failed too many times.
    /// </summary>
    public string? GetPreferredMethod(string processName)
    {
        lock (_lock)
        {
            if (_preferences.AppPreferences.TryGetValue(processName, out var pref))
            {
                // Reset preference if too many consecutive failures
                if (pref.ConsecutiveFailures >= MaxConsecutiveFailures)
                {
                    return null;
                }
                return pref.PreferredMethod;
            }
            return null;
        }
    }

    /// <summary>
    /// Checks if a specific method should be skipped for the specified application
    /// due to recent failures. Resets after 24 hours.
    /// </summary>
    public bool ShouldSkipMethod(string processName, string method)
    {
        lock (_lock)
        {
            if (_preferences.AppPreferences.TryGetValue(processName, out var pref))
            {
                // Check if failure data has expired (24 hours)
                if (DateTime.UtcNow - pref.LastAttempt > FailureResetPeriod)
                {
                    // Reset all failure counts - give methods another chance
                    pref.MethodFailures.Clear();
                    Logger.Debug("InjectionPreferences", $"Reset failure counts for {processName} (24h expired)");
                    return false;
                }
                
                // Check method-specific failure counts
                if (pref.MethodFailures.TryGetValue(method, out int failures))
                {
                    return failures >= MaxConsecutiveFailures;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Records a successful injection with the specified method.
    /// Resets the failure count for this method.
    /// </summary>
    public async Task RecordSuccessAsync(string processName, string method)
    {
        lock (_lock)
        {
            if (!_preferences.AppPreferences.TryGetValue(processName, out var pref))
            {
                pref = new AppInjectionPreference();
                _preferences.AppPreferences[processName] = pref;
            }

            pref.PreferredMethod = method;
            pref.LastSuccessfulMethod = method;
            pref.ConsecutiveFailures = 0;
            pref.LastAttempt = DateTime.UtcNow;
            
            // Reset failure count for this specific method
            pref.MethodFailures[method] = 0;
        }

        await SavePreferencesAsync();
    }

    /// <summary>
    /// Records a failed injection attempt with the specified method.
    /// Increments the failure count for this specific method.
    /// </summary>
    public async Task RecordFailureAsync(string processName, string method)
    {
        lock (_lock)
        {
            if (!_preferences.AppPreferences.TryGetValue(processName, out var pref))
            {
                pref = new AppInjectionPreference();
                _preferences.AppPreferences[processName] = pref;
            }

            // Increment failure count for this specific method
            if (!pref.MethodFailures.TryGetValue(method, out int failures))
            {
                failures = 0;
            }
            pref.MethodFailures[method] = failures + 1;
            
            pref.LastAttempt = DateTime.UtcNow;
        }

        await SavePreferencesAsync();
    }

    /// <summary>
    /// Resets preferences for the specified application.
    /// </summary>
    public async Task ResetPreferencesAsync(string processName)
    {
        lock (_lock)
        {
            _preferences.AppPreferences.Remove(processName);
        }

        await SavePreferencesAsync();
    }

    private InjectionPreferences LoadPreferences()
    {
        try
        {
            if (File.Exists(PreferencesFilePath))
            {
                var json = File.ReadAllText(PreferencesFilePath);
                var preferences = JsonSerializer.Deserialize<InjectionPreferences>(json) ?? new InjectionPreferences();
                
                // Migrate old method names to new ones
                MigratePreferences(preferences);
                
                return preferences;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("InjectionPreferences", "Failed to load injection preferences", ex);
        }

        return new InjectionPreferences();
    }

    /// <summary>
    /// Migrates old injection method names to new ones.
    /// SendKeys → ClipboardPaste (SendKeys was removed)
    /// </summary>
    private void MigratePreferences(InjectionPreferences preferences)
    {
        bool needsMigration = false;

        foreach (var kvp in preferences.AppPreferences)
        {
            var pref = kvp.Value;
            
            // Migrate old SendKeys method to ClipboardPaste
            if (pref.PreferredMethod == "SendKeys")
            {
                pref.PreferredMethod = "ClipboardPaste";
                needsMigration = true;
            }
            
            if (pref.LastSuccessfulMethod == "SendKeys")
            {
                pref.LastSuccessfulMethod = "ClipboardPaste";
                needsMigration = true;
            }
        }

        // Save migrated preferences
        if (needsMigration)
        {
            Logger.Info("InjectionPreferences", "Migrated old injection method names to new format");
            _ = SavePreferencesAsync(); // Fire and forget
        }
    }

    private async Task SavePreferencesAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencesFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_preferences, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(PreferencesFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("InjectionPreferences", "Failed to save injection preferences", ex);
        }
    }
}
