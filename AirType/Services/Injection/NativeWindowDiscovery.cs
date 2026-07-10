using System.Runtime.InteropServices;
using System.Text;

namespace AirType.Services.Injection;

internal static class NativeWindowDiscovery
{
    public static NativeWindowInfo? FindFocusedDescendant(IntPtr rootHandle, Func<NativeWindowInfo, bool> predicate)
    {
        if (rootHandle == IntPtr.Zero)
        {
            return null;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(rootHandle, out _);
        if (threadId == 0)
        {
            return null;
        }

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
        {
            return null;
        }

        if (info.hwndFocus != rootHandle && !NativeMethods.IsChild(rootHandle, info.hwndFocus))
        {
            return null;
        }

        NativeWindowInfo focused = Describe(info.hwndFocus);
        return predicate(focused) ? focused : null;
    }

    public static NativeWindowInfo? FindFirstDescendant(IntPtr rootHandle, Func<NativeWindowInfo, bool> predicate)
    {
        foreach (NativeWindowInfo window in EnumerateDescendants(rootHandle))
        {
            if (predicate(window))
            {
                return window;
            }
        }

        return null;
    }

    public static IReadOnlyList<NativeWindowInfo> EnumerateDescendants(IntPtr rootHandle)
    {
        var windows = new List<NativeWindowInfo>();
        if (rootHandle == IntPtr.Zero)
        {
            return windows;
        }

        NativeMethods.EnumChildWindows(rootHandle, (handle, _) =>
        {
            windows.Add(Describe(handle));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public static NativeWindowInfo Describe(IntPtr handle)
    {
        return new NativeWindowInfo(
            handle,
            GetClassName(handle),
            NativeMethods.IsWindowVisible(handle),
            NativeMethods.IsWindowEnabled(handle));
    }

    public static uint GetProcessId(IntPtr handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        return processId;
    }

    private static string GetClassName(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        int length = NativeMethods.GetClassName(handle, builder, builder.Capacity);
        return length <= 0 ? string.Empty : builder.ToString(0, length);
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }
    }
}
