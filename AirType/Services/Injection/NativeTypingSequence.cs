namespace AirType.Services.Injection;

internal static class NativeTypingSequence
{
    public const ushort ReturnVirtualKey = 0x0D;

    public static IReadOnlyList<NativeTextInputEvent> Build(string text)
    {
        string normalized = NormalizeNewlines(text);
        var events = new List<NativeTextInputEvent>(normalized.Length * 2);

        foreach (char ch in normalized)
        {
            if (ch == '\n')
            {
                events.Add(new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyDown, ReturnVirtualKey));
                events.Add(new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyUp, ReturnVirtualKey));
                continue;
            }

            ushort value = ch;
            events.Add(new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyDown, value));
            events.Add(new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyUp, value));
        }

        return events;
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
