param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd"),
    [string]$OutputDirectory,
    [string]$Platform = "win-x64",
    [int]$MaxBundleSizeMB = 1536,
    [string]$BaseReleaseUrl,
    [switch]$PrepareRuntime,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workerRoot = Join-Path $repoRoot "AirType.LocalAsrWorker"
$runtimeRoot = Join-Path $workerRoot "runtime"
$runtimePython = Join-Path $runtimeRoot "python.exe"
$runtimeDll = Join-Path $runtimeRoot "python312.dll"
$workerPackageRoot = Join-Path $workerRoot "airtype_asr_worker"
$modelRoot = Join-Path $workerRoot "models\ct2\small.en"
$legacyModelRoot = Join-Path $workerRoot "models\small.en"
$bundleId = "airtype-local-asr-small-en-ct2-$Platform"
$tagName = "local-asr-small-en-ct2-v$Version"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use stable semantic version format x.y.z."
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

$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
$sourceDate = (git -C $repoRoot show -s --format=%cI HEAD).Trim()
$sourceDateUtc = [DateTimeOffset]::Parse($sourceDate).ToUniversalTime().ToString("o")
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Unable to resolve Local ASR source commit provenance."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release-artifacts\local-asr"
}

if ([string]::IsNullOrWhiteSpace($BaseReleaseUrl)) {
    $BaseReleaseUrl = "https://github.com/samonjoat/AirType/releases/download/$tagName"
}

if ($PrepareRuntime) {
    & (Join-Path $PSScriptRoot "prepare-local-asr-runtime.ps1") -Models @("small.en")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to prepare local ASR runtime."
    }
}

if (!(Test-Path $runtimePython) -or !(Test-Path $runtimeDll)) {
    throw "Portable Local ASR Python runtime is missing. Run tools\prepare-local-asr-runtime.ps1 first."
}

$vcRuntimeLockPath = Join-Path $PSScriptRoot "local-asr-vc-runtime.lock.json"
$vcRuntimeMetadataPath = Join-Path $runtimeRoot "visual-cpp-runtime.json"
if (!(Test-Path -LiteralPath $vcRuntimeLockPath -PathType Leaf) -or
    !(Test-Path -LiteralPath $vcRuntimeMetadataPath -PathType Leaf)) {
    throw "Packaging refused a Local ASR runtime without pinned Visual C++ metadata."
}
$vcRuntimeLock = Get-Content -LiteralPath $vcRuntimeLockPath -Raw | ConvertFrom-Json
$vcRuntimeMetadata = Get-Content -LiteralPath $vcRuntimeMetadataPath -Raw | ConvertFrom-Json
if (($vcRuntimeLock | ConvertTo-Json -Depth 8 -Compress) -ne
    ($vcRuntimeMetadata | ConvertTo-Json -Depth 8 -Compress)) {
    throw "Packaging refused Visual C++ runtime metadata that differs from its lock."
}
foreach ($file in $vcRuntimeLock.files) {
    $runtimeFile = Join-Path $runtimeRoot $file.targetName
    if (!(Test-Path -LiteralPath $runtimeFile -PathType Leaf) -or
        (Get-FileHash -LiteralPath $runtimeFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Packaging refused an incomplete Visual C++ runtime: $($file.targetName)"
    }
}

if (Test-Path (Join-Path $runtimeRoot "pyvenv.cfg")) {
    throw "Packaging refused a host-bound Python virtual environment."
}

if (!(Test-Path (Join-Path $workerPackageRoot "__main__.py"))) {
    throw "Local ASR worker package is missing."
}

if (!(Test-Path (Join-Path $modelRoot "model.bin"))) {
    if (Test-Path (Join-Path $legacyModelRoot "model.bin")) {
        $modelRoot = $legacyModelRoot
    }
    else {
        throw "CT2 small.en model is missing. Run tools\prepare-local-asr-runtime.ps1 first."
    }
}

foreach ($requiredModelFile in @("config.json", "model.bin", "tokenizer.json")) {
    if (!(Test-Path (Join-Path $modelRoot $requiredModelFile))) {
        throw "CT2 small.en model is incomplete. Missing $requiredModelFile."
    }
}

$zipName = "$bundleId-v$Version.zip"
$metadataName = "$bundleId-v$Version.json"
$checksumName = "$bundleId-v$Version.sha256"
$stagingRoot = Join-Path $OutputDirectory "staging\$bundleId-v$Version"
$zipPath = Join-Path $OutputDirectory $zipName
$metadataPath = Join-Path $OutputDirectory $metadataName
$checksumPath = Join-Path $OutputDirectory $checksumName

if ((Test-Path $zipPath) -and !$Force) {
    throw "Bundle already exists: $zipPath. Pass -Force to replace it."
}

if (Test-Path $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$stagedWorkerRoot = Join-Path $stagingRoot "worker"
New-Item -ItemType Directory -Force -Path $stagedWorkerRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $workerRoot "pyproject.toml") -Destination (Join-Path $stagedWorkerRoot "pyproject.toml") -Force
Copy-Item -LiteralPath (Join-Path $workerRoot "requirements-runtime.txt") -Destination (Join-Path $stagedWorkerRoot "requirements-runtime.txt") -Force
Copy-Item -LiteralPath $workerPackageRoot -Destination (Join-Path $stagedWorkerRoot "airtype_asr_worker") -Recurse -Force

Copy-Item -LiteralPath $runtimeRoot -Destination (Join-Path $stagingRoot "runtime") -Recurse -Force

$stagedModelRoot = Join-Path $stagingRoot "models\ct2\small.en"
New-Item -ItemType Directory -Force -Path (Split-Path $stagedModelRoot -Parent) | Out-Null
Copy-Item -LiteralPath $modelRoot -Destination $stagedModelRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $stagingRoot "LICENSE") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $stagingRoot "THIRD_PARTY_NOTICES.md") -Force

