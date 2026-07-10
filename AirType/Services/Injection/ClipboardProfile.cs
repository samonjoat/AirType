namespace AirType.Services.Injection;

internal sealed class ClipboardProfile
{
    private static readonly HashSet<uint> TextFormatIds = new() { 1, 7, 13, 16 };
    private static readonly HashSet<string> TextFormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text",
        "UnicodeText",
        "OEMText",
        "Locale",
        "DataObject",
        "Ole Private Data",
        "CF_TEXT",
        "CF_UNICODETEXT",
        "CF_OEMTEXT",
        "CF_LOCALE"
    };

    private ClipboardProfile(bool captured, bool hasData, bool isTextOnly, bool isNonText, string description)
    {
        Captured = captured;
        HasData = hasData;
        IsTextOnly = isTextOnly;
        IsNonText = isNonText;
        Description = description;
    }

    public bool Captured { get; }

    public bool HasData { get; }

    public bool IsTextOnly { get; }

    public bool IsNonText { get; }

    public string Description { get; }

    public static ClipboardProfile FromSnapshot(ClipboardSnapshot snapshot)
    {
        if (!snapshot.Captured)
        {
            return new ClipboardProfile(false, false, false, false, "Clipboard snapshot unavailable.");
        }

        if (!snapshot.HasData || !snapshot.HasNativeFormats)
        {
            return new ClipboardProfile(true, snapshot.HasData, true, false, snapshot.Description);
        }

        foreach (ClipboardFormatSnapshot format in snapshot.NativeFormats)
        {
            if (TextFormatIds.Contains(format.FormatId) || TextFormatNames.Contains(format.FormatName))
            {
                continue;
            }

            return new ClipboardProfile(true, true, false, true, snapshot.Description);
        }

        return new ClipboardProfile(true, true, true, false, snapshot.Description);
    }
}
