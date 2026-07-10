using AirType.Services.Configuration;
using Xunit;

namespace AirType.Tests.Services;

public sealed class AppSettingsManagerTests
{
    private const string StartupRunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    [Fact]
    public void EnableSmartInsertion_DefaultsOn()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new AppSettingsManager(Path.Combine(tempDirectory.RootPath, "app_settings.json"), applyStartupRegistry: false);

        Assert.True(manager.EnableSmartInsertion);
    }

    [Fact]
    public void EnableSmartInsertion_RoundTripsThroughSettingsFile()
    {
        using var tempDirectory = new TempDirectory();
        string settingsPath = Path.Combine(tempDirectory.RootPath, "app_settings.json");

        var first = new AppSettingsManager(settingsPath, applyStartupRegistry: false);
        first.EnableSmartInsertion = false;

        var second = new AppSettingsManager(settingsPath, applyStartupRegistry: false);

        Assert.False(second.EnableSmartInsertion);
    }

    [Fact]
    public void EnableNativeTypingInjection_DefaultsOn()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new AppSettingsManager(Path.Combine(tempDirectory.RootPath, "app_settings.json"), applyStartupRegistry: false);

        Assert.True(manager.EnableNativeTypingInjection);
    }

    [Fact]
    public void EnableNativeTypingInjection_RoundTripsThroughSettingsFile()
    {
        using var tempDirectory = new TempDirectory();
        string settingsPath = Path.Combine(tempDirectory.RootPath, "app_settings.json");

        var first = new AppSettingsManager(settingsPath, applyStartupRegistry: false);
        first.EnableNativeTypingInjection = false;

        var second = new AppSettingsManager(settingsPath, applyStartupRegistry: false);

        Assert.False(second.EnableNativeTypingInjection);
    }

    [Fact]
    public void Constructor_WithLaunchOnStartupEnabled_RewritesRunValueAndClearsStartupApprovalValues()
    {
        using var tempDirectory = new TempDirectory();
        string settingsPath = Path.Combine(tempDirectory.RootPath, "app_settings.json");
        File.WriteAllText(settingsPath, "{\"LaunchOnStartup\":true}");

        var registry = new FakeStartupRegistry();
        registry.SetRawValue(StartupRunRegistryKey, "AirType", "\"C:\\Old\\AirType.exe\"");
        registry.SetRawValue(StartupRunRegistryKey, "ModernDictationApp", "\"C:\\Old\\ModernDictationApp.exe\"");
        registry.SetRawValue(StartupApprovedRunRegistryKey, "AirType", new byte[] { 3, 0, 0, 0 });
        registry.SetRawValue(StartupApprovedRunRegistryKey, "ModernDictationApp", new byte[] { 3, 0, 0, 0 });

        var manager = new AppSettingsManager(
            settingsPath,
            applyStartupRegistry: true,
            startupRegistry: registry,
            getExecutablePath: () => @"C:\Current\AirType.exe");

        Assert.True(manager.LaunchOnStartup);
        Assert.Equal("\"C:\\Current\\AirType.exe\"", registry.GetValue(StartupRunRegistryKey, "AirType"));
        Assert.Null(registry.GetValue(StartupRunRegistryKey, "ModernDictationApp"));
        Assert.Null(registry.GetValue(StartupApprovedRunRegistryKey, "AirType"));
        Assert.Null(registry.GetValue(StartupApprovedRunRegistryKey, "ModernDictationApp"));
    }

    [Fact]
    public void Constructor_WithLaunchOnStartupDisabled_RemovesRunValueAndLeavesStartupApprovalValues()
    {
        using var tempDirectory = new TempDirectory();
        string settingsPath = Path.Combine(tempDirectory.RootPath, "app_settings.json");
        File.WriteAllText(settingsPath, "{\"LaunchOnStartup\":false}");

        var airTypeApprovalValue = new byte[] { 3, 0, 0, 0 };
        var legacyApprovalValue = new byte[] { 3, 0, 0, 0 };
        var registry = new FakeStartupRegistry();
        registry.SetRawValue(StartupRunRegistryKey, "AirType", "\"C:\\Current\\AirType.exe\"");
        registry.SetRawValue(StartupRunRegistryKey, "ModernDictationApp", "\"C:\\Old\\ModernDictationApp.exe\"");
        registry.SetRawValue(StartupApprovedRunRegistryKey, "AirType", airTypeApprovalValue);
        registry.SetRawValue(StartupApprovedRunRegistryKey, "ModernDictationApp", legacyApprovalValue);

        var manager = new AppSettingsManager(
            settingsPath,
            applyStartupRegistry: true,
            startupRegistry: registry,
            getExecutablePath: () => @"C:\Current\AirType.exe");

        Assert.False(manager.LaunchOnStartup);
        Assert.Null(registry.GetValue(StartupRunRegistryKey, "AirType"));
        Assert.Null(registry.GetValue(StartupRunRegistryKey, "ModernDictationApp"));
        Assert.Equal(airTypeApprovalValue, Assert.IsType<byte[]>(registry.GetValue(StartupApprovedRunRegistryKey, "AirType")));
        Assert.Equal(legacyApprovalValue, Assert.IsType<byte[]>(registry.GetValue(StartupApprovedRunRegistryKey, "ModernDictationApp")));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"AirTypeSettingsTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        private readonly Dictionary<string, Dictionary<string, object>> _valuesByKey = new(StringComparer.OrdinalIgnoreCase);

        public object? GetValue(string keyPath, string valueName)
        {
            return _valuesByKey.TryGetValue(keyPath, out var values) && values.TryGetValue(valueName, out var value)
                ? value
                : null;
        }

        public void SetValue(string keyPath, string valueName, string value)
        {
            SetRawValue(keyPath, valueName, value);
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            if (_valuesByKey.TryGetValue(keyPath, out var values))
            {
                values.Remove(valueName);
            }
        }

        public void SetRawValue(string keyPath, string valueName, object value)
        {
            if (!_valuesByKey.TryGetValue(keyPath, out var values))
            {
                values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _valuesByKey[keyPath] = values;
            }

            values[valueName] = value;
        }
    }
}
