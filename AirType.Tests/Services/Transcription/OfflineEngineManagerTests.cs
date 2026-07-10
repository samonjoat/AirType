using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class OfflineEngineManagerTests
{
    [Fact]
    public void GetStatus_WhenWorkerIsMissing_ReturnsNotInstalled()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var status = manager.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.Equal(Path.Combine(tempDirectory.RootPath, "worker"), status.WorkerPath);
        Assert.Equal(Path.Combine(tempDirectory.RootPath, "models", "ct2", "small.en"), status.ModelPath);
        Assert.Equal("Not installed", status.StatusText);
    }

    [Fact]
    public void GetStatus_WhenLegacyEngineArtifactsExist_ReturnsReinstallRequired()
    {
        using var tempDirectory = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDirectory.RootPath, "airtype-faster-whisper-worker.exe"), "old worker");
        File.WriteAllText(Path.Combine(tempDirectory.RootPath, "Whisper.net.dll"), "old dependency");
        Directory.CreateDirectory(Path.Combine(tempDirectory.RootPath, "models"));
        File.WriteAllText(Path.Combine(tempDirectory.RootPath, "models", "ggml-base.bin"), "old model");
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var status = manager.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.Equal("Reinstall required", status.StatusText);
    }

    [Fact]
    public void GetStatus_WhenUnknownInstallArtifactsExist_ReturnsIncompleteInstall()
    {
        using var tempDirectory = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDirectory.RootPath, "models"));
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var status = manager.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.Equal("Incomplete install", status.StatusText);
    }

    [Fact]
    public async Task InstallAsync_WhenBundleSourceIsMissing_ReturnsFailure()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var result = await manager.InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("source is not available", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(tempDirectory.RootPath, "worker")));
    }

    [Fact]
    public void GetStatus_WhenBundledWorkerIsIncomplete_ReturnsCannotInstall()
    {
        using var tempDirectory = new TempDirectory();
        string bundledWorkerRoot = Path.Combine(tempDirectory.RootPath, "bundled-worker");
        CreateWorkerPackage(bundledWorkerRoot);
        var manager = new OfflineEngineManager(
            Path.Combine(tempDirectory.RootPath, "install"),
            bundleSource: null,
            bundledWorkerDirectory: bundledWorkerRoot,
            modelDownloadSource: Path.Combine(tempDirectory.RootPath, "model"),
            httpClient: new System.Net.Http.HttpClient());

        var status = manager.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.False(status.CanInstall);
        Assert.Null(status.InstallSource);
    }

    [Fact]
    public void GetStatus_WhenBundledEngineExists_ReturnsCanInstall()
    {
        using var tempDirectory = new TempDirectory();
        string bundledWorkerRoot = Path.Combine(tempDirectory.RootPath, "bundled-worker");
        CreateBundledEngineRoot(bundledWorkerRoot);
        var manager = new OfflineEngineManager(
            Path.Combine(tempDirectory.RootPath, "install"),
            bundleSource: null,
            bundledWorkerDirectory: bundledWorkerRoot,
            modelDownloadSource: null,
            httpClient: new System.Net.Http.HttpClient());

        var status = manager.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.True(status.CanInstall);
        Assert.Equal("Bundled offline engine", status.InstallSource);
    }

    [Fact]
    public void GetStatus_WhenLegacyCt2SmallModelExists_ReturnsInstalled()
    {
        using var tempDirectory = new TempDirectory();
        CreateInstalledWorkerRoot(tempDirectory.RootPath);
        CreateModelDirectory(Path.Combine(tempDirectory.RootPath, "models", "small.en"));
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var status = manager.GetStatus(LocalAsrModelCatalog.FasterWhisperSmallEnInt8);

        Assert.True(status.IsInstalled);
        Assert.Equal("Installed", status.StatusText);
        Assert.Equal(Path.Combine(tempDirectory.RootPath, "models", "small.en"), status.ModelPath);
    }

    [Fact]
    public void GetStatus_WhenRetiredWhisperCppModelIsRequested_UsesDefaultCt2Model()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var status = manager.GetStatus(LocalAsrModelCatalog.WhisperCppSmallEnQ8);

        Assert.False(status.IsInstalled);
        Assert.Equal(LocalAsrModelCatalog.FasterWhisperSmallEnInt8, status.ModelId);
        Assert.Equal("Small.en CT2", status.ModelDisplayName);
        Assert.Equal(LocalAsrBackend.FasterWhisperCt2, status.Backend);
        Assert.Equal(
            Path.Combine(tempDirectory.RootPath, "runtime", "python.exe"),
            status.RuntimePath);
        Assert.Equal(
            Path.Combine(tempDirectory.RootPath, "models", "ct2", "small.en"),
            status.ModelPath);
    }

    [Fact]
    public async Task InstallAsync_WhenBundledEngineExists_CopiesBundleWithoutRuntimeBuild()
    {
        using var tempDirectory = new TempDirectory();
        string bundledWorkerRoot = Path.Combine(tempDirectory.RootPath, "bundled-worker");
        CreateBundledEngineRoot(bundledWorkerRoot);
        string installRoot = Path.Combine(tempDirectory.RootPath, "install");
        var manager = new OfflineEngineManager(
            installRoot,
            bundleSource: null,
            bundledWorkerDirectory: bundledWorkerRoot,
            modelDownloadSource: null,
            httpClient: new System.Net.Http.HttpClient());
        var progress = new CapturingProgress();

        var result = await manager.InstallAsync(progress);
        var status = manager.GetStatus();

        Assert.True(result.Success);
        Assert.True(status.IsInstalled);
        Assert.Equal("Installed", status.StatusText);
        Assert.True(File.Exists(Path.Combine(installRoot, "worker", "airtype_asr_worker", "__main__.py")));
        Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "python.exe")));
        Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "python312.dll")));
        Assert.True(File.Exists(Path.Combine(installRoot, "models", "ct2", "small.en", "model.bin")));
        Assert.Equal(3, progress.Updates.First().Percent);
        Assert.Contains(progress.Updates, update => update.Message.Contains("Copying local ASR runtime", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(progress.Updates, update => update.Message.Contains("dependencies", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100, progress.Updates.Last().Percent);
    }

    [Fact]
    public async Task InstallAsync_WhenConfiguredBundleDownloadFails_RemovesTempInstallRoot()
    {
        using var tempDirectory = new TempDirectory();
        var before = Directory
            .EnumerateDirectories(Path.GetTempPath(), "AirTypeOfflineEngine_*")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manager = new OfflineEngineManager(
            tempDirectory.RootPath,
            bundleSource: "https://airtype.local/offline-engine.zip",
            bundledWorkerDirectory: null,
            modelDownloadSource: null,
            httpClient: new HttpClient(new FailingHttpHandler()));

        var result = await manager.InstallAsync();

        var after = Directory
            .EnumerateDirectories(Path.GetTempPath(), "AirTypeOfflineEngine_*")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.False(result.Success);
        Assert.Empty(after.Except(before, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallAsync_WhenConfiguredBundleManifestMatchesChecksum_InstallsVerifiedBundle()
    {
        using var tempDirectory = new TempDirectory();
        string bundleManifestPath = CreateReleaseBundle(tempDirectory.RootPath, checksumOverride: null);
        string installRoot = Path.Combine(tempDirectory.RootPath, "install");
        var manager = new OfflineEngineManager(
            installRoot,
            bundleSource: bundleManifestPath,
            bundledWorkerDirectory: null,
            modelDownloadSource: null,
            httpClient: new System.Net.Http.HttpClient());

        var result = await manager.InstallAsync();
        var status = manager.GetStatus();

        Assert.True(result.Success);
        Assert.True(status.IsInstalled);
        Assert.Equal("Installed", status.StatusText);
        Assert.True(File.Exists(Path.Combine(installRoot, "worker", "airtype_asr_worker", "__main__.py")));
        Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "python.exe")));
        Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "python312.dll")));
        Assert.True(File.Exists(Path.Combine(installRoot, "models", "ct2", "small.en", "model.bin")));
        string installedManifest = File.ReadAllText(Path.Combine(installRoot, "local-asr-manifest.json"));
        Assert.Contains("BundleVersion", installedManifest);
    }

    [Fact]
    public async Task InstallAsync_WhenConfiguredBundleChecksumDoesNotMatch_ReturnsFailureWithoutInstall()
    {
        using var tempDirectory = new TempDirectory();
        string bundleManifestPath = CreateReleaseBundle(tempDirectory.RootPath, checksumOverride: "0000");
        string installRoot = Path.Combine(tempDirectory.RootPath, "install");
        var manager = new OfflineEngineManager(
            installRoot,
            bundleSource: bundleManifestPath,
            bundledWorkerDirectory: null,
            modelDownloadSource: null,
            httpClient: new System.Net.Http.HttpClient());

        var result = await manager.InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("checksum", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.GetStatus().IsInstalled);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "worker")));
    }

    [Fact]
    public async Task InstallAsync_WhenBundleContainsHostBoundVenv_RejectsBundle()
    {
        using var tempDirectory = new TempDirectory();
        string bundleManifestPath = CreateReleaseBundle(
            tempDirectory.RootPath,
            checksumOverride: null,
            hostBoundRuntime: true);
        string installRoot = Path.Combine(tempDirectory.RootPath, "install");
        var manager = new OfflineEngineManager(
            installRoot,
            bundleSource: bundleManifestPath,
            bundledWorkerDirectory: null,
            modelDownloadSource: null,
            httpClient: new System.Net.Http.HttpClient());

        var result = await manager.InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("portable Python runtime", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.GetStatus().IsInstalled);
    }

    [Fact]
    public void GetStatus_WhenLegacyVenvExists_RequiresReinstall()
    {
        using var tempDirectory = new TempDirectory();
        CreateWorkerPackage(Path.Combine(tempDirectory.RootPath, "worker"));
        Directory.CreateDirectory(Path.Combine(tempDirectory.RootPath, ".venv", "Scripts"));
        File.WriteAllText(Path.Combine(tempDirectory.RootPath, ".venv", "Scripts", "python.exe"), "python");
        CreateModelDirectory(Path.Combine(tempDirectory.RootPath, "models", "ct2", "small.en"));
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        var status = manager.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.Equal("Reinstall required", status.StatusText);
    }

    [Fact]
    public async Task InstallModelAsync_WhenBundledSmallCt2ModelExists_CopiesSelectedModelOnly()
    {
        using var tempDirectory = new TempDirectory();
        string bundledWorkerRoot = Path.Combine(tempDirectory.RootPath, "bundled-worker");
        CreateInstalledWorkerRoot(bundledWorkerRoot);
        CreateModelDirectory(Path.Combine(bundledWorkerRoot, "models", "ct2", "small.en"));
        string installRoot = Path.Combine(tempDirectory.RootPath, "install");
        var manager = new OfflineEngineManager(
            installRoot,
            bundleSource: null,
            bundledWorkerDirectory: bundledWorkerRoot,
            modelDownloadSource: null,
            httpClient: new System.Net.Http.HttpClient());

        var result = await manager.InstallModelAsync(LocalAsrModelCatalog.FasterWhisperSmallEnInt8);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(installRoot, "models", "ct2", "small.en", "model.bin")));
        Assert.DoesNotContain(ModelConfig.LocalModels, model => model.Id == LocalAsrModelCatalog.FasterWhisperBaseEnInt8);
        Assert.DoesNotContain(ModelConfig.LocalModels, model => model.Id == LocalAsrModelCatalog.WhisperCppBaseEnQ8);
        Assert.DoesNotContain(ModelConfig.LocalModels, model => model.Id == LocalAsrModelCatalog.WhisperCppSmallEnQ8);
        Assert.True(manager.GetStatus(LocalAsrModelCatalog.FasterWhisperSmallEnInt8).IsInstalled);
    }

    [Fact]
    public void GetModelFolderPath_ForCt2Model_ReturnsSelectedModelDirectory()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        string folderPath = manager.GetModelFolderPath(LocalAsrModelCatalog.FasterWhisperSmallEnInt8);

        Assert.Equal(Path.Combine(tempDirectory.RootPath, "models", "ct2", "small.en"), folderPath);
    }

    [Fact]
    public async Task RemoveAsync_DeletesInstalledEngineFolder()
    {
        using var tempDirectory = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDirectory.RootPath, "worker"));
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        await manager.RemoveAsync();

        Assert.False(Directory.Exists(tempDirectory.RootPath));
    }

    [Fact]
    public async Task RemoveAsync_WhenInstallDirectoryIsDriveRoot_RefusesDeletion()
    {
        string root = Path.GetPathRoot(Path.GetTempPath()) ?? Path.GetTempPath();
        var manager = new OfflineEngineManager(root, bundleSource: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveAsync());
    }

    [Fact]
    public async Task RemoveModelAsync_WhenCt2ModelIsSelected_RemovesOnlyThatModel()
    {
        using var tempDirectory = new TempDirectory();
        CreateModelDirectory(Path.Combine(tempDirectory.RootPath, "models", "ct2", "base.en"));
        CreateModelDirectory(Path.Combine(tempDirectory.RootPath, "models", "ct2", "small.en"));
        var manager = new OfflineEngineManager(tempDirectory.RootPath, bundleSource: null);

        await manager.RemoveModelAsync(LocalAsrModelCatalog.FasterWhisperSmallEnInt8);

        Assert.True(File.Exists(Path.Combine(tempDirectory.RootPath, "models", "ct2", "base.en", "model.bin")));
        Assert.False(Directory.Exists(Path.Combine(tempDirectory.RootPath, "models", "ct2", "small.en")));
    }

    [Theory]
    [InlineData(6L * 1024 * 1024 * 1024, "Cloud fallback")]
    [InlineData(8L * 1024 * 1024 * 1024, "Small.en")]
    [InlineData(16L * 1024 * 1024 * 1024, "Small.en")]
    public void GetRecommendation_UsesRamThresholds(long ramBytes, string expectedText)
    {
        var recommendation = OfflineEngineManager.GetRecommendation(ramBytes);

        Assert.Contains(expectedText, recommendation, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateWorkerPackage(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "airtype_asr_worker"));
        File.WriteAllText(Path.Combine(root, "pyproject.toml"), "project");
        File.WriteAllText(Path.Combine(root, "airtype_asr_worker", "__main__.py"), "main");
    }

    private static void CreateInstalledWorkerRoot(string root)
    {
        CreateWorkerPackage(Path.Combine(root, "worker"));
        Directory.CreateDirectory(Path.Combine(root, "runtime"));
        File.WriteAllText(Path.Combine(root, "runtime", "python.exe"), "python");
        File.WriteAllText(Path.Combine(root, "runtime", "python312.dll"), "python-runtime");
    }

    private static void CreateBundledEngineRoot(string root)
    {
        CreateInstalledWorkerRoot(root);
        CreateModelDirectory(Path.Combine(root, "models", "ct2", "small.en"));
        File.WriteAllText(Path.Combine(root, "local-asr-manifest.json"), "{}");
    }

    private static string CreateReleaseBundle(
        string root,
        string? checksumOverride,
        bool hostBoundRuntime = false)
    {
        string bundleRoot = Path.Combine(root, "bundle-root");
        CreateBundledEngineRoot(bundleRoot);
        if (hostBoundRuntime)
        {
            Directory.Delete(Path.Combine(bundleRoot, "runtime"), recursive: true);
            Directory.CreateDirectory(Path.Combine(bundleRoot, ".venv", "Scripts"));
            File.WriteAllText(Path.Combine(bundleRoot, ".venv", "Scripts", "python.exe"), "python");
        }
        string embeddedManifestPath = Path.Combine(bundleRoot, "airtype-local-asr-bundle.json");
        WriteBundleManifest(embeddedManifestPath, "bundle.zip", checksum: null);

        string zipPath = Path.Combine(root, "bundle.zip");
        ZipFile.CreateFromDirectory(bundleRoot, zipPath);

        string checksum = checksumOverride ?? ComputeSha256(zipPath);
        string sidecarManifestPath = Path.Combine(root, "bundle.json");
        WriteBundleManifest(sidecarManifestPath, "bundle.zip", checksum);
        return sidecarManifestPath;
    }

    private static void WriteBundleManifest(string path, string zipFileName, string? checksum)
    {
        var manifest = new
        {
            schemaVersion = 1,
            bundleId = OfflineEngineManager.ExpectedBundleId,
            bundleVersion = "2026.06.27-test",
            engineId = OfflineEngineManager.ExpectedEngineId,
            modelId = LocalAsrModelCatalog.FasterWhisperSmallEnInt8,
            modelName = "small.en",
            runtimeId = LocalAsrModelCatalog.Ct2RuntimeId,
            platform = "win-x64",
            architecture = "x64",
            zipFileName,
            downloadUrl = (string?)null,
            sha256 = checksum,
            compressedBytes = (long?)null,
            uncompressedBytes = (long?)null,
            createdUtc = DateTimeOffset.UtcNow
        };

        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CreateModelDirectory(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.json"), "{}");
        File.WriteAllText(Path.Combine(root, "model.bin"), "model");
        File.WriteAllText(Path.Combine(root, "tokenizer.json"), "{}");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"AirTypeOfflineEngineTests_{Guid.NewGuid():N}");
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

    private sealed class CapturingProgress : IProgress<OfflineEngineInstallProgress>
    {
        public List<OfflineEngineInstallProgress> Updates { get; } = new();

        public void Report(OfflineEngineInstallProgress value)
        {
            Updates.Add(value);
        }
    }

    private sealed class FailingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
