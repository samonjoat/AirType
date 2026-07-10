using AirType;
using Xunit;

namespace AirType.Tests.Windowing;

public sealed class MainWindowSizingTests
{
    [Fact]
    public void CalculateMinimumTrackSize_UsesDpiScaledMinimums()
    {
        var result = MainWindow.CalculateMinimumTrackSize(
            currentMinTrackWidth: 0,
            currentMinTrackHeight: 0,
            minWidthDips: 840,
            minHeightDips: 650,
            dpiScaleX: 1.25,
            dpiScaleY: 1.25,
            workAreaWidth: null,
            workAreaHeight: null);

        Assert.Equal(1050, result.Width);
        Assert.Equal(813, result.Height);
    }

    [Fact]
    public void CalculateMinimumTrackSize_ClampsToSmallMonitorWorkArea()
    {
        var result = MainWindow.CalculateMinimumTrackSize(
            currentMinTrackWidth: 100,
            currentMinTrackHeight: 100,
            minWidthDips: 840,
            minHeightDips: 650,
            dpiScaleX: 1,
            dpiScaleY: 1,
            workAreaWidth: 700,
            workAreaHeight: 600);

        Assert.Equal(700, result.Width);
        Assert.Equal(600, result.Height);
    }

    [Fact]
    public void CalculateMinimumTrackSize_DoesNotShrinkExistingWindowsMinimums()
    {
        var result = MainWindow.CalculateMinimumTrackSize(
            currentMinTrackWidth: 900,
            currentMinTrackHeight: 700,
            minWidthDips: 840,
            minHeightDips: 650,
            dpiScaleX: 1,
            dpiScaleY: 1,
            workAreaWidth: 1000,
            workAreaHeight: 1000);

        Assert.Equal(900, result.Width);
        Assert.Equal(700, result.Height);
    }

    [Fact]
    public void MainWindow_DoesNotOverrideSecondaryMonitorMaximizeRectangle()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AirType", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("minMaxInfo.MaxPosition.X =", source);
        Assert.DoesNotContain("minMaxInfo.MaxPosition.Y =", source);
        Assert.DoesNotContain("minMaxInfo.MaxSize.X =", source);
        Assert.DoesNotContain("minMaxInfo.MaxSize.Y =", source);
        Assert.DoesNotContain("Math.Abs(workArea.Left - monitorArea.Left)", source);
        Assert.DoesNotContain("Math.Abs(workArea.Top - monitorArea.Top)", source);
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
