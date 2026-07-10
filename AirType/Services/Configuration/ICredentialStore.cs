namespace AirType.Services.Configuration;

/// <summary>
/// Stores and retrieves protected credential bytes from explicit file paths.
/// </summary>
public interface ICredentialStore
{
    void Save(string filePath, string secret);
    string? Read(string filePath);
    void Delete(string filePath);
    bool Exists(string filePath);
}

