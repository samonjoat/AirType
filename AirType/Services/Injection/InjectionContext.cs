using AirType.Models;

namespace AirType.Services.Injection;

internal sealed class InjectionContext
{
    public InjectionContext(
        WindowContext windowContext,
        string text,
        bool preserveExistingFocus,
        IClipboardManager clipboardManager,
        Func<IntPtr, bool> isForegroundWindow,
        Func<Task> dispatchPasteAsync,
        bool nativeTypingEnabled = false)
    {
        WindowContext = windowContext ?? throw new ArgumentNullException(nameof(windowContext));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        PreserveExistingFocus = preserveExistingFocus;
        ClipboardManager = clipboardManager ?? throw new ArgumentNullException(nameof(clipboardManager));
        IsForegroundWindow = isForegroundWindow ?? throw new ArgumentNullException(nameof(isForegroundWindow));
        DispatchPasteAsync = dispatchPasteAsync ?? throw new ArgumentNullException(nameof(dispatchPasteAsync));
        NativeTypingEnabled = nativeTypingEnabled;
    }

    public WindowContext WindowContext { get; }

    public string Text { get; }

    public bool PreserveExistingFocus { get; }

    public IClipboardManager ClipboardManager { get; }

    public Func<IntPtr, bool> IsForegroundWindow { get; }

    public Func<Task> DispatchPasteAsync { get; }

    public bool NativeTypingEnabled { get; }

    public IntPtr TargetHandle => WindowContext.WindowHandle;

    public bool IsForegroundTarget() => IsForegroundWindow(TargetHandle);
}
