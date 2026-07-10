using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AirType.Models.Transcription;

namespace AirType.Services.Transcription;

/// <summary>
/// Tracks per-request timings and logs performance diagnostics.
/// </summary>
public class PerformanceMonitor : IPerformanceMonitor
{
    private sealed class ActiveEntry
    {
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public TranscriptionMetrics Metrics { get; set; } = new();
    }

    private readonly ConcurrentDictionary<string, ActiveEntry> _active = new();
    private readonly object _historyLock = new();
    private readonly Queue<TranscriptionMetrics> _history = new();
    private readonly Queue<AudioPreprocessingResult> _preprocessHistory = new();
    private const int HistoryLimit = 20;

    public void StartTracking(string requestId, int audioSizeBytes, TimeSpan audioDuration)
    {
        var entry = new ActiveEntry
        {
            Metrics =
            {
                RequestId = requestId,
                StartTime = DateTime.UtcNow,
                AudioDuration = audioDuration,
                AudioSize = audioSizeBytes
            }
        };
        _active[requestId] = entry;
    }

    public void RecordCheckpoint(string requestId, string checkpointName)
    {
        if (!_active.TryGetValue(requestId, out var entry)) return;
        entry.Metrics.Checkpoints[checkpointName] = entry.Stopwatch.Elapsed;
    }

    public void CompleteTracking(string requestId, bool success)
    {
        if (!_active.TryRemove(requestId, out var entry)) return;
        entry.Stopwatch.Stop();
        entry.Metrics.TotalTime = entry.Stopwatch.Elapsed;
        entry.Metrics.Success = success;

        lock (_historyLock)
        {
            _history.Enqueue(entry.Metrics);
            while (_history.Count > HistoryLimit)
            {
                _history.Dequeue();
            }
        }

        Logger.Info("Performance", $"Request {requestId} completed in {entry.Metrics.TotalTime.TotalSeconds:F2}s (audio {entry.Metrics.AudioDuration.TotalSeconds:F2}s, size {entry.Metrics.AudioSize:N0} bytes)");

        if (entry.Metrics.AudioDuration.TotalSeconds < 10 && entry.Metrics.TotalTime.TotalSeconds > 5)
        {
            Logger.Warn("Performance", $"Latency warning: {entry.Metrics.TotalTime.TotalSeconds:F2}s for short clip ({entry.Metrics.AudioDuration.TotalSeconds:F2}s)");
        }

        var average = GetAverageLatency();
        if (average > TimeSpan.Zero)
        {
            Logger.Info("Performance", $"Average latency (last 10): {average.TotalSeconds:F2}s");
        }
    }

    public TimeSpan GetAverageLatency(int requestCount = 10)
    {
        lock (_historyLock)
        {
            var slice = _history.Reverse().Take(requestCount).ToList();
            if (slice.Count == 0) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(slice.Average(m => m.TotalTime.TotalMilliseconds));
        }
    }

    public IReadOnlyList<TranscriptionMetrics> GetHistory(int requestCount = 10)
    {
        lock (_historyLock)
        {
            return _history.Reverse().Take(requestCount).ToList();
        }
    }

    public void RecordPreprocessing(AudioPreprocessingResult result)
    {
        if (result == null) return;

        lock (_historyLock)
        {
            _preprocessHistory.Enqueue(result);
            while (_preprocessHistory.Count > HistoryLimit)
            {
                _preprocessHistory.Dequeue();
            }
        }
    }

    public PreprocessingStats GetPreprocessingStats(int requestCount = 10)
    {
        lock (_historyLock)
        {
            var slice = _preprocessHistory.Reverse().Take(requestCount).ToList();
            if (slice.Count == 0)
            {
                return new PreprocessingStats();
            }

            double avgOriginalMs = slice.Average(r => r.OriginalDuration.TotalMilliseconds);
            double avgTrimmedMs = slice.Average(r => r.TrimmedDuration.TotalMilliseconds);
            double avgLeadingTrimMs = slice.Average(r => r.LeadingSilenceTrimmed.TotalMilliseconds);
            double avgTrailingTrimMs = slice.Average(r => r.TrailingSilenceTrimmed.TotalMilliseconds);
            double avgOriginalSize = slice.Average(r => r.OriginalSize);
            double avgProcessedSize = slice.Average(r => r.ProcessedSize);
            double avgBytesSaved = avgOriginalSize - avgProcessedSize;
            double reductionPercent = avgOriginalSize <= 0 ? 0 : (avgBytesSaved / avgOriginalSize) * 100;

            return new PreprocessingStats
            {
                SampleCount = slice.Count,
                AverageOriginalDuration = TimeSpan.FromMilliseconds(avgOriginalMs),
                AverageTrimmedDuration = TimeSpan.FromMilliseconds(avgTrimmedMs),
                AverageLeadingTrim = TimeSpan.FromMilliseconds(avgLeadingTrimMs),
                AverageTrailingTrim = TimeSpan.FromMilliseconds(avgTrailingTrimMs),
                AverageOriginalSizeBytes = (int)Math.Round(avgOriginalSize),
                AverageProcessedSizeBytes = (int)Math.Round(avgProcessedSize),
                AverageBytesSaved = (int)Math.Max(0, Math.Round(avgBytesSaved)),
                AverageSizeReductionPercent = Math.Max(0, reductionPercent)
            };
        }
    }

    public TranscriptionMetrics? GetLastMetrics()
    {
        lock (_historyLock)
        {
            return _history.LastOrDefault();
        }
    }
}
