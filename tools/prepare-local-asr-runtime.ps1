param(
    [switch]$Force,
    [switch]$SkipModel,
    [string[]]$Models = @("small.en"),
    [string]$Python = "python",
    [string]$PythonVersion = "3.12.10"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$workerRoot = Join-Path $repoRoot "AirType.LocalAsrWorker"
$runtimeRoot = Join-Path $workerRoot "runtime"
$runtimePython = Join-Path $runtimeRoot "python.exe"
$runtimeDll = Join-Path $runtimeRoot "python312.dll"
$sitePackages = Join-Path $runtimeRoot "Lib\site-packages"
$runtimeRequirements = Join-Path $workerRoot "requirements-runtime.txt"
$avCompatShim = Join-Path $workerRoot "compat\av.py"
$modelRoot = Join-Path $workerRoot "models\ct2"
$manifestPath = Join-Path $workerRoot "local-asr-manifest.json"
$pythonArchiveName = "python-$PythonVersion-embed-amd64.zip"
$pythonArchiveUrl = "https://www.python.org/ftp/python/$PythonVersion/$pythonArchiveName"
$pythonArchivePath = Join-Path $env:TEMP $pythonArchiveName
$pythonArchiveSha256 = "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3"
$vcRuntimeLockPath = Join-Path $PSScriptRoot "local-asr-vc-runtime.lock.json"

function Assert-FileSha256 {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Expected,
        [Parameter(Mandatory)] [string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "$Description checksum mismatch."
    }
}

function Get-PinnedWixExecutable {
    param([Parameter(Mandatory)] [string]$Version)

    $installed = Get-Command wix.exe -ErrorAction SilentlyContinue
    if ($null -ne $installed) {
        $installedVersion = (& $installed.Source --version | Select-Object -First 1).Trim()
        if ($LASTEXITCODE -eq 0 -and $installedVersion.StartsWith("$Version+", [StringComparison]::Ordinal)) {
            return $installed.Source
        }
    }

    $toolRoot = Join-Path $env:TEMP "AirTypeBuildTools\wix-$Version"
    $toolExecutable = Join-Path $toolRoot "wix.exe"
    if (!(Test-Path -LiteralPath $toolExecutable -PathType Leaf)) {
        if (Test-Path -LiteralPath $toolRoot) {
            Remove-Item -LiteralPath $toolRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
        & dotnet tool install wix --tool-path $toolRoot --version $Version --no-cache
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install pinned WiX Toolset $Version."
        }
    }

    $toolVersion = (& $toolExecutable --version | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0 -or !$toolVersion.StartsWith("$Version+", [StringComparison]::Ordinal)) {
        throw "Pinned WiX Toolset version validation failed."
    }
    return $toolExecutable
}

function Install-PinnedVisualCppRuntime {
    param(
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string]$LockPath
    )

    if (!(Test-Path -LiteralPath $LockPath -PathType Leaf)) {
        throw "Local ASR Visual C++ runtime lock is missing."
    }
    $lockJson = Get-Content -LiteralPath $LockPath -Raw
    $lock = $lockJson | ConvertFrom-Json
    if ($lock.schemaVersion -ne 1 -or $lock.architecture -ne "x64" -or @($lock.files).Count -eq 0) {
        throw "Local ASR Visual C++ runtime lock is invalid."
    }

    $sourceRoot = Join-Path $env:TEMP "AirTypeLocalAsrSources"
    New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
    $installerPath = Join-Path $sourceRoot "VC_redist.x64.$($lock.version).exe"
    $downloadRequired = $true
    if (Test-Path -LiteralPath $installerPath -PathType Leaf) {
        $downloadRequired = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lock.installerSha256
    }
    if ($downloadRequired) {
        Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
        Invoke-WebRequest -Uri $lock.installerUrl -OutFile $installerPath
    }

    Assert-FileSha256 -Path $installerPath -Expected $lock.installerSha256 -Description "Official Visual C++ Redistributable installer"
    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $signature.SignerCertificate.Subject -ne $lock.signerSubject) {
        throw "Official Visual C++ Redistributable signature validation failed."
    }

    $wix = Get-PinnedWixExecutable -Version $lock.wixToolVersion
    $workRoot = Join-Path $env:TEMP "AirTypeVcRuntime_$([Guid]::NewGuid().ToString('N'))"
    $payloadRoot = Join-Path $workRoot "payloads"
    $bootstrapperRoot = Join-Path $workRoot "bootstrapper"
    $expandedRoot = Join-Path $workRoot "expanded"
    New-Item -ItemType Directory -Force -Path $payloadRoot, $bootstrapperRoot, $expandedRoot | Out-Null
    try {
        & $wix burn extract $installerPath -o $payloadRoot -oba $bootstrapperRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to extract the pinned Visual C++ Redistributable."
        }

        $burnManifestPath = Join-Path $bootstrapperRoot "manifest.xml"
        [xml]$burnManifest = Get-Content -LiteralPath $burnManifestPath -Raw
        $cabinetPath = [string]$lock.cabinetPath
        $cabinetPayload = @($burnManifest.SelectNodes("//*[local-name()='Payload']")) | Where-Object {
            $_.GetAttribute("FilePath").Replace('\', '/') -eq $cabinetPath
        } | Select-Object -First 1
        if ($null -eq $cabinetPayload) {
            throw "Pinned Visual C++ minimum runtime cabinet was not found in the Microsoft bundle."
        }
        $cabinetContainer = $cabinetPayload.GetAttribute("Container")
        $cabinetFile = Join-Path $payloadRoot (Join-Path $cabinetContainer $cabinetPath.Replace('/', '\'))
        if (!(Test-Path -LiteralPath $cabinetFile -PathType Leaf)) {
            $cabinetFile = Join-Path $payloadRoot $cabinetPayload.GetAttribute("SourcePath")
        }
        Assert-FileSha256 -Path $cabinetFile -Expected $lock.cabinetSha256 -Description "Visual C++ minimum runtime cabinet"

        & "$env:SystemRoot\System32\expand.exe" -F:* $cabinetFile $expandedRoot | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to expand the Visual C++ minimum runtime cabinet."
        }

        $actualFiles = @(Get-ChildItem -LiteralPath $expandedRoot -File -Force)
        if ($actualFiles.Count -ne @($lock.files).Count) {
            throw "Visual C++ minimum runtime file set differs from its lock."
        }
        foreach ($file in $lock.files) {
            $sourceFile = Join-Path $expandedRoot $file.sourceName
            Assert-FileSha256 -Path $sourceFile -Expected $file.sha256 -Description "Visual C++ runtime file $($file.sourceName)"
            $sourceItem = Get-Item -LiteralPath $sourceFile
            $sourceSignature = Get-AuthenticodeSignature -LiteralPath $sourceFile
            if ($sourceItem.VersionInfo.FileVersion -ne $lock.fileVersion -or
                $sourceSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
                $sourceSignature.SignerCertificate.Subject -ne $file.signerSubject) {
                throw "Visual C++ runtime file signature/version validation failed: $($file.sourceName)"
            }
            Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $DestinationRoot $file.targetName) -Force
        }

        $licenseSource = Join-Path $bootstrapperRoot "license.rtf"
        Assert-FileSha256 -Path $licenseSource -Expected $lock.licenseSha256 -Description "Visual C++ Redistributable license"
        $licenseDestination = Join-Path $DestinationRoot ([string]$lock.licenseRelativePath).Replace('/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $licenseDestination) | Out-Null
        Copy-Item -LiteralPath $licenseSource -Destination $licenseDestination -Force
        Copy-Item -LiteralPath $LockPath -Destination (Join-Path $DestinationRoot "visual-cpp-runtime.json") -Force
    }
    finally {
        if (Test-Path -LiteralPath $workRoot) {
            Remove-Item -LiteralPath $workRoot -Recurse -Force
        }
    }
}

