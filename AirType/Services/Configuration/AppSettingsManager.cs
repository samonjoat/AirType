using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using AirType.Models.Configuration;

namespace AirType.Services.Configuration;

/// <summary>
/// Manages application-wide settings with JSON persistence.
/// </summary>
public sealed class AppSettingsManager
{
    private const string StartupRunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupValueName = "AirType";
    private const string LegacyStartupValueName = "ModernDictationApp";

    private static readonly string DefaultSettingsFilePath = Path.Combine(
        AirTypeStoragePaths.CanonicalRoot,
        "app_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private AppSettings _settings;
    private readonly object _lock = new();
    private readonly string _settingsFilePath;
    private readonly bool _applyStartupRegistry;
    private readonly IStartupRegistry _startupRegistry;
    private readonly Func<string?> _getExecutablePath;

    public AppSettingsManager()
        : this(DefaultSettingsFilePath)
    {
    }

    internal AppSettingsManager(string settingsFilePath, bool applyStartupRegistry = true)
        : this(settingsFilePath, applyStartupRegistry, new CurrentUserStartupRegistry(), GetCurrentExecutablePath)
    {
    }

    internal AppSettingsManager(
        string settingsFilePath,
        bool applyStartupRegistry,
        IStartupRegistry startupRegistry,
        Func<string?> getExecutablePath)
    {
        _settingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
        _applyStartupRegistry = applyStartupRegistry;
        _startupRegistry = startupRegistry ?? throw new ArgumentNullException(nameof(startupRegistry));
        _getExecutablePath = getExecutablePath ?? throw new ArgumentNullException(nameof(getExecutablePath));
        _settings = LoadSettings();
        // Sync the registry with the persisted startup preference
        if (_applyStartupRegistry)
        {
            ApplyStartupRegistryKey(_settings.LaunchOnStartup);
        }
    }

    /// <summary>
    /// Gets or sets the audio retention period in days.
    /// </summary>
    public int AudioRetentionDays
    {
        get
        {
            lock (_lock)
            {
                return _settings.AudioRetentionDays;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.AudioRetentionDays != value)
                {
                    _settings.AudioRetentionDays = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the global hotkey key.
    /// </summary>
    public System.Windows.Forms.Keys GlobalHotkeyKey
    {
        get
        {
            lock (_lock)
            {
                return _settings.GlobalHotkeyKey;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.GlobalHotkeyKey != value)
                {
                    _settings.GlobalHotkeyKey = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the global hotkey modifiers.
    /// </summary>
    public System.Windows.Input.ModifierKeys GlobalHotkeyModifiers
    {
        get
        {
            lock (_lock)
            {
                return _settings.GlobalHotkeyModifiers;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.GlobalHotkeyModifiers != value)
                {
                    _settings.GlobalHotkeyModifiers = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the date of the user's first transcription.
    /// This value is set once when the first transcription is completed.
    /// </summary>
    public DateTime? FirstTranscriptionDate
    {
        get
        {
            lock (_lock)
            {
                return _settings.FirstTranscriptionDate;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.FirstTranscriptionDate != value)
                {
                    _settings.FirstTranscriptionDate = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the app should launch on Windows startup.
    /// When changed, the Windows Registry Run key is updated immediately.
    /// </summary>
    public bool LaunchOnStartup
    {
        get
        {
            lock (_lock)
            {
                return _settings.LaunchOnStartup;
            }
        }
        set
        {
            var shouldApplyStartup = false;

            lock (_lock)
            {
                if (_settings.LaunchOnStartup != value)
                {
                    _settings.LaunchOnStartup = value;
                    SaveSettings();
                    shouldApplyStartup = true;
                }
            }

            if (shouldApplyStartup && _applyStartupRegistry)
            {
                ApplyStartupRegistryKey(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the close button minimizes to tray instead of exiting.
    /// </summary>
    public bool MinimizeToTray
    {
        get
        {
            lock (_lock)
            {
                return _settings.MinimizeToTray;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.MinimizeToTray != value)
                {
                    _settings.MinimizeToTray = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the app should mute system playback while recording.
    /// </summary>
    public bool MuteSystemAudioDuringRecording
    {
        get
        {
            lock (_lock)
            {
                return _settings.MuteSystemAudioDuringRecording;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.MuteSystemAudioDuringRecording != value)
                {
                    _settings.MuteSystemAudioDuringRecording = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets whether context-aware smart insertion is enabled.
    /// </summary>
    public bool EnableSmartInsertion
    {
        get
        {
            lock (_lock)
            {
                return _settings.EnableSmartInsertion;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.EnableSmartInsertion != value)
                {
                    _settings.EnableSmartInsertion = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the native Unicode typing fallback is enabled.
    /// </summary>
    public bool EnableNativeTypingInjection
    {
        get
        {
            lock (_lock)
            {
                return _settings.EnableNativeTypingInjection;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_settings.EnableNativeTypingInjection != value)
                {
                    _settings.EnableNativeTypingInjection = value;
                    SaveSettings();
                }
            }
        }
    }

    /// <summary>
    /// Triggers an immediate cleanup of old audio and log files based on current settings.
    /// </summary>
    public void CleanupOldFilesNow()
    {
        int days = AudioRetentionDays;
        if (days <= 0) return;

        // Try to get the persistence manager from the app services
        if (System.Windows.Application.Current is App app && app.Services?.FilePersistenceManager != null)
        {
            app.Services.FilePersistenceManager.CleanupOldAudioFiles(days);
        }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    // Validate the retention days value
                    if (!IsValidRetentionDays(settings.AudioRetentionDays))
                    {
                        Logger.Warn("AppSettingsManager", 
                            $"Invalid AudioRetentionDays value {settings.AudioRetentionDays}, resetting to 0 (never delete)");
                        settings.AudioRetentionDays = 0;
                    }
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AppSettingsManager", "Failed to load app settings", ex);
        }

        return AppSettings.Default;
    }

    private void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
            Logger.Debug("AppSettingsManager", "Settings saved successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("AppSettingsManager", "Failed to save app settings", ex);
        }
    }

    /// <summary>
    /// Validates that a retention days value is one of the allowed options.
    /// </summary>
    private static bool IsValidRetentionDays(int days)
    {
        return days == 0 || days == 30 || days == 90 || days == 180;
    }

    private static string? GetCurrentExecutablePath()
    {
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }

    /// <summary>
    /// Sets or removes the Windows Registry Run key for auto-start on login.
    /// Uses HKCU so no admin rights are required.
    /// </summary>
    private void ApplyStartupRegistryKey(bool enable)
    {
        try
        {
            if (_startupRegistry.GetValue(StartupRunRegistryKey, LegacyStartupValueName) != null)
            {
                _startupRegistry.DeleteValue(StartupRunRegistryKey, LegacyStartupValueName);
                Logger.Debug("AppSettingsManager", "Legacy ModernDictationApp startup entry removed");
            }

            if (enable)
            {
                var exePath = _getExecutablePath();

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    Logger.Warn("AppSettingsManager", "Startup registry key was not set because the executable path could not be resolved");
                    return;
                }

                var desiredValue = $"\"{exePath}\"";
                var existingValue = _startupRegistry.GetValue(StartupRunRegistryKey, StartupValueName) as string;
                if (!string.Equals(existingValue, desiredValue, StringComparison.Ordinal))
                {
                    _startupRegistry.SetValue(StartupRunRegistryKey, StartupValueName, desiredValue);
                    Logger.Debug("AppSettingsManager", "Startup registry key set");
                }

                ClearStartupApprovalOverride(StartupValueName);
                ClearStartupApprovalOverride(LegacyStartupValueName);
            }
            else
            {
                if (_startupRegistry.GetValue(StartupRunRegistryKey, StartupValueName) != null)
                {
                    _startupRegistry.DeleteValue(StartupRunRegistryKey, StartupValueName);
                    Logger.Debug("AppSettingsManager", "Startup registry key removed");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AppSettingsManager", "Failed to update startup registry key", ex);
        }
    }

    private void ClearStartupApprovalOverride(string valueName)
    {
        if (_startupRegistry.GetValue(StartupApprovedRunRegistryKey, valueName) == null)
        {
            return;
        }

        _startupRegistry.DeleteValue(StartupApprovedRunRegistryKey, valueName);
        Logger.Debug("AppSettingsManager", $"StartupApproved startup entry removed for {valueName}");
    }
}

internal interface IStartupRegistry
{
    object? GetValue(string keyPath, string valueName);

    void SetValue(string keyPath, string valueName, string value);

    void DeleteValue(string keyPath, string valueName);
}

internal sealed class CurrentUserStartupRegistry : IStartupRegistry
{
    public object? GetValue(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(valueName);
    }

    public void SetValue(string keyPath, string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key?.SetValue(valueName, value);
    }

    public void DeleteValue(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
