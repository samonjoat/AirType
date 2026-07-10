using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirType.Services.Transcription;

public static class LocalAsrProtocol
{
    public const int ProtocolVersion = 1;
    public const string PcmMagic = "ATPC";
    public const int PcmHeaderLength = 24;
    public const byte PcmFrameVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(object message) =>
        JsonSerializer.Serialize(message, JsonOptions);

    public static WorkerEvent ParseWorkerEvent(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var typeElement))
        {
            throw new InvalidOperationException("Worker event is missing a type.");
        }

        string type = typeElement.GetString() ?? string.Empty;
        return type switch
        {
            "ready" => new WorkerEvent(type),
            "model_loading" => new WorkerEvent(type, Model: GetString(document, "model")),
            "model_loaded" => new WorkerEvent(
                type,
                Model: GetString(document, "model"),
                LoadMilliseconds: GetInt(document, "loadMs")),
            "prewarmed" => new WorkerEvent(type, PrewarmMilliseconds: GetInt(document, "prewarmMs")),
            "recording_started" => new WorkerEvent(type, RecordingId: GetGuid(document, "recordingId")),
            "utterance_final" => new WorkerEvent(
                type,
                RecordingId: GetGuid(document, "recordingId"),
                Index: GetInt(document, "index"),
                Text: GetString(document, "text"),
                StartMilliseconds: GetInt(document, "startMs"),
                EndMilliseconds: GetInt(document, "endMs"),
                AsrMilliseconds: GetInt(document, "asrMs")),
            "recording_complete" => new WorkerEvent(
                type,
                RecordingId: GetGuid(document, "recordingId"),
                Text: GetString(document, "text"),
                UtteranceCount: GetInt(document, "utteranceCount") ?? GetInt(document, "utterancesFinal"),
                FinalFlushMilliseconds: GetInt(document, "finalFlushMs"),
                AsrTotalMilliseconds: GetInt(document, "asrTotalMs")),
            "metrics" => new WorkerEvent(
                type,
                RecordingId: GetGuid(document, "recordingId"),
                Name: GetString(document, "name"),
                Value: GetInt(document, "value"),
                HotwordTokenCount: GetInt(document, "hotwordTokenCount"),
                UtteranceCount: GetInt(document, "utterancesFinal")),
            "error" => new WorkerEvent(
                type,
                RecordingId: GetGuid(document, "recordingId"),
                Code: GetString(document, "code"),
                Message: GetString(document, "message"),
                Severity: GetString(document, "severity")),
            _ => new WorkerEvent(type)
        };
    }

    public static byte[] BuildPcmFrame(long sequence, long firstSample, ReadOnlySpan<byte> pcmPayload, bool isFinal = false)
    {
        var frame = new byte[PcmHeaderLength + pcmPayload.Length];
        frame[0] = (byte)'A';
        frame[1] = (byte)'T';
        frame[2] = (byte)'P';
        frame[3] = (byte)'C';
        frame[4] = PcmFrameVersion;
        // The Python worker packs <4sBBHQQ>: magic, version, headerLength, reserved, sequence, firstSample.
        frame[5] = PcmHeaderLength;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), isFinal ? (ushort)1 : (ushort)0);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(8, 8), checked((ulong)sequence));
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16, 8), checked((ulong)firstSample));
        pcmPayload.CopyTo(frame.AsSpan(PcmHeaderLength));
        return frame;
    }

    private static string? GetString(JsonDocument document, string propertyName) =>
        document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;

    private static int? GetInt(JsonDocument document, string propertyName)
    {
        if (!document.RootElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static Guid? GetGuid(JsonDocument document, string propertyName)
    {
        string? value = GetString(document, propertyName);
        return Guid.TryParse(value, out var result) ? result : null;
    }

    public sealed record WorkerEvent(
        string Type,
        Guid? RecordingId = null,
        string? Model = null,
        string? Text = null,
        int? Index = null,
        int? StartMilliseconds = null,
        int? EndMilliseconds = null,
        int? AsrMilliseconds = null,
        int? LoadMilliseconds = null,
        int? PrewarmMilliseconds = null,
        int? FinalFlushMilliseconds = null,
        int? AsrTotalMilliseconds = null,
        int? UtteranceCount = null,
        string? Name = null,
        int? Value = null,
        int? HotwordTokenCount = null,
        string? Code = null,
        string? Message = null,
        string? Severity = null);
}
