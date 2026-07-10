using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Configuration;
using AirType.Models.Transcription;

namespace AirType.Services.Storage;

/// <summary>
/// Logs transcription results to formatted JSON (.jsonl) files.
/// Each transcription is saved as a beautifully formatted JSON object
/// with the same name as the audio file but with .jsonl extension.
/// </summary>
public class TranscriptionLogger : ITranscriptionLogger
{
    private const string LogsFolderName = "Logs";
    private const string TranscriptionSubFolder = "Transcription";
    private static readonly JsonSerializerOptions LogSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Gets the base directory for transcription logs
    /// </summary>
    public string LogsDirectory
    {
        get
        {
            return AirTypeStoragePaths.GetCanonicalPath(LogsFolderName, TranscriptionSubFolder);
        }
    }

    /// <summary>
    /// Saves transcription result to a formatted JSON file.
    /// Creates a provider-neutral JSON object with request and response data.
    /// </summary>
    public async Task<string> SaveTranscriptionAsync(
        RecordingSession session,
        TranscriptionProvider provider,
        string modelVersion,
        string transcribedText,
        object? request,
        object? response,
        bool isTest = false)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        if (string.IsNullOrWhiteSpace(modelVersion))
            modelVersion = "unknown";

        transcribedText ??= string.Empty;

        // Ensure logs directory exists
        if (!Directory.Exists(LogsDirectory))
        {
            Directory.CreateDirectory(LogsDirectory);
        }

        string logFilePath = GetLogFilePath(session, isTest);

        // Create comprehensive log entry with all API response data
        var logEntry = new
        {
            // Unique identifier
            id = Guid.NewGuid().ToString(),

            // Timestamp
            timestamp = DateTime.UtcNow.ToString("O"), // ISO 8601 format
            timestampLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

            // Provider metadata
            provider = provider.ToString(),
            model = modelVersion,

            // Recording metadata
            recording = new
            {
                audioFile = Path.GetFileName(session.FilePath),
                audioFilePath = session.FilePath,
                duration = session.Duration.ToString(@"hh\:mm\:ss\.f"),
                durationSeconds = session.Duration.TotalSeconds,
                mode = session.Mode.ToString(),
                status = session.Status.ToString(),
                startTime = session.StartTime.ToString("O"),
                endTime = session.EndTime?.ToString("O")
            },

            // Transcription result
            transcription = new
            {
                text = transcribedText,
                textLength = transcribedText.Length,
                wordCount = CountWords(transcribedText)
            },

            // API Request/Response (what was sent and received)
            apiRequest = request,
            apiResponse = response
        };

        // Serialize to beautifully formatted JSON with indentation
        string formattedJson = JsonSerializer.Serialize(logEntry, LogSerializerOptions);

        // Write to log file (overwrite if exists - one transcription per file)
        await File.WriteAllTextAsync(logFilePath, formattedJson);

        return logFilePath;
    }

    /// <summary>
    /// Saves Gemini transcription result to a formatted JSON file.
    /// </summary>
    public Task<string> SaveTranscriptionAsync(
        RecordingSession session,
        GeminiTranscriptionRequest request,
        GeminiTranscriptionResponse response,
        bool isTest = false)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (response == null)
            throw new ArgumentNullException(nameof(response));

        return SaveTranscriptionAsync(
            session,
            TranscriptionProvider.Gemini,
            response.ModelVersion ?? request.ModelId,
            response.Text ?? string.Empty,
            request,
            response,
            isTest);
    }

    private string GetLogFilePath(RecordingSession session, bool isTest)
    {
        // Use Session ID as filename for direct linking
        string logFilePath = Path.Combine(LogsDirectory, $"{session.Id}.json");

        // If this is a test run, add suffix
        if (isTest)
        {
            logFilePath = logFilePath.Replace(".json", "_test.json");
        }

        return logFilePath;
    }

    private static int CountWords(string text) =>
        text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Deletes the log file associated with a specific session ID.
    /// </summary>
    public void DeleteLogFile(Guid sessionId)
    {
        try
        {
            string logPath = Path.Combine(LogsDirectory, $"{sessionId}.json");
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
                Logger.Debug("TranscriptionLogger", $"Deleted log file: {sessionId}.json");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("TranscriptionLogger", $"Failed to delete log file for {sessionId}: {ex.Message}");
        }
    }
}
