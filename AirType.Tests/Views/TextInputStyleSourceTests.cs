using Xunit;

namespace AirType.Tests.Views;

public sealed class TextInputStyleSourceTests
{
    [Fact]
    public void NotesAndHistoryEditorsUseSharedTextAreaStyle()
    {
        string controlStyles = ReadRepositoryFile("AirType", "Styles", "ControlStyles.xaml");
        string notesView = ReadRepositoryFile("AirType", "Views", "NotesView.xaml");
        string editNoteContent = ReadRepositoryFile("AirType", "Views", "EditNoteContent.xaml");
        string historyView = ReadRepositoryFile("AirType", "Views", "HistoryView.xaml");

        Assert.Contains("x:Key=\"CustomTextArea\"", controlStyles);
        Assert.Contains("<ControlTemplate TargetType=\"TextBox\">", controlStyles);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"13,10\"/>", controlStyles);
        Assert.Contains("VerticalAlignment=\"Top\"", controlStyles);

        Assert.Contains("x:Name=\"NotesTextBox\"", notesView);
        Assert.Contains("Style=\"{DynamicResource CustomTextArea}\"", notesView);
        Assert.Contains("materialDesign:HintAssist.Hint=\"Start a note, or hold Space to dictate...\"", notesView);
        Assert.DoesNotContain("BorderThickness=\"0\"", notesView);
        Assert.DoesNotContain("Background=\"Transparent\"", notesView);

        Assert.Contains("x:Name=\"ContentTextBox\"", editNoteContent);
        Assert.Contains("Style=\"{DynamicResource CustomTextArea}\"", editNoteContent);
        Assert.DoesNotContain("BorderThickness=\"0\"", editNoteContent);
        Assert.DoesNotContain("Background=\"Transparent\"", editNoteContent);

        Assert.Contains("AutomationProperties.Name=\"Transcript edit text\"", historyView);
        Assert.Contains("Style=\"{DynamicResource CustomTextArea}\"", historyView);
        Assert.DoesNotContain("Style=\"{DynamicResource CustomTextBox}\"\r\n                                                             Height=\"Auto\"", historyView);
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
