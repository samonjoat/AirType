using System.Runtime.InteropServices;

namespace AirType.Services.Injection;

internal enum NativeTextInputEventKind
{
    UnicodeKeyDown,
    UnicodeKeyUp,
    VirtualKeyDown,
    VirtualKeyUp
}

internal sealed record NativeTextInputEvent(NativeTextInputEventKind Kind, ushort Value);

internal interface INativeTextInput
{
    bool Send(IReadOnlyList<NativeTextInputEvent> events);
}

internal sealed class SendInputNativeTextInput : INativeTextInput
{
    public bool Send(IReadOnlyList<NativeTextInputEvent> events)
    {
        if (events.Count == 0)
        {
            return true;
        }

        NativeMethods.INPUT[] inputs = events.Select(ToInput).ToArray();
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            Logger.Warn("TextInjection", $"SendInput dispatched {sent}/{inputs.Length} input events. LastWin32Error={error}");
        }

        return sent == inputs.Length;
    }

    private static NativeMethods.INPUT ToInput(NativeTextInputEvent inputEvent)
    {
        ushort virtualKey = 0;
        ushort scanCode = 0;
        uint flags = 0;

        switch (inputEvent.Kind)
        {
            case NativeTextInputEventKind.UnicodeKeyDown:
                scanCode = inputEvent.Value;
                flags = NativeMethods.KEYEVENTF_UNICODE;
                break;
            case NativeTextInputEventKind.UnicodeKeyUp:
                scanCode = inputEvent.Value;
                flags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;
                break;
            case NativeTextInputEventKind.VirtualKeyDown:
                virtualKey = inputEvent.Value;
                break;
            case NativeTextInputEventKind.VirtualKeyUp:
                virtualKey = inputEvent.Value;
                flags = NativeMethods.KEYEVENTF_KEYUP;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(inputEvent));
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = scanCode,
                    dwFlags = flags
                }
            }
        };
    }
}

internal static class NativeMethods
{
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
