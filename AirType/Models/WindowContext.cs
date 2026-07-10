using System;
using System.Runtime.InteropServices;

namespace AirType.Models;

/// <summary>
/// Represents metadata about a captured application window for text injection.
/// </summary>
public sealed class WindowContext
{
    /// <summary>
    /// Creates a new instance of <see cref="WindowContext"/>.
    /// </summary>
    /// <param name="windowHandle">The native handle to the captured window.</param>
    /// <param name="windowTitle">The window title at the time of capture.</param>
    /// <param name="processName">The owning process name.</param>
    /// <param name="captureTime">The UTC timestamp the window was captured.</param>
    public WindowContext(IntPtr windowHandle, string windowTitle, string processName, DateTime captureTime)
    {
        WindowHandle = windowHandle;
        WindowTitle = windowTitle ?? string.Empty;
        ProcessName = processName ?? string.Empty;
        CaptureTime = captureTime;
    }

    /// <summary>
    /// The native window handle (HWND).
    /// </summary>
    public IntPtr WindowHandle { get; }

    /// <summary>
    /// The window title captured at the time of focus capture.
    /// </summary>
    public string WindowTitle { get; }

    /// <summary>
    /// The name of the owning process.
    /// </summary>
    public string ProcessName { get; }

    /// <summary>
    /// The UTC timestamp when the window context was captured.
    /// </summary>
    public DateTime CaptureTime { get; }

    /// <summary>
    /// Indicates whether the context has a valid window handle and metadata.
    /// </summary>
    public bool IsValid()
    {
        return WindowHandle != IntPtr.Zero;
    }

    /// <summary>
    /// Checks whether the captured window is still valid and, optionally, within the specified age.
    /// </summary>
    /// <param name="maxAge">
    /// Optional maximum age. When provided, the context is considered stale if it is older than the provided duration.
    /// </param>
    public bool IsStillValid(TimeSpan? maxAge = null)
    {
        if (!IsValid())
        {
            return false;
        }

        if (maxAge.HasValue && DateTime.UtcNow - CaptureTime > maxAge.Value)
        {
            return false;
        }

        return NativeMethods.IsWindow(WindowHandle);
    }

    /// <summary>
    /// Returns the elapsed time since the window was captured.
    /// </summary>
    public TimeSpan Age => DateTime.UtcNow - CaptureTime;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);
    }
}
