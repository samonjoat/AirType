namespace AirType.Models.Configuration;

/// <summary>
/// Application-wide settings persisted to disk.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Number of days to retain audio files before automatic cleanup.
    /// Values: 30, 90, 180, or 0 (never delete).
    /// </summary>
    public int AudioRetentionDays { get; set; } = 0; // Default: never delete

    /// <summary>
    /// The key used for global hotkey dictation.
    /// </summary>
    public System.Windows.Forms.Keys GlobalHotkeyKey { get; set; } = System.Windows.Forms.Keys.Space;

    /// <summary>
    /// The modifier keys used for global hotkey dictation.
    /// </summary>
    public System.Windows.Input.ModifierKeys GlobalHotkeyModifiers { get; set; } = System.Windows.Input.ModifierKeys.Control;

    /// <summary>
    /// The date of the user's first transcription.
    /// Used for calculating usage statistics and streaks.
    /// </summary>
    public DateTime? FirstTranscriptionDate { get; set; } = null;

    /// <summary>
    /// Whether the application should launch on Windows startup.
    /// </summary>
    public bool LaunchOnStartup { get; set; } = false;

    /// <summary>
    /// Whether clicking the close button minimizes to tray instead of exiting.
    /// </summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>
    /// Whether the default playback device should be muted while recording is active.
    /// </summary>
    public bool MuteSystemAudioDuringRecording { get; set; } = false;

    /// <summary>
    /// Enables context-aware spacing and capitalization at the insertion junction.
    /// Default is on; targets without readable UI Automation TextPattern context insert as-is.
    /// </summary>
    public bool EnableSmartInsertion { get; set; } = true;

    /// <summary>
    /// Enables the broad native Unicode typing fallback after safer injection backends fail.
    /// Default is on so it can recover when clipboard paste is blocked by the target or clipboard lock.
    /// </summary>
    public bool EnableNativeTypingInjection { get; set; } = true;

    /// <summary>
    /// Provides default settings.
    /// </summary>
    public static AppSettings Default => new();
}
