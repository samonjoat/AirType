using System;
using System.Runtime.InteropServices;

namespace AirType.Services;

/// <summary>
/// Default Win32-backed implementation of hotkey operations.
/// </summary>
public sealed class Win32HotkeyNativeMethods : IHotkeyNativeMethods
{
    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short GetAsyncKeyStateNative(int vKey);

    public bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk) => RegisterHotKeyNative(hWnd, id, fsModifiers, vk);

    public bool UnregisterHotKey(IntPtr hWnd, int id) => UnregisterHotKeyNative(hWnd, id);

    public short GetAsyncKeyState(int vKey) => GetAsyncKeyStateNative(vKey);

    public int GetLastError() => Marshal.GetLastWin32Error();
}
