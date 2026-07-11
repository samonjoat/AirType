using System;
using System.IO;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class PublicDocumentationSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void PublicDocuments_HaveNoReleasePlaceholders()
    {
        string[] paths =
        {
            "LICENSE",
            "CODE_OF_CONDUCT.md",
            "CONTRIBUTING.md",
            "DCO.txt",
            "PRIVACY.md",
            "THIRD_PARTY_NOTICES.md",
            "TRADEMARKS.md",
            Path.Combine("docs", "ARCHITECTURE.md"),
            "SECURITY.md",
            Path.Combine("docs", "INSTALL.md"),
            Path.Combine("docs", "RELEASING.md")
        };

        foreach (string relativePath in paths)
        {
            string content = File.ReadAllText(Path.Combine(RepoRoot, relativePath));
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.DoesNotContain("TODO", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.com", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<support", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Readme_DescribesPublicTreeAndHonestReleaseStatus()
    {
        string readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));

        Assert.Contains("currently no supported stable binary", readme);
        Assert.Contains("Local ASR", readme);
        Assert.Contains("AGPL-3.0-only", readme);
        Assert.Contains("does not", readme);
        Assert.Contains("grant rights to the AirType name", readme);
        Assert.Contains("docs/ARCHITECTURE.md", readme);
        Assert.Contains(".\\tools\\test.ps1", readme);
        Assert.DoesNotContain("design-assets/", readme);
        Assert.DoesNotContain("implementation-plans/", readme);
        Assert.DoesNotContain("PROJECT_LOG.md", readme);
        Assert.DoesNotContain("AGENTS.md", readme);
    }

    [Fact]
    public void ReleaseGuidance_DistinguishesUnsignedCandidateFromSignedStable()
    {
        string install = File.ReadAllText(Path.Combine(RepoRoot, "docs", "INSTALL.md"));
        string releasing = File.ReadAllText(Path.Combine(RepoRoot, "docs", "RELEASING.md"));

        Assert.Contains("GitHub Releases", install);
        Assert.Contains("unsigned validation candidate", install);
        Assert.Contains("SignPath Foundation", releasing);
        Assert.Contains("Do not label it stable", releasing);
        Assert.Contains("build-public-snapshot.ps1", releasing);
        Assert.Contains(".\\tools\\test.ps1", releasing);
        Assert.Contains(".\\tools\\audit-dependencies.ps1", releasing);
        Assert.DoesNotContain("python -m pip_audit -r", releasing);
        Assert.DoesNotContain("AGENTS.md", releasing);
        Assert.DoesNotContain("implementation-plans", releasing);
    }

    [Fact]
    public void GovernanceDocuments_DeclareAgplDcoAndTrademarkBoundary()
    {
        string license = File.ReadAllText(Path.Combine(RepoRoot, "LICENSE"));
        string project = File.ReadAllText(Path.Combine(RepoRoot, "AirType", "AirType.csproj"));
        string workerProject = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType.LocalAsrWorker", "pyproject.toml"));
        string contributing = File.ReadAllText(Path.Combine(RepoRoot, "CONTRIBUTING.md"));
        string trademarks = File.ReadAllText(Path.Combine(RepoRoot, "TRADEMARKS.md"));
        string codeOfConduct = File.ReadAllText(Path.Combine(RepoRoot, "CODE_OF_CONDUCT.md"));

        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", license);
        Assert.Contains("Version 3, 19 November 2007", license);
        Assert.DoesNotContain("End User License Agreement", license, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>", project);
        Assert.Contains("license = { text = \"AGPL-3.0-only\" }", workerProject);
        Assert.Contains("git commit -s", contributing);
        Assert.Contains("DCO.txt", contributing);
        Assert.Contains("does not grant trademark rights", contributing);
        Assert.Contains("Modified distributions must use a distinct product name", trademarks);
        Assert.DoesNotContain("INSERT CONTACT METHOD", codeOfConduct);
    }

    [Fact]
    public void PrivacyNotice_DisclosesCloudInputsLocalTracesAndDeletionBoundaries()
    {
        string privacy = File.ReadAllText(Path.Combine(RepoRoot, "PRIVACY.md"));

        Assert.Contains("raw ASR transcript", privacy);
        Assert.Contains("dictionary entries", privacy);
        Assert.Contains("active transcription", privacy);
        Assert.Contains("transcription trace JSON", privacy);
        Assert.Contains("Windows Data Protection API", privacy);
        Assert.Contains("fallback priority is Groq, then OpenRouter", privacy);
        Assert.Contains("does **not** delete transcript-history rows", privacy);
        Assert.Contains("Provider-side copies", privacy);
        Assert.Contains("does not send product analytics or telemetry", privacy);
    }

    [Fact]
    public void AppProject_PublishesEndUserDocuments()
    {
        string project = File.ReadAllText(Path.Combine(RepoRoot, "AirType", "AirType.csproj"));

        Assert.Contains("..\\LICENSE", project);
        Assert.Contains("..\\PRIVACY.md", project);
        Assert.Contains("..\\THIRD_PARTY_NOTICES.md", project);
        Assert.Contains("..\\SECURITY.md", project);
        Assert.Contains("..\\docs\\INSTALL.md", project);
    }

    [Fact]
    public void PublicSource_OmitsUnusedUiAndModelRemnants()
    {
        string[] obsoleteFiles =
        {
            Path.Combine("AirType", "Converters", "DummyConverter.cs"),
            Path.Combine("AirType", "Converters", "StringEmptyToVisibilityConverter.cs"),
            Path.Combine("AirType", "Models", "HistoryEntry.cs"),
            Path.Combine("AirType", "Models", "LogEntryViewModel.cs")
        };
        Assert.All(obsoleteFiles, path => Assert.False(File.Exists(Path.Combine(RepoRoot, path))));

        string app = File.ReadAllText(Path.Combine(RepoRoot, "AirType", "App.xaml"));
        string dialog = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType", "Views", "AddToDictionaryDialog.xaml"));
        string settings = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType", "Views", "SettingsView.xaml"));
        string comboStyles = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType", "Styles", "ComboBoxStyles.xaml"));
        string project = File.ReadAllText(Path.Combine(RepoRoot, "AirType", "AirType.csproj"));

        Assert.DoesNotContain("StringEmptyToVisibilityConverter", app);
        Assert.DoesNotContain("DummyConverter", app);
        Assert.DoesNotContain("InverseBoolToVisConverter", app);
        Assert.DoesNotContain("BoolToVisConverter", app);
        Assert.DoesNotContain("MaterialDesignTheme.Defaults.xaml", app);
        Assert.Contains("InverseBooleanToVisibilityConverter", dialog);
        Assert.Contains("BooleanToVisibilityConverter", dialog);
        Assert.DoesNotContain("InverseBoolToVisConverter", dialog);
        Assert.DoesNotContain("BoolToVisConverter", dialog);
        Assert.DoesNotContain("x:Key=\"InverseBoolToVis\"", settings);
        Assert.DoesNotContain("WpfDictationUI", comboStyles);
        Assert.DoesNotContain("<PackageReference Include=\"MaterialDesignColors\"", project);
    }

    [Fact]
    public void ReleasePipeline_CollectsAndVerifiesThirdPartyEvidence()
    {
        string collector = File.ReadAllText(Path.Combine(RepoRoot, "tools", "collect-third-party-licenses.ps1"));
        string builder = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        string verifier = File.ReadAllText(Path.Combine(RepoRoot, "tools", "verify-release-package.ps1"));
        string vcRuntimeLock = File.ReadAllText(Path.Combine(RepoRoot, "tools", "local-asr-vc-runtime.lock.json"));

        Assert.Contains("project.assets.json", builder);
        Assert.Contains("collect-third-party-licenses.ps1", builder);
        Assert.Contains("RuntimeRequirementsPath", builder);
        Assert.Contains("RuntimeRequirementsPath", collector);
        Assert.Contains("LocalAsrOnly", collector);
        Assert.Contains("ThirdPartyNotices.txt", collector);
        Assert.Contains("Microsoft-Visual-Cpp-Runtime", collector);
        Assert.Contains("$vcRuntime.product", collector);
        Assert.Contains("Microsoft Visual C++ 2015-2022 Redistributable (x64)", vcRuntimeLock);
        Assert.Contains("Systran/faster-whisper-small.en", collector);
        Assert.Contains("unused PyAV package", verifier);
        Assert.Contains("stale PyAV launcher", verifier);
        Assert.Contains("AIRTYPE_PCM_ONLY_SHIM", verifier);
        Assert.Contains("THIRD_PARTY_LICENSES", verifier);
        Assert.Contains("Third-party evidence file is missing", verifier);
        Assert.Contains("PRIVACY.md", verifier);
        Assert.Contains("INSTALL.md", verifier);
    }
}
