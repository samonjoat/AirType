using System.IO;

namespace AirType.Services.Audio;

/// <summary>
/// Validates WAV audio file integrity after recording.
/// Extracted from AudioInputManager for single responsibility.
/// </summary>
public class AudioFileValidator : IAudioFileValidator
{
    // Minimum reasonable size for a WAV file (header + minimal audio)
    // WAV header is 44 bytes, so anything less than 100 bytes is suspicious
    private const int MinimumValidFileSize = 100;

    /// <summary>
    /// Validates a WAV audio file for integrity.
    /// </summary>
    public AudioFileValidationResult Validate(string? filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return AudioFileValidationResult.Failure("File path is empty");
            }

            // Check if file exists
            if (!File.Exists(filePath))
            {
                return AudioFileValidationResult.Failure("Recording file was not created");
            }

            // Check file size
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                return AudioFileValidationResult.Failure("Recording file is empty (0 bytes)");
            }

            if (fileInfo.Length < MinimumValidFileSize)
            {
                return AudioFileValidationResult.Failure(
                    $"Recording file is too small ({fileInfo.Length} bytes) - likely corrupted");
            }

            // Validate WAV file header
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            // Check RIFF header (first 4 bytes should be "RIFF")
            var riffHeader = new string(reader.ReadChars(4));
            if (riffHeader != "RIFF")
            {
                return AudioFileValidationResult.Failure(
                    "Invalid WAV file format - missing RIFF header");
            }

            // Skip file size (4 bytes)
            reader.ReadInt32();

            // Check WAVE format (next 4 bytes should be "WAVE")
            var waveFormat = new string(reader.ReadChars(4));
            if (waveFormat != "WAVE")
            {
                return AudioFileValidationResult.Failure(
                    "Invalid WAV file format - missing WAVE identifier");
            }

            // File appears valid
            return AudioFileValidationResult.Success(fileInfo.Length);
        }
        catch (UnauthorizedAccessException)
        {
            return AudioFileValidationResult.Failure(
                "Cannot access recording file - permission denied");
        }
        catch (IOException ex)
        {
            return AudioFileValidationResult.Failure(
                $"Cannot read recording file - {ex.Message}");
        }
        catch (Exception ex)
        {
            return AudioFileValidationResult.Failure(
                $"File validation error - {ex.Message}");
        }
    }
}
