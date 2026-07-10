using AirType.Models.Injection;

namespace AirType.Services.Injection;

internal interface ITextInjectionBackend
{
    string Name { get; }

    InjectionBackendPriority Priority { get; }

    bool CanAttempt(InjectionContext context);

    Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken);
}
