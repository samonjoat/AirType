using AirType.Models.Configuration;
using AirType.Services.Configuration;

namespace AirType.Services.Transcription;

public sealed class CloudFallbackPolicy : ICloudFallbackPolicy
{
    private readonly ICredentialManager _credentialManager;

    public CloudFallbackPolicy(ICredentialManager credentialManager)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
    }

    public bool TryGetFallbackProvider(out TranscriptionProvider provider)
    {
        if (!string.IsNullOrWhiteSpace(_credentialManager.GetActiveGroqApiKey()))
        {
            provider = TranscriptionProvider.Groq;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_credentialManager.GetActiveOpenRouterApiKey()))
        {
            provider = TranscriptionProvider.OpenRouter;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_credentialManager.GetActiveApiKey()))
        {
            provider = TranscriptionProvider.Gemini;
            return true;
        }

        provider = TranscriptionProvider.Local;
        return false;
    }
}
