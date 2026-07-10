using AirType.Models;
using AirType.Models.Configuration;
using AirType.Models.Transcription;

namespace AirType.Services.Storage;

/// <summary>
/// Interface for logging transcription results to persistent storage.
/// Creates JSON Lines (.jsonl) files alongside audio recordings.
/// </summary>
public interface ITranscriptionLogger
{
    /// <summary>
    /// Saves transcription result to a JSON trace file matching the recording session ID.
    /// Includes provider metadata plus the request and response objects captured by the raw STT provider.
    /// </summary>
    /// <param name="session">Recording session with audio file path and metadata</param>
    /// <param name="provider">Raw STT provider used for this transcription</param>
    /// <param name="modelVersion">Model ID or version reported by the provider</param>
    /// <param name="transcribedText">Raw transcription text returned by the provider</param>
    /// <param name="request">Transcription request sent to the provider</param>
    /// <param name="response">Transcription response returned by the provider</param>
    /// <param name="isTest">If true, uses progressive naming (_test1, _test2, etc.) to avoid overwriting</param>
    /// <returns>Path to the created log file</returns>
    Task<string> SaveTranscriptionAsync(
        RecordingSession session,
        TranscriptionProvider provider,
        string modelVersion,
        string transcribedText,
        object? request,
        object? response,
        bool isTest = false);

    /// <summary>
    /// Saves transcription result to a JSON Lines file matching the audio filename.
    /// Includes both the request sent to the API and the response received.
    /// </summary>
    /// <param name="session">Recording session with audio file path and metadata</param>
    /// <param name="request">Transcription request sent to Gemini API</param>
    /// <param name="response">Transcription response from Gemini API</param>
    /// <param name="isTest">If true, uses progressive naming (_test1, _test2, etc.) to avoid overwriting</param>
    /// <returns>Path to the created log file</returns>
    Task<string> SaveTranscriptionAsync(RecordingSession session, GeminiTranscriptionRequest request, GeminiTranscriptionResponse response, bool isTest = false);

    /// <summary>
    /// Gets the base directory for transcription logs
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Deletes the log file associated with a specific session ID.
    /// </summary>
    void DeleteLogFile(Guid sessionId);
}
