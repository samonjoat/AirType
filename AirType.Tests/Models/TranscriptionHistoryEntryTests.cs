using AirType.Models;
using Xunit;

namespace AirType.Tests.Models;

public sealed class TranscriptionHistoryEntryTests
{
    [Fact]
    public void SpeechEngineDisplay_WhenLocalFallsBackToGroq_ShowsCloud()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8 unavailable -> whisper-large-v3 -> google/gemini-2.5-flash-lite"
        };

        Assert.Equal("Cloud", entry.SpeechEngineDisplay);
    }

    [Fact]
    public void CleanerDisplay_WhenLocalFallsBackToGroq_ShowsCleanupModelOnly()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8 unavailable -> whisper-large-v3 -> google/gemini-2.5-flash-lite"
        };

        Assert.Equal("Gemini Flash Lite", entry.CleanerDisplay);
    }

    [Fact]
    public void SpeechEngineDisplay_WhenGroqCloudIsUsed_ShowsCloud()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Groq",
            ModelUsed = "whisper-large-v3 -> gemini-flash-lite-latest"
        };

        Assert.Equal("Cloud", entry.SpeechEngineDisplay);
    }

    [Fact]
    public void SpeechEngineDisplay_WhenLocalSucceeds_ShowsLocal()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8 -> google/gemini-2.5-flash-lite"
        };

        Assert.Equal("Local", entry.SpeechEngineDisplay);
    }

    [Fact]
    public void CleanerDisplay_WhenLocalSucceeds_ShowsCleanupModelOnly()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8 -> google/gemini-2.5-flash-lite"
        };

        Assert.Equal("Gemini Flash Lite", entry.CleanerDisplay);
    }

    [Fact]
    public void CleanerDisplay_WhenCleanupIsOff_ShowsOff()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Groq",
            ModelUsed = "whisper-large-v3"
        };

        Assert.Equal("Off", entry.CleanerDisplay);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    public void CleanerDisplay_WhenCleanupDidNotSucceed_ShowsOff(string cleanupStatus)
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8",
            CleanupProvider = "OpenRouter",
            CleanupModel = "google/gemini-2.5-flash-lite",
            CleanupStatus = cleanupStatus
        };

        Assert.Equal("Off", entry.CleanerDisplay);
    }

    [Fact]
    public void LegacyAliases_DoNotExposeRawRuntimeChain()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8 unavailable -> whisper-large-v3 -> google/gemini-2.5-flash-lite"
        };

        Assert.Equal("Cloud", entry.ProviderDisplay);
        Assert.Equal("Gemini Flash Lite", entry.Model);
    }

    [Fact]
    public void CleanerDisplay_WhenUnknownLegacyCleanerIsStored_RemainsReadable()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "OpenRouter",
            ModelUsed = "provider/custom-model-alpha -> vendor/cleanup-model-beta"
        };

        Assert.Equal("Cleanup Model Beta", entry.CleanerDisplay);
    }

    [Theory]
    [InlineData("google/gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite")]
    [InlineData("google/gemini-3.5-flash", "Gemini 3.5 Flash")]
    [InlineData("openai/gpt-5.4-mini", "GPT-5.4 Mini")]
    [InlineData("anthropic/claude-haiku-4.5", "Claude Haiku 4.5")]
    [InlineData("qwen/qwen3.7-plus", "Qwen3.7 Plus")]
    [InlineData("deepseek/deepseek-v4-flash", "DeepSeek V4 Flash")]
    [InlineData("mistralai/mistral-small-2603", "Mistral Small 4")]
    public void CleanerDisplay_WhenKnownCleanupAliasIsStored_UsesCuratedLabel(string cleanupModel, string expected)
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = $"faster-whisper-small-en-int8 -> {cleanupModel}"
        };

        Assert.Equal(expected, entry.CleanerDisplay);
    }

    [Fact]
    public void BodyAndFinalTranscribedText_WhenRawMetadataIsNull_UseFinalTranscribedText()
    {
        var entry = new TranscriptionHistoryEntry
        {
            TranscribedText = "final edited transcript",
            RawTranscribedText = null
        };

        Assert.Equal("final edited transcript", entry.Body);
        Assert.Equal("final edited transcript", entry.FinalTranscribedText);
        Assert.Equal("final edited transcript", entry.Preview);
    }

    [Fact]
    public void Body_WhenCleanerSucceeded_DefaultsToCleanerTranscript()
    {
        var entry = new TranscriptionHistoryEntry
        {
            TranscribedText = "cleaned transcript text",
            RawTranscribedText = "raw asr transcript text",
            CleanupModel = "google/gemini-3.1-flash-lite",
            CleanupStatus = "Succeeded"
        };

        Assert.True(entry.HasCleanerTranscript);
        Assert.True(entry.CanToggleTranscriptDisplay);
        Assert.False(entry.IsShowingAsrTranscript);
        Assert.Equal("cleaned transcript text", entry.Body);
        Assert.Equal("Cleaner", entry.TranscriptSourceDisplay);
        Assert.Equal("Gemini 3.1 Flash-Lite", entry.TranscriptSourceModelDisplay);
        Assert.Equal("Show ASR Transcript", entry.TranscriptDisplayToggleHeader);
    }

    [Fact]
    public void Body_WhenDisplayModeIsAsr_ShowsRawTranscriptAndUsesRawWordCount()
    {
        var entry = new TranscriptionHistoryEntry
        {
            TranscribedText = "cleaned transcript",
            RawTranscribedText = "raw asr transcript has five",
            CleanupModel = "google/gemini-3.1-flash-lite",
            CleanupStatus = "Succeeded",
            RawModel = "whisper-large-v3",
            TranscriptDisplayMode = TranscriptionHistoryEntry.TranscriptDisplayModeAsr
        };

        Assert.True(entry.IsShowingAsrTranscript);
        Assert.Equal("raw asr transcript has five", entry.Body);
        Assert.Equal(5, entry.Words);
        Assert.Equal("ASR", entry.TranscriptSourceDisplay);
        Assert.Equal("Whisper Large V3", entry.TranscriptSourceModelDisplay);
        Assert.Equal("Show Cleaner Transcript", entry.TranscriptDisplayToggleHeader);
        Assert.False(entry.CanEditDisplayedTranscript);
    }

    [Fact]
    public void Body_WhenCleanupDidNotRun_TreatsStoredTranscriptAsAsrWithoutToggle()
    {
        var entry = new TranscriptionHistoryEntry
        {
            TranscribedText = "raw asr transcript",
            RawTranscribedText = "raw asr transcript",
            CleanupStatus = "Skipped",
            RawModel = "whisper-large-v3"
        };

        Assert.False(entry.HasCleanerTranscript);
        Assert.False(entry.CanToggleTranscriptDisplay);
        Assert.True(entry.IsShowingAsrTranscript);
        Assert.True(entry.CanEditDisplayedTranscript);
        Assert.Equal("raw asr transcript", entry.Body);
        Assert.Equal("ASR", entry.TranscriptSourceDisplay);
        Assert.Equal("Whisper Large V3", entry.TranscriptSourceModelDisplay);
    }

    [Theory]
    [InlineData(0.5, "0s")]
    [InlineData(42.8, "42s")]
    [InlineData(83, "1m23s")]
    [InlineData(3600, "1h")]
    [InlineData(3723, "1h2m3s")]
    public void Duration_UsesCompactTimeUnits(double totalSeconds, string expected)
    {
        var entry = new TranscriptionHistoryEntry
        {
            AudioDuration = TimeSpan.FromSeconds(totalSeconds)
        };

        Assert.Equal(expected, entry.Duration);
    }

    [Fact]
    public void LocalFallbackDisplay_WhenStructuredMetadataIsAvailable_RemainsCompatible()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "whisper-large-v3",
            ProviderChain = "faster-whisper-small-en-int8 unavailable -> whisper-large-v3 -> google/gemini-2.5-flash-lite",
            FallbackReason = "Local worker unavailable",
            CleanupProvider = "OpenRouter",
            CleanupModel = "google/gemini-2.5-flash-lite",
            CleanupStatus = "Succeeded"
        };

        Assert.Equal("Cloud", entry.SpeechEngineDisplay);
        Assert.Equal("Gemini Flash Lite", entry.CleanerDisplay);
        Assert.Equal("Cloud", entry.ProviderDisplay);
        Assert.Equal("Gemini Flash Lite", entry.Model);
    }

    [Fact]
    public void SpeechEngineDisplay_WhenActualProviderIsStructured_DoesNotRequireModelChainParsing()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "whisper-large-v3",
            RequestedProvider = "Local",
            RequestedModel = "faster-whisper-small-en-int8",
            ActualProvider = "Groq",
            ActualModel = "whisper-large-v3"
        };

        Assert.Equal("Cloud", entry.SpeechEngineDisplay);
        Assert.Equal("Cloud", entry.ProviderDisplay);
    }

    [Fact]
    public void SpeechEngineDisplay_WhenActualProviderIsLocal_StaysLocalEvenWithCleanupModel()
    {
        var entry = new TranscriptionHistoryEntry
        {
            Provider = "Local",
            ModelUsed = "faster-whisper-small-en-int8 -> google/gemini-3.1-flash-lite",
            RequestedProvider = "Local",
            RequestedModel = "faster-whisper-small-en-int8",
            ActualProvider = "Local",
            ActualModel = "faster-whisper-small-en-int8",
            CleanupProvider = "OpenRouter",
            CleanupModel = "google/gemini-3.1-flash-lite",
            CleanupStatus = "Succeeded"
        };

        Assert.Equal("Local", entry.SpeechEngineDisplay);
        Assert.Equal("Gemini 3.1 Flash-Lite", entry.CleanerDisplay);
    }
}
