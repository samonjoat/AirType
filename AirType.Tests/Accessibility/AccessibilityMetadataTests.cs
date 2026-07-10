using Xunit;

namespace AirType.Tests.Accessibility;

public sealed class AccessibilityMetadataTests
{
    [Fact]
    public void Shell_ExposesAutomationNamesForNavigation()
    {
        string xaml = ReadXaml("AirType", "MainWindow.xaml");

        Assert.Contains("AutomationProperties.Name=\"AirType\"", xaml);
        Assert.DoesNotContain("Click=\"MinimizeWindow\"", xaml);
        Assert.DoesNotContain("Click=\"MaximizeRestoreWindow\"", xaml);
        Assert.DoesNotContain("Click=\"CloseWindow\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"History\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Settings\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Toggle theme\"", xaml);
        Assert.Contains("<Button x:Name=\"ThemeToggleBorder\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Collapse or expand sidebar\"", xaml);
    }

    [Fact]
    public void HistoryView_UsesStableVirtualizationAndNamesTranscriptActions()
    {
        string xaml = ReadXaml("AirType", "Views", "HistoryView.xaml");

        Assert.Contains("VirtualizingStackPanel.VirtualizationMode=\"Standard\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Transcription history\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Copy transcript\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Edit transcript\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Transcript options\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Transcript options menu\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Rerun transcript\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Download transcript audio\"", xaml);
        Assert.Contains("TranscriptDisplayToggleAutomationName", xaml);
        Assert.Contains("AutomationProperties.Name=\"Duration\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Delete transcript\"", xaml);
    }

    [Fact]
    public void SettingsView_ExposesAutomationNamesForOperationalControls()
    {
        string xaml = ReadXaml("AirType", "Views", "SettingsView.xaml");

        Assert.Contains("AutomationProperties.Name=\"Microphone\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Change global hotkey\"", xaml);
        Assert.Contains("Text=\"{Binding CurrentHotkeyDisplay}\"", xaml);
        Assert.DoesNotContain("Text=\"Ctrl\"", xaml);
        Assert.DoesNotContain("Text=\"Space\"", xaml);
        Assert.Contains("VerticalContentAlignment=\"Center\"", xaml);
        Assert.Contains("Height=\"48\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Speech engine\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Speech model\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Download offline engine\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Clean up transcript\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Gemini API key\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"OpenRouter API key\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Groq API key\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Copy system info\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Refresh recent logs\"", xaml);
    }

    [Fact]
    public void DictionaryAndCorrectionDialogs_ExposeNamesForListsAndIconOnlyActions()
    {
        string dictionaryXaml = ReadXaml("AirType", "Views", "DictionaryView.xaml");
        string addDialogXaml = ReadXaml("AirType", "Views", "AddToDictionaryDialog.xaml");

        Assert.Contains("AutomationProperties.Name=\"Dictionary entries\"", dictionaryXaml);
        Assert.Contains("AutomationProperties.Name=\"Edit dictionary entry\"", dictionaryXaml);
        Assert.Contains("AutomationProperties.Name=\"Delete dictionary entry\"", dictionaryXaml);
        Assert.Contains("AutomationProperties.Name=\"Detected corrections\"", addDialogXaml);
        Assert.Contains("AutomationProperties.Name=\"Edit detected correction\"", addDialogXaml);
        Assert.Contains("AutomationProperties.Name=\"Remove detected correction\"", addDialogXaml);
        Assert.Contains("AutomationProperties.Name=\"Save detected correction edit\"", addDialogXaml);
        Assert.Contains("AutomationProperties.Name=\"Cancel detected correction edit\"", addDialogXaml);
    }

    private static string ReadXaml(params string[] relativeParts)
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
