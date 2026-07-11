param(
    [Parameter(Mandatory)] [string]$SignedArtifactDirectory,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$ExpectedSourceCommit,
    [string]$ExpectedSourceBranch = "main",
    [string]$OutputDirectory,
    [string]$InstallerInputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "release-archive.ps1")

function Assert-FinalizationCondition {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use stable semantic version format x.y.z."
}
if ($ExpectedSourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Expected source commit must be a full 40-character Git SHA."
}
if ($ExpectedSourceBranch -ne "main") {
    throw "SignPath stable packages must originate from main."
}

$gitStatus = @(git -C $repoRoot status --porcelain)
Assert-FinalizationCondition ($gitStatus.Count -eq 0) `
    "SignPath finalization requires a clean git worktree."
$currentCommit = (git -C $repoRoot rev-parse HEAD).Trim()
Assert-FinalizationCondition `
    ($currentCommit -eq $ExpectedSourceCommit.ToLowerInvariant()) `
    "Checked-out source commit does not match the expected signing commit."

$signedRoot = (Resolve-Path -LiteralPath $SignedArtifactDirectory).Path
$airTypeExe = Join-Path $signedRoot "AirType.exe"
$manifestPath = Join-Path $signedRoot "release-manifest.json"
Assert-FinalizationCondition (Test-Path -LiteralPath $airTypeExe -PathType Leaf) `
    "SignPath output is missing AirType.exe."
Assert-FinalizationCondition (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
    "SignPath output is missing release-manifest.json."

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-FinalizationCondition ($manifest.schemaVersion -eq 1) `
    "SignPath output has an unsupported release manifest schema."
Assert-FinalizationCondition ($manifest.product -eq "AirType") `
    "SignPath output release manifest product is not AirType."
Assert-FinalizationCondition ($manifest.version -eq $Version) `
    "SignPath output release manifest version does not match."
Assert-FinalizationCondition ($manifest.platform -eq "win-x64") `
    "SignPath output release manifest platform does not match."
Assert-FinalizationCondition ($manifest.sourceCommit -eq $ExpectedSourceCommit.ToLowerInvariant()) `
    "SignPath output release manifest commit does not match."
Assert-FinalizationCondition ($manifest.sourceBranch -eq $ExpectedSourceBranch) `
    "SignPath output release manifest branch does not match."
$dirtyProperty = $manifest.PSObject.Properties["dirty"]
Assert-FinalizationCondition ($null -ne $dirtyProperty) `
    "SignPath output release manifest is missing dirty provenance."
Assert-FinalizationCondition ($dirtyProperty.Value -is [bool]) `
    "SignPath output release manifest dirty provenance is not Boolean."
Assert-FinalizationCondition ($dirtyProperty.Value -eq $false) `
    "SignPath output release manifest reports a dirty source tree."

$signature = Get-AuthenticodeSignature -LiteralPath $airTypeExe
Assert-FinalizationCondition `
    ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) `
    "SignPath returned executable does not have a valid Authenticode signature."
Assert-FinalizationCondition ($null -ne $signature.SignerCertificate) `
    "SignPath returned executable has no signer certificate."
$signatureSubject = $signature.SignerCertificate.Subject
Assert-FinalizationCondition `
    ($signatureSubject.IndexOf("SignPath Foundation", [StringComparison]::OrdinalIgnoreCase) -ge 0) `
    "Signed executable publisher is not SignPath Foundation."

$manifest.signed = $true
$manifest.signatureSubject = $signatureSubject
$manifest.executableSha256 = `
    (Get-FileHash -LiteralPath $airTypeExe -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release-artifacts\app\signed"
}
if ([string]::IsNullOrWhiteSpace($InstallerInputDirectory)) {
    $InstallerInputDirectory = Join-Path $repoRoot "release-artifacts\app\signpath-installer-input"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$installerInputRoot = [IO.Path]::GetFullPath($InstallerInputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot, $installerInputRoot | Out-Null
$zipPath = Join-Path $outputRoot "AirType-$Version-win-x64.zip"
$checksumPath = Join-Path $outputRoot "AirType-$Version-win-x64.sha256"

New-AirTypeDeterministicZip -SourceDirectory $signedRoot -DestinationPath $zipPath
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zipPath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ASCII

& powershell `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "verify-release-package.ps1") `
    -PackagePath $zipPath
if ($LASTEXITCODE -ne 0) {
    throw "Final SignPath release package verification failed."
}

$installerInputPath = Join-Path $installerInputRoot "AirType-$Version-win-x64-signing-input.msi"
& powershell `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "build-windows-installer.ps1") `
    -PayloadDirectory $signedRoot `
    -Version $Version `
    -OutputPath $installerInputPath
if ($LASTEXITCODE -ne 0) {
    throw "SignPath installer signing input build failed."
}

Write-Host "SignPath release package finalized:"
Write-Host "  Version:   $Version"
Write-Host "  Commit:    $ExpectedSourceCommit"
Write-Host "  Publisher: $signatureSubject"
Write-Host "  Package:   $zipPath"
Write-Host "  SHA-256:   $checksumPath"
Write-Host "  MSI input: $installerInputPath"
