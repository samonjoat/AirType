using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AirType.Tests.Services.Transcription;

public sealed class LocalAsrRuntimePreparationScriptTests
{
    [Fact]
    public void PrepareRuntimeScript_DefaultsToSmallCt2Only()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repoRoot, "tools", "prepare-local-asr-runtime.ps1");

        string script = File.ReadAllText(scriptPath);

        Assert.Contains("[string[]]$Models = @(\"small.en\")", script);
        Assert.Contains("[string]$PythonVersion = \"3.12.10\"", script);
        Assert.Contains("python-$PythonVersion-embed-amd64.zip", script);
        Assert.Contains("python312.dll", script);
        Assert.Contains("Lib\\site-packages", script);
        Assert.Contains("requirements-runtime.txt", script);
        Assert.Contains("--ignore-installed", script);
        Assert.Contains("--no-deps", script);
        Assert.Contains("compat\\av.py", script);
        Assert.Contains("av.libs", script);
        Assert.Contains("bin\\pyav.exe", script);
        Assert.Contains("AIRTYPE_PCM_ONLY_SHIM", script);
        Assert.Contains("Official Python embeddable package checksum mismatch.", script);
        Assert.Contains("local-asr-vc-runtime.lock.json", script);
        Assert.Contains("$installOutput = @(& dotnet tool install wix", script);
        Assert.Contains("$installExitCode = $LASTEXITCODE", script);
        Assert.Contains("Write-Host $line", script);
        Assert.Contains("function Test-PinnedWixVersion", script);
        Assert.Contains("$Actual.Equals($Expected", script);
        Assert.Contains("$Actual.StartsWith(\"$Expected+\"", script);
        Assert.Contains("burn extract", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
        Assert.Contains("$signature.Status -ne [Management.Automation.SignatureStatus]::Valid", script);
        Assert.DoesNotContain("$sourceSignature", script);
        Assert.Contains("Visual C++ minimum runtime file set differs from its lock.", script);
        Assert.Contains("$dependencies = [string[]](ConvertFrom-Json -InputObject $dependenciesJson)", script);
        Assert.DoesNotContain("-m venv $venvRoot", script);
        Assert.DoesNotContain("[string[]]$Models = @(\"base.en\", \"small.en\")", script);
    }

    [Fact]
    public void VisualCppRuntimeLock_PinsCompleteMicrosoftX64MinimumRuntime()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string lockPath = Path.Combine(repoRoot, "tools", "local-asr-vc-runtime.lock.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockPath));
        JsonElement root = document.RootElement;
        JsonElement[] files = root.GetProperty("files").EnumerateArray().ToArray();
        string[] names = files.Select(file => file.GetProperty("targetName").GetString()!).ToArray();

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("14.44.35211", root.GetProperty("version").GetString());
        Assert.Equal("x64", root.GetProperty("architecture").GetString());
        Assert.Equal("https://aka.ms/vs/17/release/14.44.35211/VC_redist.x64.exe", root.GetProperty("installerUrl").GetString());
        Assert.Equal("cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b", root.GetProperty("installerSha256").GetString());
        Assert.Equal(12, files.Length);
        Assert.Contains("msvcp140.dll", names);
        Assert.Contains("msvcp140_1.dll", names);
        Assert.Contains("vcruntime140.dll", names);
        Assert.All(files, file => Assert.Matches("^[0-9a-f]{64}$", file.GetProperty("sha256").GetString()!));
    }

    [Fact]
    public void RuntimeDependencies_UsePcmShimInsteadOfPyavCodecStack()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string project = File.ReadAllText(Path.Combine(repoRoot, "AirType.LocalAsrWorker", "pyproject.toml"));
        string runtimeLock = File.ReadAllText(Path.Combine(repoRoot, "AirType.LocalAsrWorker", "requirements-runtime.txt"));
        string shimPath = Path.Combine(repoRoot, "AirType.LocalAsrWorker", "compat", "av.py");

        Assert.DoesNotContain("\"av==", project);
        Assert.DoesNotContain("av==", runtimeLock);
        Assert.True(File.Exists(shimPath));
        Assert.Contains("AIRTYPE_PCM_ONLY_SHIM", File.ReadAllText(shimPath));
    }

    [Fact]
    public void PackageRuntimeScript_EmitsReleaseMetadataAndRejectsRetiredBaseModel()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repoRoot, "tools", "package-local-asr-bundle.ps1");

        string script = File.ReadAllText(scriptPath);

        Assert.Contains("airtype-local-asr-bundle.json", script);
        Assert.Contains(".sha256", script);
        Assert.Contains("faster-whisper-small-en-int8", script);
        Assert.Contains("Packaging refused to include retired base.en model artifacts.", script);
        Assert.Contains("Packaging refused a host-bound Python virtual environment.", script);
        Assert.Contains("Relocated Local ASR runtime smoke test failed.", script);
        Assert.Contains("Packaging refused the unused PyAV/FFmpeg codec stack.", script);
        Assert.Contains("collect-third-party-licenses.ps1", script);
        Assert.Contains("-LocalAsrOnly", script);
        Assert.Contains("THIRD_PARTY_NOTICES.md", script);
        Assert.Contains("THIRD_PARTY_LICENSES\\manifest.json", script);
        Assert.Contains("visual-cpp-runtime.json", script);
        Assert.Contains("New-DeterministicZip", script);
        Assert.Contains("sourceCommit = $sourceCommit", script);
        Assert.Contains("createdUtc = $sourceDateUtc", script);
        Assert.Contains("ToUniversalTime().ToString(\"o\")", script);
        Assert.Contains("verify-local-asr-bundle.ps1", script);
        Assert.DoesNotContain("faster-whisper-base-en-int8", script);
    }

    [Fact]
    public void LocalAsrReleaseSource_TargetsVersionedOfficialManifest()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string sourcePath = Path.Combine(repoRoot, "AirType", "offline-engine-source.txt");

        string source = File.ReadAllText(sourcePath).Trim();

        Assert.Equal(
            "https://github.com/samonjoat/AirType/releases/download/local-asr-small-en-ct2-v1.0.0/airtype-local-asr-small-en-ct2-win-x64-v1.0.0.json",
            source);
    }

    [Fact]
    public void LocalAsrReleaseVerifier_EnforcesPortableChecksummedArtifactContract()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repoRoot, "tools", "verify-local-asr-bundle.ps1");

        string script = File.ReadAllText(scriptPath);

        Assert.Contains("Local ASR bundle checksum does not match its manifest.", script);
        Assert.Contains("Local ASR archive contains an unsafe path", script);
        Assert.Contains("non-deterministic timestamp", script);
        Assert.Contains("python312.dll", script);
        Assert.Contains("pyvenv.cfg", script);
        Assert.Contains("AIRTYPE_PCM_ONLY_SHIM", script);
        Assert.Contains("sys.prefix == sys.base_prefix", script);
        Assert.Contains("THIRD_PARTY_LICENSES", script);
        Assert.Contains("visual-cpp-runtime.json", script);
        Assert.Contains("Visual C++ runtime file checksum mismatch", script);
        Assert.Contains("Visual C++ runtime file version mismatch", script);
        Assert.DoesNotContain("$runtimeFileSignature", script);
        Assert.Contains("$firstPartyTextFiles", script);
        Assert.Contains("$sitePackagesRoot", script);
        Assert.Contains("Microsoft-Visual-Cpp-Runtime", script);
        Assert.Contains("sourceCommit", script);
        Assert.Contains("source date must be normalized to UTC", script);
    }

    [Fact]
    public void AppProject_CopiesOnlyPortableRuntimeAndActiveCt2Model()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string project = File.ReadAllText(Path.Combine(repoRoot, "AirType", "AirType.csproj"));

        Assert.Contains("AirType.LocalAsrWorker\\runtime\\**\\*.*", project);
        Assert.Contains("AirType.LocalAsrWorker\\models\\ct2\\small.en\\**\\*.*", project);
        Assert.Contains("<IncludeBundledLocalAsrModel>false</IncludeBundledLocalAsrModel>", project);
        Assert.Contains("Condition=\"'$(IncludeBundledLocalAsrModel)' == 'true'\"", project);
        Assert.Contains("RemoveLegacyLocalAsrBuildArtifacts", project);
        Assert.Contains("CleanLocalAsrPublishDirectory", project);
        Assert.Contains("BeforeTargets=\"_CopyResolvedFilesToPublishPreserveNewest\"", project);
        Assert.DoesNotContain("AirType.LocalAsrWorker\\.venv\\**\\*.*", project);
        Assert.DoesNotContain("AirType.LocalAsrWorker\\models\\**\\*.*", project);
        Assert.DoesNotContain("AirType.LocalAsrWorker\\local-asr-manifest.json", project);
    }
}