if ($PythonVersion -ne "3.12.10") {
    throw "PythonVersion $PythonVersion is not pinned. Update the official artifact URL and SHA-256 before changing versions."
}

& $Python -c "import struct, sys; raise SystemExit(0 if sys.version_info[:2] == (3, 12) and struct.calcsize('P') == 8 else 1)"
if ($LASTEXITCODE -ne 0) {
    throw "A 64-bit Python 3.12 build interpreter is required to vendor Local ASR wheels."
}

if ($Force) {
    if (Test-Path $runtimeRoot) {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }

    if (!$SkipModel) {
        foreach ($modelName in $Models) {
            $targetModelRoot = Join-Path $modelRoot $modelName
            if (Test-Path $targetModelRoot) {
                Remove-Item -LiteralPath $targetModelRoot -Recurse -Force
            }
        }
    }
}

if (!(Test-Path $runtimePython) -or !(Test-Path $runtimeDll)) {
    if (Test-Path $runtimeRoot) {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }

    $downloadRequired = $true
    if (Test-Path $pythonArchivePath) {
        $existingHash = (Get-FileHash -LiteralPath $pythonArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $downloadRequired = $existingHash -ne $pythonArchiveSha256
    }

    if ($downloadRequired) {
        Invoke-WebRequest -Uri $pythonArchiveUrl -OutFile $pythonArchivePath
    }

    $archiveHash = (Get-FileHash -LiteralPath $pythonArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($archiveHash -ne $pythonArchiveSha256) {
        Remove-Item -LiteralPath $pythonArchivePath -Force -ErrorAction SilentlyContinue
        throw "Official Python embeddable package checksum mismatch."
    }

    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    Expand-Archive -LiteralPath $pythonArchivePath -DestinationPath $runtimeRoot -Force
}

if (!(Test-Path $runtimePython) -or !(Test-Path $runtimeDll)) {
    throw "Portable Python runtime is incomplete after extraction."
}

Install-PinnedVisualCppRuntime -DestinationRoot $runtimeRoot -LockPath $vcRuntimeLockPath

$pthPath = Join-Path $runtimeRoot "python312._pth"
@(
    "python312.zip"
    "."
    "Lib\site-packages"
    "import site"
) | Set-Content -LiteralPath $pthPath -Encoding ASCII

New-Item -ItemType Directory -Force -Path $sitePackages | Out-Null
$dependenciesJson = & $Python -c "import json, pathlib, sys, tomllib; print(json.dumps(tomllib.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8'))['project']['dependencies']))" (Join-Path $workerRoot "pyproject.toml")
if ($LASTEXITCODE -ne 0) {
    throw "Failed to read Local ASR dependencies from pyproject.toml."
}
$dependencies = @($dependenciesJson | ConvertFrom-Json)
$lockedDependencies = @(Get-Content -LiteralPath $runtimeRequirements |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and !$_.StartsWith("#") })
foreach ($dependency in $dependencies) {
    if ($dependency -notin $lockedDependencies) {
        throw "Runtime lock file is missing direct dependency $dependency."
    }
}

& $Python -m pip install --disable-pip-version-check --no-input --prefer-binary --only-binary=:all: --no-compile --ignore-installed --no-deps --upgrade --target $sitePackages -r $runtimeRequirements
if ($LASTEXITCODE -ne 0) {
    throw "Failed to vendor Local ASR runtime dependencies."
}

# faster-whisper imports PyAV even when callers provide NumPy PCM. AirType never
# uses its file decoder, so remove the codec stack and install a fail-closed shim.
foreach ($obsoletePath in @(
    (Join-Path $sitePackages "av"),
    (Join-Path $sitePackages "av.libs"),
    (Join-Path $sitePackages "bin\pyav.exe")
)) {
    if (Test-Path -LiteralPath $obsoletePath) {
        Remove-Item -LiteralPath $obsoletePath -Recurse -Force
    }
}
Get-ChildItem -LiteralPath $sitePackages -Directory -Filter "av-*.dist-info" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
if (!(Test-Path -LiteralPath $avCompatShim)) {
    throw "PCM-only faster-whisper compatibility shim is missing."
}
Copy-Item -LiteralPath $avCompatShim -Destination (Join-Path $runtimeRoot "av.py") -Force

$installedWorkerPackage = Join-Path $sitePackages "airtype_asr_worker"
if (Test-Path $installedWorkerPackage) {
    Remove-Item -LiteralPath $installedWorkerPackage -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $workerRoot "airtype_asr_worker") -Destination $installedWorkerPackage -Recurse -Force

Get-ChildItem -LiteralPath $runtimeRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -eq "__pycache__" } |
    Sort-Object FullName -Descending |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
