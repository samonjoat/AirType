using Xunit;

namespace AirType.Tests.Packaging;

public sealed class RepositoryHygieneSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void DeadPrivateDevelopmentTools_AreNotPresent()
    {
        Assert.False(File.Exists(Path.Combine(
            RepoRoot,
            "AirType",
            "Data",
            "MockDataGenerator.cs")));
        Assert.False(File.Exists(Path.Combine(
            RepoRoot,
            "scripts",
            "benchmark-transcription-providers.ps1")));
    }

    [Fact]
    public void PublicSource_DoesNotContainDesignMockupsOrLegacyDictionaryModel()
    {
        Assert.False(Directory.Exists(Path.Combine(RepoRoot, "AirType", "mockups")));
        Assert.False(File.Exists(Path.Combine(
            RepoRoot,
            "AirType",
            "Models",
            "DictionaryEntry.cs")));

        string snapshotScript = File.ReadAllText(Path.Combine(
            RepoRoot,
            "tools",
            "build-public-snapshot.ps1"));
        Assert.Contains("\"mockups\"", snapshotScript);
    }

    [Fact]
    public void ProductionCSharpSource_DoesNotContainAbsoluteUserHomePath()
    {
        string sourceRoot = Path.Combine(RepoRoot, "AirType");
        string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOutput(path))
            .ToArray();

        Assert.NotEmpty(sourceFiles);
        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain(@"C:\Users\", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users/", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsGeneratedOutput(string path)
    {
        string relative = Path.GetRelativePath(RepoRoot, path);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }
}
