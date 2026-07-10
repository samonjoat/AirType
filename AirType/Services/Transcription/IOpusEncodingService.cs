using System.Threading;
using System.Threading.Tasks;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public interface IOpusEncodingService
{
    /// <summary>
    /// Encodes a WAV file from disk to OGG/Opus format.
    /// </summary>
    Task<OpusEncodingResult> EncodeAsync(string wavFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encodes WAV audio data directly from memory to OGG/Opus format.
    /// This avoids the overhead of writing to a temp file and reading it back.
    /// </summary>
    /// <param name="wavData">Complete WAV file data including headers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Opus encoding result with OGG bytes</returns>
    Task<OpusEncodingResult> EncodeFromBytesAsync(byte[] wavData, CancellationToken cancellationToken = default);
}
