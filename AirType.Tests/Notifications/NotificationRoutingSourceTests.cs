using Xunit;

namespace AirType.Tests.Notifications;

public sealed class NotificationRoutingSourceTests
{
    [Fact]
    public void WorkflowFeedback_DoesNotSendDuplicateTrayBalloons()
    {
        string source = ReadRepositoryFile("AirType", "App.xaml.cs");

        Assert.DoesNotContain("_trayManager?.ShowBalloonTip", source);
        Assert.Contains("ToastService.Instance.Warning(message, title);", source);
        Assert.Contains("ToastService.Instance.Info(message, title);", source);
    }

    [Fact]
    public void NotificationDialog_RoutesContentAreaAcknowledgementsToToasts()
    {
        string source = ReadRepositoryFile("AirType", "Views", "NotificationDialog.xaml.cs");

        Assert.Contains("private static bool TryShowToast", source);
        Assert.Contains("if (!centerOnContentArea)", source);
        Assert.Contains("ToastService.Instance.Show(message, type, title);", source);
        Assert.Contains("TryShowToast(title, message, ToastType.Success, centerOnContentArea)", source);
        Assert.Contains("TryShowToast(title, message, ToastType.Warning, centerOnContentArea)", source);
        Assert.Contains("TryShowToast(title, message, ToastType.Error, centerOnContentArea)", source);
    }

    [Fact]
    public void ToastService_HasCentralRoutingControls()
    {
        string source = ReadRepositoryFile("AirType", "Services", "ToastService.cs");

        Assert.Contains("public enum ToastType { Info, Success, Warning, Error }", source);
        Assert.Contains("private const int MaxVisibleToasts = 4;", source);
        Assert.Contains("public ICommand DismissCommand", source);
        Assert.Contains("FindDuplicateToast", source);
        Assert.Contains("PackIconKind.AlertOutline", source);
    }

    [Fact]
    public void ToastTemplate_ExposesSemanticIconAndDismissAction()
    {
        string mainWindow = ReadRepositoryFile("AirType", "MainWindow.xaml");
        string controlStyles = ReadRepositoryFile("AirType", "Styles", "ControlStyles.xaml");

        Assert.Contains("x:Name=\"ToastIcon\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Dismiss notification\"", mainWindow);
        Assert.Contains("Path=DismissCommand", mainWindow);
        Assert.Contains("Value=\"Warning\"", mainWindow);
        Assert.Contains("ToastWarning", controlStyles);
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
