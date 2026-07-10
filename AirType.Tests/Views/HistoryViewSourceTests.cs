using Xunit;

namespace AirType.Tests.Views;

public sealed class HistoryViewSourceTests
{
    [Fact]
    public void EditButton_UsesViewModelCommandsWithoutPlaceholderHandler()
    {
        string xaml = ReadRepositoryFile("AirType", "Views", "HistoryView.xaml");
        string codeBehind = ReadRepositoryFile("AirType", "Views", "HistoryView.xaml.cs");

        Assert.Contains("StartEditCommand", xaml);
        Assert.Contains("SaveEditCommand", xaml);
        Assert.Contains("CancelEditCommand", xaml);
        Assert.DoesNotContain("OnEditClick", xaml);
        Assert.DoesNotContain("OnEditClick", codeBehind);
        Assert.DoesNotContain("Edit mode not implemented in this version", codeBehind);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(relativeParts)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AirType", "AirType.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AirType repository root.");
    }
}
