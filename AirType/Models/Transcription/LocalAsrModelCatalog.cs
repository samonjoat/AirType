using System.IO;

namespace AirType.Models.Transcription;

public enum LocalAsrBackend
{
    FasterWhisperCt2
}

public sealed record LocalAsrModelDescriptor(
    string Id,
    string DisplayName,
    LocalAsrBackend Backend,
    string RuntimeId,
    string ModelName,
    string InstallRelativePath,
    string DownloadSource,
    string[] ExpectedFiles,
    int DefaultCpuThreads,
    bool SupportsStreaming,
    bool SupportsPromptBias,
    bool SupportsHotwords)
{
    public bool IsFasterWhisperCt2 => Backend == LocalAsrBackend.FasterWhisperCt2;
}

public static class LocalAsrModelCatalog
{
    // Retired IDs are kept only so old config/history values can be recognized and sanitized.
    public const string FasterWhisperBaseEnInt8 = "faster-whisper-base-en-int8";
    public const string WhisperCppBaseEnQ8 = "whisper-cpp-base-en-q8_0";
    public const string WhisperCppSmallEnQ8 = "whisper-cpp-small-en-q8_0";

    public const string FasterWhisperSmallEnInt8 = "faster-whisper-small-en-int8";
    public const string DefaultModelId = FasterWhisperSmallEnInt8;
    public const string Ct2RuntimeId = "ct2-python";

    public static readonly LocalAsrModelDescriptor[] All =
    {
        new(
            FasterWhisperSmallEnInt8,
            "Small.en CT2",
            LocalAsrBackend.FasterWhisperCt2,
            Ct2RuntimeId,
            "small.en",
            Path.Combine("models", "ct2", "small.en"),
            "Systran/faster-whisper-small.en",
            new[] { "config.json", "model.bin", "tokenizer.json" },
            8,
            SupportsStreaming: true,
            SupportsPromptBias: true,
            SupportsHotwords: true)
    };

    public static LocalAsrModelDescriptor Default => GetRequired(DefaultModelId);

    public static LocalAsrModelDescriptor GetRequired(string? modelId) =>
        Find(modelId) ?? Default;

    public static LocalAsrModelDescriptor? Find(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return All.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
    }

    public static bool Contains(string? modelId) => Find(modelId) != null;
}
