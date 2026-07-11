param(
    [Parameter(Mandatory)] [string]$AppPackagePath,
    [Parameter(Mandatory)] [string]$ExpectedAppSha256,
    [Parameter(Mandatory)] [string]$InstallerPackagePath,
    [Parameter(Mandatory)] [string]$ExpectedInstallerSha256,
    [Parameter(Mandatory)] [string]$LocalAsrPackagePath,
    [Parameter(Mandatory)] [string]$ExpectedLocalAsrSha256,
    [Parameter(Mandatory)] [string]$ExpectedVersion,
    [Parameter(Mandatory)] [string]$OutputPath,
    [ValidateSet("NotSigned", "Valid")] [string]$ExpectedSignatureStatus = "NotSigned",
    [string]$WorkingDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$qaRoot = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    Join-Path $tempRoot "AirType-CleanWindows-QA"
}
else {
    [IO.Path]::GetFullPath($WorkingDirectory)
}
if ($qaRoot -eq $tempRoot -or
    !$qaRoot.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "WorkingDirectory must be a child of the current user's temporary directory."
}

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Stop-AirType {
    Get-Process -Name "AirType" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Get-Process -Name "AirType" -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    Assert-True -Condition (-not [bool](Get-Process -Name "AirType" -ErrorAction SilentlyContinue)) `
        -Message "AirType process did not stop."
}

function Invoke-MsiTransaction {
    param(
        [Parameter(Mandatory)] [ValidateSet("Install", "Uninstall")] [string]$Mode,
        [Parameter(Mandatory)] [string]$PackagePath,
        [Parameter(Mandatory)] [string]$LogPath
    )

    $operation = if ($Mode -eq "Install") { "/i" } else { "/x" }
    $process = Start-Process `
        -FilePath "msiexec.exe" `
        -ArgumentList @(
            $operation, ('"{0}"' -f $PackagePath),
            "/qn", "/norestart", "/l*v", ('"{0}"' -f $LogPath)
        ) `
        -Wait `
        -PassThru
    Assert-True -Condition ($process.ExitCode -in @(0, 3010)) `
        -Message "MSI $Mode failed with exit code $($process.ExitCode). See $LogPath."
}

function Start-And-ValidateAirType {
    param(
        [Parameter(Mandatory)] [string]$ExecutablePath,
        [Parameter(Mandatory)] [string]$StorageRoot
    )

    $env:AIRTYPE_STORAGE_ROOT = $StorageRoot
    $process = Start-Process -FilePath $ExecutablePath -WorkingDirectory (Split-Path -Parent $ExecutablePath) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $mainWindowHandle = 0
    $dataFiles = @()
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        Assert-True -Condition (!$process.HasExited) -Message "AirType exited during clean-machine launch."
        $mainWindowHandle = $process.MainWindowHandle.ToInt64()
        $dataFiles = @(Get-ChildItem -LiteralPath $StorageRoot -Recurse -File -Force -ErrorAction SilentlyContinue)
    } while (($mainWindowHandle -eq 0 -or $dataFiles.Count -eq 0) -and [DateTime]::UtcNow -lt $deadline)
    Assert-True -Condition ($mainWindowHandle -ne 0) -Message "AirType launched without a visible top-level window."
    Assert-True -Condition ($dataFiles.Count -gt 0) -Message "AirType created no isolated profile data."

    [pscustomobject][ordered]@{
        processId = $process.Id
        mainWindowHandle = $mainWindowHandle
        dataFileCount = $dataFiles.Count
    }
}

$appPackage = (Resolve-Path -LiteralPath $AppPackagePath).Path
$installerPackage = (Resolve-Path -LiteralPath $InstallerPackagePath).Path
$localAsrPackage = (Resolve-Path -LiteralPath $LocalAsrPackagePath).Path
$expectedAppHash = $ExpectedAppSha256.ToLowerInvariant()
$expectedInstallerHash = $ExpectedInstallerSha256.ToLowerInvariant()
$expectedLocalAsrHash = $ExpectedLocalAsrSha256.ToLowerInvariant()

