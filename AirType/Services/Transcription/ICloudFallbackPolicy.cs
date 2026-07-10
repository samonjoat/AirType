using AirType.Models.Configuration;

namespace AirType.Services.Transcription;

public interface ICloudFallbackPolicy
{
    bool TryGetFallbackProvider(out TranscriptionProvider provider);
}
