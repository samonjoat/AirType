param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$reportRoot = Join-Path $repoRoot ".temp\dependency-audit"
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".temp")).TrimEnd('\')
$fullReportRoot = [IO.Path]::GetFullPath($reportRoot)
if (!$fullReportRoot.StartsWith($allowedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Dependency audit output escaped .temp."
}
if (Test-Path -LiteralPath $fullReportRoot) {
    Remove-Item -LiteralPath $fullReportRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $fullReportRoot | Out-Null

$projectPath = Join-Path $repoRoot "AirType\AirType.csproj"
$requirementsPath = Join-Path $repoRoot "AirType.LocalAsrWorker\requirements-runtime.txt"
$pyprojectPath = Join-Path $repoRoot "AirType.LocalAsrWorker\pyproject.toml"
$nugetReportPath = Join-Path $fullReportRoot "nuget-vulnerabilities.json"
$pythonReportPath = Join-Path $fullReportRoot "python-vulnerabilities.json"
$pythonAuditRequirementsPath = Join-Path $fullReportRoot "python-all-requirements.txt"

& dotnet restore $projectPath --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore failed before the vulnerability audit."
}

$nugetOutput = @(& dotnet list $projectPath package --vulnerable --include-transitive --format json)
if ($LASTEXITCODE -ne 0) {
    if ($nugetOutput.Count -gt 0) {
        ($nugetOutput -join "`n") | Write-Error
    }
    throw "NuGet vulnerability audit command failed."
}
$nugetJson = $nugetOutput -join "`n"
[IO.File]::WriteAllText($nugetReportPath, $nugetJson, [Text.UTF8Encoding]::new($false))
$nugetReport = $nugetJson | ConvertFrom-Json
$nugetVulnerabilities = New-Object System.Collections.Generic.List[object]

function Find-NuGetVulnerabilities {
    param($Node)

    if ($null -eq $Node -or $Node -is [string]) {
        return
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [pscustomobject]) {
        foreach ($item in $Node) {
            Find-NuGetVulnerabilities -Node $item
        }
        return
    }
    if ($Node -is [pscustomobject]) {
        foreach ($property in $Node.PSObject.Properties) {
            if ($property.Name -eq "vulnerabilities") {
                foreach ($vulnerability in @($property.Value)) {
                    $nugetVulnerabilities.Add($vulnerability)
                }
            }
            else {
                Find-NuGetVulnerabilities -Node $property.Value
            }
        }
    }
}

Find-NuGetVulnerabilities -Node $nugetReport
if ($nugetVulnerabilities.Count -gt 0) {
    $nugetVulnerabilities | ConvertTo-Json -Depth 8 | Write-Error
    throw "NuGet dependency audit found $($nugetVulnerabilities.Count) vulnerability record(s)."
}

$exportAuditRequirements = @'
import pathlib
import sys
import tomllib

pyproject_path = pathlib.Path(sys.argv[1])
runtime_lock_path = pathlib.Path(sys.argv[2])
output_path = pathlib.Path(sys.argv[3])

configuration = tomllib.loads(pyproject_path.read_text(encoding="utf-8"))
requirements = [
    line.strip()
    for line in runtime_lock_path.read_text(encoding="utf-8").splitlines()
    if line.strip() and not line.lstrip().startswith("#")
]

def append_group(values, source):
    if not isinstance(values, list) or not all(isinstance(value, str) for value in values):
        raise TypeError(f"{source} must be a list of dependency strings")
    requirements.extend(values)

append_group(configuration.get("build-system", {}).get("requires", []), "build-system.requires")
project = configuration.get("project", {})
append_group(project.get("dependencies", []), "project.dependencies")
optional_dependencies = project.get("optional-dependencies", {})
if not isinstance(optional_dependencies, dict):
    raise TypeError("project.optional-dependencies must be a table")
for group_name, group_requirements in optional_dependencies.items():
    append_group(group_requirements, f"project.optional-dependencies.{group_name}")

unique_requirements = sorted(set(requirements), key=str.casefold)
output_path.write_text("\n".join(unique_requirements) + "\n", encoding="utf-8", newline="\n")
'@

& $Python -c $exportAuditRequirements $pyprojectPath $requirementsPath $pythonAuditRequirementsPath
if ($LASTEXITCODE -ne 0) {
    throw "Failed to construct the comprehensive Python dependency audit input."
}

& $Python -m pip_audit `
    --requirement $pythonAuditRequirementsPath `
    --strict `
    --progress-spinner off `
    --format json `
    --output $pythonReportPath
if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $pythonReportPath) {
        Get-Content -LiteralPath $pythonReportPath -Raw | Write-Error
    }
    throw "Python dependency audit found vulnerabilities or dependency errors."
}

Write-Host "Dependency audits passed."
Write-Host "  NuGet report: $nugetReportPath"
Write-Host "  Python input: $pythonAuditRequirementsPath"
Write-Host "  Python report: $pythonReportPath"
