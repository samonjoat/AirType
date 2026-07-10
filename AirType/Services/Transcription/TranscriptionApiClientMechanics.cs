using System;
using System.Net.Http;
using System.Net.Http.Headers;
using AirType.Services.Dictionary;

namespace AirType.Services.Transcription;

/// <summary>
/// Reusable HTTP and prompt mechanics shared by transcription API clients.
/// </summary>
public static class TranscriptionApiClientMechanics
{
    public static void ConfigureJsonClient(HttpClient httpClient)
    {
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
    }

    public static TimeSpan CalculateTimeout(
        TimeSpan? audioDuration,
        TimeSpan minTimeout,
        TimeSpan maxTimeout,
        double baseSeconds,
        double secondsPerAudioSecond = 2)
    {
        double rawSeconds = baseSeconds + (audioDuration?.TotalSeconds ?? 30) * secondsPerAudioSecond;
        double clamped = Math.Clamp(rawSeconds, minTimeout.TotalSeconds, maxTimeout.TotalSeconds);
        return TimeSpan.FromSeconds(clamped);
    }

    public static string ComposeSystemInstruction(
        string systemInstruction,
        IDictionaryPromptBuilder? dictionaryPromptBuilder,
        string logComponent)
    {
        if (dictionaryPromptBuilder == null)
        {
            return systemInstruction;
        }

        string dictionarySection = dictionaryPromptBuilder.BuildDictionarySection();
        if (string.IsNullOrEmpty(dictionarySection))
        {
            return systemInstruction;
        }

        Logger.Debug(logComponent, $"Added dictionary section ({dictionarySection.Length} chars) to system instruction");
        return $"{systemInstruction}\n\n{dictionarySection}";
    }

    public static string GetAudioFormatFromMime(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return "wav";
        }

        int slashIndex = mimeType.IndexOf('/');
        if (slashIndex >= 0 && slashIndex < mimeType.Length - 1)
        {
            return mimeType[(slashIndex + 1)..];
        }

        return mimeType;
    }

    public static string GetOpenRouterProviderOptionKey(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return string.Empty;
        }

        int slashIndex = modelId.IndexOf('/');
        string prefix = slashIndex > 0 ? modelId[..slashIndex] : modelId;
        return prefix.Equals("mistralai", StringComparison.OrdinalIgnoreCase)
            ? "mistral"
            : prefix;
    }
}
