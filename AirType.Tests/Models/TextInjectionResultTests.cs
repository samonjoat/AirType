using AirType.Models.Injection;
using Xunit;

namespace AirType.Tests.Models;

public sealed class TextInjectionResultTests
{
    [Fact]
    public void SuccessResult_CanRecordClipboardRestoreMetadata()
    {
        var result = TextInjectionResult.SuccessResult(
            "ClipboardPaste",
            clipboardRestoreAttempted: true,
            clipboardRestoreSucceeded: true);

        Assert.True(result.Success);
        Assert.Equal(TextInjectionOutcome.Confirmed, result.Outcome);
        Assert.True(result.IsConfirmed);
        Assert.False(result.ClipboardFallbackUsed);
        Assert.True(result.ClipboardRestoreAttempted);
        Assert.True(result.ClipboardRestoreSucceeded);
    }

    [Fact]
    public void ProvisionalResult_RequiresRecoveryWithoutConfirmingInsertion()
    {
        var result = TextInjectionResult.ProvisionalResult("ClipboardPaste", "Dispatched.");

        Assert.True(result.Success);
        Assert.Equal(TextInjectionOutcome.Provisional, result.Outcome);
        Assert.True(result.IsProvisional);
        Assert.False(result.IsConfirmed);
        Assert.True(result.RecoverySuggested);
        Assert.False(result.ClipboardFallbackUsed);
    }

    [Fact]
    public void ClipboardFallback_CanDistinguishExpectedFallback()
    {
        var expected = TextInjectionResult.ClipboardFallback("Copied by user preference.", expected: true);
        var unexpected = TextInjectionResult.ClipboardFallback("Copied after injection failure.");

        Assert.Equal(TextInjectionOutcome.ClipboardFallback, expected.Outcome);
        Assert.True(expected.ClipboardFallbackUsed);
        Assert.True(expected.ClipboardFallbackExpected);
        Assert.True(unexpected.ClipboardFallbackUsed);
        Assert.False(unexpected.ClipboardFallbackExpected);
    }
}
