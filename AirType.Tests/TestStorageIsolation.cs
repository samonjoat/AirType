using System;
using System.IO;
using System.Runtime.CompilerServices;
using AirType.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AirType.Tests;

internal static class TestStorageIsolation
{
    private const string KeepTempRootEnvironmentVariable = "AIRTYPE_KEEP_TEST_STORAGE";

    [ModuleInitializer]
    internal static void RedirectStorageRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"AirTypeTestRun_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        AirTypeStoragePaths.CanonicalRootOverride = root;

        if (string.Equals(
                Environment.GetEnvironmentVariable(KeepTempRootEnvironmentVariable),
                "1",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup; test processes can still hold log files during shutdown.
            }
        };
    }
}
