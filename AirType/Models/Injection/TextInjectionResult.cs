using System;

namespace AirType.Models.Injection;

/// <summary>
/// Represents the outcome of a text injection attempt.
/// </summary>
public sealed class TextInjectionResult
{
    public TextInjectionResult(
        bool success,
        string method,
        string message,
        bool targetVerified = false,
        bool clipboardFallbackUsed = false,
        bool clipboardFallbackExpected = false,
        bool clipboardRestoreAttempted = false,
        bool clipboardRestoreSucceeded = false)
        : this(
            success ? TextInjectionOutcome.Confirmed : TextInjectionOutcome.Failure,
            method,
            message,
            targetVerified,
            clipboardFallbackUsed,
            clipboardFallbackExpected,
            clipboardRestoreAttempted,
            clipboardRestoreSucceeded,
            recoverySuggested: false)
    {
    }

    private TextInjectionResult(
        TextInjectionOutcome outcome,
        string method,
        string message,
        bool targetVerified = false,
        bool clipboardFallbackUsed = false,
        bool clipboardFallbackExpected = false,
        bool clipboardRestoreAttempted = false,
        bool clipboardRestoreSucceeded = false,
        bool recoverySuggested = false)
    {
        Outcome = outcome;
        Success = outcome != TextInjectionOutcome.Failure;
        Method = method;
        Message = message;
        TargetVerified = targetVerified;
        ClipboardFallbackUsed = clipboardFallbackUsed;
        ClipboardFallbackExpected = clipboardFallbackExpected;
        ClipboardRestoreAttempted = clipboardRestoreAttempted;
        ClipboardRestoreSucceeded = clipboardRestoreSucceeded;
        RecoverySuggested = recoverySuggested;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the explicit injection outcome.
    /// </summary>
    public TextInjectionOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the backend chain completed with a non-failure outcome.
    /// </summary>
    public bool Success { get; }

    public bool IsConfirmed => Outcome == TextInjectionOutcome.Confirmed;

    public bool IsProvisional => Outcome == TextInjectionOutcome.Provisional;

    /// <summary>
    /// Provides the injection method used (e.g., "ClipboardPaste").
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Additional diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets a value indicating whether the target window was verified before dispatch.
    /// </summary>
    public bool TargetVerified { get; }

    /// <summary>
    /// Gets a value indicating whether the text was copied to the clipboard instead of injected.
    /// </summary>
    public bool ClipboardFallbackUsed { get; }

    /// <summary>
    /// Gets a value indicating whether clipboard-only behavior was expected by the caller.
    /// </summary>
    public bool ClipboardFallbackExpected { get; }

    /// <summary>
    /// Gets a value indicating whether AirType tried to restore the previous clipboard content.
    /// </summary>
    public bool ClipboardRestoreAttempted { get; }

    /// <summary>
    /// Gets a value indicating whether clipboard restore succeeded after a verified paste.
    /// </summary>
    public bool ClipboardRestoreSucceeded { get; }

    /// <summary>
    /// Gets a value indicating whether the caller should keep a manual-paste recovery affordance visible.
    /// </summary>
    public bool RecoverySuggested { get; }

    /// <summary>
    /// UTC timestamp when the result was generated.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Creates a successful result instance.
    /// </summary>
    public static TextInjectionResult SuccessResult(
        string method,
        string message = "Injection succeeded.",
        bool targetVerified = true,
        bool clipboardRestoreAttempted = false,
        bool clipboardRestoreSucceeded = false) =>
        new(TextInjectionOutcome.Confirmed, method, message, targetVerified, clipboardFallbackUsed: false, clipboardFallbackExpected: false, clipboardRestoreAttempted, clipboardRestoreSucceeded);

    /// <summary>
    /// Creates a provisional result for dispatched input that cannot be readback-confirmed.
    /// </summary>
    public static TextInjectionResult ProvisionalResult(
        string method,
        string message,
        bool targetVerified = true,
        bool recoverySuggested = true,
        bool clipboardRestoreAttempted = false,
        bool clipboardRestoreSucceeded = false) =>
        new(TextInjectionOutcome.Provisional, method, message, targetVerified, clipboardFallbackUsed: false, clipboardFallbackExpected: false, clipboardRestoreAttempted, clipboardRestoreSucceeded, recoverySuggested);

    /// <summary>
    /// Creates a clipboard-only fallback result.
    /// </summary>
    public static TextInjectionResult ClipboardFallback(string message, bool expected = false) =>
        new(TextInjectionOutcome.ClipboardFallback, "ClipboardOnly", message, targetVerified: false, clipboardFallbackUsed: true, clipboardFallbackExpected: expected, recoverySuggested: true);

    /// <summary>
    /// Creates a failure result instance with the specified message.
    /// </summary>
    public static TextInjectionResult Failure(string method, string message) =>
        new(TextInjectionOutcome.Failure, method, message);
}
