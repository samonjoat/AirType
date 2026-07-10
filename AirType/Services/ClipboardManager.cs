using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AirType.Services;

/// <summary>
/// Manages clipboard operations with retry logic for handling locked clipboard scenarios.
/// Uses WPF's Clipboard API on the UI thread with proper error handling.
/// </summary>
public class ClipboardManager : IClipboardManager
{
    private const int MaxClipboardFormatBytes = 128 * 1024 * 1024;
    private const int GMEM_MOVEABLE = 0x0002;
    private static readonly TimeSpan[] ClipboardSetRetryDelays =
    {
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(75),
        TimeSpan.FromMilliseconds(125),
        TimeSpan.FromMilliseconds(175),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(300)
    };

    public async Task<ClipboardSnapshot> CaptureSnapshotAsync()
    {
        try
        {
            ClipboardSnapshot snapshot = ClipboardSnapshot.Unavailable();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                snapshot = CaptureNativeSnapshot();
            });

            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.Error("Workflow", $"Failed to invoke clipboard snapshot on UI thread: {ex.Message}");
            return ClipboardSnapshot.Unavailable();
        }
    }

    public async Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.Captured)
        {
            Logger.Warn("Workflow", "Clipboard restore skipped because no usable snapshot was captured");
            return false;
        }

        bool success = await TryRestoreSnapshotAsync(snapshot);
        if (success)
        {
            Logger.Debug("Workflow", "Clipboard restore succeeded on first attempt");
            return true;
        }

        Logger.Warn("Workflow", "Clipboard restore failed, retrying after 50ms...");
        await Task.Delay(50);

        success = await TryRestoreSnapshotAsync(snapshot);
        if (success)
        {
            Logger.Info("Workflow", "Clipboard restore succeeded on retry");
            return true;
        }

        Logger.Error("Workflow", "Clipboard restore failed after retry");
        return false;
    }

    /// <summary>
    /// Copies text to the Windows clipboard with automatic retry on failure.
    /// Must be called from UI thread or will use Dispatcher to marshal the call.
    /// </summary>
    /// <param name="text">The text to copy to clipboard</param>
    /// <returns>True if successful, false if failed after retry attempts</returns>
    public async Task<bool> SetTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Logger.Warn("Workflow", "Attempted to copy null or empty text to clipboard");
            return false;
        }

        Logger.Debug("Workflow", $"Copying {text.Length} characters to clipboard...");

        return await SetTextWithRetryAsync(text, TrySetClipboardTextAsync, delay => Task.Delay(delay));
    }

    internal static async Task<bool> SetTextWithRetryAsync(
        string text,
        Func<string, Task<bool>> trySetClipboardTextAsync,
        Func<TimeSpan, Task> delayAsync)
    {
        if (trySetClipboardTextAsync == null)
        {
            throw new ArgumentNullException(nameof(trySetClipboardTextAsync));
        }

        if (delayAsync == null)
        {
            throw new ArgumentNullException(nameof(delayAsync));
        }

        int maxAttempts = ClipboardSetRetryDelays.Length + 1;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            bool success = await trySetClipboardTextAsync(text);
            if (success)
            {
                if (attempt == 1)
                {
                    Logger.Debug("Workflow", "Clipboard copy succeeded on first attempt");
                }
                else
                {
                    Logger.Info("Workflow", $"Clipboard copy succeeded on attempt {attempt}");
                }

                return true;
            }

            if (attempt < maxAttempts)
            {
                TimeSpan delay = ClipboardSetRetryDelays[attempt - 1];
                Logger.Warn(
                    "Workflow",
                    $"Clipboard copy attempt {attempt}/{maxAttempts} failed, retrying after {delay.TotalMilliseconds:F0}ms...");
                await delayAsync(delay);
            }
        }

        Logger.Error(
            "Workflow",
            $"Clipboard copy failed after {maxAttempts} attempts over {GetTotalClipboardSetRetryDelayMilliseconds()}ms");
        return false;
    }

    /// <summary>
    /// Attempts to set clipboard text on the UI thread.
    /// Handles COMException which occurs when clipboard is locked by another process.
    /// </summary>
    private async Task<bool> TrySetClipboardTextAsync(string text)
    {
        try
        {
            // Clipboard operations must run on STA thread (UI thread)
            bool success = false;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    success = true;
                }
                catch (COMException ex)
                {
                    Logger.Warn("Workflow", $"COMException - Clipboard locked: {ex.Message}");
                    success = false;
                }
                catch (Exception ex)
                {
                    Logger.Error("Workflow", $"Unexpected clipboard error: {ex.Message}");
                    success = false;
                }
            });

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error("Workflow", $"Failed to invoke clipboard operation on UI thread: {ex.Message}");
            return false;
        }
    }

    private static int GetTotalClipboardSetRetryDelayMilliseconds()
    {
        int total = 0;
        foreach (TimeSpan delay in ClipboardSetRetryDelays)
        {
            total += (int)delay.TotalMilliseconds;
        }

        return total;
    }

    private async Task<bool> TryRestoreSnapshotAsync(ClipboardSnapshot snapshot)
    {
        try
        {
            bool success = false;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (snapshot.HasNativeFormats)
                    {
                        success = RestoreNativeSnapshot(snapshot);
                    }
                    else
                    {
                        System.Windows.Clipboard.Clear();
                        success = true;
                    }
                }
                catch (COMException ex)
                {
                    Logger.Warn("Workflow", $"COMException - Clipboard restore locked: {ex.Message}");
                    success = false;
                }
                catch (Exception ex)
                {
                    Logger.Error("Workflow", $"Unexpected clipboard restore error: {ex.Message}");
                    success = false;
                }
            });

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error("Workflow", $"Failed to invoke clipboard restore on UI thread: {ex.Message}");
            return false;
        }
    }

    private static ClipboardSnapshot CaptureNativeSnapshot()
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            Logger.Warn("Workflow", $"OpenClipboard failed while capturing snapshot. Win32Error={Marshal.GetLastWin32Error()}");
            return ClipboardSnapshot.Unavailable();
        }

        try
        {
            var formats = new List<ClipboardFormatSnapshot>();
            var skipped = new List<string>();
            uint format = 0;
            int discoveredCount = 0;

            while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
            {
                discoveredCount++;
                string formatName = GetFormatName(format);

                IntPtr handle = NativeMethods.GetClipboardData(format);
                if (handle == IntPtr.Zero)
                {
                    skipped.Add($"{formatName}:null");
                    continue;
                }

                UIntPtr sizePtr = NativeMethods.GlobalSize(handle);
                ulong size64 = sizePtr.ToUInt64();
                if (size64 == 0 || size64 > MaxClipboardFormatBytes || size64 > int.MaxValue)
                {
                    skipped.Add($"{formatName}:size={size64}");
                    continue;
                }

                IntPtr source = NativeMethods.GlobalLock(handle);
                if (source == IntPtr.Zero)
                {
                    skipped.Add($"{formatName}:lock");
                    continue;
                }

                try
                {
                    var data = new byte[(int)size64];
                    Marshal.Copy(source, data, 0, data.Length);
                    formats.Add(new ClipboardFormatSnapshot(format, formatName, data));
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }

            int enumError = Marshal.GetLastWin32Error();
            if (discoveredCount == 0)
            {
                Logger.Debug("Workflow", "Clipboard native snapshot captured empty clipboard");
                return ClipboardSnapshot.Empty();
            }

            if (formats.Count == 0)
            {
                Logger.Warn(
                    "Workflow",
                    $"Clipboard native snapshot found {discoveredCount} formats but captured none. EnumError={enumError}; Skipped={string.Join("; ", skipped)}");
                return ClipboardSnapshot.Unavailable();
            }

            string description = $"NativeFormats={formats.Count}/{discoveredCount}; Skipped={skipped.Count}; Names=[{string.Join(", ", formats.ConvertAll(f => f.FormatName))}]";
            if (skipped.Count > 0)
            {
                description += $"; SkippedDetails=[{string.Join("; ", skipped)}]";
            }

            Logger.Debug("Workflow", $"Clipboard native snapshot captured. {description}");
            return ClipboardSnapshot.CapturedNative(formats, description);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool RestoreNativeSnapshot(ClipboardSnapshot snapshot)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            Logger.Warn("Workflow", $"OpenClipboard failed while restoring snapshot. Win32Error={Marshal.GetLastWin32Error()}");
            return false;
        }

        var ownedHandles = new List<IntPtr>();

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                Logger.Warn("Workflow", $"EmptyClipboard failed while restoring snapshot. Win32Error={Marshal.GetLastWin32Error()}");
                return false;
            }

            int restoreCount = 0;
            foreach (ClipboardFormatSnapshot format in snapshot.NativeFormats)
            {
                IntPtr targetHandle = NativeMethods.GlobalAlloc(GMEM_MOVEABLE, new UIntPtr((uint)format.Data.Length));
                if (targetHandle == IntPtr.Zero)
                {
                    Logger.Warn("Workflow", $"GlobalAlloc failed for clipboard format {format.FormatName} ({format.Data.Length} bytes).");
                    continue;
                }

                ownedHandles.Add(targetHandle);
                IntPtr target = NativeMethods.GlobalLock(targetHandle);
                if (target == IntPtr.Zero)
                {
                    Logger.Warn("Workflow", $"GlobalLock failed for restore format {format.FormatName}.");
                    continue;
                }

                try
                {
                    Marshal.Copy(format.Data, 0, target, format.Data.Length);
                }
                finally
                {
                    NativeMethods.GlobalUnlock(targetHandle);
                }

                IntPtr setResult = NativeMethods.SetClipboardData(format.FormatId, targetHandle);
                if (setResult == IntPtr.Zero)
                {
                    Logger.Warn("Workflow", $"SetClipboardData failed for format {format.FormatName}. Win32Error={Marshal.GetLastWin32Error()}");
                    continue;
                }

                ownedHandles.Remove(targetHandle);
                targetHandle = IntPtr.Zero;
                restoreCount++;
            }

            if (restoreCount == 0)
            {
                Logger.Warn("Workflow", "Native clipboard restore did not restore any formats.");
                return false;
            }

            Logger.Debug("Workflow", $"Native clipboard restore completed. RestoredFormats={restoreCount}/{snapshot.NativeFormats.Count}");
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();

            foreach (IntPtr handle in ownedHandles)
            {
                NativeMethods.GlobalFree(handle);
            }
        }
    }

    private static string GetFormatName(uint format)
    {
        string? knownName = format switch
        {
            1 => "CF_TEXT",
            2 => "CF_BITMAP",
            3 => "CF_METAFILEPICT",
            4 => "CF_SYLK",
            5 => "CF_DIF",
            6 => "CF_TIFF",
            7 => "CF_OEMTEXT",
            8 => "CF_DIB",
            9 => "CF_PALETTE",
            10 => "CF_PENDATA",
            11 => "CF_RIFF",
            12 => "CF_WAVE",
            13 => "CF_UNICODETEXT",
            14 => "CF_ENHMETAFILE",
            15 => "CF_HDROP",
            16 => "CF_LOCALE",
            17 => "CF_DIBV5",
            _ => null
        };

        if (knownName != null)
        {
            return knownName;
        }

        var name = new StringBuilder(256);
        int length = NativeMethods.GetClipboardFormatName(format, name, name.Capacity);
        return length > 0 ? name.ToString() : $"Format#{format}";
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(int uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
