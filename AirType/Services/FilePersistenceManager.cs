using System.IO;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Manages file persistence operations for audio recordings
/// </summary>
public class FilePersistenceManager : IFilePersistenceManager
{
    private const string AppFolderName = "AirType";
    private const string AudioFolderName = "Audio_files";
    private const string FilePrefix = "Recording_";
    private const string FileExtension = ".wav";
    private string? _lastErrorMessage;
    private bool _disposed = false;

    public string? LastErrorMessage => _lastErrorMessage;

    /// <summary>
    /// Gets the base directory for audio files
    /// </summary>
    public string AudioDirectory
    {
        get
        {
            return AirTypeStoragePaths.GetCanonicalPath(AudioFolderName);
        }
    }

    /// <summary>
    /// Ensures the audio directory structure exists
    /// </summary>
    /// <returns>True if directory exists or was created successfully</returns>
    public bool EnsureDirectoryExists()
    {
        _lastErrorMessage = null;

        try
        {
            string audioDir = AudioDirectory;

            if (!Directory.Exists(audioDir))
            {
                Directory.CreateDirectory(audioDir);

                if (!Directory.Exists(audioDir))
                {
                    _lastErrorMessage = $"Could not create {audioDir}.";
                    return false;
                }
            }

            string testFile = Path.Combine(audioDir, $"test_{Guid.NewGuid()}.tmp");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                _lastErrorMessage =
                    "Windows blocked AirType from saving under %LOCALAPPDATA%. If Controlled Folder Access is enabled, allow AirType.exe or choose another writable folder.";
                return false;
            }
            catch (Exception)
            {
                _lastErrorMessage = $"AirType cannot write to {audioDir}.";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Access denied when creating audio directory");
            _lastErrorMessage =
                "Windows denied access to %LOCALAPPDATA%. Allow AirType.exe under Windows Security > Virus & threat protection > Controlled folder access.";
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error ensuring directory exists: {ex.Message}");
            _lastErrorMessage = "Unexpected error while preparing the AirType audio folder.";
            return false;
        }
    }

    /// <summary>
    /// Generates a unique filename using the session ID
    /// </summary>
    /// <param name="sessionId">The Unique Identifier for the session</param>
    /// <returns>Full file path for the new recording</returns>
    public string GenerateFilePath(Guid sessionId)
    {
        string audioDir = AudioDirectory;
        return Path.Combine(audioDir, $"{sessionId}{FileExtension}");
    }

    /// <summary>
    /// Validates that a file path is within the expected audio directory
    /// </summary>
    /// <param name="filePath">File path to validate</param>
    /// <returns>True if the path is valid and secure</returns>
    public bool ValidateFilePath(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;
            
            // Get full paths to prevent directory traversal attacks
            string fullFilePath = Path.GetFullPath(filePath);
            string fullAudioDir = Path.GetFullPath(AudioDirectory);
            
            // Ensure the file is within the audio directory
            if (!fullFilePath.StartsWith(fullAudioDir, StringComparison.OrdinalIgnoreCase))
                return false;
            
            // Ensure it's a WAV file
            if (!Path.GetExtension(fullFilePath).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
                return false;
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely deletes a recording file with proper error handling
    /// </summary>
    /// <param name="filePath">Path to the file to delete</param>
    /// <returns>True if file was deleted or didn't exist, false on error</returns>
    public bool DeleteRecordingFile(string filePath)
    {
        _lastErrorMessage = null;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return true; // Nothing to delete

            // Validate the file path for security
            if (!ValidateFilePath(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"Invalid file path for deletion: {filePath}");
                _lastErrorMessage = "Attempted to delete a file outside the managed audio directory.";
                return false;
            }
            
            if (!File.Exists(filePath))
                return true; // File doesn't exist, consider it deleted
            
            // Attempt to delete with retry logic for file locks
            int maxRetries = 3;
            int retryDelay = 100; // milliseconds
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    File.Delete(filePath);
                    
                    // Verify deletion
                    if (!File.Exists(filePath))
                        return true;
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // File might be locked, wait and retry
                    Thread.Sleep(retryDelay);
                    retryDelay *= 2; // Exponential backoff
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting recording file {filePath}: {ex.Message}");
            _lastErrorMessage = $"Could not delete recording at {filePath}.";
            return false;
        }
    }

    /// <summary>
    /// Gets information about available disk space in the audio directory
    /// </summary>
    /// <returns>Available space in bytes, or -1 if unable to determine</returns>
    public long GetAvailableDiskSpace()
    {
        try
        {
            string audioDir = AudioDirectory;
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(audioDir) ?? "C:");
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return -1; // Unable to determine
        }
    }

    /// <summary>
    /// Estimates the required disk space for a recording of given duration
    /// </summary>
    /// <param name="durationSeconds">Recording duration in seconds</param>
    /// <returns>Estimated file size in bytes</returns>
    public long EstimateFileSize(double durationSeconds)
    {
        // WAV file size calculation: Sample Rate * Bits Per Sample * Channels * Duration / 8
        // Plus WAV header overhead (approximately 44 bytes)
        const int sampleRate = 16000;
        const int bitsPerSample = 16;
        const int channels = 1;
        const int headerSize = 44;
        
        long dataSize = (long)(sampleRate * bitsPerSample * channels * durationSeconds / 8);
        return dataSize + headerSize;
    }

