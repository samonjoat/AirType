using Xunit;

namespace AirType.Tests.ViewModels;

public sealed class SettingsHotkeySourceTests
{
    [Fact]
    public void SaveHotkey_DoesNotSuppressSettingsRegistrationFailureFeedback()
    {
        string source = ReadRepositoryFile("AirType", "ViewModels", "SettingsViewModel.cs");

        Assert.DoesNotContain("_suppressHotkeyFailedNotification", source);
        Assert.Contains("NotificationDialog.ShowWarning(\"Hotkey Error\"", source);
        Assert.Contains("bool registered = _hotkeyManager.RegisterHotkey(_pendingKey, _pendingModifiers);", source);
    }

    [Fact]
    public void TextBoxTemplate_RespectsVerticalContentAlignment()
    {
        string source = ReadRepositoryFile("AirType", "Styles", "ControlStyles.xaml");

        Assert.Contains("VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"", source);
        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Top\"/>", source);
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
