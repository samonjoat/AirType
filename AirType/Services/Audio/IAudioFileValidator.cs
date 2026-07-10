namespace AirType.Services.Audio;

/// <summary>
/// Validates audio file integrity after recording.
/// </summary>
public interface IAudioFileValidator
{
    /// <summary>
    /// Validates a WAV audio file for integrity.
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <returns>Validation result with status and any error message</returns>
    AudioFileValidationResult Validate(string? filePath);
}

/// <summary>
/// Result of audio file validation.
/// </summary>
public class AudioFileValidationResult
{
    /// <summary>
    /// Whether the file is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// File size in bytes (if valid).
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static AudioFileValidationResult Success(long fileSizeBytes) => new()
    {
        IsValid = true,
        FileSizeBytes = fileSizeBytes
    };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static AudioFileValidationResult Failure(string errorMessage) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage
    };
}
