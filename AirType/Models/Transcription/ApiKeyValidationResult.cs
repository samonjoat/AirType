namespace AirType.Models.Transcription;

/// <summary>
/// Result of API key validation.
/// </summary>
public class ApiKeyValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public ApiKeyErrorType ErrorType { get; set; }
}

/// <summary>
/// Types of API key errors.
/// </summary>
public enum ApiKeyErrorType
{
    None,
    Missing,
    Invalid,
    QuotaExceeded,
    NetworkError,
    Unknown
}
