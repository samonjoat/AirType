using AirType.Models;
using AirType.Models.Configuration;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class CleanupContextResolverTests
{
    [Theory]
    [InlineData("WindowsTerminal", CleanupContextCategory.Terminal, true, true)]
    [InlineData("cmd.exe", CleanupContextCategory.Terminal, true, true)]
    [InlineData("pwsh", CleanupContextCategory.Terminal, true, true)]
    [InlineData("Code", CleanupContextCategory.Editor, true, false)]
    [InlineData("notepad++", CleanupContextCategory.Editor, true, false)]
    [InlineData("OUTLOOK", CleanupContextCategory.Email, false, false)]
    [InlineData("thunderbird.exe", CleanupContextCategory.Email, false, false)]
    [InlineData("slack", CleanupContextCategory.Chat, false, false)]
    [InlineData("discord", CleanupContextCategory.Chat, false, false)]
    [InlineData("chrome", CleanupContextCategory.Generic, false, false)]
    [InlineData("unknown-app", CleanupContextCategory.Generic, false, false)]
    public void Resolve_AutoMapsProcessToExpectedCategory(
        string processName,
        CleanupContextCategory expectedCategory,
        bool expectedForcesPlain,
        bool expectedSingleLine)
    {
        var context = new WindowContext(new IntPtr(1), "Window", processName, DateTime.UtcNow);

        var result = CleanupContextResolver.Resolve(context, CleanupContextMode.Auto);

        Assert.Equal(expectedCategory, result.Category);
        Assert.Equal(expectedForcesPlain, result.ForcesPlain);
        Assert.Equal(expectedSingleLine, result.SingleLine);
    }

    [Fact]
    public void Resolve_DoesNotInjectRawProcessNameIntoGuidance()
    {
        var context = new WindowContext(new IntPtr(1), "Window", "private-business-app", DateTime.UtcNow);

        var result = CleanupContextResolver.Resolve(context, CleanupContextMode.Auto);

        Assert.DoesNotContain("private-business-app", result.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CleanupContextMode.Terminal, CleanupContextCategory.Terminal)]
    [InlineData(CleanupContextMode.Editor, CleanupContextCategory.Editor)]
    [InlineData(CleanupContextMode.Email, CleanupContextCategory.Email)]
    [InlineData(CleanupContextMode.Chat, CleanupContextCategory.Chat)]
    [InlineData(CleanupContextMode.Generic, CleanupContextCategory.Generic)]
    public void Resolve_ManualModeOverridesProcess(
        CleanupContextMode mode,
        CleanupContextCategory expectedCategory)
    {
        var context = new WindowContext(new IntPtr(1), "Window", "OUTLOOK", DateTime.UtcNow);

        var result = CleanupContextResolver.Resolve(context, mode);

        Assert.Equal(expectedCategory, result.Category);
    }

    [Theory]
    [InlineData(CleanupContextCategory.Terminal, TextFormattingMode.Markdown, TextFormattingMode.PlainText)]
    [InlineData(CleanupContextCategory.Editor, TextFormattingMode.Markdown, TextFormattingMode.PlainText)]
    [InlineData(CleanupContextCategory.Email, TextFormattingMode.Markdown, TextFormattingMode.Markdown)]
    [InlineData(CleanupContextCategory.Generic, TextFormattingMode.PlainText, TextFormattingMode.PlainText)]
    public void ResolveEffectiveFormatting_ForcesPlainOnlyForPlainContexts(
        CleanupContextCategory category,
        TextFormattingMode globalFormatting,
        TextFormattingMode expected)
    {
        var context = CleanupContextResolver.CreateResolution(category);

        var result = CleanupContextResolver.ResolveEffectiveFormatting(context, globalFormatting);

        Assert.Equal(expected, result);
    }
}
