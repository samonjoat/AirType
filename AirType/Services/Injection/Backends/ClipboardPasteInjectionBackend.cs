using System.Security.Cryptography;
using System.Text;
using AirType.Models.Injection;

namespace AirType.Services.Injection.Backends;

internal sealed class ClipboardPasteInjectionBackend : ITextInjectionBackend
{
    private static readonly TimeSpan PasteSettlementDelay = TimeSpan.FromMilliseconds(550);

    public string Name => "ClipboardPaste";

    public InjectionBackendPriority Priority => InjectionBackendPriority.ClipboardPaste;

    public bool CanAttempt(InjectionContext context) => true;

    public async Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
    {
        ClipboardSnapshot snapshot = await context.ClipboardManager.CaptureSnapshotAsync();
        Logger.Debug("TextInjection", $"Clipboard snapshot captured before paste. {DescribeSnapshot(snapshot)}");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!snapshot.Captured)
            {
                Logger.Warn("TextInjection", "Clipboard snapshot was unavailable; skipping clipboard paste to preserve existing clipboard.");
                return TextInjectionResult.Failure(Name, "Clipboard snapshot unavailable.");
            }

            ClipboardProfile profile = ClipboardProfile.FromSnapshot(snapshot);
            if (profile.IsNonText)
            {
                Logger.Info("TextInjection", "Non-text clipboard snapshot detected; preserving clipboard for later backend fallback.");
                return TextInjectionResult.Failure(Name, "Non-text clipboard preserved for later backend fallback.");
            }

            bool clipboardSet = await context.ClipboardManager.SetTextAsync(context.Text);
            Logger.Debug("TextInjection", $"Clipboard.SetText result before paste: Success={clipboardSet}, InjectedLength={context.Text.Length}, InjectedSha256={ComputeShortHash(context.Text)}");
            if (!clipboardSet)
            {
                return TextInjectionResult.Failure(Name, "Failed to place text on clipboard for paste dispatch.");
            }

            await LogClipboardProbeAsync("after SetText before paste", context.Text);

            Logger.Debug("TextInjection", "Transcription placed on clipboard for paste dispatch.");
            Logger.Debug("TextInjection", $"Foreground before paste dispatch. MatchesTarget={context.IsForegroundTarget()}");
            if (!context.IsForegroundTarget())
            {
                Logger.Warn("TextInjection", "Target window changed before clipboard paste dispatch; transcription left on clipboard.");
                return TextInjectionResult.ClipboardFallback("Transcription copied to clipboard — press Ctrl+V to paste.");
            }

            await context.DispatchPasteAsync();
            Logger.Debug("TextInjection", $"Paste dispatch completed; waiting {PasteSettlementDelay.TotalMilliseconds:F0}ms for foreground settlement.");

            await Task.Delay(PasteSettlementDelay, cancellationToken);

            if (!context.IsForegroundTarget())
            {
                Logger.Warn("TextInjection", "Target window changed during clipboard paste settlement; transcription left on clipboard.");
                return TextInjectionResult.ClipboardFallback("Transcription copied to clipboard — press Ctrl+V to paste.");
            }

            Logger.Info("TextInjection", "Clipboard paste dispatched to a verified target; result is provisional because insertion cannot be read back.");
            await LogClipboardProbeAsync("after provisional paste", context.Text);

            return TextInjectionResult.ProvisionalResult(
                Name,
                "Clipboard paste dispatched; insertion could not be readback-confirmed. Transcription remains on the clipboard for manual retry.",
                targetVerified: true,
                recoverySuggested: true);
        }
        catch (Exception ex)
        {
            Logger.Debug("TextInjection", $"Clipboard paste failed: {ex.Message}");
            return TextInjectionResult.Failure(Name, ex.Message);
        }
    }

    private static async Task LogClipboardProbeAsync(string phase, string injectedText)
    {
        ClipboardProbe probe = await ProbeClipboardAsync(injectedText);
        Logger.Debug("TextInjection", $"Clipboard probe {phase}. {probe}");
    }

    private static async Task<ClipboardProbe> ProbeClipboardAsync(string injectedText)
    {
        try
        {
            ClipboardProbe probe = ClipboardProbe.Failed("Probe did not run.");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    System.Windows.IDataObject? dataObject = System.Windows.Clipboard.GetDataObject();
                    string formats = DescribeDataObject(dataObject);
                    bool containsUnicodeText = System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText);
                    string? text = containsUnicodeText
                        ? System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText)
                        : null;

                    probe = ClipboardProbe.Captured(
                        formats,
                        containsUnicodeText,
                        text?.Length ?? 0,
                        string.Equals(text, injectedText, StringComparison.Ordinal),
                        text == null ? null : ComputeShortHash(text));
                }
                catch (Exception ex)
                {
                    probe = ClipboardProbe.Failed($"{ex.GetType().Name}: {ex.Message}");
                }
            });

            return probe;
        }
        catch (Exception ex)
        {
            return ClipboardProbe.Failed($"Dispatcher probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string DescribeSnapshot(ClipboardSnapshot snapshot)
    {
        return $"Captured={snapshot.Captured}, HasData={snapshot.HasData}, NativeFormatCount={snapshot.NativeFormats.Count}, Description={snapshot.Description}, {DescribeDataObject(snapshot.DataObject)}";
    }

    private static string DescribeDataObject(System.Windows.IDataObject? dataObject)
    {
        if (dataObject == null)
        {
            return "Formats=<none>";
        }

        try
        {
            string[] formats = dataObject.GetFormats(autoConvert: false);
            if (formats.Length == 0)
            {
                return "Formats=<empty>";
            }

            return $"FormatCount={formats.Length}, Formats=[{string.Join(", ", formats)}]";
        }
        catch (Exception ex)
        {
            return $"Formats=<unreadable:{ex.GetType().Name}>";
        }
    }

    private static string ComputeShortHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 6);
    }

    private sealed record ClipboardProbe(
        bool Success,
        string Formats,
        bool ContainsUnicodeText,
        int TextLength,
        bool TextMatchesInjected,
        string? TextSha256,
        string? Error)
    {
        public static ClipboardProbe Captured(
            string formats,
            bool containsUnicodeText,
            int textLength,
            bool textMatchesInjected,
            string? textSha256) =>
            new(true, formats, containsUnicodeText, textLength, textMatchesInjected, textSha256, null);

        public static ClipboardProbe Failed(string error) =>
            new(false, "Formats=<unknown>", false, 0, false, null, error);

        public override string ToString()
        {
            if (!Success)
            {
                return $"Success=False, Error={Error}";
            }

            return $"{Formats}, ContainsUnicodeText={ContainsUnicodeText}, TextLength={TextLength}, TextMatchesInjected={TextMatchesInjected}, TextSha256={TextSha256 ?? "<none>"}";
        }
    }
}
