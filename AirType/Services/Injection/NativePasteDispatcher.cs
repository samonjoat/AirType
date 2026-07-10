using System.Runtime.InteropServices;

namespace AirType.Services.Injection;

internal sealed class NativePasteDispatcher : INativePasteDispatcher
{
    private const uint WM_PASTE = 0x0302;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_BLOCK = 0x0001;
    private const uint SendTimeoutMs = 500;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyV = 0x56;

    private readonly INativeTextInput _nativeTextInput;

    public NativePasteDispatcher()
        : this(new SendInputNativeTextInput())
    {
    }

    internal NativePasteDispatcher(INativeTextInput nativeTextInput)
    {
        _nativeTextInput = nativeTextInput ?? throw new ArgumentNullException(nameof(nativeTextInput));
    }

    public Task DispatchPasteAsync(IntPtr targetHandle)
    {
        NativeWindowInfo? messageTarget =
            NativeWindowDiscovery.FindFocusedDescendant(targetHandle, IsPasteMessageTarget) ??
            NativeWindowDiscovery.FindFirstDescendant(targetHandle, IsPasteMessageTarget);

        if (messageTarget != null && TryDispatchWindowPaste(messageTarget.Handle))
        {
            Logger.Debug("TextInjection", $"Dispatched WM_PASTE to '{messageTarget.ClassName}'.");
            return Task.CompletedTask;
        }

        IReadOnlyList<NativeTextInputEvent> ctrlV = new[]
        {
            new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyDown, VirtualKeyControl),
            new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyDown, VirtualKeyV),
            new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyUp, VirtualKeyV),
            new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyUp, VirtualKeyControl)
        };

        bool sent = _nativeTextInput.Send(ctrlV);
        Logger.Debug("TextInjection", $"Dispatched native Ctrl+V via SendInput. Success={sent}");
        if (!sent)
        {
            throw new InvalidOperationException("Native Ctrl+V SendInput dispatch failed.");
        }

        return Task.CompletedTask;
    }

    private static bool IsPasteMessageTarget(NativeWindowInfo window)
    {
        if (!window.IsVisible || !window.IsEnabled)
        {
            return false;
        }

        string className = window.ClassName;
        return className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
               className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDispatchWindowPaste(IntPtr handle)
    {
        IntPtr result = NativeMethods.SendMessageTimeout(
            handle,
            WM_PASTE,
            IntPtr.Zero,
            IntPtr.Zero,
            SMTO_ABORTIFHUNG | SMTO_BLOCK,
            SendTimeoutMs,
            out _);

        return result != IntPtr.Zero;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);
    }
}
