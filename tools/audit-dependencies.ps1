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
$nugetReportPath = Join-Path $fullReportRoot "nuget-vulnerabilities.json"
$pythonReportPath = Join-Path $fullReportRoot "python-vulnerabilities.json"

$nugetOutput = @(& dotnet list $projectPath package --vulnerable --include-transitive --format json)
if ($LASTEXITCODE -ne 0) {
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

& $Python -m pip_audit `
    --requirement $requirementsPath `
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
Write-Host "  Python report: $pythonReportPath"
