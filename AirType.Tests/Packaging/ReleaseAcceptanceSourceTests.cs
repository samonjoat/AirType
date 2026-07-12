using System.Text.Json;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class ReleaseAcceptanceSourceTests
{
    private const string AppSha256 = "ae16ba175d0d0acb17bca02b84780bdb736db9eca23b6dc0116fe2347e0e7b1a";
    private const string InstallerSha256 = "f238955190d9eb2ac4c143f81c1eb98ff84ca6fc7d46e2d08e8c751650f548b4";
    private const string LocalAsrSha256 = "a508e64f52475628913515d339f1452f68ae3f43f47b6bb5424965bc0c093241";

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Evidence_CoversExactCandidateOnWindows10AndWindows11()
    {
        JsonElement windows10 = ReadEvidence("windows-10.json");
        JsonElement windows11 = ReadEvidence("windows-11.json");

        AssertEvidence(windows10, "10.0.19044", expectedWindowsAppRuntimeCount: 0);
        AssertEvidence(windows11, "10.0.26200", expectedWindowsAppRuntimeCount: 4);

        string report = File.ReadAllText(Path.Combine(
            RepoRoot, "docs", "release-acceptance", "1.0.0", "README.md"));
        Assert.Contains(AppSha256, report);
        Assert.Contains(InstallerSha256, report);
        Assert.Contains(LocalAsrSha256, report);
        Assert.Contains("f1537b335b57a003a9050f69b3e4d8b6dbe836e9", report);
        Assert.Contains("29166944421", report);
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
        Assert.Equal(2, evidence.GetProperty("schemaVersion").GetInt32());
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

        JsonElement installer = evidence.GetProperty("installer");
        Assert.Equal(InstallerSha256, installer.GetProperty("sha256").GetString());
        Assert.Equal("NotSigned", installer.GetProperty("signatureStatus").GetString());
        Assert.Equal("NotSigned", installer.GetProperty("installedExecutableSignatureStatus").GetString());
        Assert.Equal("1.0.0.0", installer.GetProperty("fileVersion").GetString());
        Assert.True(installer.GetProperty("firstLaunch").GetProperty("mainWindowHandle").GetInt64() > 0);
        Assert.True(installer.GetProperty("reinstallLaunch").GetProperty("mainWindowHandle").GetInt64() > 0);
        Assert.True(installer.GetProperty("userDataRetained").GetBoolean());
        Assert.True(installer.GetProperty("finalRemovalComplete").GetBoolean());

        JsonElement localAsr = evidence.GetProperty("localAsr");
        Assert.Equal(LocalAsrSha256, localAsr.GetProperty("sha256").GetString());
        Assert.True(localAsr.GetProperty("portableRuntimeImports").GetBoolean());
        Assert.True(localAsr.GetProperty("visualCppRuntimeAppLocal").GetBoolean());
        Assert.True(localAsr.GetProperty("basePrefixEqualsPrefix").GetBoolean());
        Assert.True(localAsr.GetProperty("modelInference").GetBoolean());
        Assert.True(localAsr.GetProperty("removalComplete").GetBoolean());
    }
}
