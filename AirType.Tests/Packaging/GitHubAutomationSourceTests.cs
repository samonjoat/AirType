using System.Text.RegularExpressions;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class GitHubAutomationSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Workflows_RestrictPermissionsAndPinActions()
    {
        string workflowRoot = Path.Combine(RepoRoot, ".github", "workflows");
        string[] workflows = Directory.GetFiles(workflowRoot, "*.yml");

        Assert.Equal(5, workflows.Length);
        foreach (string workflowPath in workflows)
        {
            string workflow = File.ReadAllText(workflowPath);
            Assert.Contains("permissions:\n  contents: read", Normalize(workflow));
            Assert.DoesNotContain("pull_request_target", workflow);
            Assert.DoesNotContain("contents: write", workflow);
            Assert.DoesNotContain("pull-requests: write", workflow);
            if (!string.Equals(Path.GetFileName(workflowPath), "codeql.yml", StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain("security-events: write", workflow);
            }

            foreach (Match match in Regex.Matches(workflow, @"uses:\s+[^\s@]+@([^\s#]+)"))
            {
                Assert.Matches("^[0-9a-f]{40}$", match.Groups[1].Value);
            }
        }
    }

    [Fact]
    public void CodeQlWorkflow_IsPinnedManualAndActivatesOnlyWhenPublic()
    {
        string workflow = ReadWorkflow("codeql.yml");

        Assert.Contains("if: github.event.repository.private == false", workflow);
        Assert.Contains("security-events: write", workflow);
        Assert.Contains("runs-on: windows-2025", workflow);
        Assert.Contains("languages: csharp", workflow);
        Assert.Contains("build-mode: manual", workflow);
        Assert.Contains("queries: security-extended", workflow);
        Assert.Contains("dotnet clean .\\AirType\\AirType.csproj", workflow);
        Assert.Contains("dotnet build .\\AirType\\AirType.csproj", workflow);
        Assert.Contains("github/codeql-action/init@99df26d4f13ea111d4ec1a7dddef6063f76b97e9", workflow);
        Assert.Contains("github/codeql-action/analyze@99df26d4f13ea111d4ec1a7dddef6063f76b97e9", workflow);
        Assert.DoesNotContain("github/codeql-action/autobuild", workflow);
    }

    [Fact]
    public void CiWorkflow_RunsBuildTestsDcoSoundsAndSnapshot()
    {
        string workflow = ReadWorkflow("ci.yml");

        Assert.Contains("dotnet clean .\\AirType\\AirType.csproj", workflow);
        Assert.Contains("dotnet build .\\AirType\\AirType.csproj", workflow);
        Assert.Contains(".\\tools\\test.ps1 -NoRestore", workflow);
        Assert.DoesNotContain("dotnet test", workflow);
        Assert.Contains("verify-dco.ps1", workflow);
        Assert.Contains("--verify-production", workflow);
        Assert.Contains("build-public-snapshot.ps1", workflow);
        Assert.Contains("python -m pytest", workflow);
    }

    [Fact]
    public void TestRunner_IsolatesStorageAndDisablesWindowsAppSdkStartup()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot, "tools", "test.ps1"));
        string testProject = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType.Tests", "AirType.Tests.csproj"));
        string testInitializer = File.ReadAllText(
            Path.Combine(RepoRoot, "AirType.Tests", "TestStorageIsolation.cs"));

        Assert.Contains("AIRTYPE_STORAGE_ROOT", script);
        Assert.Contains("[IO.Path]::GetTempPath()", script);
        Assert.Contains("Refusing to remove test storage outside", script);
        Assert.Contains("WindowsAppSdkBootstrapInitialize=false", script);
        Assert.Contains("WindowsAppSdkUndockedRegFreeWinRTInitialize=false", script);
        Assert.Contains("--blame-hang-timeout", script);
        Assert.Contains("<WindowsAppSdkBootstrapInitialize>false", testProject);
        Assert.Contains("<WindowsAppSdkUndockedRegFreeWinRTInitialize>false", testProject);
        Assert.DoesNotContain("ModuleInitializer", testInitializer);
    }

    [Fact]
    public void SecurityWorkflow_RunsPinnedAuditsAndGitleaks()
    {
        string workflow = ReadWorkflow("security.yml");
        string auditScript = File.ReadAllText(Path.Combine(RepoRoot, "tools", "audit-dependencies.ps1"));

        Assert.Contains("pip-audit==2.10.1", workflow);
        Assert.Contains("audit-dependencies.ps1", workflow);
        Assert.Contains("gitleaks/gitleaks-action@", workflow);
        Assert.Contains("pull-requests: read", workflow);
        Assert.Contains("GITLEAKS_ENABLE_COMMENTS: 'false'", workflow);
        Assert.Contains("dotnet restore $projectPath", auditScript);
        Assert.Contains("--vulnerable --include-transitive --format json", auditScript);
        Assert.Contains("python-all-requirements.txt", auditScript);
        Assert.Contains("configuration.get(\"build-system\", {})", auditScript);
        Assert.Contains("project.get(\"dependencies\", [])", auditScript);
        Assert.Contains("project.get(\"optional-dependencies\", {})", auditScript);
        Assert.Contains("--requirement $pythonAuditRequirementsPath", auditScript);
        Assert.Contains("python-vulnerabilities.json", auditScript);
        Assert.DoesNotContain("--requirement $requirementsPath", auditScript);
        Assert.True(
            auditScript.IndexOf("dotnet restore $projectPath", StringComparison.Ordinal) <
            auditScript.IndexOf("dotnet list $projectPath package", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseCandidateWorkflow_CannotPublishARelease()
    {
        string workflow = ReadWorkflow("release-candidate.yml");

        Assert.Contains("workflow_dispatch", workflow);
        Assert.Contains("build-release.ps1", workflow);
        Assert.Contains("-unsigned", workflow);
        Assert.Contains("-unsigned.msi", workflow);
        Assert.Contains("-unsigned.msi.sha256", workflow);
        Assert.Contains("actions/upload-artifact@", workflow);
        Assert.Contains("Published as GitHub Release: no", workflow);
        Assert.DoesNotContain("gh release", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create-release", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignReleaseWorkflow_IsPinnedReadOnlyManualAndCannotPublish()
    {
        string workflow = ReadWorkflow("sign-release.yml");

        Assert.Contains("workflow_dispatch", workflow);
        Assert.Contains("runs-on: windows-2025", workflow);
        Assert.Contains("environment: release-signing", workflow);
        Assert.Contains("actions: read", workflow);
        Assert.Contains("-KeepStaging", workflow);
        Assert.Contains("github-artifact-id:", workflow);
        Assert.Contains("SignPath/github-action-submit-signing-request@b9d91eadd323de506c0c81cf0c7fe7438f3360fd", workflow);
        Assert.Contains("SIGNPATH_API_TOKEN", workflow);
        Assert.Contains("SIGNPATH_ORGANIZATION_ID", workflow);
        Assert.Contains("SIGNPATH_PROJECT_SLUG", workflow);
        Assert.Contains("SIGNPATH_SIGNING_POLICY_SLUG", workflow);
        Assert.Contains("SIGNPATH_PAYLOAD_ARTIFACT_CONFIGURATION_SLUG", workflow);
        Assert.Contains("SIGNPATH_INSTALLER_ARTIFACT_CONFIGURATION_SLUG", workflow);
        Assert.Contains("artifact-configuration-slug:", workflow);
        Assert.Contains("upload-installer-input", workflow);
        Assert.Contains("finalize-signpath-release.ps1", workflow);
        Assert.Contains("finalize-signpath-installer.ps1", workflow);
        Assert.Contains("win-x64.msi.sha256", workflow);
        Assert.Contains("Published as GitHub Release: no", workflow);
        Assert.DoesNotContain("contents: write", workflow);
        Assert.DoesNotContain("gh release", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create-release", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dependabot_CoversActionsNuGetAndPython()
    {
        string dependabot = File.ReadAllText(Path.Combine(RepoRoot, ".github", "dependabot.yml"));

        Assert.Contains("package-ecosystem: github-actions", dependabot);
        Assert.Contains("package-ecosystem: nuget", dependabot);
        Assert.Contains("package-ecosystem: pip", dependabot);
        Assert.Contains("/AirType.LocalAsrWorker", dependabot);
        Assert.DoesNotContain("python-runtime:", dependabot);
    }

    private static string ReadWorkflow(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", name));

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
