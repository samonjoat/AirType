using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirType.Controls;

internal static class LatencyChartAutomationSummary
{
    public static string DescribeValues(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return "API latency chart, no samples";
        }

        double average = values.Average();
        double minimum = values.Min();
        double maximum = values.Max();
        return string.Format(
            CultureInfo.InvariantCulture,
            "API latency chart, {0} samples, average {1:0.##} seconds, minimum {2:0.##} seconds, maximum {3:0.##} seconds",
            values.Count,
            average,
            minimum,
            maximum);
    }
}

internal static class WordActivityLineChartAutomationSummary
{
    public static string DescribeValues(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return "Word activity chart, no data points";
        }

        double peak = values.Max();
        double latest = values[^1];
        return string.Format(
            CultureInfo.InvariantCulture,
            "Word activity chart, {0} data points, peak {1:0.##} words, latest {2:0.##} words",
            values.Count,
            peak,
            latest);
    }
}
