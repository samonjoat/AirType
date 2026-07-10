using AirType.Models.Injection;

namespace AirType.Services.Injection.Backends;

internal sealed class NativeTypingInjectionBackend : ITextInjectionBackend
{
    private readonly INativeTextInput _nativeTextInput;

    public NativeTypingInjectionBackend()
        : this(new SendInputNativeTextInput())
    {
    }

    internal NativeTypingInjectionBackend(INativeTextInput nativeTextInput)
    {
        _nativeTextInput = nativeTextInput ?? throw new ArgumentNullException(nameof(nativeTextInput));
    }

    public string Name => "NativeTyping";

    public InjectionBackendPriority Priority => InjectionBackendPriority.NativeTyping;

    public bool CanAttempt(InjectionContext context) =>
        context.NativeTypingEnabled && context.IsForegroundTarget();

    public Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.NativeTypingEnabled)
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "Native typing fallback is disabled."));
        }

        if (!context.IsForegroundTarget())
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "Target window changed before native typing dispatch."));
        }

        IReadOnlyList<NativeTextInputEvent> events = NativeTypingSequence.Build(context.Text);
        if (!_nativeTextInput.Send(events))
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "Native typing SendInput dispatch failed."));
        }

        return Task.FromResult(TextInjectionResult.ProvisionalResult(
            Name,
            "Native typing dispatched; insertion could not be readback-confirmed.",
            targetVerified: true,
            recoverySuggested: true));
    }
}
