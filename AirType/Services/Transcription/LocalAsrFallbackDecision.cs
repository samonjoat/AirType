using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

internal static class LocalAsrFallbackDecision
{
    public static bool ShouldFallback(LocalAsrFallbackReason reason, bool cloudFallbackConfigured)
    {
        if (!cloudFallbackConfigured)
        {
            return false;
        }

        return reason switch
        {
            LocalAsrFallbackReason.None => false,
            LocalAsrFallbackReason.WorkerUnavailable => true,
            LocalAsrFallbackReason.WorkerWarming => true,
            LocalAsrFallbackReason.WorkerError => true,
            LocalAsrFallbackReason.LocalFlushTimeout => true,
            LocalAsrFallbackReason.ModelMissing => true,
            LocalAsrFallbackReason.EmptyResult => true,
            LocalAsrFallbackReason.SessionRejected => true,
            _ => false
        };
    }
}
