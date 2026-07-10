namespace AirType.Models.Configuration;

/// <summary>
/// Determines how transcribed text should be post-processed before
/// injection/clipboard operations.
/// </summary>
public enum TextFormattingMode
{
    PlainText = 0,
    Markdown = 1
}