& (Join-Path $PSScriptRoot "collect-third-party-licenses.ps1") `
    -PythonRuntimeRoot (Join-Path $stagingRoot "runtime") `
    -RuntimeRequirementsPath (Join-Path $stagedWorkerRoot "requirements-runtime.txt") `
    -OutputDirectory (Join-Path $stagingRoot "THIRD_PARTY_LICENSES") `
    -LocalAsrOnly
if ($LASTEXITCODE -ne 0) {
    throw "Local ASR third-party license collection failed."
}

Get-ChildItem -LiteralPath $stagingRoot -Directory -Recurse -Force |
    Where-Object {
        $_.Name -in @("__pycache__", ".pytest_cache", ".cache", "base.en") -or
        $_.FullName.EndsWith("airtype_asr_worker.egg-info", [StringComparison]::OrdinalIgnoreCase)
    } |
    Sort-Object FullName -Descending |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

if (Test-Path (Join-Path $stagingRoot "models\base.en")) {
    throw "Packaging refused to include retired base.en model artifacts."
}
foreach ($requiredNotice in @("LICENSE", "THIRD_PARTY_NOTICES.md", "THIRD_PARTY_LICENSES\manifest.json")) {
    if (!(Test-Path (Join-Path $stagingRoot $requiredNotice))) {
        throw "Local ASR bundle is missing required notice: $requiredNotice"
    }
}

$stagedRuntimeRoot = Join-Path $stagingRoot "runtime"
if (!(Test-Path (Join-Path $stagedRuntimeRoot "python.exe")) -or
    !(Test-Path (Join-Path $stagedRuntimeRoot "python312.dll")) -or
    !(Test-Path (Join-Path $stagedRuntimeRoot "av.py")) -or
    (Test-Path (Join-Path $stagedRuntimeRoot "pyvenv.cfg"))) {
    throw "Packaging refused an incomplete or host-bound Python runtime."
}
if ((Test-Path (Join-Path $stagedRuntimeRoot "Lib\site-packages\av")) -or
    (Test-Path (Join-Path $stagedRuntimeRoot "Lib\site-packages\av.libs")) -or
    (Test-Path (Join-Path $stagedRuntimeRoot "Lib\site-packages\bin\pyav.exe")) -or
    @(Get-ChildItem -LiteralPath (Join-Path $stagedRuntimeRoot "Lib\site-packages") -Directory -Filter "av-*.dist-info").Count -gt 0) {
    throw "Packaging refused the unused PyAV/FFmpeg codec stack."
}

$allStagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -Recurse -File)
$textFiles = @($allStagedFiles | Where-Object { $_.Extension -in @(".cfg", ".json", ".md", ".pth", ".py", ".txt", ".toml") })
$projectPathPatterns = @([Regex]::Escape($repoRoot), "my_builds", "dictation-ui-modernization")
$leakedPath = $textFiles |
    Select-String -Pattern $projectPathPatterns -List |
    Select-Object -First 1
