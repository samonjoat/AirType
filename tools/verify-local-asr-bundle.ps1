param(
    [Parameter(Mandatory)] [string]$ManifestPath,
    [string]$ExpectedBaseReleaseUrl,
    [switch]$KeepExtraction
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Equal {
    param(
        [Parameter(Mandatory)] $Actual,
        [Parameter(Mandatory)] $Expected,
        [Parameter(Mandatory)] [string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', found '$Actual'."
    }
}

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$RelativePath
    )

    if (!(Test-Path -LiteralPath (Join-Path $Root $RelativePath) -PathType Leaf)) {
        throw "Local ASR bundle is missing required file: $RelativePath"
    }
}

$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$artifactRoot = Split-Path -Parent $resolvedManifestPath
$vcRuntimeLockPath = Join-Path $PSScriptRoot "local-asr-vc-runtime.lock.json"
$manifestJson = Get-Content -LiteralPath $resolvedManifestPath -Raw
$manifest = $manifestJson | ConvertFrom-Json
$manifestDocument = [Text.Json.JsonDocument]::Parse($manifestJson)
try {
    $sourceDateUtcText = $manifestDocument.RootElement.GetProperty("sourceDateUtc").GetString()
    $createdUtcText = $manifestDocument.RootElement.GetProperty("createdUtc").GetString()
}
finally {
    $manifestDocument.Dispose()
}

Assert-Equal $manifest.schemaVersion 1 "Unsupported Local ASR manifest schema."
Assert-Equal $manifest.bundleId "airtype-local-asr-small-en-ct2-win-x64" "Unexpected Local ASR bundle ID."
Assert-Equal $manifest.engineId "airtype-local-asr" "Unexpected Local ASR engine ID."
Assert-Equal $manifest.modelId "faster-whisper-small-en-int8" "Unexpected Local ASR model ID."
Assert-Equal $manifest.modelName "small.en" "Unexpected Local ASR model name."
Assert-Equal $manifest.runtimeId "ct2-python" "Unexpected Local ASR runtime ID."
Assert-Equal $manifest.platform "win-x64" "Unexpected Local ASR platform."
Assert-Equal $manifest.architecture "x64" "Unexpected Local ASR architecture."

$version = [string]$manifest.bundleVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Local ASR bundle version must use stable semantic version format x.y.z."
}
$expectedStem = "airtype-local-asr-small-en-ct2-win-x64-v$version"
$expectedZipName = "$expectedStem.zip"
$expectedManifestName = "$expectedStem.json"
$expectedChecksumName = "$expectedStem.sha256"
Assert-Equal (Split-Path -Leaf $resolvedManifestPath) $expectedManifestName "Unexpected Local ASR manifest filename."
Assert-Equal $manifest.zipFileName $expectedZipName "Unexpected Local ASR ZIP filename."

if ([string]::IsNullOrWhiteSpace($ExpectedBaseReleaseUrl)) {
    $ExpectedBaseReleaseUrl = "https://github.com/samonjoat/AirType/releases/download/local-asr-small-en-ct2-v$version"
}
$ExpectedBaseReleaseUrl = $ExpectedBaseReleaseUrl.TrimEnd('/')
Assert-Equal $manifest.downloadUrl "$ExpectedBaseReleaseUrl/$expectedZipName" "Unexpected Local ASR download URL."

if ([string]$manifest.sha256 -notmatch '^[0-9a-f]{64}$') {
    throw "Local ASR manifest SHA-256 must be 64 lowercase hexadecimal characters."
}
if ([string]$manifest.sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Local ASR manifest source commit must be a full Git SHA."
}
$parsedSourceDate = [DateTimeOffset]::MinValue
if (![DateTimeOffset]::TryParse($sourceDateUtcText, [ref]$parsedSourceDate)) {
    throw "Local ASR manifest source date is invalid."
}
if ($parsedSourceDate.Offset -ne [TimeSpan]::Zero) {
    throw "Local ASR manifest source date must be normalized to UTC."
}
Assert-Equal $createdUtcText $sourceDateUtcText "Local ASR creation date must be derived from the source commit."

