using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Captures metadata about the active foreground window for later automation tasks.
/// </summary>
public sealed class WindowManager : IWindowManager
{
    private const int MaxWindowTitleLength = 512;

    /// <inheritdoc />
    public WindowContext? CaptureCurrentWindow()
    {
        IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return null;
        }

        string windowTitle = GetWindowTitle(foregroundWindow);
        string processName = GetProcessName(foregroundWindow);

        return new WindowContext(
            foregroundWindow,
            windowTitle,
            processName,
            DateTime.UtcNow);
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        int capacity = NativeMethods.GetWindowTextLength(windowHandle);
        if (capacity <= 0)
        {
            capacity = MaxWindowTitleLength;
        }

        var stringBuilder = new StringBuilder(capacity + 1);
        int length = NativeMethods.GetWindowText(windowHandle, stringBuilder, stringBuilder.Capacity);

        if (length <= 0)
        {
            return string.Empty;
        }

        return stringBuilder.ToString(0, Math.Min(length, stringBuilder.Length));
    }

    private static string GetProcessName(IntPtr windowHandle)
    {
        if (NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId) == 0 || processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"WindowManager: Failed to resolve process name for PID {processId}: {ex.Message}");
            return string.Empty;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
