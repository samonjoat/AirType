using System.Runtime.InteropServices;
using AirType.Models.Injection;

namespace AirType.Services.Injection.Backends;

internal sealed class Win32EditInjectionBackend : ITextInjectionBackend
{
    private const uint EM_REPLACESEL = 0x00C2;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_BLOCK = 0x0001;
    private const uint SendTimeoutMs = 1000;

    public string Name => "Win32EditDirect";

    public InjectionBackendPriority Priority => InjectionBackendPriority.Win32EditDirect;

    public bool CanAttempt(InjectionContext context)
    {
        return FindEditTarget(context.TargetHandle) != null;
    }

    public Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.IsForegroundTarget())
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "Target window changed before Win32 edit insert."));
        }

        NativeWindowInfo? target = FindEditTarget(context.TargetHandle);
        if (target == null)
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "No Win32 Edit/RichEdit target found."));
        }

        IntPtr result = NativeMethods.SendMessageTimeout(
            target.Handle,
            EM_REPLACESEL,
            new IntPtr(1),
            context.Text,
            SMTO_ABORTIFHUNG | SMTO_BLOCK,
            SendTimeoutMs,
            out _);

        if (result == IntPtr.Zero)
        {
            return Task.FromResult(TextInjectionResult.Failure(
                Name,
                $"EM_REPLACESEL timed out or failed for class '{target.ClassName}'. Win32Error={Marshal.GetLastWin32Error()}"));
        }

        if (!context.IsForegroundTarget())
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "Target window changed during Win32 edit insert."));
        }

        return Task.FromResult(TextInjectionResult.SuccessResult(
            Name,
            $"Win32 Edit/RichEdit direct insert completed for class '{target.ClassName}'.",
            targetVerified: true));
    }

    private static NativeWindowInfo? FindEditTarget(IntPtr rootHandle)
    {
        return NativeWindowDiscovery.FindFocusedDescendant(rootHandle, IsEditTarget) ??
               NativeWindowDiscovery.FindFirstDescendant(rootHandle, IsEditTarget);
    }

    private static bool IsEditTarget(NativeWindowInfo window)
    {
        if (!window.IsVisible || !window.IsEnabled)
        {
            return false;
        }

        string className = window.ClassName;
        return className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
               className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
               className.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);
    }
}
