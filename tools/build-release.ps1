param(
    [string]$Version = "1.0.0",
    [string]$OutputDirectory,
    [switch]$PublicRelease,
    [string]$CertificateThumbprint = $env:AIRTYPE_SIGNING_CERTIFICATE_THUMBPRINT,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipRuntimePreparation
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "AirType\AirType.csproj"
$publishProfile = "win-x64-self-contained"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use stable semantic version format x.y.z."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release-artifacts\app"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path $outputRoot "staging\AirType-$Version-win-x64"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Remove-SafeDirectory {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedRoot
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullAllowedRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if (!$fullPath.StartsWith($fullAllowedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside release output: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Get-PortableRelativePath {
    param(
        [Parameter(Mandatory)] [string]$BaseDirectory,
        [Parameter(Mandatory)] [string]$Path
    )

    $basePath = [IO.Path]::GetFullPath($BaseDirectory).TrimEnd('\') + '\'
    $targetPath = [IO.Path]::GetFullPath($Path)
    $baseUri = [Uri]::new($basePath)
    $targetUri = [Uri]::new($targetPath)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Resolve-SignTool {
    $windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (!(Test-Path -LiteralPath $windowsKitsRoot)) {
        throw "Windows SDK SignTool was not found."
    }

    $signTool = Get-ChildItem -LiteralPath $windowsKitsRoot -Directory |
        Sort-Object { try { [Version]$_.Name } catch { [Version]'0.0' } } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($signTool)) {
        throw "Windows SDK x64 SignTool was not found."
    }

    return $signTool
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory)] [string]$SourceDirectory,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    $sourceRoot = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
    $stream = [IO.File]::Open($DestinationPath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
                Sort-Object FullName |
                ForEach-Object {
                    $relativePath = (Get-PortableRelativePath -BaseDirectory $sourceRoot -Path $_.FullName).Replace('\', '/')
                    $entry = $archive.CreateEntry($relativePath, [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $fixedTimestamp
                    $entryStream = $entry.Open()
                    try {
                        $fileStream = [IO.File]::OpenRead($_.FullName)
                        try {
                            $fileStream.CopyTo($entryStream)
                        }
                        finally {
                            $fileStream.Dispose()
                        }
                    }
                    finally {
                        $entryStream.Dispose()
                    }
                }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$gitStatus = @(git -C $repoRoot status --porcelain)
$branch = (git -C $repoRoot branch --show-current).Trim()
$commit = (git -C $repoRoot rev-parse HEAD).Trim()
$sourceDateUtc = (git -C $repoRoot show -s --format=%cI HEAD).Trim()

if ($PublicRelease) {
    if ($gitStatus.Count -ne 0) {
        throw "Public release requires a clean git worktree."
    }
    if ($branch -ne "main") {
        throw "Public release must be built from main."
    }
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "Public release requires AIRTYPE_SIGNING_CERTIFICATE_THUMBPRINT or -CertificateThumbprint."
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
Remove-SafeDirectory -Path $stagingRoot -AllowedRoot $outputRoot
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

if (!$SkipRuntimePreparation) {
    Invoke-Checked `
        -FilePath "powershell" `
        -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "prepare-local-asr-runtime.ps1"), "-SkipModel") `
        -FailureMessage "Portable Local ASR runtime preparation failed."
}

$fourPartVersion = "$Version.0"
Invoke-Checked `
    -FilePath "dotnet" `
    -Arguments @(
        "publish", $projectPath,
        "--configuration", "Release",
        "/p:PublishProfile=$publishProfile",
        "/p:PublishDir=$stagingRoot\",
        "/p:Version=$Version",
        "/p:AssemblyVersion=$fourPartVersion",
        "/p:FileVersion=$fourPartVersion",
        "/p:InformationalVersion=$Version"
    ) `
    -FailureMessage "AirType self-contained publish failed."

$airTypeExe = Join-Path $stagingRoot "AirType.exe"
if (!(Test-Path -LiteralPath $airTypeExe)) {
    throw "Published AirType.exe is missing."
}

Invoke-Checked `
    -FilePath "powershell" `
    -Arguments @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $PSScriptRoot "collect-third-party-licenses.ps1"),
        "-ProjectAssetsPath", (Join-Path $repoRoot "AirType\obj\project.assets.json"),
        "-PythonRuntimeRoot", (Join-Path $stagingRoot "LocalAsrWorker\runtime"),
        "-RuntimeRequirementsPath", (Join-Path $stagingRoot "LocalAsrWorker\requirements-runtime.txt"),
        "-PublishedAppRoot", $stagingRoot,
        "-OutputDirectory", (Join-Path $stagingRoot "THIRD_PARTY_LICENSES")
    ) `
    -FailureMessage "Third-party license collection failed."

$signed = $false
$signatureSubject = $null
if (![string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $thumbprint = $CertificateThumbprint.Replace(" ", "")
    $signTool = Resolve-SignTool
    Invoke-Checked `
        -FilePath $signTool `
        -Arguments @("sign", "/sha1", $thumbprint, "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256", $airTypeExe) `
        -FailureMessage "Authenticode signing failed."

    $signature = Get-AuthenticodeSignature -LiteralPath $airTypeExe
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "AirType.exe signature verification failed: $($signature.StatusMessage)"
    }
    $signed = $true
    $signatureSubject = $signature.SignerCertificate.Subject
}

if ($PublicRelease -and !$signed) {
    throw "Public release cannot continue with an unsigned executable."
}

$exeHash = (Get-FileHash -LiteralPath $airTypeExe -Algorithm SHA256).Hash.ToLowerInvariant()
$releaseManifest = [ordered]@{
    schemaVersion = 1
    product = "AirType"
    version = $Version
    platform = "win-x64"
    sourceCommit = $commit
    sourceBranch = $branch
    dirty = $gitStatus.Count -ne 0
    sourceDateUtc = $sourceDateUtc
    signed = $signed
    signatureSubject = $signatureSubject
    executableSha256 = $exeHash
}
$releaseManifest |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $stagingRoot "release-manifest.json") -Encoding UTF8

$artifactStem = "AirType-$Version-win-x64" + $(if ($signed) { "" } else { "-unsigned" })
$zipPath = Join-Path $outputRoot "$artifactStem.zip"
$checksumPath = Join-Path $outputRoot "$artifactStem.sha256"
New-DeterministicZip -SourceDirectory $stagingRoot -DestinationPath $zipPath

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zipPath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ASCII

$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if (!$archive.Entries.Where({ $_.FullName -eq "AirType.exe" })) {
        throw "Release archive verification failed: AirType.exe is missing."
    }
}
finally {
    $archive.Dispose()
}

Remove-SafeDirectory -Path $stagingRoot -AllowedRoot $outputRoot

$verificationArguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "verify-release-package.ps1"),
    "-PackagePath", $zipPath
)
if (!$signed) {
    $verificationArguments += "-AllowUnsigned"
}
Invoke-Checked `
    -FilePath "powershell" `
    -Arguments $verificationArguments `
    -FailureMessage "Release package verification failed."

Write-Host "AirType release package created:"
Write-Host "  Version:  $Version"
Write-Host "  Commit:   $commit"
Write-Host "  Signed:   $signed"
Write-Host "  Package:  $zipPath"
Write-Host "  SHA-256:  $checksumPath"
