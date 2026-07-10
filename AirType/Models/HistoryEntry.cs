using System;

namespace AirType.Models;

public class HistoryEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Time => Date.ToString("t");
    public string DateLabel { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Words { get; set; }
    public string Duration { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string AudioPath { get; set; } = string.Empty;
    public string AudioName { get; set; } = string.Empty;
    public bool IsFirstOfDay { get; set; }
}
