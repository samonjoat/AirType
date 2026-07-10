using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Injection;
using AirType.Services.Injection;
using AirType.Services.Injection.Backends;

namespace AirType.Services;

/// <summary>
/// Public text-injection facade. It verifies the target window, then delegates actual
/// insertion to the backend chain.
/// </summary>
public sealed class TextInjectionService : ITextInjectionService
{
    private static readonly TimeSpan FocusStabilizationDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ContextMaxAge = TimeSpan.FromMinutes(1);
    private readonly IClipboardManager _clipboardManager;
    private readonly TextInjectionCoordinator _coordinator;
    private readonly INativePasteDispatcher _pasteDispatcher;
    private readonly ICaretContextReader _caretContextReader;
    private readonly Func<bool> _nativeTypingEnabled;
    private readonly Func<bool> _smartInsertionEnabled;
    private readonly Func<IntPtr, bool> _isForegroundWindow;

    private static readonly System.Text.RegularExpressions.Regex ControlCharRegex =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public TextInjectionService()
        : this(new ClipboardManager(), nativeTypingEnabled: null)
    {
    }

    public TextInjectionService(
        IClipboardManager clipboardManager,
        Func<bool>? nativeTypingEnabled = null,
        Func<bool>? smartInsertionEnabled = null)
        : this(
            clipboardManager,
            CreateDefaultCoordinator(),
            new NativePasteDispatcher(),
            nativeTypingEnabled,
            new UIAutomationCaretContextReader(),
            smartInsertionEnabled)
    {
    }

    internal TextInjectionService(
        IClipboardManager clipboardManager,
        ICaretContextReader caretContextReader,
        Func<bool>? nativeTypingEnabled = null,
        Func<bool>? smartInsertionEnabled = null)
        : this(
            clipboardManager,
            CreateDefaultCoordinator(),
            new NativePasteDispatcher(),
            nativeTypingEnabled,
            caretContextReader,
            smartInsertionEnabled)
    {
    }

    internal TextInjectionService(
        IClipboardManager clipboardManager,
        TextInjectionCoordinator coordinator,
        INativePasteDispatcher? pasteDispatcher = null,
        Func<bool>? nativeTypingEnabled = null,
        ICaretContextReader? caretContextReader = null,
        Func<bool>? smartInsertionEnabled = null,
        Func<IntPtr, bool>? isForegroundWindow = null)
    {
        _clipboardManager = clipboardManager ?? throw new ArgumentNullException(nameof(clipboardManager));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _pasteDispatcher = pasteDispatcher ?? new NativePasteDispatcher();
        _caretContextReader = caretContextReader ?? new UIAutomationCaretContextReader();
        _nativeTypingEnabled = nativeTypingEnabled ?? (() => false);
        _smartInsertionEnabled = smartInsertionEnabled ?? (() => true);
        _isForegroundWindow = isForegroundWindow ?? IsForegroundWindow;
    }

