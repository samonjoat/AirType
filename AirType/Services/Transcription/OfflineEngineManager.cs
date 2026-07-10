using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public sealed class OfflineEngineManager : IOfflineEngineManager
{
    public const string BundleSourceEnvironmentVariable = "AIRTYPE_LOCAL_ENGINE_BUNDLE_URL";
    public const string BundleSourceFileName = "offline-engine-source.txt";
    public const string BundledWorkerDirectoryName = "LocalAsrWorker";
    public const string WorkerDirectoryName = "worker";
    public const string PythonRuntimeDirectoryName = "runtime";
    public const string LegacyVirtualEnvironmentDirectoryName = ".venv";
    public const string ModelDirectoryName = "models";
    public const string ModelName = "small.en";
    public const string ModelRepositoryName = "Systran/faster-whisper-small.en";
    public const string DefaultModelDownloadSource = ModelName;
    public const string ManifestFileName = "local-asr-manifest.json";
    public const string BundleManifestFileName = "airtype-local-asr-bundle.json";
    public const string ExpectedBundleId = "airtype-local-asr-small-en-ct2-win-x64";
    public const string ExpectedEngineId = "airtype-local-asr";
    public const string AllowRuntimeBuildEnvironmentVariable = "AIRTYPE_ALLOW_LOCAL_ASR_RUNTIME_BUILD";
    private const string TempInstallPrefix = "AirTypeOfflineEngine_";
    private const string PartialDownloadSuffix = ".partial";
    private const int RemoveDirectoryMaxAttempts = 8;
    private const int RemoveDirectoryRetryDelayMilliseconds = 250;

    public static string DefaultInstallDirectory =>
        Path.Combine(AirTypeStoragePaths.CanonicalRoot, "LocalTranscription");

    public static string DefaultWorkerPath => Path.Combine(DefaultInstallDirectory, WorkerDirectoryName);

    public static string DefaultModelPath =>
        Path.Combine(DefaultInstallDirectory, ModelDirectoryName, "ct2", ModelName);

    public static string LegacyDefaultModelPath =>
        Path.Combine(DefaultInstallDirectory, ModelDirectoryName, ModelName);

    private readonly string _installDirectory;
    private readonly string? _bundleSource;
    private readonly string? _bundledWorkerDirectory;
    private readonly string? _modelDownloadSource;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OfflineEngineManager()
        : this(
            DefaultInstallDirectory,
            ResolveConfiguredBundleSource(),
            ResolveBundledWorkerDirectory(),
            DefaultModelDownloadSource,
            new HttpClient(),
            ownsHttpClient: true)
    {
    }

    public OfflineEngineManager(string installDirectory, string? bundleSource)
        : this(
            installDirectory,
            bundleSource,
            bundledWorkerDirectory: null,
            modelDownloadSource: DefaultModelDownloadSource,
            new HttpClient(),
            ownsHttpClient: true)
    {
    }

    internal OfflineEngineManager(
        string installDirectory,
        string? bundleSource,
        HttpClient httpClient,
        bool ownsHttpClient = false)
        : this(
            installDirectory,
            bundleSource,
            bundledWorkerDirectory: null,
            modelDownloadSource: DefaultModelDownloadSource,
            httpClient,
            ownsHttpClient)
    {
    }

    internal OfflineEngineManager(
        string installDirectory,
        string? bundleSource,
        string? bundledWorkerDirectory,
        string? modelDownloadSource,
        HttpClient httpClient,
        bool ownsHttpClient = false)
    {
        _installDirectory = !string.IsNullOrWhiteSpace(installDirectory)
            ? installDirectory
            : throw new ArgumentException("Install directory is required.", nameof(installDirectory));
        _bundleSource = string.IsNullOrWhiteSpace(bundleSource) ? null : bundleSource.Trim();
        _bundledWorkerDirectory = string.IsNullOrWhiteSpace(bundledWorkerDirectory)
            ? null
            : bundledWorkerDirectory.Trim();
        _modelDownloadSource = string.IsNullOrWhiteSpace(modelDownloadSource)
            ? null
            : modelDownloadSource.Trim();
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public string InstallDirectory => _installDirectory;

    public string WorkerPath => Path.Combine(_installDirectory, WorkerDirectoryName);

    public string ModelPath => GetModelPath(LocalAsrModelCatalog.Default);

    public OfflineEngineStatus GetStatus() => GetStatus(null);

    public OfflineEngineStatus GetStatus(string? modelId)
    {
        CleanupStaleInstallArtifacts();

        var model = LocalAsrModelCatalog.GetRequired(modelId);
        bool runtimeInstalled = IsRuntimeInstalled(model);
        bool modelInstalled = IsModelInstalled(model, out var actualModelPath);
        bool isInstalled = runtimeInstalled && modelInstalled;
        bool legacyEngineInstalled = IsLegacyEnginePresent(_installDirectory);
        bool hasInstallArtifacts = HasInstallArtifacts(_installDirectory);

        string statusText = isInstalled
            ? "Installed"
            : legacyEngineInstalled
                ? "Reinstall required"
                : runtimeInstalled && !modelInstalled
                    ? "Model not installed"
                    : modelInstalled && !runtimeInstalled
                        ? "Runtime missing"
                        : hasInstallArtifacts
                            ? "Incomplete install"
                            : "Not installed";

        string? installSource = ResolveInstallSourceDescription(model);

        return new OfflineEngineStatus(
            isInstalled,
            _installDirectory,
            WorkerPath,
            GetRecommendation(GetAvailableMemoryBytes()),
            _bundleSource,
            CanInstallModel(model),
            statusText,
            actualModelPath ?? GetModelPath(model),
            installSource,
            model.Id,
            model.DisplayName,
            model.Backend,
            GetRuntimePath(model));
    }

    public async Task<OfflineEngineInstallResult> InstallAsync(
        IProgress<OfflineEngineInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await InstallModelAsync(LocalAsrModelCatalog.DefaultModelId, progress, cancellationToken);

    public async Task<OfflineEngineInstallResult> InstallModelAsync(
        string? modelId,
        IProgress<OfflineEngineInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = LocalAsrModelCatalog.GetRequired(modelId);
        if (!CanInstallModel(model))
        {
            return new OfflineEngineInstallResult(
                false,
                $"Offline engine source is not available for {model.DisplayName}.");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"{TempInstallPrefix}{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(_installDirectory);
            progress?.Report(new OfflineEngineInstallProgress(3, $"Preparing {model.DisplayName} install"));

            var bundleManifest = await InstallCt2ModelAsync(model, tempRoot, progress, cancellationToken);

            var installedStatus = GetStatus(model.Id);
            if (!installedStatus.IsInstalled)
            {
                throw new InvalidOperationException($"{model.DisplayName} install did not produce a valid local engine.");
            }

            progress?.Report(new OfflineEngineInstallProgress(90, "Writing local ASR manifest"));
            WriteManifest(bundleManifest);
            progress?.Report(new OfflineEngineInstallProgress(100, $"{model.DisplayName} installed"));
            return new OfflineEngineInstallResult(true, $"{model.DisplayName} installed.");
        }
        catch (OperationCanceledException)
        {
            return new OfflineEngineInstallResult(false, $"{model.DisplayName} install was canceled.");
        }
        catch (Exception ex)
        {
            Logger.Error("OfflineEngineManager", $"{model.DisplayName} install failed: {ex.Message}", ex);
            return new OfflineEngineInstallResult(false, $"{model.DisplayName} install failed: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string installDirectory = Path.GetFullPath(_installDirectory);
        string? root = Path.GetPathRoot(installDirectory);
        if (string.IsNullOrWhiteSpace(installDirectory) ||
            string.Equals(installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove unsafe offline engine directory: {installDirectory}");
        }

        Exception? lastException = null;
        for (int attempt = 1; attempt <= RemoveDirectoryMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(installDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(installDirectory, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (IsTransientRemoveException(ex))
            {
                lastException = ex;
                if (attempt >= RemoveDirectoryMaxAttempts)
                {
                    break;
                }

                await Task.Delay(RemoveDirectoryRetryDelayMilliseconds, cancellationToken);
            }
        }

        throw new IOException(
            $"Offline engine removal is blocked because a file under '{installDirectory}' is still in use. Close active dictation and try again.",
            lastException);
    }

    public Task RemoveModelAsync(string? modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = LocalAsrModelCatalog.GetRequired(modelId);

        foreach (string path in GetCt2ModelCandidatePaths(model))
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        WriteManifest();
        return Task.CompletedTask;
    }

    public string GetModelFolderPath(string? modelId)
    {
        var model = LocalAsrModelCatalog.GetRequired(modelId);
        return GetModelPath(model);
    }

    private static bool IsTransientRemoveException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    public static string GetRecommendation(long availableMemoryBytes)
    {
        const long gigabyte = 1024L * 1024 * 1024;

        if (availableMemoryBytes < 8 * gigabyte)
        {
            return "Cloud fallback recommended for this RAM profile.";
        }

        if (availableMemoryBytes < 16 * gigabyte)
        {
            return "Small.en local engine should run, with cloud fallback recommended.";
        }

        return "Small.en local engine recommended.";
    }

    private async Task<LocalAsrBundleManifest?> InstallCt2ModelAsync(
        LocalAsrModelDescriptor model,
        string tempRoot,
        IProgress<OfflineEngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var preparedSource = await PrepareBundledSourceAsync(model, tempRoot, progress, cancellationToken);
        string? sourceRoot = preparedSource.SourceRoot;
        progress?.Report(new OfflineEngineInstallProgress(14, "Preparing CT2 worker"));
        EnsureCt2WorkerInstalled(sourceRoot, tempRoot);
        await EnsurePythonRuntimeInstalledAsync(sourceRoot, tempRoot, progress, cancellationToken);

        string targetModelPath = GetModelPath(model);
        if (IsModelDirectoryPresent(targetModelPath))
        {
            progress?.Report(new OfflineEngineInstallProgress(82, $"{model.DisplayName} already installed"));
            return preparedSource.Manifest;
        }

        if (TryCopyBundledCt2Model(model, sourceRoot, targetModelPath, tempRoot, progress))
        {
            return preparedSource.Manifest;
        }

        progress?.Report(new OfflineEngineInstallProgress(62, $"Downloading {model.DisplayName} model"));
        string tempModelPath = Path.Combine(tempRoot, model.ModelName);
        Directory.CreateDirectory(tempModelPath);
        await DownloadFasterWhisperModelAsync(
            model,
            tempModelPath,
            ResolveRuntimePythonPath(Path.Combine(_installDirectory, PythonRuntimeDirectoryName)),
            cancellationToken);

        EnsureModelDirectoryPresent(tempModelPath, model.DisplayName);
        progress?.Report(new OfflineEngineInstallProgress(78, $"Installing {model.DisplayName} model"));
        ReplaceDirectory(tempModelPath, targetModelPath);
        return preparedSource.Manifest;
    }

    private async Task<PreparedBundleSource> PrepareBundledSourceAsync(
        LocalAsrModelDescriptor model,
        string tempRoot,
        IProgress<OfflineEngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_bundleSource))
        {
            string zipPath = Path.Combine(tempRoot, "bundle.zip");
            string stagingRoot = Path.Combine(tempRoot, "bundle");
            var resolvedBundle = await ResolveBundleSourceAsync(zipPath, tempRoot, progress, cancellationToken);
            progress?.Report(new OfflineEngineInstallProgress(12, "Extracting offline engine"));
            ZipFile.ExtractToDirectory(resolvedBundle.ZipPath, stagingRoot);
            var manifest = ValidateExtractedBundle(stagingRoot, model, resolvedBundle.Manifest);
            return new PreparedBundleSource(stagingRoot, manifest);
        }

        return new PreparedBundleSource(
            !string.IsNullOrWhiteSpace(_bundledWorkerDirectory) &&
            Directory.Exists(_bundledWorkerDirectory)
                ? _bundledWorkerDirectory
                : null,
            Manifest: null);
    }

    private async Task<ResolvedBundleSource> ResolveBundleSourceAsync(
        string downloadTargetPath,
        string tempRoot,
        IProgress<OfflineEngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(_bundleSource, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                if (IsBundleManifestSource(uri.AbsolutePath))
                {
                    string manifestPath = Path.Combine(tempRoot, BundleManifestFileName);
                    progress?.Report(new OfflineEngineInstallProgress(5, "Downloading local ASR bundle metadata"));
                    await DownloadFileAsync(uri, manifestPath, progress, 5, 7, "Downloading local ASR bundle metadata", cancellationToken);
                    var manifest = ReadAndValidateBundleManifest(manifestPath);
                    Uri zipUri = ResolveBundleDownloadUri(uri, manifest);
                    progress?.Report(new OfflineEngineInstallProgress(8, "Downloading local ASR bundle"));
                    await DownloadFileAsync(zipUri, downloadTargetPath, progress, 8, 45, "Downloading local ASR bundle", cancellationToken);
                    VerifyBundleChecksum(downloadTargetPath, manifest);
                    return new ResolvedBundleSource(downloadTargetPath, manifest);
                }

                progress?.Report(new OfflineEngineInstallProgress(8, "Downloading local ASR bundle"));
                await DownloadFileAsync(uri, downloadTargetPath, progress, 8, 45, "Downloading local ASR bundle", cancellationToken);
                return new ResolvedBundleSource(downloadTargetPath, Manifest: null);
            }

            if (uri.Scheme == Uri.UriSchemeFile)
            {
                return await ResolveLocalBundleSourceAsync(uri.LocalPath, downloadTargetPath, tempRoot, progress, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(_bundleSource) && File.Exists(_bundleSource))
        {
            return await ResolveLocalBundleSourceAsync(_bundleSource, downloadTargetPath, tempRoot, progress, cancellationToken);
        }

        throw new InvalidOperationException($"Offline engine bundle source could not be resolved: {_bundleSource}");
    }

    private async Task<ResolvedBundleSource> ResolveLocalBundleSourceAsync(
        string sourcePath,
        string downloadTargetPath,
        string tempRoot,
        IProgress<OfflineEngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (IsBundleManifestSource(sourcePath))
        {
            var manifest = ReadAndValidateBundleManifest(sourcePath);
            if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) &&
                (downloadUri.Scheme == Uri.UriSchemeHttp || downloadUri.Scheme == Uri.UriSchemeHttps))
            {
                progress?.Report(new OfflineEngineInstallProgress(8, "Downloading local ASR bundle"));
                await DownloadFileAsync(downloadUri, downloadTargetPath, progress, 8, 45, "Downloading local ASR bundle", cancellationToken);
                VerifyBundleChecksum(downloadTargetPath, manifest);
                return new ResolvedBundleSource(downloadTargetPath, manifest);
            }

            string zipPath = ResolveLocalBundleZipPath(sourcePath, manifest);
            VerifyBundleChecksum(zipPath, manifest);
            return new ResolvedBundleSource(zipPath, manifest);
        }

        return new ResolvedBundleSource(sourcePath, Manifest: null);
    }

    private static bool IsBundleManifestSource(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private static LocalAsrBundleManifest ReadAndValidateBundleManifest(string path)
    {
        var manifest = LocalAsrBundleManifest.Read(path);
        ValidateBundleManifest(manifest);
        return manifest;
    }

    private static void ValidateBundleManifest(LocalAsrBundleManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported local ASR bundle manifest schema: {manifest.SchemaVersion}.");
        }

        if (!string.Equals(manifest.BundleId, ExpectedBundleId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported local ASR bundle id: {manifest.BundleId}.");
        }

        if (!string.Equals(manifest.EngineId, ExpectedEngineId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported local ASR engine id: {manifest.EngineId}.");
        }

        if (!string.Equals(manifest.ModelId, LocalAsrModelCatalog.FasterWhisperSmallEnInt8, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported local ASR model id: {manifest.ModelId}.");
        }

        if (!string.Equals(manifest.RuntimeId, LocalAsrModelCatalog.Ct2RuntimeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported local ASR runtime id: {manifest.RuntimeId}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ZipFileName) && string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            throw new InvalidOperationException("Local ASR bundle manifest does not specify a download URL or zip file name.");
        }
    }

    private static Uri ResolveBundleDownloadUri(Uri manifestUri, LocalAsrBundleManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.DownloadUrl) &&
            Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            return downloadUri;
        }

        string zipFileName = !string.IsNullOrWhiteSpace(manifest.ZipFileName)
            ? manifest.ZipFileName
            : throw new InvalidOperationException("Local ASR bundle manifest is missing its zip file name.");

        return new Uri(manifestUri, zipFileName);
    }

    private static string ResolveLocalBundleZipPath(string manifestPath, LocalAsrBundleManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.DownloadUrl) &&
            Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) &&
            downloadUri.Scheme == Uri.UriSchemeFile)
        {
            return downloadUri.LocalPath;
        }

        if (!string.IsNullOrWhiteSpace(manifest.DownloadUrl) &&
            File.Exists(manifest.DownloadUrl))
        {
            return manifest.DownloadUrl;
        }

        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? AppContext.BaseDirectory;
        string zipFileName = !string.IsNullOrWhiteSpace(manifest.ZipFileName)
            ? manifest.ZipFileName
            : Path.GetFileName(manifest.DownloadUrl ?? string.Empty);

        if (string.IsNullOrWhiteSpace(zipFileName))
        {
            throw new InvalidOperationException("Local ASR bundle manifest is missing its zip file name.");
        }

        string zipPath = Path.Combine(baseDirectory, zipFileName);
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Local ASR bundle zip was not found.", zipPath);
        }

        return zipPath;
    }

    private static void VerifyBundleChecksum(string zipPath, LocalAsrBundleManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return;
        }

        using var stream = File.OpenRead(zipPath);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(stream);
        string actual = Convert.ToHexString(hash).ToLowerInvariant();
        string expected = manifest.Sha256.Trim().ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Local ASR bundle checksum verification failed.");
        }
    }

    private static LocalAsrBundleManifest? ValidateExtractedBundle(
        string sourceRoot,
        LocalAsrModelDescriptor model,
        LocalAsrBundleManifest? expectedManifest)
    {
        LocalAsrBundleManifest? embeddedManifest = null;
        string embeddedManifestPath = Path.Combine(sourceRoot, BundleManifestFileName);
        if (File.Exists(embeddedManifestPath))
        {
            embeddedManifest = ReadAndValidateBundleManifest(embeddedManifestPath);
        }

        var effectiveManifest = expectedManifest ?? embeddedManifest;
        if (expectedManifest != null && embeddedManifest != null &&
            !string.Equals(expectedManifest.BundleVersion, embeddedManifest.BundleVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Local ASR bundle metadata does not match the extracted bundle.");
        }

        string bundledWorkerRoot = Path.Combine(sourceRoot, WorkerDirectoryName);
        if (!IsWorkerPackagePresent(bundledWorkerRoot) && !IsWorkerPackagePresent(sourceRoot))
        {
            throw new InvalidOperationException("Local ASR bundle worker package is incomplete.");
        }

        if (!IsPortablePythonRuntimePresent(Path.Combine(sourceRoot, PythonRuntimeDirectoryName)))
        {
            throw new InvalidOperationException("Local ASR bundle is missing its portable Python runtime.");
        }

        if (!GetBundledCt2ModelCandidatePaths(sourceRoot, model).Any(IsModelDirectoryPresent))
        {
            throw new InvalidOperationException($"Local ASR bundle is missing {model.DisplayName} model files.");
        }

        return effectiveManifest;
    }

    private void EnsureCt2WorkerInstalled(string? sourceRoot, string tempRoot)
    {
        if (IsWorkerPackagePresent(WorkerPath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new InvalidOperationException("Bundled local ASR worker package is missing.");
        }

        string bundledWorkerRoot = Path.Combine(sourceRoot, WorkerDirectoryName);
        if (IsWorkerPackagePresent(bundledWorkerRoot))
        {
            string tempWorkerPath = Path.Combine(tempRoot, WorkerDirectoryName);
            CopyDirectory(bundledWorkerRoot, tempWorkerPath);
            ReplaceDirectory(tempWorkerPath, WorkerPath);
            return;
        }

        if (IsWorkerPackagePresent(sourceRoot))
        {
            string tempWorkerPath = Path.Combine(tempRoot, WorkerDirectoryName);
            CopyWorkerPackageFromRoot(sourceRoot, tempWorkerPath);
            ReplaceDirectory(tempWorkerPath, WorkerPath);
            return;
        }

        throw new InvalidOperationException("Bundled local ASR worker package is incomplete.");
    }

    private async Task EnsurePythonRuntimeInstalledAsync(
        string? sourceRoot,
        string tempRoot,
        IProgress<OfflineEngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string runtimeRoot = Path.Combine(_installDirectory, PythonRuntimeDirectoryName);
        if (IsPythonRuntimePresent(runtimeRoot))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(sourceRoot))
        {
            string bundledRuntimeRoot = Path.Combine(sourceRoot, PythonRuntimeDirectoryName);
            if (IsPortablePythonRuntimePresent(bundledRuntimeRoot))
            {
                progress?.Report(new OfflineEngineInstallProgress(26, "Copying local ASR runtime"));
                string tempRuntimeRoot = Path.Combine(tempRoot, PythonRuntimeDirectoryName);
                CopyDirectory(bundledRuntimeRoot, tempRuntimeRoot);
                ReplaceDirectory(tempRuntimeRoot, runtimeRoot);
                return;
            }
        }

        if (!CanBuildRuntimeDuringInstall())
        {
            throw new InvalidOperationException(
                $"Bundled offline engine is missing its Python runtime. Run tools\\prepare-local-asr-runtime.ps1 before building AirType, or set {AllowRuntimeBuildEnvironmentVariable}=1 for development fallback installs.");
        }

        progress?.Report(new OfflineEngineInstallProgress(22, "Preparing Python runtime"));
        string python = ResolvePythonExecutable();
        string tempVenvBuildRoot = Path.Combine(tempRoot, PythonRuntimeDirectoryName);
        await RunProcessAsync(python, new[] { "-m", "venv", tempVenvBuildRoot }, workingDirectory: _installDirectory, cancellationToken);

        string venvPython = ResolveRuntimePythonPath(tempVenvBuildRoot);
        progress?.Report(new OfflineEngineInstallProgress(38, "Updating Python package tools"));
        await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--disable-pip-version-check", "--no-input", "--upgrade", "pip", "setuptools", "wheel" }, workingDirectory: _installDirectory, cancellationToken);
        progress?.Report(new OfflineEngineInstallProgress(50, "Installing local ASR dependencies"));
        await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--disable-pip-version-check", "--no-input", "--prefer-binary", "--only-binary=:all:", "." }, workingDirectory: WorkerPath, cancellationToken);
        ReplaceDirectory(tempVenvBuildRoot, runtimeRoot);
    }

    private bool TryCopyBundledCt2Model(
        LocalAsrModelDescriptor model,
        string? sourceRoot,
        string targetModelPath,
        string tempRoot,
        IProgress<OfflineEngineInstallProgress>? progress)
    {
        if (!string.IsNullOrWhiteSpace(_modelDownloadSource) &&
            Directory.Exists(_modelDownloadSource) &&
            string.Equals(model.Id, LocalAsrModelCatalog.FasterWhisperSmallEnInt8, StringComparison.Ordinal))
        {
            progress?.Report(new OfflineEngineInstallProgress(62, $"Copying {model.DisplayName} model"));
            string tempModelPath = Path.Combine(tempRoot, $"{model.ModelName}-bundled");
            CopyDirectory(_modelDownloadSource, tempModelPath);
            EnsureModelDirectoryPresent(tempModelPath, model.DisplayName);
            ReplaceDirectory(tempModelPath, targetModelPath);
            progress?.Report(new OfflineEngineInstallProgress(82, $"Copied {model.DisplayName} model"));
            return true;
        }

        foreach (string bundledPath in GetBundledCt2ModelCandidatePaths(sourceRoot, model))
        {
            if (IsModelDirectoryPresent(bundledPath))
            {
                progress?.Report(new OfflineEngineInstallProgress(62, $"Copying {model.DisplayName} model"));
                string tempModelPath = Path.Combine(tempRoot, $"{model.ModelName}-bundled");
                CopyDirectory(bundledPath, tempModelPath);
                EnsureModelDirectoryPresent(tempModelPath, model.DisplayName);
                ReplaceDirectory(tempModelPath, targetModelPath);
                progress?.Report(new OfflineEngineInstallProgress(82, $"Copied {model.DisplayName} model"));
                return true;
            }
        }

        return false;
    }

    private static async Task DownloadFasterWhisperModelAsync(
        LocalAsrModelDescriptor model,
        string modelPath,
        string python,
        CancellationToken cancellationToken)
    {
        await RunProcessAsync(
            python,
            new[] { "-c", "from faster_whisper.utils import download_model; import sys; download_model(sys.argv[2], output_dir=sys.argv[1])", modelPath, model.DownloadSource },
            workingDirectory: Path.GetDirectoryName(modelPath) ?? AppContext.BaseDirectory,
            cancellationToken);
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string targetPath,
        IProgress<OfflineEngineInstallProgress>? progress,
        int startPercent,
        int endPercent,
        string message,
        CancellationToken cancellationToken)
    {
        string partialPath = $"{targetPath}{PartialDownloadSuffix}";
        TryDeleteFile(partialPath);
        TryDeleteFile(targetPath);

        try
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = File.Create(partialPath);
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength is not > 0)
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Close();
                File.Move(partialPath, targetPath);
                progress?.Report(new OfflineEngineInstallProgress(endPercent, message));
                return;
            }

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int lastPercent = startPercent;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                int percent = startPercent + (int)Math.Round((endPercent - startPercent) * Math.Min(1, totalRead / (double)contentLength.Value));
                if (percent > lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(new OfflineEngineInstallProgress(percent, message));
                }
            }

            await destination.FlushAsync(cancellationToken);
            destination.Close();
            File.Move(partialPath, targetPath);
        }
        catch
        {
            TryDeleteFile(partialPath);
            throw;
        }
    }

    private bool CanInstallModel(LocalAsrModelDescriptor model)
    {
        return IsRuntimeInstalled(model) ||
               !string.IsNullOrWhiteSpace(_bundleSource) ||
               CanInstallFromBundledWorker() ||
               CanBuildRuntimeDuringInstall();
    }

    private bool CanInstallFromBundledWorker() =>
        !string.IsNullOrWhiteSpace(_bundledWorkerDirectory) &&
        Directory.Exists(_bundledWorkerDirectory) &&
        (IsWorkerPackagePresent(_bundledWorkerDirectory) ||
         IsWorkerPackagePresent(Path.Combine(_bundledWorkerDirectory, WorkerDirectoryName))) &&
        (IsPortablePythonRuntimePresent(Path.Combine(_bundledWorkerDirectory, PythonRuntimeDirectoryName)) ||
         CanBuildRuntimeDuringInstall());

    private string? ResolveInstallSourceDescription(LocalAsrModelDescriptor model)
    {
        if (!string.IsNullOrWhiteSpace(_bundleSource))
        {
            return "Configured bundle";
        }

        if (!string.IsNullOrWhiteSpace(_bundledWorkerDirectory) &&
            Directory.Exists(_bundledWorkerDirectory))
        {
            return IsPortablePythonRuntimePresent(Path.Combine(_bundledWorkerDirectory, PythonRuntimeDirectoryName))
                ? "Bundled offline engine"
                : CanBuildRuntimeDuringInstall()
                    ? "Developer runtime build"
                    : null;
        }

        return CanBuildRuntimeDuringInstall()
            ? "Developer runtime build"
            : null;
    }

    private static string? ResolveConfiguredBundleSource()
    {
        string? environmentSource = Environment.GetEnvironmentVariable(BundleSourceEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentSource))
        {
            return environmentSource.Trim();
        }

        string executableSourcePath = Path.Combine(AppContext.BaseDirectory, BundleSourceFileName);
        if (TryReadSourceFile(executableSourcePath, out var source))
        {
            return source;
        }

        return null;
    }

    private static string ResolveBundledWorkerDirectory()
    {
        string localEngineDirectory = Path.Combine(AppContext.BaseDirectory, BundledWorkerDirectoryName);
        if (Directory.Exists(localEngineDirectory))
        {
            return localEngineDirectory;
        }

        string sourceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AirType.LocalAsrWorker"));
        return Directory.Exists(sourceDirectory) ? sourceDirectory : localEngineDirectory;
    }

    private static bool TryReadSourceFile(string path, out string? source)
    {
        source = null;
        if (!File.Exists(path))
        {
            return false;
        }

        source = File
            .ReadLines(path)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'));

        return !string.IsNullOrWhiteSpace(source);
    }

    private bool IsRuntimeInstalled(LocalAsrModelDescriptor model) =>
        IsWorkerPackagePresent(WorkerPath) &&
        IsPythonRuntimePresent(Path.Combine(_installDirectory, PythonRuntimeDirectoryName));

    private bool IsModelInstalled(LocalAsrModelDescriptor model, out string? actualModelPath)
    {
        actualModelPath = null;
        foreach (string path in GetCt2ModelCandidatePaths(model))
        {
            if (IsModelDirectoryPresent(path))
            {
                actualModelPath = path;
                return true;
            }
        }

        return false;
    }

    private string GetRuntimePath(LocalAsrModelDescriptor model) =>
        ResolveRuntimePythonPath(Path.Combine(_installDirectory, PythonRuntimeDirectoryName));

    private string GetModelPath(LocalAsrModelDescriptor model) =>
        Path.Combine(_installDirectory, model.InstallRelativePath);

    private IEnumerable<string> GetCt2ModelCandidatePaths(LocalAsrModelDescriptor model)
    {
        yield return GetModelPath(model);

        if (string.Equals(model.Id, LocalAsrModelCatalog.FasterWhisperSmallEnInt8, StringComparison.Ordinal))
        {
            yield return Path.Combine(_installDirectory, ModelDirectoryName, ModelName);
        }
    }

    private static IEnumerable<string> GetBundledCt2ModelCandidatePaths(string? sourceRoot, LocalAsrModelDescriptor model)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            yield break;
        }

        yield return Path.Combine(sourceRoot, model.InstallRelativePath);
        yield return Path.Combine(sourceRoot, ModelDirectoryName, model.ModelName);
    }

    private static bool IsWorkerPackagePresent(string path) =>
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "airtype_asr_worker", "__main__.py"));

    private static bool IsModelDirectoryPresent(string path) =>
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "config.json")) &&
        File.Exists(Path.Combine(path, "model.bin")) &&
        File.Exists(Path.Combine(path, "tokenizer.json"));

    private static void EnsureModelDirectoryPresent(string path, string displayName)
    {
        if (!IsModelDirectoryPresent(path))
        {
            throw new InvalidOperationException($"{displayName} model files are incomplete.");
        }
    }

    private static bool IsLegacyEnginePresent(string installDirectory)
    {
        if (!Directory.Exists(installDirectory))
        {
            return false;
        }

        return File.Exists(Path.Combine(installDirectory, "airtype-faster-whisper-worker.exe")) ||
               File.Exists(Path.Combine(installDirectory, "airtype-faster-whisper-worker.dll")) ||
               File.Exists(Path.Combine(installDirectory, "Whisper.net.dll")) ||
               File.Exists(Path.Combine(installDirectory, "models", "ggml-base.bin")) ||
               File.Exists(Path.Combine(installDirectory, LegacyVirtualEnvironmentDirectoryName, "Scripts", "python.exe"));
    }

    private static bool HasInstallArtifacts(string installDirectory)
    {
        return Directory.Exists(installDirectory) &&
               Directory.EnumerateFileSystemEntries(installDirectory).Any();
    }

    private static bool IsPythonRuntimePresent(string path) =>
        File.Exists(ResolveRuntimePythonPath(path));

    private static bool IsPortablePythonRuntimePresent(string path) =>
        File.Exists(Path.Combine(path, "python.exe")) &&
        File.Exists(Path.Combine(path, "python312.dll")) &&
        !File.Exists(Path.Combine(path, "pyvenv.cfg"));

    internal static string ResolveRuntimePythonPath(string runtimeRoot)
    {
        string portablePython = Path.Combine(runtimeRoot, "python.exe");
        if (File.Exists(portablePython))
        {
            return portablePython;
        }

        string developerVenvPython = Path.Combine(runtimeRoot, "Scripts", "python.exe");
        return File.Exists(developerVenvPython) ? developerVenvPython : portablePython;
    }

    private static string ResolvePythonExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("AIRTYPE_PYTHON");
        return string.IsNullOrWhiteSpace(configured) ? "python" : configured;
    }

    private static bool CanBuildRuntimeDuringInstall()
    {
        string? value = Environment.GetEnvironmentVariable(AllowRuntimeBuildEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        try
        {
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string stderr = await stderrTask;
            string stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}: {stderr}{stdout}");
            }
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void CopyWorkerPackageFromRoot(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        string pyprojectPath = Path.Combine(sourceDirectory, "pyproject.toml");
        if (File.Exists(pyprojectPath))
        {
            File.Copy(pyprojectPath, Path.Combine(targetDirectory, "pyproject.toml"), overwrite: true);
        }

        string packageRoot = Path.Combine(sourceDirectory, "airtype_asr_worker");
        if (Directory.Exists(packageRoot))
        {
            CopyDirectory(packageRoot, Path.Combine(targetDirectory, "airtype_asr_worker"));
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string targetSubdirectory = Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, directory));
            Directory.CreateDirectory(targetSubdirectory);
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string targetFile = Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static void ReplaceDirectory(string sourceDirectory, string targetDirectory)
    {
        string? parent = Path.GetDirectoryName(targetDirectory);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        string previousDirectory = $"{targetDirectory}.previous-{Guid.NewGuid():N}";
        bool movedExisting = false;
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, previousDirectory);
                movedExisting = true;
            }

            Directory.Move(sourceDirectory, targetDirectory);

            if (movedExisting && Directory.Exists(previousDirectory))
            {
                Directory.Delete(previousDirectory, recursive: true);
            }
        }
        catch
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            if (movedExisting && Directory.Exists(previousDirectory))
            {
                Directory.Move(previousDirectory, targetDirectory);
            }

            throw;
        }
    }

    private void WriteManifest(LocalAsrBundleManifest? bundleManifest = null)
    {
        Directory.CreateDirectory(_installDirectory);
        var manifest = new
        {
            schemaVersion = 2,
            installRoot = _installDirectory,
            bundle = bundleManifest == null
                ? null
                : new
                {
                    bundleManifest.BundleId,
                    bundleManifest.BundleVersion,
                    bundleManifest.EngineId,
                    bundleManifest.ModelId,
                    bundleManifest.RuntimeId,
                    bundleManifest.Platform,
                    bundleManifest.Architecture,
                    bundleManifest.Sha256,
                    bundleManifest.CreatedUtc
                },
            runtimes = new Dictionary<string, object>
            {
                [LocalAsrModelCatalog.Ct2RuntimeId] = new
                {
                    installed = IsRuntimeInstalled(LocalAsrModelCatalog.GetRequired(LocalAsrModelCatalog.FasterWhisperSmallEnInt8)),
                    path = Path.GetRelativePath(
                        _installDirectory,
                        ResolveRuntimePythonPath(Path.Combine(_installDirectory, PythonRuntimeDirectoryName)))
                }
            },
            models = LocalAsrModelCatalog.All.ToDictionary(
                model => model.Id,
                model =>
                {
                    bool installed = IsModelInstalled(model, out var actualPath);
                    return new
                    {
                        installed,
                        backend = model.Backend.ToString(),
                        path = Path.GetRelativePath(_installDirectory, actualPath ?? GetModelPath(model)),
                        source = model.DownloadSource
                    };
                })
        };

        string manifestPath = Path.Combine(_installDirectory, ManifestFileName);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CleanupStaleInstallArtifacts()
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-1);

        foreach (string directory in Directory.EnumerateDirectories(Path.GetTempPath(), $"{TempInstallPrefix}*"))
        {
            TryDeleteIfStale(directory, cutoff);
        }

        string? parentDirectory = Path.GetDirectoryName(_installDirectory);
        string installName = Path.GetFileName(_installDirectory);
        if (!string.IsNullOrWhiteSpace(parentDirectory) && Directory.Exists(parentDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(parentDirectory, $"{installName}.previous-*"))
            {
                TryDeleteIfStale(directory, cutoff);
            }
        }
    }

    private static void TryDeleteIfStale(string directory, DateTime cutoffUtc)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (info.Exists && info.LastWriteTimeUtc < cutoffUtc)
            {
                info.Delete(recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static long GetAvailableMemoryBytes()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
            {
                return info.TotalAvailableMemoryBytes;
            }
        }
        catch
        {
        }

        return 16L * 1024 * 1024 * 1024;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record PreparedBundleSource(string? SourceRoot, LocalAsrBundleManifest? Manifest);

    private sealed record ResolvedBundleSource(string ZipPath, LocalAsrBundleManifest? Manifest);
}
