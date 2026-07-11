param(
    [Parameter(Mandatory)] [string]$SignedArtifactDirectory,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$ExpectedSourceCommit,
    [string]$ExpectedSourceBranch = "main",
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-InstallerFinalizationCondition {
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
    throw "SignPath stable installers must originate from main."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$gitStatus = @(git -C $repoRoot status --porcelain)
Assert-InstallerFinalizationCondition ($gitStatus.Count -eq 0) `
    "SignPath installer finalization requires a clean git worktree."
$currentCommit = (git -C $repoRoot rev-parse HEAD).Trim()
Assert-InstallerFinalizationCondition `
    ($currentCommit -eq $ExpectedSourceCommit.ToLowerInvariant()) `
    "Checked-out source commit does not match the expected installer signing commit."

$returnedRoot = (Resolve-Path -LiteralPath $SignedArtifactDirectory).Path
$returnedPackages = @(Get-ChildItem -LiteralPath $returnedRoot -Recurse -File -Filter "*.msi")
Assert-InstallerFinalizationCondition ($returnedPackages.Count -eq 1) `
    "SignPath installer output must contain exactly one MSI."
$returnedPackage = $returnedPackages[0].FullName
$signature = Get-AuthenticodeSignature -LiteralPath $returnedPackage
Assert-InstallerFinalizationCondition `
    ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) `
    "SignPath returned installer does not have a valid Authenticode signature."
Assert-InstallerFinalizationCondition ($null -ne $signature.SignerCertificate) `
    "SignPath returned installer has no signer certificate."
$signatureSubject = $signature.SignerCertificate.Subject
Assert-InstallerFinalizationCondition `
    ($signatureSubject.IndexOf("SignPath Foundation", [StringComparison]::OrdinalIgnoreCase) -ge 0) `
    "Signed installer publisher is not SignPath Foundation."

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release-artifacts\app\signed"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$packagePath = Join-Path $outputRoot "AirType-$Version-win-x64.msi"
$checksumPath = "$packagePath.sha256"
Copy-Item -LiteralPath $returnedPackage -Destination $packagePath -Force
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($packagePath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ASCII

& powershell `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "verify-windows-installer.ps1") `
    -PackagePath $packagePath `
    -ExpectedVersion $Version `
    -ExpectedInstallerSignatureStatus Valid `
    -ExpectedPayloadSignatureStatus Valid `
    -ExpectedSourceCommit $ExpectedSourceCommit `
    -ExpectedSourceBranch $ExpectedSourceBranch
if ($LASTEXITCODE -ne 0) {
    throw "Final SignPath Windows installer verification failed."
}

Write-Host "SignPath Windows installer finalized:"
Write-Host "  Version:   $Version"
Write-Host "  Commit:    $ExpectedSourceCommit"
Write-Host "  Publisher: $signatureSubject"
Write-Host "  Package:   $packagePath"
Write-Host "  SHA-256:   $checksumPath"
