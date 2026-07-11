using System.IO;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class ReleasePackagingSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void GlobalJson_PinsStableDotNet8Sdk()
    {
        string globalJson = File.ReadAllText(Path.Combine(RepoRoot, "global.json"));

        Assert.Contains("\"version\": \"8.0.422\"", globalJson);
        Assert.Contains("\"allowPrerelease\": false", globalJson);
    }

    [Fact]
    public void AppProject_DefinesDeliberateReleaseIdentity()
    {
        string project = File.ReadAllText(Path.Combine(RepoRoot, "AirType", "AirType.csproj"));

        Assert.Contains("<Version>1.0.0</Version>", project);
        Assert.Contains("<FileVersion>1.0.0.0</FileVersion>", project);
        Assert.Contains("<InformationalVersion>1.0.0</InformationalVersion>", project);
        Assert.Contains("<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>", project);
    }

    [Fact]
    public void ReleaseScript_RequiresSigningForPublicReleaseAndLabelsUnsignedPackages()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));

        Assert.Contains("Public release requires a clean git worktree.", script);
        Assert.Contains("Public release must be built from main.", script);
        Assert.Contains("AIRTYPE_SIGNING_CERTIFICATE_THUMBPRINT", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
        Assert.Contains("-unsigned", script);
        Assert.Contains("release-archive.ps1", script);
        Assert.Contains("New-AirTypeDeterministicZip", script);
        Assert.Contains("KeepStaging", script);
        Assert.Contains("release-manifest.json", script);
        Assert.Contains("sourceBranch = $branch", script);
        Assert.Contains("dirty = $gitStatus.Count -ne 0", script);
        Assert.Contains(".sha256", script);
        Assert.Contains("verify-release-package.ps1", script);
        Assert.DoesNotContain("CertificatePassword", script);
    }

    [Fact]
    public void SignPathFinalizer_IsFailClosedAndUsesSharedDeterministicArchive()
    {
        string finalizer = File.ReadAllText(
            Path.Combine(RepoRoot, "tools", "finalize-signpath-release.ps1"));
        string archive = File.ReadAllText(Path.Combine(RepoRoot, "tools", "release-archive.ps1"));

        Assert.Contains("SignPath finalization requires a clean git worktree", finalizer);
        Assert.Contains("SignPath stable packages must originate from main", finalizer);
        Assert.Contains("release manifest is missing dirty provenance", finalizer);
        Assert.Contains("dirty provenance is not Boolean", finalizer);
        Assert.Contains("Get-AuthenticodeSignature", finalizer);
        Assert.Contains("SignPath Foundation", finalizer);
        Assert.Contains("release-manifest.json", finalizer);
        Assert.Contains("New-AirTypeDeterministicZip", finalizer);
        Assert.Contains("verify-release-package.ps1", finalizer);
        Assert.DoesNotContain("AllowUnsigned", finalizer);
        Assert.Contains("function New-AirTypeDeterministicZip", archive);
        Assert.Contains("Release archive destination must be outside", archive);
        Assert.Contains("CompressionLevel]::Optimal", archive);
        Assert.Contains("2000, 1, 1", archive);
    }

    [Fact]
    public void ReleaseVerifier_ChecksPortableFreshProfileArtifactContract()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot, "tools", "verify-release-package.ps1"));

        Assert.Contains("Release package checksum does not match its sidecar.", script);
        Assert.Contains("Release archive contains an unsafe path", script);
        Assert.Contains("Microsoft.WindowsDesktop.App", script);
        Assert.Contains("Microsoft.WindowsAppRuntime.Bootstrap.dll", script);
        Assert.Contains("Fonts\\Inter\\OFL.txt", script);
        Assert.Contains("Third-party manifest must contain exactly one Inter variable fonts record.", script);
        Assert.Contains("SIL Open Font License 1.1", script);
        Assert.Contains("python312.dll", script);
        Assert.Contains("Get-PeMachine", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
        Assert.Contains("$firstPartyTextFiles", script);
        Assert.Contains("$sitePackagesRoot", script);
        Assert.Contains("AIRTYPE_STORAGE_ROOT", script);
        Assert.Contains("fresh-storage-root", script);
        Assert.Contains("StartupTimeoutSeconds = 20", script);
        Assert.Contains("Start-Sleep -Milliseconds 500", script);
        Assert.Contains("AirType exited during fresh-profile startup validation.", script);
        Assert.Contains("Close running AirType processes before release startup validation.", script);
    }
}
