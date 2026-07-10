using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AirType.Models.Injection;

namespace AirType.Services.Injection.Backends;

internal sealed class ScintillaInjectionBackend : ITextInjectionBackend
{
    private const uint SCI_REPLACESEL = 2170;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_BLOCK = 0x0001;
    private const uint SendTimeoutMs = 1000;

    public string Name => "ScintillaDirect";

    public InjectionBackendPriority Priority => InjectionBackendPriority.ScintillaDirect;

    public bool CanAttempt(InjectionContext context)
    {
        return FindScintillaTarget(context.TargetHandle) != null;
    }

    public Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.IsForegroundTarget())
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "Target window changed before Scintilla direct insert."));
        }

        NativeWindowInfo? target = FindScintillaTarget(context.TargetHandle);
        if (target == null)
        {
            return Task.FromResult(TextInjectionResult.Failure(Name, "No Scintilla editor target found."));
        }

        try
        {
            DirectInsertResult insertResult = ReplaceSelection(target.Handle, context.Text);
            if (!insertResult.Success)
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, insertResult.Message));
            }

            if (!context.IsForegroundTarget())
            {
                return Task.FromResult(TextInjectionResult.Failure(Name, "Target window changed during Scintilla direct insert."));
            }

            return Task.FromResult(TextInjectionResult.SuccessResult(
                Name,
                $"Scintilla direct insert completed for class '{target.ClassName}'.",
                targetVerified: true));
        }
        catch (Exception ex)
        {
            Logger.Debug("TextInjection", $"Scintilla direct insert failed: {ex.Message}");
            return Task.FromResult(TextInjectionResult.Failure(Name, ex.Message));
        }
    }

    private static NativeWindowInfo? FindScintillaTarget(IntPtr rootHandle)
    {
        static bool IsScintilla(NativeWindowInfo window) =>
            window.IsVisible &&
            window.IsEnabled &&
            window.ClassName.Contains("Scintilla", StringComparison.OrdinalIgnoreCase);

        return NativeWindowDiscovery.FindFocusedDescendant(rootHandle, IsScintilla) ??
               NativeWindowDiscovery.FindFirstDescendant(rootHandle, IsScintilla);
    }

    private static DirectInsertResult ReplaceSelection(IntPtr scintillaHandle, string text)
    {
        uint processId = NativeWindowDiscovery.GetProcessId(scintillaHandle);
        if (processId == 0)
        {
            return DirectInsertResult.Failed("Unable to resolve Scintilla process id.");
        }

        IntPtr processHandle = NativeMethods.OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId);

        if (processHandle == IntPtr.Zero)
        {
            return DirectInsertResult.Failed($"OpenProcess failed for Scintilla target. Win32Error={Marshal.GetLastWin32Error()}");
        }

        IntPtr remoteBuffer = IntPtr.Zero;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text + '\0');
            remoteBuffer = NativeMethods.VirtualAllocEx(
                processHandle,
                IntPtr.Zero,
                new UIntPtr((uint)bytes.Length),
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (remoteBuffer == IntPtr.Zero)
            {
                return DirectInsertResult.Failed($"VirtualAllocEx failed for Scintilla target. Win32Error={Marshal.GetLastWin32Error()}");
            }

            if (!NativeMethods.WriteProcessMemory(processHandle, remoteBuffer, bytes, new UIntPtr((uint)bytes.Length), out UIntPtr bytesWritten) ||
                bytesWritten.ToUInt64() != (ulong)bytes.Length)
            {
                return DirectInsertResult.Failed($"WriteProcessMemory failed for Scintilla target. Win32Error={Marshal.GetLastWin32Error()}");
            }

            IntPtr sendResult = NativeMethods.SendMessageTimeout(
                scintillaHandle,
                SCI_REPLACESEL,
                IntPtr.Zero,
                remoteBuffer,
                SMTO_ABORTIFHUNG | SMTO_BLOCK,
                SendTimeoutMs,
                out _);

            if (sendResult == IntPtr.Zero)
            {
                return DirectInsertResult.Failed($"SCI_REPLACESEL timed out or failed. Win32Error={Marshal.GetLastWin32Error()}");
            }

            return DirectInsertResult.Completed();
        }
        finally
        {
            if (remoteBuffer != IntPtr.Zero)
            {
                NativeMethods.VirtualFreeEx(processHandle, remoteBuffer, UIntPtr.Zero, MEM_RELEASE);
            }

            NativeMethods.CloseHandle(processHandle);
        }
    }

    private sealed record DirectInsertResult(bool Success, string Message)
    {
        public static DirectInsertResult Completed() => new(true, "Completed.");

        public static DirectInsertResult Failed(string message) => new(false, message);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flAllocationType,
            uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            UIntPtr nSize,
            out UIntPtr lpNumberOfBytesWritten);

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
