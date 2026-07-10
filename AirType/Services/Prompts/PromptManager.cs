using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AirType.Models.Configuration;
using AirType.Models;
using AirType.Services;

namespace AirType.Services.Prompts;

/// <summary>
/// Manages prompt profiles, including persistence for active selection and custom prompt text.
/// </summary>
public class PromptManager : IPromptManager
{
    public const string CustomProfileName = "Custom";
    private const string ComponentName = "PromptManager";
    private const string ConfigFileName = "prompt_config.json";
    private const string CustomPromptFileName = "custom_prompt.txt";

    private readonly string _storageDirectory;
    private readonly string _configFilePath;
    private readonly string _customPromptFilePath;
    private readonly List<PromptProfile> _builtInProfiles;
    private readonly Dictionary<string, PromptProfile> _builtInProfileLookup;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _syncRoot = new();

    private PromptConfig _config;
    private PromptProfile? _customProfile;

    /// <summary>
    /// Initializes the manager using the default %LOCALAPPDATA%\AirType folder.
    /// </summary>
    public PromptManager()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes the manager using a specific storage path (primarily for testing).
    /// </summary>
    public PromptManager(string? dictationAppFolderOverride)
    {
        _storageDirectory = string.IsNullOrWhiteSpace(dictationAppFolderOverride)
            ? AirTypeStoragePaths.CanonicalRoot
            : dictationAppFolderOverride!;

        Directory.CreateDirectory(_storageDirectory);

        _configFilePath = Path.Combine(_storageDirectory, ConfigFileName);
        _customPromptFilePath = Path.Combine(_storageDirectory, CustomPromptFileName);

        _builtInProfiles = BuiltInPrompts.All.Select(CloneProfile).ToList();
        _builtInProfileLookup = _builtInProfiles.ToDictionary(
            profile => profile.Name,
            profile => profile,
            StringComparer.OrdinalIgnoreCase);

        _customProfile = LoadCustomPrompt();
        _config = LoadConfig();

        EnsureActiveProfileIsValid();
    }

    public IReadOnlyList<PromptProfile> GetAllProfiles()
    {
        lock (_syncRoot)
        {
            var list = new List<PromptProfile>(_builtInProfiles.Select(CloneProfile));
            if (_customProfile != null)
            {
                list.Add(CloneProfile(_customProfile));
            }

            return list;
        }
    }

    public PromptProfile? GetProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return null;

        lock (_syncRoot)
        {
            return GetProfileInternal(profileName);
        }
    }

    public PromptProfile GetActiveProfile()
    {
        lock (_syncRoot)
        {
            var profile = GetProfileInternal(_config.ActiveProfile);
            if (profile != null)
            {
                return profile;
            }

            Logger.Warn(ComponentName,
                $"Active profile '{_config.ActiveProfile}' missing. Falling back to '{PromptConfig.DefaultProfileName}'.");

            _config.ActiveProfile = PromptConfig.DefaultProfileName;
            SaveConfigUnsafe();
            return CloneProfile(_builtInProfileLookup[PromptConfig.DefaultProfileName]);
        }
    }

    public bool SetActiveProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("Profile name cannot be empty.", nameof(profileName));

        lock (_syncRoot)
        {
            var profile = GetProfileInternal(profileName, clone: false);
            if (profile == null)
            {
                Logger.Warn(ComponentName, $"Attempted to set unknown profile '{profileName}'.");
                return false;
            }

            _config.ActiveProfile = profile.Name;
            SaveConfigUnsafe();
            return true;
        }
    }

    public PromptProfile SaveCustomPrompt(string systemInstruction)
    {
        if (string.IsNullOrWhiteSpace(systemInstruction))
            throw new ArgumentException("System instruction cannot be empty.", nameof(systemInstruction));

        lock (_syncRoot)
        {
            File.WriteAllText(_customPromptFilePath, systemInstruction);
            _customProfile = new PromptProfile
            {
                Name = CustomProfileName,
                Content = systemInstruction,
                IsBuiltIn = false
            };

            _config.ActiveProfile = _customProfile.Name;
            SaveConfigUnsafe();

            Logger.Info(ComponentName, "Custom prompt saved and set as active.");
            return CloneProfile(_customProfile);
        }
    }

    public bool HasCustomPrompt()
    {
        lock (_syncRoot)
        {
            return _customProfile != null;
        }
    }

    private PromptProfile? GetProfileInternal(string? profileName, bool clone = true)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return null;

        if (string.Equals(profileName, CustomProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return _customProfile == null
                ? null
                : clone
                    ? CloneProfile(_customProfile)
                    : _customProfile;
        }

        if (_builtInProfileLookup.TryGetValue(profileName, out var profile))
        {
            return clone ? CloneProfile(profile) : profile;
        }

        return null;
    }

    private PromptConfig LoadConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return PromptConfig.Default;
            }

            string json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<PromptConfig>(json, _jsonOptions);
            if (config == null || string.IsNullOrWhiteSpace(config.ActiveProfile))
            {
                return PromptConfig.Default;
            }

            return config;
        }
        catch (Exception ex)
        {
            Logger.Warn(ComponentName,
                $"Failed to load prompt config. Using defaults. Error: {ex.Message}");
            return PromptConfig.Default;
        }
    }

    private void SaveConfigUnsafe()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, _jsonOptions);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ComponentName, "Failed to save prompt config.", ex);
            throw;
        }
    }

    private PromptProfile? LoadCustomPrompt()
    {
        try
        {
            if (!File.Exists(_customPromptFilePath))
            {
                return null;
            }

            string instruction = File.ReadAllText(_customPromptFilePath);
            if (string.IsNullOrWhiteSpace(instruction))
            {
                Logger.Warn(ComponentName, "Custom prompt file was empty; ignoring.");
                return null;
            }

            return new PromptProfile
            {
                Name = CustomProfileName,
                Content = instruction,
                IsBuiltIn = false
            };
        }
        catch (Exception ex)
        {
            Logger.Warn(ComponentName,
                $"Failed to load custom prompt. Error: {ex.Message}");
            return null;
        }
    }

    private void EnsureActiveProfileIsValid()
    {
        lock (_syncRoot)
        {
            if (GetProfileInternal(_config.ActiveProfile, clone: false) != null)
            {
                return;
            }

            _config.ActiveProfile = PromptConfig.DefaultProfileName;
            SaveConfigUnsafe();
        }
    }

    private static PromptProfile CloneProfile(PromptProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Content = profile.Content,
        IsBuiltIn = profile.IsBuiltIn
    };
}
