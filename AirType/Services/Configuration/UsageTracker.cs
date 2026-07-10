using System;
using System.IO;
using System.Text.Json;

namespace AirType.Services.Configuration;

/// <summary>
/// Tracks developer fallback API key usage with persistent JSON storage.
/// </summary>
public class UsageTracker : IUsageTracker
{
    private readonly string _usageFilePath;
    private UsageData? _cachedData;
    private readonly object _lockObject = new();

    private const int FALLBACK_USAGE_LIMIT = 100;
    private const int OPENROUTER_FALLBACK_LIMIT = 100;
    private const int GROQ_FALLBACK_LIMIT = 100;

    /// <summary>
    /// Initializes a new instance of UsageTracker.
    /// Creates usage file in %LOCALAPPDATA%\AirType\usage.json
    /// </summary>
    public UsageTracker()
    {
        string appFolder = AirTypeStoragePaths.CanonicalRoot;
        Directory.CreateDirectory(appFolder);
        _usageFilePath = Path.Combine(appFolder, "usage.json");
    }

    /// <summary>
    /// Gets the current fallback usage count from persistent storage.
    /// </summary>
    public int GetFallbackUsageCount()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            return data.FallbackUsageCount;
        }
    }

    /// <summary>
    /// Increments fallback usage counter and persists to disk.
    /// </summary>
    public void IncrementFallbackUsage()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            data.FallbackUsageCount++;
            data.LastFallbackUsed = DateTime.UtcNow;
            SaveUsageData(data);
            _cachedData = data;
        }
    }

    /// <summary>
    /// Calculates remaining quota based on current usage.
    /// </summary>
    public int GetRemainingFallbackQuota()
    {
        return Math.Max(0, FALLBACK_USAGE_LIMIT - GetFallbackUsageCount());
    }

    /// <summary>
    /// Resets usage counter to zero. For testing purposes.
    /// </summary>
    public void ResetFallbackUsage()
    {
        lock (_lockObject)
        {
            UsageData data = new UsageData
            {
                FallbackUsageCount = 0,
                LastFallbackUsed = null
            };
            SaveUsageData(data);
            _cachedData = data;
        }
    }

    /// <summary>
    /// Gets the current OpenRouter fallback usage count from persistent storage.
    /// </summary>
    public int GetOpenRouterFallbackUsageCount()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            return data.OpenRouterFallbackUsageCount;
        }
    }

    /// <summary>
    /// Increments OpenRouter fallback usage counter and persists to disk.
    /// </summary>
    public void IncrementOpenRouterFallbackUsage()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            data.OpenRouterFallbackUsageCount++;
            data.LastOpenRouterFallbackUsed = DateTime.UtcNow;
            SaveUsageData(data);
            _cachedData = data;
        }
    }

    /// <summary>
    /// Calculates remaining OpenRouter quota based on current usage.
    /// </summary>
    public int GetRemainingOpenRouterQuota()
    {
        return Math.Max(0, OPENROUTER_FALLBACK_LIMIT - GetOpenRouterFallbackUsageCount());
    }

    /// <summary>
    /// Resets OpenRouter usage counter to zero. For testing purposes.
    /// </summary>
    public void ResetOpenRouterFallbackUsage()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            data.OpenRouterFallbackUsageCount = 0;
            data.LastOpenRouterFallbackUsed = null;
            SaveUsageData(data);
            _cachedData = data;
        }
    }

    /// <summary>
    /// Gets the current Groq fallback usage count from persistent storage.
    /// </summary>
    public int GetGroqFallbackUsageCount()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            return data.GroqFallbackUsageCount;
        }
    }

    /// <summary>
    /// Increments Groq fallback usage counter and persists to disk.
    /// </summary>
    public void IncrementGroqFallbackUsage()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            data.GroqFallbackUsageCount++;
            data.LastGroqFallbackUsed = DateTime.UtcNow;
            SaveUsageData(data);
            _cachedData = data;
        }
    }

    /// <summary>
    /// Calculates remaining Groq quota based on current usage.
    /// </summary>
    public int GetRemainingGroqQuota()
    {
        return Math.Max(0, GROQ_FALLBACK_LIMIT - GetGroqFallbackUsageCount());
    }

    /// <summary>
    /// Resets Groq usage counter to zero. For testing purposes.
    /// </summary>
    public void ResetGroqFallbackUsage()
    {
        lock (_lockObject)
        {
            UsageData data = LoadUsageData();
            data.GroqFallbackUsageCount = 0;
            data.LastGroqFallbackUsed = null;
            SaveUsageData(data);
            _cachedData = data;
        }
    }

    /// <summary>
    /// Loads usage data from JSON file with caching.
    /// </summary>
    private UsageData LoadUsageData()
    {
        if (_cachedData != null)
            return _cachedData;

        try
        {
            if (File.Exists(_usageFilePath))
            {
                string json = File.ReadAllText(_usageFilePath);
                _cachedData = JsonSerializer.Deserialize<UsageData>(json) ?? new UsageData();
                return _cachedData;
            }
        }
        catch
        {
            // If file is corrupted, start fresh
        }

        _cachedData = new UsageData();
        return _cachedData;
    }

    /// <summary>
    /// Saves usage data to JSON file.
    /// </summary>
    private void SaveUsageData(UsageData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_usageFilePath, json);
        }
        catch
        {
            // Failed to save - not critical, will retry next time
        }
    }

    /// <summary>
    /// Internal data structure for JSON serialization.
    /// </summary>
    private class UsageData
    {
        public int FallbackUsageCount { get; set; }
        public DateTime? LastFallbackUsed { get; set; }
        public int OpenRouterFallbackUsageCount { get; set; }
        public DateTime? LastOpenRouterFallbackUsed { get; set; }
        public int GroqFallbackUsageCount { get; set; }
        public DateTime? LastGroqFallbackUsed { get; set; }
    }
}
