using AirType.Models.Injection;

namespace AirType.Services.Injection.Backends;

internal sealed class ClipboardOnlyInjectionBackend : ITextInjectionBackend
{
    public string Name => "ClipboardOnly";

    public InjectionBackendPriority Priority => InjectionBackendPriority.ClipboardOnly;

    public bool CanAttempt(InjectionContext context) => true;

    public async Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool copied = await context.ClipboardManager.SetTextAsync(context.Text);
            if (!copied)
            {
                return TextInjectionResult.Failure(Name, "Fatal clipboard error.");
            }

            Logger.Info("TextInjection", "Copied transcription to clipboard only after all injection backends failed.");
            return TextInjectionResult.ClipboardFallback("Transcription copied to clipboard — press Ctrl+V to paste.");
        }
        catch
        {
            return TextInjectionResult.Failure(Name, "Fatal clipboard error.");
        }
    }
}
