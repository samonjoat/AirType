using System.Text;
using AirType.Models.Configuration;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Creates text-only cleanup requests from explicit prompt, dictionary, and formatting inputs.
/// </summary>
public static class TranscriptCleanupRequestFactory
{
    public static TranscriptCleanupRequest Create(
        CleanupProvider cleanupProvider,
        string modelId,
        string rawTranscript,
        CleanupContextResolution context,
        CleanupIntensity intensity,
        TextFormattingMode effectiveFormatting,
        string? dictionarySection,
        string? styleOverrideGuidance = null)
    {
        var request = Create(
            modelId,
            rawTranscript,
            context,
            intensity,
            effectiveFormatting,
            dictionarySection,
            styleOverrideGuidance);
        request.Temperature = cleanupProvider switch
        {
            CleanupProvider.Groq => 0.2,
            _ => request.Temperature
        };

        return request;
    }

    public static TranscriptCleanupRequest Create(
        string modelId,
        string rawTranscript,
        CleanupContextResolution context,
        CleanupIntensity intensity,
        TextFormattingMode effectiveFormatting,
        string? dictionarySection,
        string? styleOverrideGuidance = null)
    {
        var system = new StringBuilder();

        system.AppendLine("SECTION 1 - IDENTITY");
        system.AppendLine("You are the speech-to-text cleanup editor built into AirType, a Windows dictation app. A separate transcription engine has already turned the user's speech into a raw transcript. Return a clean, faithful version of that transcript, which AirType pastes directly into the user's active application on their behalf. Because your output is inserted verbatim, return only the finished text.");
        system.AppendLine();
        system.AppendLine("SECTION 2 - DESTINATION CONTEXT");
        system.AppendLine(context.Guidance);
        if (!string.IsNullOrWhiteSpace(styleOverrideGuidance))
        {
            system.AppendLine("Apply this style where it does not conflict with the destination context above:");
            system.AppendLine(styleOverrideGuidance.Trim());
        }
        system.AppendLine();
        system.AppendLine("SECTION 3 - CLEANUP DEPTH");
        system.AppendLine(GetIntensityInstruction(intensity));
        system.AppendLine();
        system.AppendLine("SECTION 4 - CORE RULES");
        system.AppendLine("Preserve the speaker's intended meaning. Keep names, acronyms, URLs, email addresses, file paths, commands, numbers, versions, and technical casing such as API, JSON, camelCase, and snake_case intact. Keep any [inaudible] marker. Base edits only on the transcript and the user vocabulary guide.");

        if (!string.IsNullOrWhiteSpace(dictionarySection))
        {
            system.AppendLine();
            system.AppendLine("SECTION 5 - USER VOCABULARY GUIDE");
            system.AppendLine("Apply this user vocabulary and likely corrections; prefer the listed spellings, and apply each correction when the surrounding context fits rather than blindly.");
            system.AppendLine(dictionarySection.Trim());
        }

        system.AppendLine();
        system.AppendLine("SECTION 6 - FORMATTING");
        system.AppendLine(GetFormattingInstruction(effectiveFormatting));
        system.AppendLine();
        system.AppendLine("SECTION 7 - OUTPUT CONTRACT");
        system.AppendLine("Return only the cleaned transcript text. No preamble, explanation, notes, headings, commentary, apologies, or code fences; do not describe changes or restate instructions.");

        return new TranscriptCleanupRequest
        {
            ModelId = modelId,
            SystemInstruction = system.ToString().Trim(),
            UserInstruction = $"Raw transcript:\n{rawTranscript}",
            Temperature = 0.1,
            MaxOutputTokens = 4096
        };
    }

    private static string GetIntensityInstruction(CleanupIntensity intensity) => intensity switch
    {
        CleanupIntensity.Light =>
            "Fix casing, punctuation, and clear errors only; keep the user's wording and order as spoken.",
        _ =>
            "Fix grammar, casing, punctuation, and sentence boundaries; remove fillers, false starts, repeated fragments, and retracted spans; keep corrected wording when the user self-corrects."
    };

    private static string GetFormattingInstruction(TextFormattingMode effectiveFormatting) =>
        effectiveFormatting == TextFormattingMode.Markdown
            ? "Use Markdown only where it improves readability."
            : "Return plain text only.";
}
