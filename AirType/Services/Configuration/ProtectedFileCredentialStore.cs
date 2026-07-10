using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AirType.Services.Configuration;

/// <summary>
/// DPAPI-backed credential storage for the current Windows user.
/// </summary>
public sealed class ProtectedFileCredentialStore : ICredentialStore
{
    public void Save(string filePath, string secret)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Credential path cannot be empty.", nameof(filePath));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Credential secret cannot be empty.", nameof(secret));

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        byte[] encryptedBytes = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(filePath, encryptedBytes);
    }

    public string? Read(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            byte[] encryptedBytes = File.ReadAllBytes(filePath);
            byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string filePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Preserve existing credential-delete behavior: best-effort cleanup.
        }
    }

    public bool Exists(string filePath) => !string.IsNullOrEmpty(Read(filePath));
}

