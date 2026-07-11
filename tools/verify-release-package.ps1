param(
    [Parameter(Mandatory)] [string]$PackagePath,
    [switch]$AllowUnsigned,
    [switch]$SkipLaunchSmoke,
    [switch]$KeepExtraction,
    [ValidateRange(5, 120)] [int]$StartupTimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-ReleaseCondition {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory)] [string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        try {
            Assert-ReleaseCondition ($reader.ReadUInt16() -eq 0x5A4D) "Executable is missing the DOS PE header."
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            $stream.Position = $peOffset
            Assert-ReleaseCondition ($reader.ReadUInt32() -eq 0x00004550) "Executable is missing the PE signature."
            return $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$package = (Resolve-Path -LiteralPath $PackagePath).Path
Assert-ReleaseCondition ([IO.Path]::GetExtension($package) -eq ".zip") "Release package must be a ZIP archive."
$checksumPath = [IO.Path]::ChangeExtension($package, ".sha256")
Assert-ReleaseCondition (Test-Path -LiteralPath $checksumPath) "Release SHA-256 sidecar is missing."

$expectedHash = (Get-Content -LiteralPath $checksumPath -Raw).Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-ReleaseCondition ($expectedHash -eq $actualHash) "Release package checksum does not match its sidecar."

$tempParent = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')
$extractionRoot = Join-Path $tempParent ("AirTypeReleaseValidation_" + [Guid]::NewGuid().ToString("N"))
Assert-ReleaseCondition ($extractionRoot.StartsWith($tempParent + '\', [StringComparison]::OrdinalIgnoreCase)) "Validation extraction escaped the temporary directory."

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($entry in $archive.Entries) {
        $candidate = [IO.Path]::GetFullPath((Join-Path $extractionRoot $entry.FullName))
        Assert-ReleaseCondition ($candidate.StartsWith($extractionRoot + '\', [StringComparison]::OrdinalIgnoreCase)) "Release archive contains an unsafe path: $($entry.FullName)"
    }
}
finally {
    $archive.Dispose()
}

New-Item -ItemType Directory -Force -Path $extractionRoot | Out-Null
$launchedProcess = $null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $extractionRoot)

    $airTypeExe = Join-Path $extractionRoot "AirType.exe"
    $runtimeConfigPath = Join-Path $extractionRoot "AirType.runtimeconfig.json"
    $releaseManifestPath = Join-Path $extractionRoot "release-manifest.json"
    $thirdPartyRoot = Join-Path $extractionRoot "THIRD_PARTY_LICENSES"
    $thirdPartyManifestPath = Join-Path $thirdPartyRoot "manifest.json"
    $pythonRuntimeRoot = Join-Path $extractionRoot "LocalAsrWorker\runtime"
    $pythonExe = Join-Path $pythonRuntimeRoot "python.exe"

    foreach ($requiredPath in @(
        $airTypeExe,
        $runtimeConfigPath,
        $releaseManifestPath,
        (Join-Path $extractionRoot "LICENSE"),
        (Join-Path $extractionRoot "PRIVACY.md"),
        (Join-Path $extractionRoot "CODE_SIGNING_POLICY.md"),
        (Join-Path $extractionRoot "THIRD_PARTY_NOTICES.md"),
        (Join-Path $extractionRoot "SECURITY.md"),
        (Join-Path $extractionRoot "INSTALL.md"),
        (Join-Path $extractionRoot "Fonts\Inter\OFL.txt"),
        $thirdPartyManifestPath,
        (Join-Path $extractionRoot "coreclr.dll"),
        (Join-Path $extractionRoot "PresentationFramework.dll"),
        (Join-Path $extractionRoot "Microsoft.WindowsAppRuntime.Bootstrap.dll"),
        $pythonExe,
        (Join-Path $pythonRuntimeRoot "python312.dll"),
        (Join-Path $pythonRuntimeRoot "python312._pth"),
        (Join-Path $pythonRuntimeRoot "av.py"),
        (Join-Path $extractionRoot "LocalAsrWorker\airtype_asr_worker\__main__.py"),
        (Join-Path $extractionRoot "LocalAsrWorker\requirements-runtime.txt")
    )) {
        Assert-ReleaseCondition (Test-Path -LiteralPath $requiredPath) "Release package is missing required file: $requiredPath"
    }

    Assert-ReleaseCondition (!(Test-Path -LiteralPath (Join-Path $extractionRoot "LocalAsrWorker\.venv"))) "Release package contains a host-bound .venv."
    Assert-ReleaseCondition (!(Test-Path -LiteralPath (Join-Path $pythonRuntimeRoot "pyvenv.cfg"))) "Release package contains host-bound pyvenv.cfg."
    Assert-ReleaseCondition (!(Test-Path -LiteralPath (Join-Path $extractionRoot "LocalAsrWorker\local-asr-manifest.json"))) "Release package contains a generated developer manifest."
    Assert-ReleaseCondition (!(Test-Path -LiteralPath (Join-Path $pythonRuntimeRoot "Lib\site-packages\av"))) "Release package contains the unused PyAV package."
    Assert-ReleaseCondition (!(Test-Path -LiteralPath (Join-Path $pythonRuntimeRoot "Lib\site-packages\av.libs"))) "Release package contains the unused FFmpeg codec libraries."
    Assert-ReleaseCondition (!(Test-Path -LiteralPath (Join-Path $pythonRuntimeRoot "Lib\site-packages\bin\pyav.exe"))) "Release package contains a stale PyAV launcher."
    Assert-ReleaseCondition (@(Get-ChildItem -LiteralPath (Join-Path $pythonRuntimeRoot "Lib\site-packages") -Directory -Filter "av-*.dist-info").Count -eq 0) "Release package contains stale PyAV metadata."

    $thirdPartyManifest = Get-Content -LiteralPath $thirdPartyManifestPath -Raw | ConvertFrom-Json
    Assert-ReleaseCondition ($thirdPartyManifest.schemaVersion -eq 1) "Unsupported third-party manifest schema."
    $thirdPartyEntries = @($thirdPartyManifest.entries)
    Assert-ReleaseCondition ($thirdPartyEntries.Count -gt 0) "Third-party manifest has no dependency entries."
    $thirdPartyEcosystems = @($thirdPartyEntries | ForEach-Object { $_.ecosystem } | Sort-Object -Unique)
    foreach ($requiredEcosystem in @("Asset", "NuGet", "Python", "Runtime", "Model")) {
        Assert-ReleaseCondition ($thirdPartyEcosystems -contains $requiredEcosystem) "Third-party manifest is missing $requiredEcosystem records."
    }
    $interEntries = @($thirdPartyEntries | Where-Object { $_.name -eq "Inter variable fonts" })
    Assert-ReleaseCondition ($interEntries.Count -eq 1) "Third-party manifest must contain exactly one Inter variable fonts record."
    Assert-ReleaseCondition ($interEntries[0].declaredLicense -eq "SIL Open Font License 1.1") "Inter font inventory has an unexpected license."
    foreach ($thirdPartyEntry in $thirdPartyEntries) {
        Assert-ReleaseCondition (![string]::IsNullOrWhiteSpace($thirdPartyEntry.name)) "Third-party manifest contains an unnamed entry."
        Assert-ReleaseCondition (![string]::IsNullOrWhiteSpace($thirdPartyEntry.version)) "Third-party manifest contains an unversioned entry."
        $evidenceFiles = @($thirdPartyEntry.evidenceFiles)
        Assert-ReleaseCondition ($evidenceFiles.Count -gt 0) "Third-party manifest entry has no evidence: $($thirdPartyEntry.name)"
        foreach ($evidenceFile in $evidenceFiles) {
            $evidencePath = [IO.Path]::GetFullPath((Join-Path $thirdPartyRoot $evidenceFile))
            Assert-ReleaseCondition ($evidencePath.StartsWith($thirdPartyRoot + '\', [StringComparison]::OrdinalIgnoreCase)) "Third-party evidence path escaped its root: $evidenceFile"
            Assert-ReleaseCondition (Test-Path -LiteralPath $evidencePath) "Third-party evidence file is missing: $evidenceFile"
        }
    }

    $manifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
    Assert-ReleaseCondition ($manifest.schemaVersion -eq 1) "Unsupported release manifest schema."
    Assert-ReleaseCondition ($manifest.product -eq "AirType") "Release manifest product is not AirType."
    Assert-ReleaseCondition ($manifest.platform -eq "win-x64") "Release manifest platform is not win-x64."
    Assert-ReleaseCondition ($manifest.version -match '^\d+\.\d+\.\d+$') "Release manifest version is invalid."

    $versionInfo = (Get-Item -LiteralPath $airTypeExe).VersionInfo
    Assert-ReleaseCondition ($versionInfo.ProductVersion -eq $manifest.version) "Executable product version does not match release manifest."
    Assert-ReleaseCondition ($versionInfo.FileVersion -eq "$($manifest.version).0") "Executable file version does not match release manifest."
    Assert-ReleaseCondition ((Get-PeMachine -Path $airTypeExe) -eq 0x8664) "AirType.exe is not an x64 PE executable."

    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $frameworkNames = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
    Assert-ReleaseCondition ($frameworkNames -contains "Microsoft.NETCore.App") "Self-contained .NET runtime is missing."
    Assert-ReleaseCondition ($frameworkNames -contains "Microsoft.WindowsDesktop.App") "Self-contained .NET Desktop runtime is missing."

    $signature = Get-AuthenticodeSignature -LiteralPath $airTypeExe
    $isSigned = $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid
    Assert-ReleaseCondition ($isSigned -eq [bool]$manifest.signed) "Executable signature status does not match release manifest."
    if (!$AllowUnsigned) {
        Assert-ReleaseCondition $isSigned "Public release executable is not Authenticode signed."
        Assert-ReleaseCondition (![bool]$manifest.dirty) "Public release manifest reports a dirty worktree."
        Assert-ReleaseCondition ($manifest.sourceBranch -eq "main") "Public release manifest was not built from main."
    }

    $allFiles = @(Get-ChildItem -LiteralPath $extractionRoot -Recurse -File)
    $textExtensions = @(".cfg", ".json", ".md", ".pth", ".py", ".txt", ".toml")
    $textFiles = @($allFiles | Where-Object { $_.Extension -in $textExtensions })
    $verifierRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $projectPathPatterns = @(
        [Regex]::Escape($verifierRepoRoot),
        "my_builds",
        "dictation-ui-modernization"
    )
    $developerPathLeak = $textFiles |
        Select-String -Pattern $projectPathPatterns -List -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $developerPathLeak) {
        $sitePackagesRoot = [IO.Path]::GetFullPath((Join-Path $extractionRoot "LocalAsrWorker\runtime\Lib\site-packages")).TrimEnd('\')
        $workerPackageRoot = Join-Path $sitePackagesRoot "airtype_asr_worker"
        $firstPartyTextFiles = @($textFiles | Where-Object {
            !$_.FullName.StartsWith($sitePackagesRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $_.FullName.StartsWith($workerPackageRoot + '\', [StringComparison]::OrdinalIgnoreCase)
        })
        $hostBindingFiles = @($firstPartyTextFiles | Where-Object { $_.Extension -in @(".cfg", ".json", ".pth", ".py", ".toml") })
        $hostBindingPattern = '(?i)([A-Z]:[\\/]Users[\\/][^\\/\r\n]+[\\/]|/home/[^/\r\n]+/)'
        $developerPathLeak = $hostBindingFiles |
            Select-String -Pattern $hostBindingPattern -List -ErrorAction SilentlyContinue |
            Select-Object -First 1
    }
    if ($null -ne $developerPathLeak) {
        throw "Release package contains a developer path in $($developerPathLeak.Path)."
    }

    & $pythonExe -c "import av; assert av.AIRTYPE_PCM_ONLY_SHIM; import airtype_asr_worker, ctranslate2, faster_whisper, onnxruntime, sys; raise SystemExit(0 if sys.base_prefix == sys.prefix else 1)"
    Assert-ReleaseCondition ($LASTEXITCODE -eq 0) "Portable Local ASR runtime import or isolation check failed."

    if (!$SkipLaunchSmoke) {
        $existingAirType = @(Get-Process -Name "AirType" -ErrorAction SilentlyContinue)
        Assert-ReleaseCondition ($existingAirType.Count -eq 0) "Close running AirType processes before release startup validation."

        $freshStorageRoot = Join-Path $extractionRoot "fresh-storage-root"
        $previousStorageRoot = $env:AIRTYPE_STORAGE_ROOT
        try {
            $env:AIRTYPE_STORAGE_ROOT = $freshStorageRoot
            $launchedProcess = Start-Process -FilePath $airTypeExe -WorkingDirectory $extractionRoot -WindowStyle Hidden -PassThru
        }
        finally {
            $env:AIRTYPE_STORAGE_ROOT = $previousStorageRoot
        }

        $startupDeadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        do {
            $running = Get-Process -Id $launchedProcess.Id -ErrorAction SilentlyContinue
            Assert-ReleaseCondition ($null -ne $running) "AirType exited during fresh-profile startup validation."
            if (Test-Path -LiteralPath $freshStorageRoot) {
                break
            }
            Start-Sleep -Milliseconds 500
        } while ([DateTime]::UtcNow -lt $startupDeadline)

        Assert-ReleaseCondition (Test-Path -LiteralPath $freshStorageRoot) "AirType did not initialize its isolated storage root."
    }

    [pscustomobject]@{
        Package = $package
        Version = $manifest.version
        Signed = $isSigned
        SourceBranch = $manifest.sourceBranch
        Dirty = [bool]$manifest.dirty
        Checksum = $actualHash
        LaunchSmoke = !$SkipLaunchSmoke
    } | ConvertTo-Json -Compress | Write-Host
}
finally {
    if ($null -ne $launchedProcess) {
        Stop-Process -Id $launchedProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if (!$KeepExtraction -and (Test-Path -LiteralPath $extractionRoot)) {
        Remove-Item -LiteralPath $extractionRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    elseif ($KeepExtraction) {
        Write-Host "Validation extraction retained at $extractionRoot"
    }
}
