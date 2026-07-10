using System;
using AirType.Services.Dictionary;
using AirType.Services.Transcription;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Prompts;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class TranscriptionApiClientMechanicsTests
{
    [Fact]
    public void CalculateTimeout_UsesBasePlusAudioDurationAndClamps()
    {
        var timeout = TranscriptionApiClientMechanics.CalculateTimeout(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(90),
            baseSeconds: 15);

        Assert.Equal(TimeSpan.FromSeconds(55), timeout);

        var clamped = TranscriptionApiClientMechanics.CalculateTimeout(
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(90),
            baseSeconds: 15);

        Assert.Equal(TimeSpan.FromSeconds(90), clamped);
    }

    [Theory]
    [InlineData("audio/wav", "wav")]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("", "wav")]
    [InlineData("wav", "wav")]
    public void GetAudioFormatFromMime_ExtractsProviderFormat(string mimeType, string expected)
    {
        Assert.Equal(expected, TranscriptionApiClientMechanics.GetAudioFormatFromMime(mimeType));
    }

    [Theory]
    [InlineData("openai/gpt-4o-mini-transcribe", "openai")]
    [InlineData("mistralai/voxtral-mini-transcribe", "mistral")]
    [InlineData("groq/whisper-large-v3", "groq")]
    [InlineData("custom-provider/model", "custom-provider")]
    [InlineData("", "")]
    public void GetOpenRouterProviderOptionKey_MapsKnownProviderPrefixes(string modelId, string expected)
    {
        Assert.Equal(expected, TranscriptionApiClientMechanics.GetOpenRouterProviderOptionKey(modelId));
    }

    [Fact]
    public void ComposeSystemInstruction_AppendsDictionarySectionWhenPresent()
    {
        var builder = new FakeDictionaryPromptBuilder("Dictionary hints");

        string result = TranscriptionApiClientMechanics.ComposeSystemInstruction(
            "Base instruction",
            builder,
            "Test");

        Assert.Equal("Base instruction\n\nDictionary hints", result);
    }

    [Fact]
    public void ComposeSystemInstruction_WhenDictionaryIsEmpty_ReturnsBaseInstruction()
    {
        var builder = new FakeDictionaryPromptBuilder("");

        string result = TranscriptionApiClientMechanics.ComposeSystemInstruction(
            "Base instruction",
            builder,
            "Test");

        Assert.Equal("Base instruction", result);
    }

    [Fact]
    public void CreateGroqRawRequest_DoesNotIncludeDictionaryPrompt()
    {
        GroqTranscriptionRequest request = TranscriptionRequestFactory.CreateGroqRawRequest("whisper-large-v3");

        Assert.Equal("whisper-large-v3", request.ModelId);
        Assert.Null(request.Prompt);
    }

    [Fact]
    public void CreateOpenRouterSttRawRequest_DoesNotIncludeDictionaryBiasPrompt()
    {
        OpenRouterSttTranscriptionRequest request =
            TranscriptionRequestFactory.CreateOpenRouterSttRawRequest("openai/gpt-4o-mini-transcribe");

        Assert.Equal("openai/gpt-4o-mini-transcribe", request.ModelId);
        Assert.Null(request.DictionaryBiasPrompt);
    }

    [Fact]
    public void CreateCleanupRequest_IncludesPromptDictionaryTranscriptAndFormatting()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Generic);

        TranscriptCleanupRequest request = TranscriptCleanupRequestFactory.Create(
            "gemini-3.1-flash-lite",
            "raw transcript",
            context,
            CleanupIntensity.Standard,
            TextFormattingMode.Markdown,
            "Known vocabulary: AirType.",
            "Follow the user's style.");

        Assert.Equal("gemini-3.1-flash-lite", request.ModelId);
        Assert.Contains("SECTION 1 - IDENTITY", request.SystemInstruction);
        Assert.Contains("SECTION 2 - DESTINATION CONTEXT", request.SystemInstruction);
        Assert.Contains("SECTION 3 - CLEANUP DEPTH", request.SystemInstruction);
        Assert.Contains("SECTION 4 - CORE RULES", request.SystemInstruction);
        Assert.Contains("SECTION 5 - USER VOCABULARY GUIDE", request.SystemInstruction);
        Assert.Contains("SECTION 6 - FORMATTING", request.SystemInstruction);
        Assert.Contains("SECTION 7 - OUTPUT CONTRACT", request.SystemInstruction);
        Assert.Contains("Return only the cleaned transcript text.", request.SystemInstruction);
        Assert.Contains("Destination context: a general text field.", request.SystemInstruction);
        Assert.Contains("Apply this style where it does not conflict with the destination context above:", request.SystemInstruction);
        Assert.Contains("Follow the user's style.", request.SystemInstruction);
        Assert.Contains("Known vocabulary: AirType.", request.SystemInstruction);
        Assert.Contains("Use Markdown only where it improves readability.", request.SystemInstruction);
        Assert.Equal("Raw transcript:\nraw transcript", request.UserInstruction);
        Assert.DoesNotContain("Markdown", request.UserInstruction);
    }

    [Fact]
    public void CreateCleanupRequest_OrdersSectionsAndOmitsDictionaryWhenEmpty()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Email);

        TranscriptCleanupRequest request = TranscriptCleanupRequestFactory.Create(
            "gemini-3.1-flash-lite",
            "raw transcript",
            context,
            CleanupIntensity.Light,
            TextFormattingMode.PlainText,
            "");

        AssertSectionOrder(
            request.SystemInstruction,
            "SECTION 1 - IDENTITY",
            "SECTION 2 - DESTINATION CONTEXT",
            "SECTION 3 - CLEANUP DEPTH",
            "SECTION 4 - CORE RULES",
            "SECTION 6 - FORMATTING",
            "SECTION 7 - OUTPUT CONTRACT");
        Assert.DoesNotContain("SECTION 5 - USER VOCABULARY GUIDE", request.SystemInstruction);
        Assert.Contains("Fix casing, punctuation, and clear errors only", request.SystemInstruction);
        Assert.Contains("Return plain text only.", request.SystemInstruction);
    }

    [Fact]
    public void CreateCleanupRequest_ForGroqUsesLowDeterministicTemperature()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Generic);

        TranscriptCleanupRequest request = TranscriptCleanupRequestFactory.Create(
            CleanupProvider.Groq,
            "llama-3.1-8b-instant",
            "raw transcript",
            context,
            CleanupIntensity.Standard,
            TextFormattingMode.PlainText,
            null,
            null);

        Assert.Equal(0.2, request.Temperature);
    }

    [Fact]
    public void CreateCleanupRequest_TerminalContextWithStyleOverrideKeepsTerminalGuidance()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Terminal);

        TranscriptCleanupRequest request = TranscriptCleanupRequestFactory.Create(
            "gemini-3.1-flash-lite",
            "raw transcript",
            context,
            CleanupIntensity.Standard,
            TextFormattingMode.PlainText,
            null,
            "Use a faithful cleanup style.");

        Assert.Contains("Destination context: a terminal.", request.SystemInstruction);
        Assert.Contains("shell command / command-line input", request.SystemInstruction);
        Assert.Contains("Apply this style where it does not conflict with the destination context above:", request.SystemInstruction);
        Assert.Contains("Use a faithful cleanup style.", request.SystemInstruction);
    }

    [Fact]
    public void ClassicStyleOverrideGuidance_DoesNotContainDestinationContext()
    {
        Assert.DoesNotContain("Destination context:", BuiltInPrompts.ClassicStyleOverrideGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("classic faithful cleanup style", BuiltInPrompts.ClassicStyleOverrideGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeSystemInstruction_ForRawTranscriptionKeepsClassicInstructionAndDictionaryAppend()
    {
        var builder = new FakeDictionaryPromptBuilder("Dictionary hints");

        string result = TranscriptionApiClientMechanics.ComposeSystemInstruction(
            GeminiTranscriptionRequest.DefaultSystemInstruction,
            builder,
            "Test");

        Assert.StartsWith(DefaultTranscriptionInstructions.Classic, result, StringComparison.Ordinal);
        Assert.Equal($"{DefaultTranscriptionInstructions.Classic}\n\nDictionary hints", result);
        Assert.Equal(OpenRouterTranscriptionRequest.DefaultSystemInstruction, GeminiTranscriptionRequest.DefaultSystemInstruction);
    }

    private static void AssertSectionOrder(string text, params string[] sections)
    {
        int previousIndex = -1;
        foreach (string section in sections)
        {
            int index = text.IndexOf(section, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Expected '{section}' after index {previousIndex}. Text: {text}");
            previousIndex = index;
        }
    }

    private sealed class FakeDictionaryPromptBuilder : IDictionaryPromptBuilder
    {
        private readonly string _section;

        public FakeDictionaryPromptBuilder(string section)
        {
            _section = section;
        }

        public string BuildDictionarySection() => _section;

        public int GetEstimatedTokenCount() => _section.Length / 4;
    }
}
