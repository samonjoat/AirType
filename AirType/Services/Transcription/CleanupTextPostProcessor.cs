using System.Text.RegularExpressions;
using AirType.Models.Configuration;

namespace AirType.Services.Transcription;

internal static class CleanupTextPostProcessor
{
    private static readonly Regex MarkdownBoldRegex =
        new(@"(?<!\*)\*\*(?!\s)(.+?)(?<!\s)\*\*(?!\*)", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MarkdownItalicAsteriskRegex =
        new(@"(?<![\w*])\*(?![\s*])(.+?)(?<!\s)\*(?![\w*])", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MarkdownItalicUnderscoreRegex =
        new(@"(?<![\w_])_(?![\s_])(.+?)(?<!\s)_(?![\w_])", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MarkdownInlineCodeRegex = new(@"`([^`]*)`", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownImageRegex = new(@"!\[[^\]]*\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownQuoteRegex = new(@"^>\s?", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex SurroundingCodeFenceRegex =
        new(@"^\s*```[^\r\n]*\r?\n(?<body>.*?)(?:\r?\n)?```\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex LeadingListMarkerRegex = new(@"^\s*[-*]\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LineBreakWithPaddingRegex = new(@"[ \t]*\r?\n[ \t]*|[ \t]*\r[ \t]*", RegexOptions.Compiled);
    private static readonly Regex WindowsPathRegex =
        new(@"(?<![\w])(?:[A-Za-z]:\\|\\\\)[^\s`""'<>|]+", RegexOptions.Compiled);

    public static string Apply(
        string transcribedText,
        CleanupContextResolution context,
        TextFormattingMode effectiveFormatting)
    {
        string decoded = DecodeEscapedText(transcribedText);

        if (context.Category is CleanupContextCategory.Terminal or CleanupContextCategory.Editor)
        {
            return ApplyContextSafePostProcessing(decoded, context);
        }

        string result = effectiveFormatting == TextFormattingMode.PlainText
            ? StripMarkdownFormatting(decoded)
            : decoded;

        return result.TrimEnd();
    }

    internal static string DecodeEscapedText(string escapedText)
    {
        if (string.IsNullOrEmpty(escapedText))
        {
            return escapedText;
        }

        var protectedPaths = new List<string>();
        string protectedText = WindowsPathRegex.Replace(escapedText, match =>
        {
            int index = protectedPaths.Count;
            protectedPaths.Add(match.Value);
            return $"__AIRTYPE_PATH_{index}__";
        });

        string decoded = protectedText
            .Replace(@"\r\n", "\r\n")
            .Replace(@"\n", "\n")
            .Replace(@"\r", "\r")
            .Replace(@"\t", "\t")
            .Replace(@"\*", "*")
            .Replace(@"\_", "_")
            .Replace(@"\`", "`");

        for (int i = 0; i < protectedPaths.Count; i++)
        {
            decoded = decoded.Replace($"__AIRTYPE_PATH_{i}__", protectedPaths[i]);
        }

        return decoded;
    }

    private static string ApplyContextSafePostProcessing(
        string text,
        CleanupContextResolution context)
    {
        string result = StripSurroundingCodeFence(text);
        result = LeadingListMarkerRegex.Replace(result, string.Empty);

        if (context.Category == CleanupContextCategory.Terminal)
        {
            result = StripSurroundingSingleBackticks(result);
            result = LineBreakWithPaddingRegex.Replace(result, " ");
            result = result.Replace('\t', ' ').Trim();
            result = TrimTerminalTrailingPeriod(result);
        }

        return result.TrimEnd();
    }

    private static string StripMarkdownFormatting(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string result = text;
        result = MarkdownBoldRegex.Replace(result, "$1");
        result = MarkdownItalicAsteriskRegex.Replace(result, "$1");
        result = MarkdownItalicUnderscoreRegex.Replace(result, "$1");
        result = MarkdownInlineCodeRegex.Replace(result, "$1");
        result = MarkdownImageRegex.Replace(result, string.Empty);
        result = MarkdownLinkRegex.Replace(result, "$1");
        result = MarkdownQuoteRegex.Replace(result, string.Empty);
        return result;
    }

    private static string StripSurroundingCodeFence(string text)
    {
        var match = SurroundingCodeFenceRegex.Match(text);
        return match.Success ? match.Groups["body"].Value : text;
    }

    private static string StripSurroundingSingleBackticks(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '`' && trimmed[^1] == '`'
            ? trimmed[1..^1]
            : text;
    }

    private static string TrimTerminalTrailingPeriod(string text)
    {
        string trimmed = text.TrimEnd();
        if (!trimmed.EndsWith(".", StringComparison.Ordinal) ||
            trimmed.EndsWith("...", StringComparison.Ordinal) ||
            trimmed.EndsWith(" .", StringComparison.Ordinal))
        {
            return trimmed;
        }

        string withoutPeriod = trimmed[..^1];
        if (withoutPeriod.Length == 0)
        {
            return trimmed;
        }

        char previous = withoutPeriod[^1];
        return char.IsLetterOrDigit(previous) || previous is '"' or '\'' or ')' or ']'
            ? withoutPeriod
            : trimmed;
    }
}