if (!$leakedPath) {
    $sitePackagesRoot = [IO.Path]::GetFullPath((Join-Path $stagedRuntimeRoot "Lib\site-packages")).TrimEnd('\')
    $workerPackageRoot = Join-Path $sitePackagesRoot "airtype_asr_worker"
    $firstPartyTextFiles = @($textFiles | Where-Object {
        !$_.FullName.StartsWith($sitePackagesRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $_.FullName.StartsWith($workerPackageRoot + '\', [StringComparison]::OrdinalIgnoreCase)
    })
    $hostBindingFiles = $firstPartyTextFiles | Where-Object { $_.Extension -in @(".cfg", ".json", ".pth", ".py", ".toml") }
    $hostBindingPattern = '(?i)([A-Z]:[\\/]Users[\\/][^\\/\r\n]+[\\/]|/home/[^/\r\n]+/)'
    $leakedPath = $hostBindingFiles |
        Select-String -Pattern $hostBindingPattern -List |
        Select-Object -First 1
}
if ($leakedPath) {
    throw "Packaging refused developer path leakage in $($leakedPath.Path)."
}

$previousDontWriteBytecode = $env:PYTHONDONTWRITEBYTECODE
$env:PYTHONDONTWRITEBYTECODE = "1"
try {
    & (Join-Path $stagedRuntimeRoot "python.exe") -c "import av; assert av.AIRTYPE_PCM_ONLY_SHIM; import airtype_asr_worker, faster_whisper, ctranslate2"
    if ($LASTEXITCODE -ne 0) {
        throw "Relocated Local ASR runtime smoke test failed."
    }
}
finally {
    if ($null -eq $previousDontWriteBytecode) {
        Remove-Item Env:PYTHONDONTWRITEBYTECODE -ErrorAction SilentlyContinue
    }
    else {
        $env:PYTHONDONTWRITEBYTECODE = $previousDontWriteBytecode
    }
}

$uncompressedBytes = (Get-ChildItem -LiteralPath $stagingRoot -Recurse -File | Measure-Object Length -Sum).Sum
$createdUtc = $sourceDateUtc

$embeddedManifest = [ordered]@{
    schemaVersion = 1
    bundleId = $bundleId
    bundleVersion = $Version
    engineId = "airtype-local-asr"
    modelId = "faster-whisper-small-en-int8"
    modelName = "small.en"
    runtimeId = "ct2-python"
    platform = $Platform
    architecture = "x64"
    zipFileName = $zipName
    downloadUrl = "$BaseReleaseUrl/$zipName"
    sha256 = $null
    compressedBytes = $null
    uncompressedBytes = [Int64]$uncompressedBytes
    createdUtc = $createdUtc
    sourceCommit = $sourceCommit
    sourceDateUtc = $sourceDateUtc
}

$embeddedManifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $stagingRoot "airtype-local-asr-bundle.json") -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-DeterministicZip -SourceDirectory $stagingRoot -DestinationPath $zipPath

$compressedBytes = (Get-Item -LiteralPath $zipPath).Length
$compressedMB = [math]::Round($compressedBytes / 1MB, 2)
if ($compressedMB -gt $MaxBundleSizeMB) {
    Remove-Item -LiteralPath $zipPath -Force
    throw "Bundle size $compressedMB MB exceeds limit $MaxBundleSizeMB MB."
}

$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$sidecarManifest = [ordered]@{
    schemaVersion = 1
    bundleId = $bundleId
    bundleVersion = $Version
    engineId = "airtype-local-asr"
    modelId = "faster-whisper-small-en-int8"
    modelName = "small.en"
    runtimeId = "ct2-python"
    platform = $Platform
    architecture = "x64"
    zipFileName = $zipName
    downloadUrl = "$BaseReleaseUrl/$zipName"
    sha256 = $sha256
    compressedBytes = [Int64]$compressedBytes
    uncompressedBytes = [Int64]$uncompressedBytes
    createdUtc = $createdUtc
    sourceCommit = $sourceCommit
    sourceDateUtc = $sourceDateUtc
}

$sidecarManifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $metadataPath -Encoding UTF8

"$sha256  $zipName" | Set-Content -LiteralPath $checksumPath -Encoding ASCII

& (Join-Path $PSScriptRoot "verify-local-asr-bundle.ps1") `
    -ManifestPath $metadataPath `
    -ExpectedBaseReleaseUrl $BaseReleaseUrl

Write-Host "Local ASR release bundle created:"
Write-Host "  Tag:      $tagName"
Write-Host "  Zip:      $zipPath"
Write-Host "  Metadata: $metadataPath"
Write-Host "  SHA256:   $checksumPath"
Write-Host "  Size:     $compressedMB MB compressed"
