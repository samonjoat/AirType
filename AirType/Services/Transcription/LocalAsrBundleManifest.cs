using System.IO;
using System.Text.Json;

namespace AirType.Services.Transcription;

internal sealed class LocalAsrBundleManifest
{
    public int SchemaVersion { get; init; }

    public string BundleId { get; init; } = string.Empty;

    public string BundleVersion { get; init; } = string.Empty;

    public string EngineId { get; init; } = string.Empty;

    public string ModelId { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string RuntimeId { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public string Architecture { get; init; } = string.Empty;

    public string ZipFileName { get; init; } = string.Empty;

    public string? DownloadUrl { get; init; }

    public string? Sha256 { get; init; }

    public long? CompressedBytes { get; init; }

    public long? UncompressedBytes { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public static LocalAsrBundleManifest Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<LocalAsrBundleManifest>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidOperationException($"Local ASR bundle manifest is empty: {path}");
    }
}