    /// <summary>
    /// Checks if there's sufficient disk space for a recording
    /// </summary>
    /// <param name="estimatedDurationSeconds">Estimated recording duration</param>
    /// <returns>True if there's sufficient space</returns>
    public bool HasSufficientDiskSpace(double estimatedDurationSeconds = 3600) // Default 1 hour
    {
        try
        {
            _lastErrorMessage = null;
            long availableSpace = GetAvailableDiskSpace();
            if (availableSpace == -1)
                return true; // Assume sufficient if we can't determine

            long requiredSpace = EstimateFileSize(estimatedDurationSeconds);
            long bufferSpace = 50 * 1024 * 1024; // 50MB buffer

            bool hasSpace = availableSpace > (requiredSpace + bufferSpace);
            if (!hasSpace)
            {
                _lastErrorMessage = "Not enough disk space to start recording. Free up space or choose another drive.";
            }

            return hasSpace;
        }
        catch
        {
            return true; // Assume sufficient on error
        }
    }

    /// <summary>
    /// Creates a recording session model with file path
    /// </summary>
    /// <param name="mode">Recording mode</param>
    /// <returns>New recording session with generated file path</returns>
    public RecordingSession CreateRecordingSession(RecordingMode mode)
    {
        _lastErrorMessage = null;
        try
        {
            var id = Guid.NewGuid();
            return new RecordingSession
            {
                Id = id,
                StartTime = DateTime.UtcNow,
                FilePath = GenerateFilePath(id),
                Mode = mode,
                Status = RecordingStatus.Recording
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error building recording session: {ex.Message}");
            _lastErrorMessage = "Unable to generate a recording file name under %LOCALAPPDATA%.";
            throw;
        }
    }

    /// <summary>
    /// Updates recording session when completed
    /// </summary>
    /// <param name="session">Recording session to update</param>
    /// <param name="status">Final status</param>
    public void CompleteRecordingSession(RecordingSession session, RecordingStatus status)
    {
        if (session == null)
            return;
        
        session.EndTime = DateTime.UtcNow;
        session.Status = status;
        
        // If cancelled or error, attempt to clean up the file
        if (status == RecordingStatus.Cancelled || status == RecordingStatus.Error)
        {
            DeleteRecordingFile(session.FilePath);
        }
    }

    private void DeleteLogFileSafe(string fileNameWithoutExtension)
    {
        try
        {
            string logPath = AirTypeStoragePaths.GetCanonicalPath("Logs", "Transcription", $"{fileNameWithoutExtension}.json");
            
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
                Logger.Debug("FilePersistenceManager", $"Deleted corresponding log file: {fileNameWithoutExtension}.json");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("FilePersistenceManager", $"Failed to delete log file for {fileNameWithoutExtension}: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up audio files older than the specified retention period.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain files. If 0, no cleanup is performed.</param>
    /// <returns>Number of files deleted</returns>
    public int CleanupOldAudioFiles(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            Logger.Debug("FilePersistenceManager", "Audio retention disabled (never delete)");
            return 0;
        }

        int deletedCount = 0;
        try
        {
            var audioDir = AudioDirectory;
            if (!Directory.Exists(audioDir))
            {
                return 0;
            }

            var cutoffDate = DateTime.Now.AddDays(-retentionDays);
            var audioFiles = Directory.GetFiles(audioDir, "*.wav");

            Logger.Info("FilePersistenceManager", 
                $"Checking {audioFiles.Length} audio files for cleanup (retention: {retentionDays} days)");

            foreach (var filePath in audioFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        if (DeleteRecordingFile(filePath))
                        {
                            deletedCount++;
                            Logger.Debug("FilePersistenceManager", 
                                $"Deleted old audio file: {Path.GetFileName(filePath)} (created {fileInfo.CreationTime:yyyy-MM-dd})");
                            
                            // Also try to delete corresponding JSON log file
                            DeleteLogFileSafe(fileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("FilePersistenceManager", 
                        $"Failed to check/delete audio file {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                Logger.Info("FilePersistenceManager", 
                    $"Audio cleanup complete: deleted {deletedCount} files older than {retentionDays} days");
            }

            // Also cleanup application log files
            CleanupOldAppLogs(retentionDays);
        }
        catch (Exception ex)
        {
            Logger.Error("FilePersistenceManager", "Error during audio file cleanup", ex);
        }

        return deletedCount;
    }

    private void CleanupOldAppLogs(int retentionDays)
    {
        try
        {
            string appLogDir = AirTypeStoragePaths.GetCanonicalPath("Logs", "App");
            
            if (!Directory.Exists(appLogDir)) return;

            var cutoffDate = DateTime.Now.AddDays(-retentionDays);
            var logFiles = Directory.GetFiles(appLogDir, "App_*.log");

            foreach (var filePath in logFiles)
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    try { File.Delete(filePath); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Disposes of all resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for proper disposal pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // FilePersistenceManager doesn't hold unmanaged resources
            // but we clear the error message for consistency
            _lastErrorMessage = null;
            _disposed = true;
        }
    }
}
