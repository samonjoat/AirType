using AirType.Services.Injection;
using System.Runtime.InteropServices;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class NativePasteDispatchTests
{
    [Fact]
    public void TextInjectionService_DoesNotUseWindowsFormsSendKeys()
    {
        string source = File.ReadAllText(FindRepoFile("AirType", "Services", "TextInjectionService.cs"));

        Assert.DoesNotContain("SendKeys.Send", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms.SendKeys", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeInputStruct_MatchesWin32InputSize()
    {
        int expectedSize = Environment.Is64BitProcess ? 40 : 28;

        Assert.Equal(expectedSize, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static string FindRepoFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from {AppContext.BaseDirectory}.");
    }
}
