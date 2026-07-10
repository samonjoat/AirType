using AirType.Services;
using Xunit;

namespace AirType.Tests.Services;

public sealed class WindowsEnvironmentGuardTests
{
    [Fact]
    public void ResolveWindirFallback_WhenWindirExists_ReturnsNull()
    {
        string? fallback = WindowsEnvironmentGuard.ResolveWindirFallback(
            @"C:\Windows",
            @"D:\Windows",
            @"E:\Windows");

        Assert.Null(fallback);
    }

    [Fact]
    public void ResolveWindirFallback_WhenWindirIsMissing_UsesSystemRoot()
    {
        string? fallback = WindowsEnvironmentGuard.ResolveWindirFallback(
            currentWindir: null,
            systemRoot: @"C:\Windows",
            windowsFolder: @"D:\Windows");

        Assert.Equal(@"C:\Windows", fallback);
    }

    [Fact]
    public void ResolveWindirFallback_WhenWindirAndSystemRootAreMissing_UsesWindowsFolder()
    {
        string? fallback = WindowsEnvironmentGuard.ResolveWindirFallback(
            currentWindir: "",
            systemRoot: " ",
            windowsFolder: @"C:\Windows");

        Assert.Equal(@"C:\Windows", fallback);
    }
}