Assert-True -Condition ($ExpectedVersion -match '^\d+\.\d+\.\d+$') `
    -Message "ExpectedVersion must use x.y.z format."
$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
Assert-True -Condition `
    ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) `
    -Message "Clean Windows installer acceptance must run from an elevated PowerShell session."

$os = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem
$dotnetDesktopRoots = @(@(
    "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App",
    "${env:ProgramFiles(x86)}\dotnet\shared\Microsoft.WindowsDesktop.App"
) | Where-Object { Test-Path -LiteralPath $_ })
$pythonRuntimeRoots = @(@(
    "$env:ProgramFiles\Python*",
    "${env:ProgramFiles(x86)}\Python*",
    "$env:LOCALAPPDATA\Programs\Python"
) | Where-Object { Test-Path $_ })
$windowsAppRuntimePackages = @(Get-AppxPackage -Name "Microsoft.WindowsAppRuntime*" -ErrorAction SilentlyContinue)

Assert-True -Condition ($dotnetDesktopRoots.Count -eq 0) -Message "Clean VM unexpectedly has a .NET Desktop Runtime installation."
Assert-True -Condition ($pythonRuntimeRoots.Count -eq 0) -Message "Clean VM unexpectedly has a Python runtime installation."

$inputRoot = Join-Path $qaRoot "Input"
$appRoot = Join-Path $qaRoot "App"
$storageRoot = Join-Path $qaRoot "Data"
$engineRoot = Join-Path $qaRoot "LocalAsr"
$installedAppRoot = Join-Path $env:ProgramFiles "AirType"
$installedExecutable = Join-Path $installedAppRoot "AirType.exe"
$startMenuShortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\AirType\AirType.lnk"
Stop-AirType
Assert-True -Condition (!(Test-Path -LiteralPath $installedExecutable)) `
    -Message "Clean VM already contains an AirType installation."
