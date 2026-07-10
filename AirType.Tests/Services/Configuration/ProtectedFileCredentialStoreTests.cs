using System;
using System.IO;
using AirType.Services.Configuration;
using Xunit;

namespace AirType.Tests.Services.Configuration;

public sealed class ProtectedFileCredentialStoreTests
{
    [Fact]
    public void SaveReadDelete_RoundTripsProtectedSecret()
    {
        using var tempDirectory = new TempDirectory();
        string filePath = Path.Combine(tempDirectory.RootPath, "credential.dat");
        var store = new ProtectedFileCredentialStore();

        store.Save(filePath, "secret-value");

        Assert.True(File.Exists(filePath));
        Assert.True(store.Exists(filePath));
        Assert.Equal("secret-value", store.Read(filePath));

        store.Delete(filePath);

        Assert.False(File.Exists(filePath));
        Assert.False(store.Exists(filePath));
        Assert.Null(store.Read(filePath));
    }

    [Fact]
    public void Read_WhenFileIsMissing_ReturnsNull()
    {
        using var tempDirectory = new TempDirectory();
        string filePath = Path.Combine(tempDirectory.RootPath, "missing.dat");
        var store = new ProtectedFileCredentialStore();

        Assert.Null(store.Read(filePath));
        Assert.False(store.Exists(filePath));
    }

    [Fact]
    public void Save_WhenSecretIsEmpty_ThrowsArgumentException()
    {
        using var tempDirectory = new TempDirectory();
        string filePath = Path.Combine(tempDirectory.RootPath, "credential.dat");
        var store = new ProtectedFileCredentialStore();

        var exception = Assert.Throws<ArgumentException>(() => store.Save(filePath, ""));

        Assert.Equal("secret", exception.ParamName);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"AirTypeCredentialTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

