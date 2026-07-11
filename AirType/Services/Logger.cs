using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AirType.Services;

/// <summary>
/// Edge Case #3: Structured logging service with levels, timestamps, and file rotation
/// </summary>
public class Logger : IDisposable
{
    private static Logger? _instance;
    private static readonly object _lock = new();
    
    private readonly string _logDirectory;
    // PERFORMANCE: Using Channel<T> instead of ConcurrentQueue with polling
    // Channel provides efficient async waiting without CPU-wasting polling loops
    private readonly Channel<LogEntry> _logChannel;
    internal readonly ConcurrentQueue<LogEntry> _logQueue; // Kept for GetRecentLogs compatibility
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _writeTask;
    private readonly int _maxLogFileSizeMb = 10;
    private readonly int _maxLogFiles = 7;
    private bool _disposed;

    public enum LogLevel
    {
        DEBUG = 0,
        INFO = 1,
        WARN = 2,
        ERROR = 3,
        SUCCESS = 4
    }

    // SECURITY: Use INFO level in Release builds to reduce log verbosity
#if DEBUG
    public static LogLevel MinimumLogLevel { get; set; } = LogLevel.DEBUG;
#else
    public static LogLevel MinimumLogLevel { get; set; } = LogLevel.INFO;
#endif

    private Logger()
    {
        // Log directory: %LOCALAPPDATA%/AirType/Logs/App
        _logDirectory = AirTypeStoragePaths.GetCanonicalPath("Logs", "App");
        
        // Ensure directory exists
        Directory.CreateDirectory(_logDirectory);
        
        // PERFORMANCE: Channel provides efficient async waiting without polling
        _logChannel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,  // Only ProcessLogQueue reads from channel
            SingleWriter = false  // Multiple threads can write log entries
        });
        _logQueue = new ConcurrentQueue<LogEntry>(); // Kept for GetRecentLogs compatibility
        _cancellationTokenSource = new CancellationTokenSource();
        
        // Start background writer task
        _writeTask = Task.Run(ProcessLogQueue);
        
        // Clean up old log files
        CleanupOldLogFiles();
        
        // Intercept ALL Debug.WriteLine calls
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new UnifiedDebugListener(this));
    }

    public static Logger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        try
                        {
                            _instance = new Logger();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Logger] Failed to initialize: {ex.Message}");
                            // Create a minimal fallback instance
                            throw;
                        }
                    }
                }
            }
            return _instance;
        }
    }

    public static void Debug(string component, string message, object? context = null)
    {
        try
        {
            Instance.Log(LogLevel.DEBUG, component, message, context);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
        }
    }

    public static void Info(string component, string message, object? context = null)
    {
        try
        {
            Instance.Log(LogLevel.INFO, component, message, context);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
        }
    }

    public static void Warn(string component, string message, object? context = null)
    {
        try
        {
            Instance.Log(LogLevel.WARN, component, message, context);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
        }
    }

    public static void Error(string component, string message, Exception? exception = null, object? context = null)
    {
        try
        {
            Instance.Log(LogLevel.ERROR, component, message, context, exception);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[{component}] ERROR: {message}");
            if (exception != null)
                System.Diagnostics.Debug.WriteLine($"  Exception: {exception.Message}");
        }
    }

    private void Log(LogLevel level, string component, string message, object? context = null, Exception? exception = null)
    {
        if (level < MinimumLogLevel || _disposed)
            return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Component = component,
            Message = message,
            Context = context,
            Exception = exception,
            ThreadId = Thread.CurrentThread.ManagedThreadId
        };

        // Write to channel for async file writing (non-blocking)
        _logChannel.Writer.TryWrite(entry);
        
        // Also keep in queue for GetRecentLogs compatibility
        _logQueue.Enqueue(entry);
    }

    /// <summary>
    /// Internal method for UnifiedDebugListener to enqueue entries directly.
    /// </summary>
    internal void EnqueueEntry(LogEntry entry)
    {
        if (_disposed) return;
        
        _logChannel.Writer.TryWrite(entry);
        _logQueue.Enqueue(entry);
    }

    internal string FormatLogEntry(LogEntry entry)
    {
        var sb = new StringBuilder();
        
        // Timestamp
        sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        
        // Level
        sb.Append($"[{entry.Level,-5}] ");
        
        // Thread ID
        sb.Append($"[T{entry.ThreadId:D3}] ");
        
        // Component
        sb.Append($"[{entry.Component}] ");
        
        // Message
        sb.Append(entry.Message);
        
        // Context
        if (entry.Context != null)
        {
            sb.Append($" | Context: {SerializeContext(entry.Context)}");
        }
        
        // Exception
        if (entry.Exception != null)
        {
            sb.AppendLine();
            sb.Append($"  Exception: {entry.Exception.GetType().Name}: {entry.Exception.Message}");
            sb.AppendLine();
            sb.Append($"  StackTrace: {entry.Exception.StackTrace}");
        }
        
        return sb.ToString();
    }

    private string SerializeContext(object context)
    {
        try
        {
            // Simple serialization for common types
            if (context is string str)
                return str;
            
            var properties = context.GetType().GetProperties();
            var parts = new System.Collections.Generic.List<string>();
            
            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(context);
                    parts.Add($"{prop.Name}={value}");
                }
                catch
                {
                    // Ignore property access errors
                }
            }
            
            return string.Join(", ", parts);
        }
        catch
        {
            return context.ToString() ?? "null";
        }
    }

    private async Task ProcessLogQueue()
    {
        var reader = _logChannel.Reader;
        
        try
        {
            // PERFORMANCE: Efficiently wait for items using async enumeration
            // No polling - only wakes when items are available or cancelled
            await foreach (var entry in reader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                try
                {
                    await WriteLogEntryToFile(entry);
                }
                catch (Exception ex)
                {
                    // Log to Debug console if file writing fails
                    System.Diagnostics.Debug.WriteLine($"Logger error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown - drain remaining items
        }
        
        // Flush any remaining entries that might have been queued during shutdown
        while (reader.TryRead(out var entry))
        {
            try
            {
                await WriteLogEntryToFile(entry);
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }
    }

    private async Task WriteLogEntryToFile(LogEntry entry)
    {
        try
        {
            string logFilePath = GetDailyLogFilePath(_logDirectory, DateTime.Now);

            // Check file size and rotate if needed
            if (File.Exists(logFilePath))
            {
                var fileInfo = new FileInfo(logFilePath);
                if (fileInfo.Length > _maxLogFileSizeMb * 1024 * 1024)
                {
                    RotateLogFile(logFilePath);
                }
            }
            
            var logLine = FormatLogEntry(entry) + Environment.NewLine;
            await File.AppendAllTextAsync(logFilePath, logLine);
        }
        catch
        {
            // Silently fail - don't want logging to crash the app
        }
    }

    private void RotateLogFile(string activeLogFilePath)
    {
        try
        {
            string rotatedFilePath = GetUniqueRotatedLogFilePath(_logDirectory, DateTime.Now);
            
            if (File.Exists(activeLogFilePath))
            {
                File.Move(activeLogFilePath, rotatedFilePath);
            }
            
            CleanupOldLogFiles();
        }
        catch
        {
            // Ignore rotation errors
        }
    }

    private void CleanupOldLogFiles()
    {
        try
        {
            CleanupOldLogFiles(
                _logDirectory,
                GetDailyLogFilePath(_logDirectory, DateTime.Now),
                _maxLogFiles);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    internal static string GetDailyLogFilePath(string logDirectory, DateTime timestamp)
    {
        return Path.Combine(logDirectory, $"App_{timestamp:yyyyMMdd}.log");
    }

    internal static string GetUniqueRotatedLogFilePath(string logDirectory, DateTime timestamp)
    {
        string stem = $"App_{timestamp:yyyyMMdd_HHmmss_fff}";
        string candidate = Path.Combine(logDirectory, $"{stem}.log");
        int collisionSuffix = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(logDirectory, $"{stem}_{collisionSuffix}.log");
            collisionSuffix++;
        }

        return candidate;
    }

    internal static int CleanupOldLogFiles(
        string logDirectory,
        string activeLogFilePath,
        int maxHistoricalFiles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHistoricalFiles);

        if (!Directory.Exists(logDirectory))
        {
            return 0;
        }

        string activeFullPath = Path.GetFullPath(activeLogFilePath);
        var filesToDelete = Directory
            .EnumerateFiles(logDirectory, "App_*.log")
            .Concat(Directory.EnumerateFiles(logDirectory, "DictationApp_*.log"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !string.Equals(
                Path.GetFullPath(path),
                activeFullPath,
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(maxHistoricalFiles)
            .ToArray();

        int deletedCount = 0;
        foreach (FileInfo file in filesToDelete)
        {
            try
            {
                file.Delete();
                deletedCount++;
            }
            catch
            {
                // Logging failures must not terminate the application.
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// Gets the most recent log entries from the in-memory queue and log file.
    /// </summary>
    /// <param name="count">Maximum number of entries to return (default: 100)</param>
    /// <returns>List of recent log entries, newest first</returns>
    public List<LogEntry> GetRecentLogs(int count = 100)
    {
        var entries = new List<LogEntry>();
        // Use second-level precision for deduplication (ignore milliseconds)
        var seen = new HashSet<string>();

        try
        {
            // Get entries from in-memory queue (most recent)
            var queueEntries = _logQueue.ToArray();
            foreach (var entry in queueEntries)
            {
                // Key: timestamp (to seconds) + level + message
                var key = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}|{entry.Level}|{entry.Message}";
                if (seen.Add(key))
                {
                    entries.Add(entry);
                }
            }

            // If we need more entries, read from today's log file.
            string logFilePath = GetDailyLogFilePath(_logDirectory, DateTime.Now);
            if (entries.Count < count && File.Exists(logFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(logFilePath);
                    var neededCount = count - entries.Count;
                    var startIndex = Math.Max(0, lines.Length - neededCount);

                    for (int i = startIndex; i < lines.Length; i++)
                    {
                        var entry = ParseLogLine(lines[i]);
                        if (entry != null)
                        {
                            var key = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}|{entry.Level}|{entry.Message}";
                            if (seen.Add(key))
                            {
                                entries.Add(entry);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore file read errors
                }
            }

            // Return newest first
            return entries.OrderByDescending(e => e.Timestamp).Take(count).ToList();
        }
        catch
        {
            return new List<LogEntry>();
        }
    }
    
    /// <summary>
    /// Clears the in-memory log queue and truncates the current log file.
    /// </summary>
    public void ClearLogFile()
    {
        try
        {
            // Clear in-memory queue
            _logQueue.Clear();
            
            // Truncate today's file if it exists.
            string logFilePath = GetDailyLogFilePath(_logDirectory, DateTime.Now);
            if (File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, string.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Logger] Failed to clear log file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Parses a log line from the file into a LogEntry object.
    /// </summary>
    private LogEntry? ParseLogLine(string line)
    {
        try
        {
            // Format: [2025-01-12 10:30:45.123] [INFO ] [T001] [Component] Message
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("["))
                return null;
            
            var parts = line.Split(new[] { "] [", "] " }, StringSplitOptions.None);
            if (parts.Length < 5)
                return null;
            
            var timestampStr = parts[0].TrimStart('[');
            var levelStr = parts[1].Trim();
            var componentStr = parts[3];
            var message = string.Join("] ", parts.Skip(4));
            
            if (!DateTime.TryParse(timestampStr, out var timestamp))
                return null;
            
            if (!Enum.TryParse<LogLevel>(levelStr, true, out var level))
                level = LogLevel.DEBUG;
            
            return new LogEntry
            {
                Timestamp = timestamp,
                Level = level,
                Component = componentStr,
                Message = message,
                ThreadId = 0
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        try
        {
            // Signal the channel that no more items will be written
            _logChannel.Writer.Complete();
            
            // Cancel the token to stop any waiting operations
            _cancellationTokenSource.Cancel();
            
            // Wait for the write task to finish processing remaining items
            _writeTask.Wait(TimeSpan.FromSeconds(5));
            _cancellationTokenSource.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Component { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public object? Context { get; set; }
        public Exception? Exception { get; set; }
        public int ThreadId { get; set; }
    }
}

/// <summary>
/// Unified debug listener that captures ALL Debug.WriteLine calls and routes them through the Logger
/// </summary>
internal class UnifiedDebugListener : System.Diagnostics.TraceListener
{
    private readonly Logger _logger;
    private readonly StringBuilder _lineBuffer = new();

    public UnifiedDebugListener(Logger logger)
    {
        _logger = logger;
    }

    public override void Write(string? message)
    {
        if (message != null)
        {
            _lineBuffer.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        if (message != null)
        {
            _lineBuffer.Append(message);
        }

        var fullMessage = _lineBuffer.ToString();
        _lineBuffer.Clear();

        if (!string.IsNullOrWhiteSpace(fullMessage))
        {
            // Parse and format the message
            var (component, logLevel, cleanMessage) = ParseMessageForLogging(fullMessage);
            
            // Create a raw log entry
            var entry = new Logger.LogEntry
            {
                Timestamp = DateTime.Now,
                Level = logLevel,
                Component = component,
                Message = cleanMessage,
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };
            
            // Queue for async file writing via channel
            _logger.EnqueueEntry(entry);
        }
    }


    
    private (string component, Logger.LogLevel level, string message) ParseMessageForLogging(string fullMessage)
    {
        // Default values
        string component = "Debug";
        Logger.LogLevel level = Logger.LogLevel.DEBUG;
        string message = fullMessage;
        
        // Try to extract component from [Component] prefix
        if (fullMessage.StartsWith("[") && fullMessage.Contains("]"))
        {
            int endBracket = fullMessage.IndexOf(']');
            component = fullMessage.Substring(1, endBracket - 1);
            message = fullMessage.Substring(endBracket + 1).TrimStart();
        }
        
        // Determine log level based on keywords and component
        if (message.Contains("ERROR") || message.Contains("error") || message.Contains("failed") || 
            message.Contains("Failed") || message.Contains("Exception") || message.Contains("exception"))
        {
            level = Logger.LogLevel.ERROR;
        }
        else if (message.Contains("WARNING") || message.Contains("Warning") || message.Contains("⚠") ||
                 message.Contains("Quiet audio") || message.Contains("retry") || message.Contains("Retry"))
        {
            level = Logger.LogLevel.WARN;
        }
        else if (message.Contains("succeeded") || message.Contains("Succeeded") || 
                 message.Contains("success") || message.Contains("Success") ||
                 message.Contains("âœ“") || message.Contains("✓"))
        {
            level = Logger.LogLevel.SUCCESS;
        }
        else if (message.Contains("started") || message.Contains("completed") ||
                 message.Contains("initialized") || message.Contains("Initialized") || message.Contains("saved") ||
                 message.Contains("Transcription cancelled") || message.Contains("Recording cancelled") ||
                 message.Contains("Esc key pressed"))
        {
            level = Logger.LogLevel.INFO;
        }
        // Everything else stays DEBUG
        
        return (component, level, message);
    }
}
