namespace AirType.Services;

internal static class WindowsEnvironmentGuard
{
    public const string WindirVariable = "WINDIR";
    public const string SystemRootVariable = "SystemRoot";

    public static void EnsureWindir()
    {
        string? fallback = ResolveWindirFallback(
            Environment.GetEnvironmentVariable(WindirVariable),
            Environment.GetEnvironmentVariable(SystemRootVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            Environment.SetEnvironmentVariable(WindirVariable, fallback);
        }
    }

    internal static string? ResolveWindirFallback(
        string? currentWindir,
        string? systemRoot,
        string? windowsFolder)
    {
        if (!string.IsNullOrWhiteSpace(currentWindir))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            return systemRoot;
        }

        return !string.IsNullOrWhiteSpace(windowsFolder) ? windowsFolder : null;
    }
}
