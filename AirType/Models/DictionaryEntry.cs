namespace AirType.Models;

public class DictionaryEntry
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty; // "word" or "correction"
    public string? Value { get; set; } // For word type
    public string? From { get; set; } // For correction type
    public string? To { get; set; } // For correction type

    public string DisplayText =>
        Type == "correction" ? $"{From} → {To}" : Value ?? string.Empty;
}
