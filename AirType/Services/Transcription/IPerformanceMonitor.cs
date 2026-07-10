using System;
using System.Collections.Generic;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

public interface IPerformanceMonitor
{
    void StartTracking(string requestId, int audioSizeBytes, TimeSpan audioDuration);
    void RecordCheckpoint(string requestId, string checkpointName);
    void CompleteTracking(string requestId, bool success);
    TimeSpan GetAverageLatency(int requestCount = 10);
    IReadOnlyList<TranscriptionMetrics> GetHistory(int requestCount = 10);
    void RecordPreprocessing(AudioPreprocessingResult result);
    PreprocessingStats GetPreprocessingStats(int requestCount = 10);
    TranscriptionMetrics? GetLastMetrics();
}
