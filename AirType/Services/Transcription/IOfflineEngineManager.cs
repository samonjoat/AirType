using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public interface IOfflineEngineManager
{
    string InstallDirectory { get; }

    string WorkerPath { get; }

    OfflineEngineStatus GetStatus();

    OfflineEngineStatus GetStatus(string? modelId);

    Task<OfflineEngineInstallResult> InstallAsync(
        IProgress<OfflineEngineInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OfflineEngineInstallResult> InstallModelAsync(
        string? modelId,
        IProgress<OfflineEngineInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(CancellationToken cancellationToken = default);

    Task RemoveModelAsync(string? modelId, CancellationToken cancellationToken = default);

    string GetModelFolderPath(string? modelId);
}

public sealed record OfflineEngineStatus(
    bool IsInstalled,
    string InstallDirectory,
    string WorkerPath,
    string Recommendation,
    string? BundleSource,
    bool CanInstall,
    string StatusText,
    string ModelPath,
    string? InstallSource,
    string ModelId,
    string ModelDisplayName,
    LocalAsrBackend Backend,
    string RuntimePath);

public sealed record OfflineEngineInstallResult(bool Success, string Message);

public sealed record OfflineEngineInstallProgress(int Percent, string Message);
