using System;

namespace AirType.Services;

/// <summary>
/// Abstraction over Win32 hotkey operations to enable testing without P/Invoke.
/// </summary>
public interface IHotkeyNativeMethods
{
    bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    bool UnregisterHotKey(IntPtr hWnd, int id);
    short GetAsyncKeyState(int vKey);
    int GetLastError();
}
