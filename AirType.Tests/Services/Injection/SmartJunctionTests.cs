using AirType.Services.Injection;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class SmartJunctionTests
{
    [Fact]
    public void Apply_WhenDocumentIsEmpty_ReturnsTextUnchanged()
    {
        var context = new CaretContext(null, IsDocumentEmpty: true, IsCaretAtStart: true);

        string result = SmartJunction.Apply("Sentence two.", context);

        Assert.Equal("Sentence two.", result);
    }

    [Fact]
    public void Apply_WhenCaretIsAtStart_ReturnsTextUnchanged()
    {
        var context = new CaretContext(null, IsDocumentEmpty: false, IsCaretAtStart: true);

        string result = SmartJunction.Apply("Sentence two.", context);

        Assert.Equal("Sentence two.", result);
    }

    [Theory]
    [InlineData('.', "sentence two.", " Sentence two.")]
    [InlineData('!', "it worked.", " It worked.")]
    [InlineData('?', "yes.", " Yes.")]
    public void Apply_AfterSentenceEndingPrecedingCharacter_AddsSpaceAndCapitalizes(
        char preceding,
        string newText,
        string expected)
    {
        string result = SmartJunction.Apply(newText, Context(preceding));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_MidSentence_LowercasesFirstAlphabeticCharacter()
    {
        string result = SmartJunction.Apply("Next phrase.", Context('e'));

        Assert.Equal(" next phrase.", result);
    }

    [Fact]
    public void Apply_MidSentenceAfterComma_LowercasesFirstAlphabeticCharacter()
    {
        string result = SmartJunction.Apply("Next phrase.", Context(','));

        Assert.Equal(" next phrase.", result);
    }

    [Theory]
    [InlineData("I agree.", " I agree.")]
    [InlineData("I'm ready.", " I'm ready.")]
    [InlineData("API returned data.", " API returned data.")]
    [InlineData("HTTP status is green.", " HTTP status is green.")]
    public void Apply_MidSentence_PreservesIAndAcronymCasing(string newText, string expected)
    {
        string result = SmartJunction.Apply(newText, Context('e'));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(' ', "Next phrase.", "Next phrase.")]
    [InlineData('\t', "Next phrase.", "Next phrase.")]
    [InlineData('\n', "Next phrase.", "Next phrase.")]
    public void Apply_AfterWhitespace_DoesNotAddSpaceOrChangeCasing(
        char preceding,
        string newText,
        string expected)
    {
        string result = SmartJunction.Apply(newText, Context(preceding));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_WhenNewTextAlreadyStartsWithWhitespace_DoesNotAddSecondSpace()
    {
        string result = SmartJunction.Apply(" Sentence two.", Context('.'));

        Assert.Equal(" Sentence two.", result);
    }

    [Theory]
    [InlineData('(')]
    [InlineData('[')]
    [InlineData('{')]
    [InlineData('"')]
    [InlineData('\'')]
    public void Apply_AfterOpeningBracketOrQuote_DoesNotAddSpaceOrChangeCasing(char preceding)
    {
        string result = SmartJunction.Apply("Nested phrase.", Context(preceding));

        Assert.Equal("Nested phrase.", result);
    }

    [Theory]
    [InlineData('.', ". Sentence two.", " Sentence two.")]
    [InlineData('!', "! it worked.", " It worked.")]
    [InlineData('?', "? yes.", " Yes.")]
    public void Apply_AfterSentenceEnding_DropsLeadingDuplicatePunctuation(
        char preceding,
        string newText,
        string expected)
    {
        string result = SmartJunction.Apply(newText, Context(preceding));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(')', "(Sentence one.)", "sentence two.", " Sentence two.")]
    [InlineData(']', "[Sentence one.]", "sentence two.", " Sentence two.")]
    [InlineData('}', "{Sentence one.}", "sentence two.", " Sentence two.")]
    [InlineData('"', "Sentence one.\"", "sentence two.", " Sentence two.")]
    [InlineData('\'', "Sentence one.'", "sentence two.", " Sentence two.")]
    public void Apply_AfterSentenceEndingBeforeClosingWrapper_AddsSpaceAndCapitalizes(
        char preceding,
        string precedingText,
        string newText,
        string expected)
    {
        string result = SmartJunction.Apply(newText, Context(preceding, precedingText));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_AfterSentenceEndingBeforeClosingQuote_DropsLeadingDuplicatePunctuation()
    {
        string result = SmartJunction.Apply(". sentence two.", Context('"', "Sentence one.\""));

        Assert.Equal(" Sentence two.", result);
    }

    [Fact]
    public void Apply_AfterOpeningQuote_DoesNotAddSpaceOrChangeCasing()
    {
        string result = SmartJunction.Apply("Quoted phrase.", Context('"', "\""));

        Assert.Equal("Quoted phrase.", result);
    }

    [Fact]
    public void Apply_WhenNewTextIsEmpty_ReturnsEmpty()
    {
        string result = SmartJunction.Apply(string.Empty, Context('.'));

        Assert.Equal(string.Empty, result);
    }

    private static CaretContext Context(char preceding) =>
        new(preceding, IsDocumentEmpty: false, IsCaretAtStart: false);

    private static CaretContext Context(char preceding, string precedingText) =>
        new(preceding, IsDocumentEmpty: false, IsCaretAtStart: false, precedingText);
}
