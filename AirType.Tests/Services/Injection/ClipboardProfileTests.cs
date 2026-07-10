using AirType.Services;
using AirType.Services.Injection;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class ClipboardProfileTests
{
    [Fact]
    public void FromSnapshot_TextFormatsOnly_ClassifiesAsTextOnly()
    {
        ClipboardSnapshot snapshot = ClipboardSnapshot.CapturedNative(
            new[]
            {
                new ClipboardFormatSnapshot(13, "CF_UNICODETEXT", new byte[] { 1, 2 }),
                new ClipboardFormatSnapshot(16, "CF_LOCALE", new byte[] { 3, 4 })
            },
            "text");

        ClipboardProfile profile = ClipboardProfile.FromSnapshot(snapshot);

        Assert.True(profile.Captured);
        Assert.True(profile.HasData);
        Assert.True(profile.IsTextOnly);
        Assert.False(profile.IsNonText);
    }

    [Fact]
    public void FromSnapshot_WpfTextAncillaryFormats_ClassifiesAsTextOnly()
    {
        ClipboardSnapshot snapshot = ClipboardSnapshot.CapturedNative(
            new[]
            {
                new ClipboardFormatSnapshot(49161, "DataObject", new byte[] { 1, 2 }),
                new ClipboardFormatSnapshot(13, "CF_UNICODETEXT", new byte[] { 3, 4 }),
                new ClipboardFormatSnapshot(49171, "Ole Private Data", new byte[] { 5, 6 }),
                new ClipboardFormatSnapshot(16, "CF_LOCALE", new byte[] { 7, 8 }),
                new ClipboardFormatSnapshot(1, "CF_TEXT", new byte[] { 9, 10 }),
                new ClipboardFormatSnapshot(7, "CF_OEMTEXT", new byte[] { 11, 12 })
            },
            "wpf text");

        ClipboardProfile profile = ClipboardProfile.FromSnapshot(snapshot);

        Assert.True(profile.Captured);
        Assert.True(profile.HasData);
        Assert.True(profile.IsTextOnly);
        Assert.False(profile.IsNonText);
    }

    [Fact]
    public void FromSnapshot_FileDropFormat_ClassifiesAsNonText()
    {
        ClipboardSnapshot snapshot = ClipboardSnapshot.CapturedNative(
            new[]
            {
                new ClipboardFormatSnapshot(15, "CF_HDROP", new byte[] { 1, 2 }),
                new ClipboardFormatSnapshot(13, "CF_UNICODETEXT", new byte[] { 3, 4 })
            },
            "file");

        ClipboardProfile profile = ClipboardProfile.FromSnapshot(snapshot);

        Assert.True(profile.Captured);
        Assert.True(profile.HasData);
        Assert.False(profile.IsTextOnly);
        Assert.True(profile.IsNonText);
    }

    [Fact]
    public void FromSnapshot_Unavailable_DoesNotReportNonText()
    {
        ClipboardProfile profile = ClipboardProfile.FromSnapshot(ClipboardSnapshot.Unavailable());

        Assert.False(profile.Captured);
        Assert.False(profile.HasData);
        Assert.False(profile.IsTextOnly);
        Assert.False(profile.IsNonText);
    }
}