$zipPath = Join-Path $artifactRoot $expectedZipName
$checksumPath = Join-Path $artifactRoot $expectedChecksumName
Assert-RequiredFile -Root $artifactRoot -RelativePath $expectedZipName
Assert-RequiredFile -Root $artifactRoot -RelativePath $expectedChecksumName

$actualSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-Equal $actualSha256 $manifest.sha256 "Local ASR bundle checksum does not match its manifest."
$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
Assert-Equal $checksumLine "$actualSha256  $expectedZipName" "Local ASR bundle checksum sidecar is invalid."
Assert-Equal (Get-Item -LiteralPath $zipPath).Length ([Int64]$manifest.compressedBytes) "Local ASR compressed size does not match its manifest."

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $seenEntries = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $normalized = $entry.FullName.Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($normalized) -or
            $normalized -match '^[\\/]' -or
            $normalized -match '^[A-Za-z]:' -or
            $normalized -match '(^|/)\.\.(/|$)') {
            throw "Local ASR archive contains an unsafe path: $($entry.FullName)"
        }
        if (!$seenEntries.Add($normalized)) {
            throw "Local ASR archive contains a duplicate path: $($entry.FullName)"
        }
        $expectedTimestamp = [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)
        if ($entry.LastWriteTime.DateTime -ne $expectedTimestamp) {
            throw "Local ASR archive contains a non-deterministic timestamp: $($entry.FullName)"
        }
    }
}
finally {
    $archive.Dispose()
}