Get-ChildItem -LiteralPath $runtimeRoot -Filter "direct_url.json" -File -Recurse -Force |
    Remove-Item -Force -ErrorAction SilentlyContinue

if (Test-Path (Join-Path $runtimeRoot "pyvenv.cfg")) {
    throw "Portable runtime unexpectedly contains pyvenv.cfg."
}

& $runtimePython -c "import av; assert av.AIRTYPE_PCM_ONLY_SHIM; import airtype_asr_worker, faster_whisper, ctranslate2, onnxruntime, numpy, websockets, psutil, tokenizers, huggingface_hub"
if ($LASTEXITCODE -ne 0) {
    throw "Portable Local ASR runtime import smoke test failed."
}

if (!$SkipModel) {
    foreach ($modelName in $Models) {
        $targetModelRoot = Join-Path $modelRoot $modelName
        $legacyModelRoot = Join-Path $workerRoot "models\$modelName"

        if (!(Test-Path (Join-Path $targetModelRoot "model.bin")) -and
            (Test-Path (Join-Path $legacyModelRoot "model.bin"))) {
            New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null
            Copy-Item -LiteralPath $legacyModelRoot -Destination $targetModelRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Force -Path $targetModelRoot | Out-Null
        & $runtimePython -c "from faster_whisper.utils import download_model; import sys; download_model(sys.argv[2], output_dir=sys.argv[1])" $targetModelRoot $modelName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to download faster-whisper $modelName model."
        }
    }
}

$manifest = [ordered]@{
    schemaVersion = 2
    installRoot = $workerRoot
    runtimes = [ordered]@{
        "ct2-python" = [ordered]@{
            installed = (Test-Path $runtimePython) -and (Test-Path $runtimeDll)
            distribution = "python-embedded"
            version = $PythonVersion
            path = "runtime/python.exe"
        }
    }
    models = [ordered]@{}
    createdUtc = (Get-Date).ToUniversalTime().ToString("o")
    worker = "airtype-asr-worker"
}

foreach ($modelName in $Models) {
    $modelId = if ($modelName -eq "small.en") { "faster-whisper-small-en-int8" } else { "custom-ct2-$modelName" }
    $manifest.models[$modelId] = [ordered]@{
        installed = Test-Path (Join-Path (Join-Path $modelRoot $modelName) "model.bin")
        backend = "ct2"
        path = "models/ct2/$modelName"
        source = "Systran/faster-whisper-$modelName"
    }
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Local ASR runtime prepared:"
Write-Host "  Runtime: $runtimeRoot"
Write-Host "  Models:  $($Models -join ', ')"
Write-Host "  Default AirType model: small.en CT2"
Write-Host "  Manifest: $manifestPath"