if (Test-Path -LiteralPath $qaRoot) {
    Remove-Item -LiteralPath $qaRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $inputRoot, $appRoot, $storageRoot, $engineRoot | Out-Null

try {
    $localAppPackage = Join-Path $inputRoot "AirType-app.zip"
    $localInstallerPackage = Join-Path $inputRoot "AirType-installer.msi"
    $localAsrPackageCopy = Join-Path $inputRoot "AirType-local-asr.zip"
    Copy-Item -LiteralPath $appPackage -Destination $localAppPackage -Force
    Copy-Item -LiteralPath $installerPackage -Destination $localInstallerPackage -Force
    Copy-Item -LiteralPath $localAsrPackage -Destination $localAsrPackageCopy -Force
    $appHash = (Get-FileHash -LiteralPath $localAppPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $installerHash = (Get-FileHash -LiteralPath $localInstallerPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $localAsrHash = (Get-FileHash -LiteralPath $localAsrPackageCopy -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True -Condition ($appHash -eq $expectedAppHash) -Message "Application candidate checksum mismatch."
    Assert-True -Condition ($installerHash -eq $expectedInstallerHash) -Message "Installer candidate checksum mismatch."
    Assert-True -Condition ($localAsrHash -eq $expectedLocalAsrHash) -Message "Local ASR candidate checksum mismatch."

    $installerSignatureStatus = (Get-AuthenticodeSignature -LiteralPath $localInstallerPackage).Status.ToString()
    Assert-True -Condition ($installerSignatureStatus -eq $ExpectedSignatureStatus) `
        -Message "Installer signature status is $installerSignatureStatus; expected $ExpectedSignatureStatus."
    Invoke-MsiTransaction -Mode Install -PackagePath $localInstallerPackage -LogPath (Join-Path $qaRoot "install-1.log")
    Assert-True -Condition (Test-Path -LiteralPath $installedExecutable -PathType Leaf) `
        -Message "AirType.exe is missing after MSI installation."
    Assert-True -Condition (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf) `
        -Message "AirType Start menu shortcut is missing after MSI installation."
    Assert-True -Condition `
        (Test-Path -LiteralPath (Join-Path $installedAppRoot "System.Private.CoreLib.dll") -PathType Leaf) `
        -Message "Installed self-contained .NET runtime is incomplete."
    $installedVersion = (Get-Item -LiteralPath $installedExecutable).VersionInfo.FileVersion
    Assert-True -Condition ($installedVersion -eq "$ExpectedVersion.0") `
        -Message "Unexpected installed AirType file version: $installedVersion"
    $installedSignatureStatus = (Get-AuthenticodeSignature -LiteralPath $installedExecutable).Status.ToString()
    Assert-True -Condition ($installedSignatureStatus -eq $ExpectedSignatureStatus) `
        -Message "Installed executable signature status is $installedSignatureStatus; expected $ExpectedSignatureStatus."
    $installerFirstLaunch = Start-And-ValidateAirType -ExecutablePath $installedExecutable -StorageRoot $storageRoot
    Stop-AirType
    $storedFileCount = @(Get-ChildItem -LiteralPath $storageRoot -Recurse -File -Force).Count
    Invoke-MsiTransaction -Mode Uninstall -PackagePath $localInstallerPackage -LogPath (Join-Path $qaRoot "uninstall-1.log")
    Assert-True -Condition (!(Test-Path -LiteralPath $installedExecutable)) `
        -Message "MSI uninstall left AirType.exe installed."
    Assert-True -Condition (!(Test-Path -LiteralPath $startMenuShortcut)) `
        -Message "MSI uninstall left the AirType Start menu shortcut installed."
    Assert-True -Condition (@(Get-ChildItem -LiteralPath $storageRoot -Recurse -File -Force).Count -ge $storedFileCount) `
        -Message "MSI uninstall removed user data."

    Invoke-MsiTransaction -Mode Install -PackagePath $localInstallerPackage -LogPath (Join-Path $qaRoot "install-2.log")
    $installerReinstallLaunch = Start-And-ValidateAirType -ExecutablePath $installedExecutable -StorageRoot $storageRoot
    Stop-AirType
    Invoke-MsiTransaction -Mode Uninstall -PackagePath $localInstallerPackage -LogPath (Join-Path $qaRoot "uninstall-2.log")
    Assert-True -Condition (!(Test-Path -LiteralPath $installedExecutable)) `
        -Message "Final MSI uninstall left AirType.exe installed."
    Assert-True -Condition (!(Test-Path -LiteralPath $startMenuShortcut)) `
        -Message "Final MSI uninstall left the AirType Start menu shortcut installed."

    [IO.Compression.ZipFile]::ExtractToDirectory($localAppPackage, $appRoot)
    $appExecutable = Join-Path $appRoot "AirType.exe"
    Assert-True -Condition (Test-Path -LiteralPath $appExecutable -PathType Leaf) -Message "AirType.exe is missing after extraction."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $appRoot "System.Private.CoreLib.dll") -PathType Leaf) `
        -Message "The self-contained .NET runtime is incomplete after extraction."
    $fileVersion = (Get-Item -LiteralPath $appExecutable).VersionInfo.FileVersion
    Assert-True -Condition ($fileVersion -eq "$ExpectedVersion.0") -Message "Unexpected AirType file version: $fileVersion"
    $signatureStatus = (Get-AuthenticodeSignature -LiteralPath $appExecutable).Status.ToString()
    Assert-True -Condition ($signatureStatus -eq $ExpectedSignatureStatus) `
        -Message "Candidate signature status is $signatureStatus; expected $ExpectedSignatureStatus."

    $firstLaunch = Start-And-ValidateAirType -ExecutablePath $appExecutable -StorageRoot $storageRoot
    Stop-AirType
    $restart = Start-And-ValidateAirType -ExecutablePath $appExecutable -StorageRoot $storageRoot
    Stop-AirType

    [IO.Compression.ZipFile]::ExtractToDirectory($localAsrPackageCopy, $engineRoot)
    $runtimeRoot = Join-Path $engineRoot "runtime"
    $runtimePython = Join-Path $runtimeRoot "python.exe"
    $modelRoot = Join-Path $engineRoot "models\ct2\small.en"
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $runtimeRoot "python312.dll")) -Message "Portable CPython DLL is missing."
    Assert-True -Condition (!(Test-Path -LiteralPath (Join-Path $runtimeRoot "pyvenv.cfg"))) -Message "Local ASR contains a host-bound virtual environment."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $runtimeRoot "visual-cpp-runtime.json")) -Message "Pinned Visual C++ runtime metadata is missing."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $runtimeRoot "msvcp140.dll")) -Message "App-local MSVCP140 runtime is missing."
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $runtimeRoot "msvcp140_1.dll")) -Message "App-local MSVCP140_1 runtime is missing."

    $env:PYTHONDONTWRITEBYTECODE = "1"
    $runtimeSmoke = & $runtimePython -c "import json, sys; import av; assert av.AIRTYPE_PCM_ONLY_SHIM; import airtype_asr_worker, faster_whisper, ctranslate2, onnxruntime; print(json.dumps({'basePrefixEqualsPrefix': sys.base_prefix == sys.prefix}))"
    Assert-True -Condition ($LASTEXITCODE -eq 0) -Message "Portable Local ASR runtime import smoke failed."
    $runtimeResult = $runtimeSmoke | ConvertFrom-Json
    Assert-True -Condition ([bool]$runtimeResult.basePrefixEqualsPrefix) -Message "Portable Python still depends on a base installation."

    $modelSmoke = & $runtimePython -c "import json, os, numpy as np; from faster_whisper import WhisperModel; threads=max(1, min(os.cpu_count() or 2, 4)); model=WhisperModel(r'$modelRoot', device='cpu', compute_type='int8', cpu_threads=threads); segments, info=model.transcribe(np.zeros(8000, dtype=np.float32), language='en', beam_size=1, vad_filter=False); list(segments); print(json.dumps({'duration': float(getattr(info, 'duration', 0.0) or 0.0), 'cpuThreads': threads}))"
    Assert-True -Condition ($LASTEXITCODE -eq 0) -Message "Local ASR model load/inference smoke failed."
    $modelResult = $modelSmoke | ConvertFrom-Json

    Remove-Item -LiteralPath $engineRoot -Recurse -Force
    Assert-True -Condition (!(Test-Path -LiteralPath $engineRoot)) -Message "Local ASR removal left its install directory behind."

    $result = [pscustomobject][ordered]@{
        schemaVersion = 2
        testedUtc = (Get-Date).ToUniversalTime().ToString("o")
        os = [pscustomobject][ordered]@{
            caption = $os.Caption
            version = $os.Version
            buildNumber = $os.BuildNumber
            architecture = $os.OSArchitecture
            totalPhysicalMemory = [Int64]$computer.TotalPhysicalMemory
        }
        cleanEnvironment = [pscustomobject][ordered]@{
            dotnetDesktopRuntimeRoots = @($dotnetDesktopRoots)
            pythonRuntimeRoots = @($pythonRuntimeRoots)
            windowsAppRuntimePackageCount = $windowsAppRuntimePackages.Count
        }
        app = [pscustomobject][ordered]@{
            sha256 = $appHash
            fileVersion = $fileVersion
            signatureStatus = $signatureStatus
            firstLaunch = $firstLaunch
            restart = $restart
        }
        installer = [pscustomobject][ordered]@{
            sha256 = $installerHash
            signatureStatus = $installerSignatureStatus
            installedExecutableSignatureStatus = $installedSignatureStatus
            fileVersion = $installedVersion
            firstLaunch = $installerFirstLaunch
            reinstallLaunch = $installerReinstallLaunch
            userDataRetained = $true
            finalRemovalComplete = $true
        }
        localAsr = [pscustomobject][ordered]@{
            sha256 = $localAsrHash
            portableRuntimeImports = $true
            visualCppRuntimeAppLocal = $true
            basePrefixEqualsPrefix = [bool]$runtimeResult.basePrefixEqualsPrefix
            modelInference = $true
            inferenceDurationSeconds = [double]$modelResult.duration
            inferenceCpuThreads = [int]$modelResult.cpuThreads
            removalComplete = $true
        }
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 8
}
finally {
    Stop-AirType
    if ((Test-Path -LiteralPath $installedExecutable) -and
        (Test-Path -LiteralPath (Join-Path $inputRoot "AirType-installer.msi"))) {
        try {
            Invoke-MsiTransaction `
                -Mode Uninstall `
                -PackagePath (Join-Path $inputRoot "AirType-installer.msi") `
                -LogPath (Join-Path $qaRoot "uninstall-cleanup.log")
        }
        catch {
            Write-Warning "Emergency MSI cleanup failed: $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $qaRoot) {
        Remove-Item -LiteralPath $qaRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
