using System;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Interface for file persistence operations
/// </summary>
public interface IFilePersistenceManager : IDisposable
{
    /// <summary>
    /// Gets the base directory for audio files
    /// </summary>
    string AudioDirectory { get; }

    /// <summary>
    /// Provides details about the last persistence error encountered, if any.
    /// </summary>
    string? LastErrorMessage { get; }

    /// <summary>
    /// Ensures the audio directory structure exists
    /// </summary>
    /// <returns>True if directory exists or was created successfully</returns>
    bool EnsureDirectoryExists();

    /// <summary>
    /// Generates a unique filename using the session ID
    /// </summary>
    /// <param name="sessionId">The Unique Identifier for the session</param>
    /// <returns>Full file path for the new recording</returns>
    string GenerateFilePath(Guid sessionId);

    /// <summary>
    /// Validates that a file path is within the expected audio directory
    /// </summary>
    /// <param name="filePath">File path to validate</param>
    /// <returns>True if the path is valid and secure</returns>
    bool ValidateFilePath(string filePath);

    /// <summary>
    /// Safely deletes a recording file with proper error handling
    /// </summary>
    /// <param name="filePath">Path to the file to delete</param>
    /// <returns>True if file was deleted or didn't exist, false on error</returns>
    bool DeleteRecordingFile(string filePath);

    /// <summary>
    /// Gets information about available disk space in the audio directory
    /// </summary>
    /// <returns>Available space in bytes, or -1 if unable to determine</returns>
    long GetAvailableDiskSpace();

    /// <summary>
    /// Estimates the required disk space for a recording of given duration
    /// </summary>
    /// <param name="durationSeconds">Recording duration in seconds</param>
    /// <returns>Estimated file size in bytes</returns>
    long EstimateFileSize(double durationSeconds);

    /// <summary>
    /// Checks if there's sufficient disk space for a recording
    /// </summary>
    /// <param name="estimatedDurationSeconds">Estimated recording duration</param>
    /// <returns>True if there's sufficient space</returns>
    bool HasSufficientDiskSpace(double estimatedDurationSeconds = 3600);

    /// <summary>
    /// Creates a recording session model with file path
    /// </summary>
    /// <param name="mode">Recording mode</param>
    /// <returns>New recording session with generated file path</returns>
    RecordingSession CreateRecordingSession(RecordingMode mode);

    /// <summary>
    /// Updates recording session when completed
    /// </summary>
    /// <param name="session">Recording session to update</param>
    /// <param name="status">Final status</param>
    void CompleteRecordingSession(RecordingSession session, RecordingStatus status);

    /// <summary>
    /// Cleans up audio files older than the specified retention period.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain files. If 0, no cleanup is performed.</param>
    /// <returns>Number of files deleted</returns>
    int CleanupOldAudioFiles(int retentionDays);
}
