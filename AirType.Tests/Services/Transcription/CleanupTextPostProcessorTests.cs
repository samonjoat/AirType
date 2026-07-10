using AirType.Models.Configuration;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class CleanupTextPostProcessorTests
{
    [Fact]
    public void Apply_TerminalPreservesBackslashesAndCollapsesToSingleLine()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Terminal);

        string result = CleanupTextPostProcessor.Apply(
            "cd C:\\Users\\Sam\\project\n dotnet test C:\\Users\\Sam\\project\\AirType.Tests.csproj",
            context,
            TextFormattingMode.PlainText);

        Assert.Equal("cd C:\\Users\\Sam\\project dotnet test C:\\Users\\Sam\\project\\AirType.Tests.csproj", result);
    }

    [Fact]
    public void Apply_WhenCleanupDisabledAndTerminalTarget_StillFormatsRawTranscriptSafely()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Terminal);

        string result = CleanupTextPostProcessor.Apply(
            "copy C:\\Users\\Sam\\input.txt\nC:\\Users\\Sam\\output.txt",
            context,
            TextFormattingMode.PlainText);

        Assert.Equal("copy C:\\Users\\Sam\\input.txt C:\\Users\\Sam\\output.txt", result);
    }

    [Fact]
    public void Apply_EditorPreservesSnakeCaseAsterisksAndPaths()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Editor);

        string result = CleanupTextPostProcessor.Apply(
            "```csharp\nvar snake_case = a * b;\nvar path = \"C:\\Users\\Sam\";\n```",
            context,
            TextFormattingMode.PlainText);

        Assert.Equal("var snake_case = a * b;\nvar path = \"C:\\Users\\Sam\";", result);
    }

    [Fact]
    public void Apply_GenericPlainDoesNotStripBackslashesOrSnakeCase()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Generic);

        string result = CleanupTextPostProcessor.Apply(
            "Open `C:\\temp\\new\\file.txt` and keep snake_case.",
            context,
            TextFormattingMode.PlainText);

        Assert.Equal("Open C:\\temp\\new\\file.txt and keep snake_case.", result);
    }

    [Fact]
    public void Apply_GenericPlainDoesNotMangleArithmeticAsterisks()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Generic);

        string result = CleanupTextPostProcessor.Apply(
            "Calculate a * b * c before sending.",
            context,
            TextFormattingMode.PlainText);

        Assert.Equal("Calculate a * b * c before sending.", result);
    }

    [Fact]
    public void Apply_EmailPlainStillStripsMarkdown()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Email);

        string result = CleanupTextPostProcessor.Apply(
            "**Hello** [team](https://example.com), please review `AirType`.\n> quoted",
            context,
            TextFormattingMode.PlainText);

        Assert.Equal("Hello team, please review AirType.\nquoted", result);
    }

    [Fact]
    public void Apply_GenericMarkdownKeepsMarkdownButDecodesEscapedNewlines()
    {
        var context = CleanupContextResolver.CreateResolution(CleanupContextCategory.Generic);

        string result = CleanupTextPostProcessor.Apply(
            "**Hello**\\n- item",
            context,
            TextFormattingMode.Markdown);

        Assert.Equal("**Hello**\n- item", result);
    }
}
