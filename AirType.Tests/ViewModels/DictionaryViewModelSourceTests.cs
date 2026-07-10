using Xunit;

namespace AirType.Tests.ViewModels;

public sealed class DictionaryViewModelSourceTests
{
    [Fact]
    public void DictionaryPage_RemovesImportExportAndDefaultsToVocabulary()
    {
        string source = ReadRepositoryFile("AirType", "ViewModels", "DictionaryViewModel.cs");
        string xaml = ReadRepositoryFile("AirType", "Views", "DictionaryView.xaml");

        Assert.Contains("private string _selectedTab = \"vocabulary\";", source);
        Assert.Contains(".OrderByDescending(e => e.CreatedAt)", source);
        Assert.Contains(".ThenByDescending(e => e.ModifiedAt)", source);
        Assert.DoesNotContain("ImportCommand", source);
        Assert.DoesNotContain("ExportCommand", source);
        Assert.DoesNotContain("ImportDictionary", source);
        Assert.DoesNotContain("ExportDictionary", source);
        Assert.DoesNotContain("new Microsoft.Win32.OpenFileDialog", source);
        Assert.DoesNotContain("new Microsoft.Win32.SaveFileDialog", source);

        Assert.DoesNotContain("Import dictionary", xaml);
        Assert.DoesNotContain("Export dictionary", xaml);
        Assert.Contains("Add dictionary entry", xaml);
    }

    [Fact]
    public void EditEntryCommand_UsesUpdateEntryAndEditState()
    {
        string source = ReadRepositoryFile("AirType", "ViewModels", "DictionaryViewModel.cs");
        string xaml = ReadRepositoryFile("AirType", "Views", "DictionaryView.xaml");

        Assert.Contains("EditEntryCommand = new RelayCommand<DictionaryEntry>(EditEntry);", source);
        Assert.Contains("private void EditEntry(DictionaryEntry? entry)", source);
        Assert.Contains("_dictionaryManager.UpdateEntryAsync(updated)", source);
        Assert.Contains("ReplaceLocalEntry(updated)", source);
        Assert.Contains("EntryPanelTitle", source);
        Assert.Contains("IsEntryTypeSelectorEnabled", source);
        Assert.DoesNotContain("Edit mode not implemented in this version", source);

        Assert.Contains("Text=\"{Binding EntryPanelTitle}\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding IsEntryTypeSelectorEnabled}\"", xaml);
        Assert.Contains("Content=\"{Binding SaveWordButtonText}\"", xaml);
        Assert.Contains("Content=\"{Binding SaveCorrectionButtonText}\"", xaml);
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