    public async Task<TextInjectionResult> InjectTextAsync(WindowContext context, string text, bool preserveExistingFocus = false)
    {
        if (context == null || string.IsNullOrWhiteSpace(text))
        {
            return TextInjectionResult.Failure("None", "Invalid input or context.");
        }

        text = SanitizeTextForInjection(text);

        if (!context.IsStillValid(ContextMaxAge))
        {
            return TextInjectionResult.Failure("None", "Window context expired.");
        }

        IntPtr targetHandle = context.WindowHandle;
        string processName = context.ProcessName ?? "Unknown";

        Logger.Info("TextInjection", $"=== Starting injection for '{processName}' ({text.Length} chars) ===");

        try
        {
            if (!await VerifyTargetAsync(targetHandle, processName, preserveExistingFocus, _isForegroundWindow))
            {
                return await CopyToClipboardOnlyAsync(text, preserveExistingFocus
                    ? "Active target changed before injection"
                    : "Target verification failed after retarget");
            }

            if (_smartInsertionEnabled())
            {
                text = ApplySmartInsertionJunction(text, targetHandle);
            }

            var injectionContext = new InjectionContext(
                context,
                text,
                preserveExistingFocus,
                _clipboardManager,
                _isForegroundWindow,
                () => _pasteDispatcher.DispatchPasteAsync(targetHandle),
                _nativeTypingEnabled());

            TextInjectionResult result = await _coordinator.InjectAsync(injectionContext);
            if (result.Success)
            {
                Logger.Info("TextInjection", result.Message);
                return result;
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("TextInjection", "Unexpected error during injection", ex);
            return await CopyToClipboardOnlyAsync(text, ex.Message);
        }
    }

    private static TextInjectionCoordinator CreateDefaultCoordinator()
    {
        return new TextInjectionCoordinator(new ITextInjectionBackend[]
        {
            new ScintillaInjectionBackend(),
            new Win32EditInjectionBackend(),
            new UIAutomationInjectionBackend(),
            new ClipboardPasteInjectionBackend(),
            new NativeTypingInjectionBackend(),
            new ClipboardOnlyInjectionBackend()
        });
    }

    private string ApplySmartInsertionJunction(string text, IntPtr targetHandle)
    {
        var stopwatch = Stopwatch.StartNew();
        CaretContext? caretContext = _caretContextReader.ReadPrecedingContext(targetHandle);
        stopwatch.Stop();

        Logger.Debug(
            "TextInjection",
            $"Smart insertion caret read completed in {stopwatch.Elapsed.TotalMilliseconds:F1}ms. ContextAvailable={caretContext != null}");

        if (caretContext == null)
        {
            return text;
        }

        string adjustedText = SmartJunction.Apply(text, caretContext);
        if (!string.Equals(adjustedText, text, StringComparison.Ordinal))
        {
            Logger.Debug(
                "TextInjection",
                $"Smart insertion adjusted junction text. OriginalLength={text.Length}, AdjustedLength={adjustedText.Length}");
        }

        return adjustedText;
    }

    private static async Task<bool> VerifyTargetAsync(
        IntPtr targetHandle,
        string processName,
        bool preserveExistingFocus,
        Func<IntPtr, bool> isForegroundWindow)
    {
        if (!preserveExistingFocus)
        {
            RestoreWindowIfMinimized(targetHandle);
            Logger.Info("TextInjection", $"Retargeting '{processName}' for direct injection.");
            if (!TryFocusWindow(targetHandle, isForegroundWindow))
            {
                Logger.Warn("TextInjection", "Failed to retarget window - copying to clipboard only");
                return false;
            }

            await Task.Delay(FocusStabilizationDelay);
            if (!isForegroundWindow(targetHandle))
            {
                Logger.Warn("TextInjection", "Target window could not be verified after retarget - copying to clipboard only");
                return false;
            }

            Logger.Info("TextInjection", "Target window verified after retarget.");
            return true;
        }

        bool targetVerified = isForegroundWindow(targetHandle);
        Logger.Info("TextInjection", $"Using existing foreground window for injection. Verified={targetVerified}");
        if (!targetVerified)
        {
            Logger.Warn("TextInjection", "Active target changed before injection - copying to clipboard only");
        }

        return targetVerified;
    }

    private async Task<TextInjectionResult> CopyToClipboardOnlyAsync(string text, string reason)
    {
        try
        {
            bool copied = await _clipboardManager.SetTextAsync(text);
            if (copied)
            {
                Logger.Info("TextInjection", $"Copied transcription to clipboard only. Reason: {reason}");
                return TextInjectionResult.ClipboardFallback("Transcription copied to clipboard — press Ctrl+V to paste.");
            }

            return TextInjectionResult.Failure("ClipboardOnly", "Fatal clipboard error");
        }
        catch
        {
            return TextInjectionResult.Failure("ClipboardOnly", "Fatal clipboard error");
        }
    }

    private static void RestoreWindowIfMinimized(IntPtr handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }
    }

    private static bool TryFocusWindow(IntPtr handle, Func<IntPtr, bool> isForegroundWindow)
    {
        NativeMethods.SetForegroundWindow(handle);
        return isForegroundWindow(handle);
    }

    private static bool IsForegroundWindow(IntPtr handle)
    {
        return NativeMethods.GetForegroundWindow() == handle;
    }

    private static string SanitizeTextForInjection(string text)
    {
        return string.IsNullOrEmpty(text) ? text : ControlCharRegex.Replace(text, string.Empty);
    }

    private static class NativeMethods
    {
        public const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
