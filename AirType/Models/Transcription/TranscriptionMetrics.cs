using System;
using System.Collections.Generic;

namespace AirType.Models.Transcription;

/// <summary>
/// Captures timing and metadata for a single transcription request.
/// </summary>
public class TranscriptionMetrics
{
    public string RequestId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan AudioDuration { get; set; }
    public int AudioSize { get; set; }
    public Dictionary<string, TimeSpan> Checkpoints { get; } = new();
    public TimeSpan TotalTime { get; set; }
    public bool Success { get; set; }
}
