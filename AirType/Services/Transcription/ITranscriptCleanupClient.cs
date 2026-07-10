using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public interface ITranscriptCleanupClient
{
    Task<TranscriptCleanupResponse> CleanupTranscriptAsync(
        TranscriptCleanupRequest request,
        CancellationToken cancellationToken = default);
}
