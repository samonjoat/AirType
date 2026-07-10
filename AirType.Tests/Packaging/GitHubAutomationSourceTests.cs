using System.Text.RegularExpressions;
using Xunit;

namespace AirType.Tests.Packaging;

public sealed class GitHubAutomationSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Workflows_UseReadOnlyPermissionsAndPinnedActions()
    {
        string workflowRoot = Path.Combine(RepoRoot, ".github", "workflows");
        string[] workflows = Directory.GetFiles(workflowRoot, "*.yml");

        Assert.Equal(3, workflows.Length);
        foreach (string workflowPath in workflows)
        {
            string workflow = File.ReadAllText(workflowPath);
            Assert.Contains("permissions:\n  contents: read", Normalize(workflow));
            Assert.DoesNotContain("pull_request_target", workflow);
            Assert.DoesNotContain("contents: write", workflow);

            foreach (Match match in Regex.Matches(workflow, @"uses:\s+[^\s@]+@([^\s#]+)"))
            {
                Assert.Matches("^[0-9a-f]{40}$", match.Groups[1].Value);
            }
        }
    }

    [Fact]
    public void CiWorkflow_RunsBuildTestsDcoSoundsAndSnapshot()
    {
        string workflow = ReadWorkflow("ci.yml");

        Assert.Contains("dotnet clean .\\AirType.sln", workflow);
        Assert.Contains("dotnet build .\\AirType.sln", workflow);
        Assert.Contains("dotnet test .\\AirType.Tests", workflow);
        Assert.Contains("verify-dco.ps1", workflow);
        Assert.Contains("--verify-production", workflow);
        Assert.Contains("build-public-snapshot.ps1", workflow);
        Assert.Contains("python -m pytest", workflow);
    }

    [Fact]
    public void SecurityWorkflow_RunsPinnedAuditsAndGitleaks()
    {
        string workflow = ReadWorkflow("security.yml");
        string auditScript = File.ReadAllText(Path.Combine(RepoRoot, "tools", "audit-dependencies.ps1"));

        Assert.Contains("pip-audit==2.10.1", workflow);
        Assert.Contains("audit-dependencies.ps1", workflow);
        Assert.Contains("gitleaks/gitleaks-action@", workflow);
        Assert.Contains("--vulnerable --include-transitive --format json", auditScript);
        Assert.Contains("python-vulnerabilities.json", auditScript);
    }

    [Fact]
    public void ReleaseCandidateWorkflow_CannotPublishARelease()
    {
        string workflow = ReadWorkflow("release-candidate.yml");

        Assert.Contains("workflow_dispatch", workflow);
        Assert.Contains("build-release.ps1", workflow);
        Assert.Contains("-unsigned", workflow);
        Assert.Contains("actions/upload-artifact@", workflow);
        Assert.Contains("Published as GitHub Release: no", workflow);
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
    }

    private static string ReadWorkflow(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", name));

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
