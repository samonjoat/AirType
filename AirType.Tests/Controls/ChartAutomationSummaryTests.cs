using AirType.Controls;
using Xunit;

namespace AirType.Tests.Controls;

public sealed class ChartAutomationSummaryTests
{
    [Fact]
    public void LatencyChartDescribeValues_WhenEmpty_ReportsNoSamples()
    {
        string summary = LatencyChartAutomationSummary.DescribeValues(Array.Empty<double>());

        Assert.Equal("API latency chart, no samples", summary);
    }

    [Fact]
    public void LatencyChartDescribeValues_IncludesCountAverageMinimumAndMaximum()
    {
        string summary = LatencyChartAutomationSummary.DescribeValues(new[] { 1.0, 2.0, 4.0 });

        Assert.Equal("API latency chart, 3 samples, average 2.33 seconds, minimum 1 seconds, maximum 4 seconds", summary);
    }

    [Fact]
    public void WordActivityLineChartDescribeValues_WhenEmpty_ReportsNoDataPoints()
    {
        string summary = WordActivityLineChartAutomationSummary.DescribeValues(Array.Empty<double>());

        Assert.Equal("Word activity chart, no data points", summary);
    }

    [Fact]
    public void WordActivityLineChartDescribeValues_IncludesCountPeakAndLatest()
    {
        string summary = WordActivityLineChartAutomationSummary.DescribeValues(new[] { 0.0, 100.0, 50.0 });

        Assert.Equal("Word activity chart, 3 data points, peak 100 words, latest 50 words", summary);
    }
}
