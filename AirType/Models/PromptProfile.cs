namespace AirType.Models;

public class PromptProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }

    public override string ToString() => Name;

    public static PromptProfile CreateNew => new() { Id = -1, Name = "Create New...", IsBuiltIn = true };
}
