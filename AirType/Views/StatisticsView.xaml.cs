using System.Windows;
using System.Windows.Controls;
using AirType.ViewModels;

namespace AirType.Views;

/// <summary>
/// Statistics page displaying user transcription analytics, achievements, and usage trends.
/// Wired to StatisticsViewModel for live data from DailyStatisticsManager.
/// </summary>
public partial class StatisticsView : UserControl
{
    public StatisticsView()
    {
        InitializeComponent();

        if (Application.Current is App app && app.Services != null)
        {
            var services = app.Services;
            DataContext = new StatisticsViewModel(
                services.DailyStatisticsManager,
                services.HistoryDatabase,
                services.HistoryManager);
        }
    }
}
