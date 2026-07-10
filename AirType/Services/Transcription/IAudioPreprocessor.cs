using System.Threading;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Trims leading/trailing silence and validates audio before upload.
/// </summary>
public interface IAudioPreprocessor
{
    Task<AudioPreprocessingResult> PreprocessAsync(
        byte[] wavData,
        RecordingSession session,
        CancellationToken cancellationToken = default);
}
