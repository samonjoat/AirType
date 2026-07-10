using System;
using System.IO;
using AirType.Services;
using AirType.Services.Database;
using AirType.Services.Storage;
using Xunit;

namespace AirType.Tests.Services;

public class AirTypeStoragePathsTests
{
    [Fact]
    public void Canonical_path_owners_derive_from_canonical_root()
    {
        string canonicalRoot = AirTypeStoragePaths.CanonicalRoot;
        string? configuredRoot = Environment.GetEnvironmentVariable(
            AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable);

        Assert.False(string.IsNullOrWhiteSpace(configuredRoot));
        Assert.Equal(Path.GetFullPath(configuredRoot), canonicalRoot);
        Assert.Equal(Path.Combine(canonicalRoot, "Logs", "App"), AirTypeStoragePaths.GetCanonicalPath("Logs", "App"));
        Assert.Equal(Path.Combine(canonicalRoot, "dictation.db"), DatabaseInitializer.DbPath);

        var filePersistenceManager = new FilePersistenceManager();
        Assert.Equal(Path.Combine(canonicalRoot, "Audio_files"), filePersistenceManager.AudioDirectory);

        var transcriptionLogger = new TranscriptionLogger();
        Assert.Equal(Path.Combine(canonicalRoot, "Logs", "Transcription"), transcriptionLogger.LogsDirectory);
    }

    [Fact]
    public void Canonical_root_defaults_to_local_appdata_when_override_unset()
    {
        string? originalOverride = AirTypeStoragePaths.CanonicalRootOverride;
        string? originalEnvironmentRoot = Environment.GetEnvironmentVariable(
            AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable);

        try
        {
            AirTypeStoragePaths.CanonicalRootOverride = null;
            Environment.SetEnvironmentVariable(AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable, null);

            string expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AirTypeStoragePaths.AppFolderName);

            Assert.Equal(expectedRoot, AirTypeStoragePaths.CanonicalRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable,
                originalEnvironmentRoot);
            AirTypeStoragePaths.CanonicalRootOverride = originalOverride;
        }
    }

    [Fact]
    public void Process_storage_root_isolates_canonical_and_legacy_paths()
    {
        string? originalOverride = AirTypeStoragePaths.CanonicalRootOverride;
        string? originalEnvironmentRoot = Environment.GetEnvironmentVariable(
            AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable);
        string isolatedRoot = Path.Combine(Path.GetTempPath(), $"AirTypeProcessRoot_{Guid.NewGuid():N}");

        try
        {
            AirTypeStoragePaths.CanonicalRootOverride = null;
            Environment.SetEnvironmentVariable(
                AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable,
                isolatedRoot);

            Assert.Equal(Path.GetFullPath(isolatedRoot), AirTypeStoragePaths.CanonicalRoot);
            Assert.Equal(
                Path.Combine(isolatedRoot, ".compat", "Local", AirTypeStoragePaths.LegacyAppFolderName),
                AirTypeStoragePaths.LegacyCanonicalRoot);
            Assert.Equal(
                Path.Combine(isolatedRoot, ".compat", "Roaming", AirTypeStoragePaths.LegacyAppFolderName),
                AirTypeStoragePaths.LegacyRoamingRoot);
            Assert.Equal(
                Path.Combine(isolatedRoot, ".compat", "Roaming", AirTypeStoragePaths.AppFolderName),
                AirTypeStoragePaths.RoamingCompatibilityRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable,
                originalEnvironmentRoot);
            AirTypeStoragePaths.CanonicalRootOverride = originalOverride;
        }
    }

    [Fact]
    public void Legacy_roots_continue_to_use_real_user_folders()
    {
        string? originalEnvironmentRoot = Environment.GetEnvironmentVariable(
            AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable, null);

            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AirTypeStoragePaths.LegacyAppFolderName),
                AirTypeStoragePaths.LegacyCanonicalRoot);

            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AirTypeStoragePaths.LegacyAppFolderName),
                AirTypeStoragePaths.LegacyRoamingRoot);

            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AirTypeStoragePaths.AppFolderName),
                AirTypeStoragePaths.RoamingCompatibilityRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AirTypeStoragePaths.ProcessStorageRootEnvironmentVariable,
                originalEnvironmentRoot);
        }
    }
}
