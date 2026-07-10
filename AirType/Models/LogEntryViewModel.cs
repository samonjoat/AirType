using System;

namespace AirType.Models;

/// <summary>
/// View model for displaying log entries in the diagnostics UI.
/// </summary>
public class LogEntryViewModel
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
    public string LevelIcon => Level switch
    {
        "ERROR" => "❌",
        "WARN" => "⚠️",
        "INFO" => "ℹ️",
        "DEBUG" => "🔍",
        _ => "•"
    };
    public string LevelColor => Level switch
    {
        "ERROR" => "#FFCC0000",
        "WARN" => "#FFFF8800",
        "INFO" => "#FF0066CC",
        "DEBUG" => "#FF666666",
        _ => "#FF999999"
    };
}
