using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Injection;

namespace AirType.Services;

/// <summary>
/// Provides text injection capabilities for placing transcription results into target windows.
/// </summary>
public interface ITextInjectionService
{
    /// <summary>
    /// Attempts to inject text into the specified window using the available methods.
    /// </summary>
    /// <param name="context">The previously captured window context.</param>
    /// <param name="text">The text to inject.</param>
    /// <returns>A <see cref="TextInjectionResult"/> describing the outcome.</returns>
    Task<TextInjectionResult> InjectTextAsync(WindowContext context, string text, bool preserveExistingFocus = false);
}
