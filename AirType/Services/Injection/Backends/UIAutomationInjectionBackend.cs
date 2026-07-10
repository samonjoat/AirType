using AirType.Models.Injection;

namespace AirType.Services.Injection.Backends;

internal sealed class UIAutomationInjectionBackend : ITextInjectionBackend
{
    private readonly IUIAutomationTextTargetResolver _targetResolver;

    public UIAutomationInjectionBackend()
        : this(new UIAutomationTextTargetResolver())
    {
    }

    internal UIAutomationInjectionBackend(IUIAutomationTextTargetResolver targetResolver)
    {
        _targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
    }

    public string Name => "UIAutomationInsert";

    public InjectionBackendPriority Priority => InjectionBackendPriority.UIAutomationInsert;

    public bool CanAttempt(InjectionContext context) => _targetResolver.ResolveTarget(context.TargetHandle) != null;

    public Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IUIAutomationTextTarget? target = _targetResolver.ResolveTarget(context.TargetHandle);
            if (target == null)
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, "No UIAutomation text target found."));
            }

            if (!target.IsEnabled || !target.IsKeyboardFocusable || target.IsPassword)
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, $"UIAutomation target is not a safe editable control: {target.Description}."));
            }

            if (!target.SupportsValuePattern)
            {
                string reason = target.SupportsTextPattern
                    ? "UIAutomation TextPattern is readable but has no safe write/insert API."
                    : "UIAutomation target exposes no writable text pattern.";
                return Task.FromResult(TextInjectionResult.Failure(Name, reason));
            }

            if (target.IsValueReadOnly)
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, "UIAutomation ValuePattern is read-only."));
            }

            string existingValue = target.GetValue();
            if (!string.IsNullOrEmpty(existingValue))
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, "UIAutomation target already contains text; refusing to overwrite."));
            }

            target.SetValue(context.Text);
            string readback = target.GetValue();
            if (!string.Equals(readback, context.Text, StringComparison.Ordinal))
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, "UIAutomation readback did not match inserted text."));
            }

            return Task.FromResult(TextInjectionResult.SuccessResult(
                Name,
                $"UIAutomation ValuePattern insert confirmed for {target.Description}.",
                targetVerified: true));
        }
        catch (Exception ex)
        {
            Logger.Debug("TextInjection", $"UIAutomation insert failed: {ex.Message}");
            return Task.FromResult(TextInjectionResult.Failure(Name, ex.Message));
        }
    }
}
