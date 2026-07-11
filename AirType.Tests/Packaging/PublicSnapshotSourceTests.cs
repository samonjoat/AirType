using Xunit;

namespace AirType.Tests.Packaging;

public sealed class PublicSnapshotSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Solution_ContainsApplicationAndTestProjects()
    {
        string solution = File.ReadAllText(Path.Combine(RepoRoot, "AirType.sln"));

        Assert.Contains("AirType\\AirType.csproj", solution);
        Assert.Contains("AirType.Tests\\AirType.Tests.csproj", solution);
    }

    [Fact]
    public void SnapshotScript_UsesExplicitAllowlistAndRejectsPrivateRoots()
    {
        string script = File.ReadAllText(
            Path.Combine(RepoRoot, "tools", "build-public-snapshot.ps1"));

        Assert.Contains("Test-PublicSourcePath", script);
        Assert.Contains("Public snapshot generation requires a clean worktree", script);
        Assert.Contains("design-assets", script);
        Assert.Contains("implementation-plans", script);
        Assert.DoesNotContain("PROJECT_LOG.md\"", ExtractExactFileAllowlist(script));
        Assert.Contains("Public snapshot must not contain Git history or refs", script);
        Assert.Contains("Potential credential pattern", script);
        Assert.Contains("--verify-production", script);
        Assert.Contains("tools/test.ps1", script);
        Assert.Contains("@(\"restore\", \".\\AirType.sln\")", script);
        Assert.Contains("Clear-GeneratedValidationArtifacts", script);
        Assert.Contains("Generated validation directories remain", script);
        Assert.Contains("\"packaging/\"", script);
        Assert.Contains("\"packaging/windows/AirType.wxs\"", script);
        Assert.Contains("\"tools/finalize-signpath-installer.ps1\"", script);
        Assert.Contains("\"tools/verify-windows-installer.ps1\"", script);
    }

    [Fact]
    public void InterFonts_IncludeOflEvidenceInSourceAndReleasePackage()
    {
        string ofl = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType", "Fonts", "Inter", "OFL.txt"));
        string project = File.ReadAllText(Path.Combine(RepoRoot, "AirType", "AirType.csproj"));
        string notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD_PARTY_NOTICES.md"));
        string collector = File.ReadAllText(
            Path.Combine(RepoRoot, "tools", "collect-third-party-licenses.ps1"));

        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", ofl);
        Assert.Contains("Fonts\\Inter\\OFL.txt", project);
        Assert.Contains("SIL Open Font License 1.1", notices);
        Assert.Contains("Inter variable fonts", collector);
        Assert.Contains("6136f73372fedc37b80fd1a8ec3e21734073ff17376f3672718f186779672e7a", collector);
        Assert.Contains("0be2399ea925f1f83ff974764761da9860ec50742ed29a5d4c1ffd0c5c7ac3a8", collector);
    }

    private static string ExtractExactFileAllowlist(string script)
    {
        int start = script.IndexOf("$exactFiles = @(", StringComparison.Ordinal);
        int end = script.IndexOf("    )", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return script[start..end];
    }
}
