namespace AirType.Models.Transcription;

public enum LocalAsrWorkerState
{
    Stopped,
    Starting,
    Ready,
    ModelLoading,
    ModelLoaded,
    Prewarmed,
    Unavailable,
    Faulted
}

public enum LocalAsrSessionState
{
    Created,
    Starting,
    Active,
    Stopping,
    Complete,
    Cancelled,
    Failed
}

public enum LocalAsrFallbackReason
{
    None,
    WorkerUnavailable,
    WorkerWarming,
    WorkerError,
    LocalFlushTimeout,
    ModelMissing,
    EmptyResult,
    SessionRejected
}

public sealed record LocalAsrOptions(
    string ModelId,
    string ModelName,
    LocalAsrBackend Backend,
    string Device,
    string ComputeType,
    int CpuThreads,
    int SampleRate,
    int Channels,
    int FrameMilliseconds,
    int HotwordTokenCap,
    int InitialPromptTokenCap)
{
    public static LocalAsrOptions Default { get; } = new(
        ModelId: LocalAsrModelCatalog.Default.Id,
        ModelName: LocalAsrModelCatalog.Default.ModelName,
        Backend: LocalAsrModelCatalog.Default.Backend,
        Device: "cpu",
        ComputeType: "int8",
        CpuThreads: LocalAsrModelCatalog.Default.DefaultCpuThreads,
        SampleRate: 16000,
        Channels: 1,
        FrameMilliseconds: 50,
        HotwordTokenCap: 100,
        InitialPromptTokenCap: 180);

    public static LocalAsrOptions FromModel(LocalAsrModelDescriptor model) => Default with
    {
        ModelId = model.Id,
        ModelName = model.ModelName,
        Backend = model.Backend,
        CpuThreads = model.DefaultCpuThreads,
        HotwordTokenCap = model.SupportsPromptBias ? Default.HotwordTokenCap : 0
    };
}

public sealed record LocalAsrUtterance(
    int Index,
    string Text,
    int StartMilliseconds,
    int EndMilliseconds,
    int AsrMilliseconds);

public sealed record LocalAsrDiagnostics(
    int? WorkerStartupMilliseconds = null,
    int? ModelLoadMilliseconds = null,
    int? PrewarmMilliseconds = null,
    int? FinalFlushMilliseconds = null,
    int? AsrTotalMilliseconds = null,
    int? HotwordTokenCount = null,
    int? InitialPromptMaxTokenCount = null,
    string? WorkerSessionId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record LocalAsrResult(
    Guid RecordingId,
    bool IsComplete,
    string Text,
    string ModelUsed,
    LocalAsrFallbackReason FallbackReason,
    IReadOnlyList<LocalAsrUtterance> Utterances,
    LocalAsrDiagnostics Diagnostics)
{
    public static LocalAsrResult Incomplete(
        Guid recordingId,
        string modelUsed,
        LocalAsrFallbackReason reason,
        LocalAsrDiagnostics? diagnostics = null) =>
        new(recordingId, false, string.Empty, modelUsed, reason, Array.Empty<LocalAsrUtterance>(), diagnostics ?? new LocalAsrDiagnostics());
}
