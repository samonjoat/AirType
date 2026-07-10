using System;

namespace AirType.Models.Transcription;

/// <summary>
/// Aggregated statistics for recent audio preprocessing operations.
/// </summary>
public class PreprocessingStats
{
    public int SampleCount { get; set; }
    public TimeSpan AverageOriginalDuration { get; set; }
    public TimeSpan AverageTrimmedDuration { get; set; }
    public TimeSpan AverageLeadingTrim { get; set; }
    public TimeSpan AverageTrailingTrim { get; set; }
    public int AverageOriginalSizeBytes { get; set; }
    public int AverageProcessedSizeBytes { get; set; }
    public int AverageBytesSaved { get; set; }
    public double AverageSizeReductionPercent { get; set; }
}
