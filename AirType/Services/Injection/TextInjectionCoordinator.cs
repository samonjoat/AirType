using AirType.Models.Injection;

namespace AirType.Services.Injection;

internal sealed class TextInjectionCoordinator
{
    private readonly IReadOnlyList<ITextInjectionBackend> _backends;

    public TextInjectionCoordinator(IEnumerable<ITextInjectionBackend> backends)
    {
        _backends = backends
            .OrderBy(backend => backend.Priority)
            .ToArray();
    }

    public async Task<TextInjectionResult> InjectAsync(InjectionContext context, CancellationToken cancellationToken = default)
    {
        foreach (ITextInjectionBackend backend in _backends)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!backend.CanAttempt(context))
            {
                Logger.Debug("TextInjection", $"Skipping backend {backend.Name}; CanAttempt=False.");
                continue;
            }

            Logger.Info("TextInjection", $"Trying injection backend {backend.Name}.");
            TextInjectionResult result = await backend.TryInjectAsync(context, cancellationToken);

            if (result.Outcome != TextInjectionOutcome.Failure)
            {
                Logger.Info("TextInjection", $"Backend {backend.Name} completed with outcome {result.Outcome}.");
                return result;
            }

            Logger.Debug("TextInjection", $"Backend {backend.Name} did not complete injection: {result.Message}");
        }

        return TextInjectionResult.Failure("None", "No injection backend completed successfully.");
    }
}
