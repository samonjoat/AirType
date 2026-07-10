using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace AirType.Services;

public sealed class ClipboardFormatSnapshot
{
    public ClipboardFormatSnapshot(uint formatId, string formatName, byte[] data)
    {
        FormatId = formatId;
        FormatName = formatName;
        Data = data;
    }

    public uint FormatId { get; }
    public string FormatName { get; }
    public byte[] Data { get; }
}

public sealed class ClipboardSnapshot
{
    private ClipboardSnapshot(
        IDataObject? dataObject,
        IReadOnlyList<ClipboardFormatSnapshot> nativeFormats,
        bool hasData,
        bool captured,
        string description)
    {
        DataObject = dataObject;
        NativeFormats = nativeFormats;
        HasData = hasData;
        Captured = captured;
        Description = description;
    }

    public IDataObject? DataObject { get; }
    public IReadOnlyList<ClipboardFormatSnapshot> NativeFormats { get; }
    public bool HasData { get; }
    public bool Captured { get; }
    public string Description { get; }
    public bool HasNativeFormats => NativeFormats.Count > 0;

    public static ClipboardSnapshot CapturedData(IDataObject dataObject) =>
        new(dataObject, Array.Empty<ClipboardFormatSnapshot>(), hasData: true, captured: true, "WPF IDataObject snapshot");

    public static ClipboardSnapshot CapturedNative(IReadOnlyList<ClipboardFormatSnapshot> nativeFormats, string description) =>
        new(null, nativeFormats, hasData: nativeFormats.Count > 0, captured: true, description);

    public static ClipboardSnapshot Empty() =>
        new(null, Array.Empty<ClipboardFormatSnapshot>(), hasData: false, captured: true, "Empty clipboard");

    public static ClipboardSnapshot Unavailable() =>
        new(null, Array.Empty<ClipboardFormatSnapshot>(), hasData: false, captured: false, "Unavailable clipboard");
}

/// <summary>
/// Interface for managing clipboard operations.
/// Provides methods to copy text to the Windows clipboard with error handling.
/// </summary>
public interface IClipboardManager
{
    /// <summary>
    /// Captures the current clipboard data object so it can be restored after a verified paste.
    /// </summary>
    Task<ClipboardSnapshot> CaptureSnapshotAsync();

    /// <summary>
    /// Restores a previously captured clipboard snapshot.
    /// </summary>
    Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot);

    /// <summary>
    /// Copies the specified text to the Windows clipboard.
    /// Includes retry logic for handling clipboard lock scenarios.
    /// </summary>
    /// <param name="text">The text to copy to the clipboard</param>
    /// <returns>True if the operation succeeded, false if it failed after retries</returns>
    Task<bool> SetTextAsync(string text);
}
