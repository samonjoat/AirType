namespace AirType.Models.Transcription;

public sealed record AudioPcmFrame(
    Guid RecordingId,
    long Sequence,
    byte[] Pcm16Mono16Khz,
    int BytesRecorded,
    DateTimeOffset CapturedAt);
