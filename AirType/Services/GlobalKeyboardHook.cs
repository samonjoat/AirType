using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AirType.Services;

/// <summary>
/// Provides low-level keyboard hook for capturing global keyboard events
/// even when the application doesn't have focus.
/// Handles WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, and WM_SYSKEYUP
/// so that system keys (Win, Alt) are captured before the shell intercepts them.
/// </summary>
public class GlobalKeyboardHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _disposed = false;

    /// <summary>Fires on key-down with the raw virtual-key code.</summary>
    public event EventHandler<int>? KeyDown;

    /// <summary>Fires on key-up with the raw virtual-key code.</summary>
    public event EventHandler<int>? KeyUp;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            Logger.Warn("GlobalKeyboardHook", $"Failed to start keyboard hook. Win32Error={Marshal.GetLastWin32Error()}");
        }
        else
        {
            Logger.Info("GlobalKeyboardHook", "Keyboard hook started");
        }
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        Logger.Info("GlobalKeyboardHook", "Keyboard hook stopped");
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (var curProcess = Process.GetCurrentProcess())
        using (var curModule = curProcess.MainModule)
        {
            if (curModule == null)
                return IntPtr.Zero;

            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            int vkCode = Marshal.ReadInt32(lParam);

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    KeyDown?.Invoke(this, vkCode);
                }));
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    KeyUp?.Invoke(this, vkCode);
                }));
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
            }
            _disposed = true;
        }
    }
}
