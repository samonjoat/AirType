using AirType.Models;
using AirType.Models.Configuration;

namespace AirType.Services.Transcription;

public enum CleanupContextCategory
{
    Terminal = 0,
    Editor = 1,
    Email = 2,
    Chat = 3,
    Generic = 4
}

public sealed record CleanupContextResolution(
    CleanupContextCategory Category,
    string Guidance,
    bool ForcesPlain,
    bool SingleLine);

public static class CleanupContextResolver
{
    public static CleanupContextResolution Resolve(
        WindowContext? windowContext,
        CleanupContextMode mode)
    {
        var category = mode == CleanupContextMode.Auto
            ? ResolveCategoryFromProcess(windowContext?.ProcessName)
            : ResolveCategoryFromMode(mode);

        return CreateResolution(category);
    }

    public static TextFormattingMode ResolveEffectiveFormatting(
        CleanupContextResolution context,
        TextFormattingMode globalFormattingMode)
    {
        return context.ForcesPlain ? TextFormattingMode.PlainText : globalFormattingMode;
    }

    public static CleanupContextResolution CreateResolution(CleanupContextCategory category) => category switch
    {
        CleanupContextCategory.Terminal => new CleanupContextResolution(
            category,
            "Destination context: a terminal. Produce a shell command / command-line input: a single line, no Markdown, no trailing period, preserving exact commands, flags, paths, and casing.",
            ForcesPlain: true,
            SingleLine: true),
        CleanupContextCategory.Editor => new CleanupContextResolution(
            category,
            "Destination context: a code editor. Treat it as code or a code comment: preserve identifiers, symbols, and casing exactly; keep prose minimal.",
            ForcesPlain: true,
            SingleLine: false),
        CleanupContextCategory.Email => new CleanupContextResolution(
            category,
            "Destination context: an email. Produce well-formed email prose in paragraphs; keep any greeting or sign-off the user dictated.",
            ForcesPlain: false,
            SingleLine: false),
        CleanupContextCategory.Chat => new CleanupContextResolution(
            category,
            "Destination context: a chat app. Keep it concise and conversational with minimal formatting.",
            ForcesPlain: false,
            SingleLine: false),
        _ => new CleanupContextResolution(
            CleanupContextCategory.Generic,
            "Destination context: a general text field. Produce clean, natural prose.",
            ForcesPlain: false,
            SingleLine: false)
    };

    private static CleanupContextCategory ResolveCategoryFromMode(CleanupContextMode mode) => mode switch
    {
        CleanupContextMode.Terminal => CleanupContextCategory.Terminal,
        CleanupContextMode.Editor => CleanupContextCategory.Editor,
        CleanupContextMode.Email => CleanupContextCategory.Email,
        CleanupContextMode.Chat => CleanupContextCategory.Chat,
        CleanupContextMode.Generic => CleanupContextCategory.Generic,
        _ => CleanupContextCategory.Generic
    };

    private static CleanupContextCategory ResolveCategoryFromProcess(string? processName)
    {
        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return CleanupContextCategory.Generic;
        }

        if (IsTerminalProcess(normalized))
        {
            return CleanupContextCategory.Terminal;
        }

        if (IsEditorProcess(normalized))
        {
            return CleanupContextCategory.Editor;
        }

        if (IsEmailProcess(normalized))
        {
            return CleanupContextCategory.Email;
        }

        if (IsChatProcess(normalized))
        {
            return CleanupContextCategory.Chat;
        }

        return CleanupContextCategory.Generic;
    }

    private static string NormalizeProcessName(string? processName)
    {
        string normalized = (processName ?? string.Empty).Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private static bool IsTerminalProcess(string processName) =>
        Matches(processName, "WindowsTerminal", "wt", "cmd", "powershell", "pwsh", "conhost");

    private static bool IsEditorProcess(string processName) =>
        Matches(processName, "Code", "devenv", "rider", "rider64", "sublime_text", "notepad++");

    private static bool IsEmailProcess(string processName) =>
        Matches(processName, "OUTLOOK", "thunderbird");

    private static bool IsChatProcess(string processName) =>
        Matches(processName, "ms-teams", "teams", "slack", "discord");

    private static bool Matches(string processName, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(processName, candidate, StringComparison.OrdinalIgnoreCase));
}
