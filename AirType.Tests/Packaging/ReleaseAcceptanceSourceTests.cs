using System.Text.Json;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class ReleaseAcceptanceSourceTests
{
    private const string AppSha256 = "33cd18122376ddcd92294254ba599e797c0b9702e77a700a2a5844b4f9581c29";
    private const string LocalAsrSha256 = "a6f05138713b2eae78cb573fe51c201731ad34ab06ed7d5b8cfc58491e646ac8";

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Evidence_CoversExactCandidateOnWindows10AndWindows11()
    {
        JsonElement windows10 = ReadEvidence("windows-10.json");
        JsonElement windows11 = ReadEvidence("windows-11.json");

        AssertEvidence(windows10, "10.0.19044", expectedWindowsAppRuntimeCount: 0);
        AssertEvidence(windows11, "10.0.26200", expectedWindowsAppRuntimeCount: 1);

        string report = File.ReadAllText(Path.Combine(
            RepoRoot, "docs", "release-acceptance", "1.0.0", "README.md"));
        Assert.Contains(AppSha256, report);
        Assert.Contains(LocalAsrSha256, report);
        Assert.Contains("150eef37825e0405194d9c6c49c475e53e20689c", report);
        Assert.Contains("Screen Sketch, Alarms, and Teams depend on it", report);
        Assert.Contains("remain private", report);
    }

    [Fact]
    public void Verifier_EnforcesCleanPortableCandidateContract()
    {
        string verifier = File.ReadAllText(Path.Combine(
            RepoRoot, "tools", "verify-clean-windows-candidate.ps1"));
        string snapshot = File.ReadAllText(Path.Combine(
            RepoRoot, "tools", "build-public-snapshot.ps1"));

        Assert.Contains("Microsoft.WindowsDesktop.App", verifier);
        Assert.Contains("Programs\\Python", verifier);
        Assert.Contains("System.Private.CoreLib.dll", verifier);
        Assert.Contains("visual-cpp-runtime.json", verifier);
        Assert.Contains("msvcp140.dll", verifier);
        Assert.Contains("WhisperModel", verifier);
        Assert.Contains("basePrefixEqualsPrefix", verifier);
        Assert.Contains("removalComplete", verifier);
        Assert.Contains("ExpectedSignatureStatus", verifier);
        Assert.Contains("InstallerPackagePath", verifier);
        Assert.Contains("ExpectedInstallerSha256", verifier);
        Assert.Contains("Invoke-MsiTransaction", verifier);
        Assert.Contains("WindowsBuiltInRole]::Administrator", verifier);
        Assert.Contains("ProgramFiles", verifier);
        Assert.Contains("AirType.lnk", verifier);
        Assert.Contains("userDataRetained", verifier);
        Assert.Contains("finalRemovalComplete", verifier);
        Assert.Contains("schemaVersion = 2", verifier);
        Assert.Contains("WorkingDirectory must be a child", verifier);
        Assert.Contains("docs/release-acceptance/", snapshot);
    }

    private static JsonElement ReadEvidence(string fileName)
    {
        string path = Path.Combine(
            RepoRoot, "docs", "release-acceptance", "1.0.0", fileName);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static void AssertEvidence(
        JsonElement evidence,
        string expectedOsVersion,
        int expectedWindowsAppRuntimeCount)
    {
        Assert.Equal(1, evidence.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expectedOsVersion, evidence.GetProperty("os").GetProperty("version").GetString());

        JsonElement environment = evidence.GetProperty("cleanEnvironment");
        Assert.Equal(0, environment.GetProperty("dotnetDesktopRuntimeRoots").GetArrayLength());
        Assert.Equal(0, environment.GetProperty("pythonRuntimeRoots").GetArrayLength());
        Assert.Equal(
            expectedWindowsAppRuntimeCount,
            environment.GetProperty("windowsAppRuntimePackageCount").GetInt32());

        JsonElement app = evidence.GetProperty("app");
        Assert.Equal(AppSha256, app.GetProperty("sha256").GetString());
        Assert.Equal("1.0.0.0", app.GetProperty("fileVersion").GetString());
        Assert.Equal("NotSigned", app.GetProperty("signatureStatus").GetString());
        Assert.True(app.GetProperty("firstLaunch").GetProperty("mainWindowHandle").GetInt64() > 0);
        Assert.True(app.GetProperty("restart").GetProperty("mainWindowHandle").GetInt64() > 0);

        JsonElement localAsr = evidence.GetProperty("localAsr");
        Assert.Equal(LocalAsrSha256, localAsr.GetProperty("sha256").GetString());
        Assert.True(localAsr.GetProperty("portableRuntimeImports").GetBoolean());
        Assert.True(localAsr.GetProperty("visualCppRuntimeAppLocal").GetBoolean());
        Assert.True(localAsr.GetProperty("basePrefixEqualsPrefix").GetBoolean());
        Assert.True(localAsr.GetProperty("modelInference").GetBoolean());
        Assert.True(localAsr.GetProperty("removalComplete").GetBoolean());
    }
}
