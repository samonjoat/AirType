using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Provides access to window focus metadata for text injection workflows.
/// </summary>
public interface IWindowManager
{
    /// <summary>
    /// Captures the currently focused window and returns its metadata.
    /// </summary>
    /// <returns>The captured <see cref="WindowContext"/> or <c>null</c> if no window is focused.</returns>
    WindowContext? CaptureCurrentWindow();
}