$extractionRoot = Join-Path ([IO.Path]::GetTempPath()) "AirTypeLocalAsrVerify_$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $extractionRoot | Out-Null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $extractionRoot)

    $requiredFiles = @(
        "airtype-local-asr-bundle.json",
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "THIRD_PARTY_LICENSES\manifest.json",
        "worker\pyproject.toml",
        "worker\requirements-runtime.txt",
        "worker\airtype_asr_worker\__main__.py",
        "runtime\python.exe",
        "runtime\python312.dll",
        "runtime\av.py",
        "runtime\visual-cpp-runtime.json",
        "runtime\msvcp140.dll",
        "runtime\msvcp140_1.dll",
        "runtime\licenses\Microsoft-Visual-Cpp-Runtime\license.rtf",
        "models\ct2\small.en\config.json",
        "models\ct2\small.en\model.bin",
        "models\ct2\small.en\tokenizer.json"
    )
    foreach ($relativePath in $requiredFiles) {
        Assert-RequiredFile -Root $extractionRoot -RelativePath $relativePath
    }

    $allFiles = @(Get-ChildItem -LiteralPath $extractionRoot -Recurse -File -Force)
    $allDirectories = @(Get-ChildItem -LiteralPath $extractionRoot -Recurse -Directory -Force)
    $forbiddenDirectory = $allDirectories | Where-Object {
        $_.Name -in @(".venv", "__pycache__", ".pytest_cache", ".cache", "base.en", "av", "av.libs") -or
        $_.Name -like "av-*.dist-info" -or
        $_.Name -like "airtype_asr_worker.egg-info"
    } | Select-Object -First 1
    if ($forbiddenDirectory) {
        throw "Local ASR bundle contains forbidden generated/runtime directory: $($forbiddenDirectory.FullName)"
    }
    $forbiddenFile = $allFiles | Where-Object {
        $_.Name -eq "pyvenv.cfg" -or $_.Extension -eq ".pyc" -or $_.Name -eq "pyav.exe"
    } | Select-Object -First 1
    if ($forbiddenFile) {
        throw "Local ASR bundle contains forbidden generated/runtime file: $($forbiddenFile.FullName)"
    }

    $textFiles = $allFiles | Where-Object { $_.Extension -in @(".cfg", ".json", ".pth", ".py", ".txt", ".toml", ".md") }
    $verifierRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $exactDeveloperPathPatterns = @([Regex]::Escape($verifierRepoRoot), [Regex]::Escape($env:USERPROFILE))
    $pathLeak = $textFiles | Select-String -Pattern $exactDeveloperPathPatterns -List | Select-Object -First 1
    if (!$pathLeak) {
        $hostBindingFiles = $allFiles | Where-Object { $_.Extension -in @(".cfg", ".json", ".pth", ".toml") }
        $hostBindingPattern = '(?i)([A-Z]:\\Users\\[^\\\r\n]+\\|/home/[^/\r\n]+/)'
        $pathLeak = $hostBindingFiles | Select-String -Pattern $hostBindingPattern -List | Select-Object -First 1
    }
    if ($pathLeak) {
        throw "Local ASR bundle contains an absolute developer path in $($pathLeak.Path)."
    }

    $sourceVcRuntime = Get-Content -LiteralPath $vcRuntimeLockPath -Raw | ConvertFrom-Json
    $bundledVcRuntime = Get-Content -LiteralPath (Join-Path $extractionRoot "runtime\visual-cpp-runtime.json") -Raw | ConvertFrom-Json
    $sourceVcRuntimeCanonical = $sourceVcRuntime | ConvertTo-Json -Depth 8 -Compress
    $bundledVcRuntimeCanonical = $bundledVcRuntime | ConvertTo-Json -Depth 8 -Compress
    Assert-Equal $bundledVcRuntimeCanonical $sourceVcRuntimeCanonical "Bundled Visual C++ runtime lock differs from the release source."
    Assert-Equal $sourceVcRuntime.schemaVersion 1 "Unsupported Visual C++ runtime lock schema."
    Assert-Equal $sourceVcRuntime.architecture "x64" "Unexpected Visual C++ runtime architecture."
    foreach ($file in $sourceVcRuntime.files) {
        $runtimeFile = Join-Path $extractionRoot (Join-Path "runtime" $file.targetName)
        Assert-RequiredFile -Root (Join-Path $extractionRoot "runtime") -RelativePath $file.targetName
        $runtimeFileHash = (Get-FileHash -LiteralPath $runtimeFile -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Equal $runtimeFileHash $file.sha256 "Visual C++ runtime file checksum mismatch: $($file.targetName)"
        $runtimeFileItem = Get-Item -LiteralPath $runtimeFile
        Assert-Equal $runtimeFileItem.VersionInfo.FileVersion $sourceVcRuntime.fileVersion "Visual C++ runtime file version mismatch: $($file.targetName)"
        $runtimeFileSignature = Get-AuthenticodeSignature -LiteralPath $runtimeFile
        Assert-Equal $runtimeFileSignature.Status ([Management.Automation.SignatureStatus]::Valid) "Visual C++ runtime file signature is invalid: $($file.targetName)"
        Assert-Equal $runtimeFileSignature.SignerCertificate.Subject $file.signerSubject "Visual C++ runtime file signer differs: $($file.targetName)"
    }
    $vcLicenseRelativePath = ([string]$sourceVcRuntime.licenseRelativePath).Replace('/', '\')
    $vcLicensePath = Join-Path (Join-Path $extractionRoot "runtime") $vcLicenseRelativePath
    Assert-RequiredFile -Root (Join-Path $extractionRoot "runtime") -RelativePath $vcLicenseRelativePath
    $vcLicenseHash = (Get-FileHash -LiteralPath $vcLicensePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Equal $vcLicenseHash $sourceVcRuntime.licenseSha256 "Visual C++ Redistributable license checksum mismatch."

    $embedded = Get-Content -LiteralPath (Join-Path $extractionRoot "airtype-local-asr-bundle.json") -Raw | ConvertFrom-Json
    foreach ($property in @(
        "schemaVersion", "bundleId", "bundleVersion", "engineId", "modelId", "modelName",
        "runtimeId", "platform", "architecture", "zipFileName", "downloadUrl", "uncompressedBytes",
        "createdUtc", "sourceCommit", "sourceDateUtc")) {
        Assert-Equal $embedded.$property $manifest.$property "Embedded Local ASR manifest property '$property' differs from its sidecar."
    }
    if ($null -ne $embedded.sha256 -or $null -ne $embedded.compressedBytes) {
        throw "Embedded Local ASR manifest must not claim its enclosing ZIP checksum or compressed size."
    }

    $inventoryRoot = Join-Path $extractionRoot "THIRD_PARTY_LICENSES"
    $inventory = Get-Content -LiteralPath (Join-Path $inventoryRoot "manifest.json") -Raw | ConvertFrom-Json
    Assert-Equal $inventory.schemaVersion 1 "Unsupported Local ASR third-party inventory schema."
    Assert-Equal $inventory.inventoryScope "Local ASR runtime dependencies and included model" "Unexpected Local ASR inventory scope."
    $ecosystems = @($inventory.entries | ForEach-Object ecosystem | Sort-Object -Unique)
    foreach ($requiredEcosystem in @("Python", "Runtime", "Model")) {
        if ($requiredEcosystem -notin $ecosystems) {
            throw "Local ASR third-party inventory is missing ecosystem: $requiredEcosystem"
        }
    }
    $vcInventoryEntry = @($inventory.entries | Where-Object {
        $_.ecosystem -eq "Runtime" -and $_.name -eq $sourceVcRuntime.product
    })
    if ($vcInventoryEntry.Count -ne 1 -or $vcInventoryEntry[0].version -ne $sourceVcRuntime.version) {
        throw "Local ASR third-party inventory is missing the pinned Visual C++ runtime record."
    }
    foreach ($entry in $inventory.entries) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.ecosystem) -or
            [string]::IsNullOrWhiteSpace([string]$entry.name) -or
            [string]::IsNullOrWhiteSpace([string]$entry.version) -or
            @($entry.evidenceFiles).Count -eq 0) {
            throw "Local ASR third-party record lacks license evidence: $($entry.name)"
        }
        foreach ($evidenceFile in @($entry.evidenceFiles)) {
            Assert-RequiredFile -Root $inventoryRoot -RelativePath ([string]$evidenceFile).Replace('/', '\')
        }
    }

    $runtimeRoot = Join-Path $extractionRoot "runtime"
    Push-Location $runtimeRoot
    $previousDontWriteBytecode = $env:PYTHONDONTWRITEBYTECODE
    $env:PYTHONDONTWRITEBYTECODE = "1"
    try {
        & (Join-Path $runtimeRoot "python.exe") -c "import sys; assert sys.prefix == sys.base_prefix; import av; assert av.AIRTYPE_PCM_ONLY_SHIM; import airtype_asr_worker, faster_whisper, ctranslate2, onnxruntime"
        if ($LASTEXITCODE -ne 0) {
            throw "Extracted Local ASR runtime import validation failed."
        }
    }
    finally {
        if ($null -eq $previousDontWriteBytecode) {
            Remove-Item Env:PYTHONDONTWRITEBYTECODE -ErrorAction SilentlyContinue
        }
        else {
            $env:PYTHONDONTWRITEBYTECODE = $previousDontWriteBytecode
        }
        Pop-Location
    }

    [pscustomobject][ordered]@{
        manifestPath = $resolvedManifestPath
        zipPath = $zipPath
        version = $version
        sha256 = $actualSha256
        compressedBytes = [Int64]$manifest.compressedBytes
        sourceCommit = [string]$manifest.sourceCommit
        runtimeImportSmoke = $true
        thirdPartyRecords = @($inventory.entries).Count
        verifiedUtc = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json -Depth 4
}
finally {
    if (!$KeepExtraction -and (Test-Path -LiteralPath $extractionRoot)) {
        Remove-Item -LiteralPath $extractionRoot -Recurse -Force
    }
    elseif ($KeepExtraction) {
        Write-Host "Verified Local ASR extraction retained at $extractionRoot"
    }
}
