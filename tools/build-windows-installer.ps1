param(
    [Parameter(Mandatory)] [string]$PayloadDirectory,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-InstallerBuildCondition {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Normalize-WindowsAppSdkMsiLanguageMetadata {
    param([Parameter(Mandatory)] [string]$Path)

    $installer = $null
    $database = $null
    $selectView = $null
    $fileIds = [Collections.Generic.List[string]]::new()
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($Path, 1)
        $selectView = $database.OpenView('SELECT `File`, `FileName`, `Language` FROM `File`')
        $selectView.Execute()
        while ($record = $selectView.Fetch()) {
            try {
                $fileName = ($record.StringData(2) -split '\|')[-1]
                if ($fileName -match '^(?i:Microsoft\.UI\.Xaml(?:\.Phone)?\.dll(?:\.mui)?)$') {
                    $fileId = $record.StringData(1)
                    Assert-InstallerBuildCondition ($fileId -match '^[A-Za-z0-9_.]+$') `
                        "Windows App SDK MSI file identifier is unsafe."
                    $fileIds.Add($fileId)
                }
            }
            finally {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
            }
        }
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($selectView) | Out-Null
        $selectView = $null

        Assert-InstallerBuildCondition ($fileIds.Count -gt 0) `
            "Windows installer contains no Windows App SDK XAML language rows to normalize."
        foreach ($fileId in $fileIds) {
            $updateView = $database.OpenView(
                "UPDATE ``File`` SET ``Language`` = '1033' WHERE ``File`` = '$fileId'")
            try {
                $updateView.Execute()
            }
            finally {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($updateView) | Out-Null
            }
        }
        $database.Commit()
    }
    finally {
        if ($null -ne $selectView) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($selectView) | Out-Null
        }
        if ($null -ne $database) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
        }
        if ($null -ne $installer) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
        }
    }

    $installer = $null
    $database = $null
    $verifyView = $null
    $remainingLanguages = 0
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($Path, 0)
        $verifyView = $database.OpenView('SELECT `FileName`, `Language` FROM `File`')
        $verifyView.Execute()
        while ($record = $verifyView.Fetch()) {
            try {
                $fileName = ($record.StringData(1) -split '\|')[-1]
                if ($fileName -match '^(?i:Microsoft\.UI\.Xaml(?:\.Phone)?\.dll(?:\.mui)?)$' -and
                    $record.StringData(2) -ne '1033') {
                    $remainingLanguages++
                }
            }
            finally {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
            }
        }
    }
    finally {
        if ($null -ne $verifyView) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($verifyView) | Out-Null
        }
        if ($null -ne $database) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
        }
        if ($null -ne $installer) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
        }
    }

    Assert-InstallerBuildCondition ($remainingLanguages -eq 0) `
        "Windows App SDK MSI language metadata normalization was incomplete."
    Write-Host "Normalized Windows App SDK MSI language metadata for $($fileIds.Count) files."
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use stable semantic version format x.y.z."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$payloadRoot = (Resolve-Path -LiteralPath $PayloadDirectory).Path.TrimEnd('\')
$output = [IO.Path]::GetFullPath($OutputPath)
Assert-InstallerBuildCondition ([IO.Path]::GetExtension($output) -eq ".msi") `
    "Windows installer output must use the .msi extension."
Assert-InstallerBuildCondition `
    (!$output.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase)) `
    "Windows installer output must be outside the payload directory."

$airTypeExe = Join-Path $payloadRoot "AirType.exe"
$manifestPath = Join-Path $payloadRoot "release-manifest.json"
Assert-InstallerBuildCondition (Test-Path -LiteralPath $airTypeExe -PathType Leaf) `
    "Installer payload is missing AirType.exe."
Assert-InstallerBuildCondition (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
    "Installer payload is missing release-manifest.json."

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-InstallerBuildCondition ($manifest.schemaVersion -eq 1) `
    "Installer payload has an unsupported release manifest schema."
Assert-InstallerBuildCondition ($manifest.product -eq "AirType") `
    "Installer payload manifest product is not AirType."
Assert-InstallerBuildCondition ($manifest.version -eq $Version) `
    "Installer payload manifest version does not match."
Assert-InstallerBuildCondition ($manifest.platform -eq "win-x64") `
    "Installer payload manifest platform is not win-x64."

$payloadSignatureStatus = (Get-AuthenticodeSignature -LiteralPath $airTypeExe).Status.ToString()
Assert-InstallerBuildCondition ($payloadSignatureStatus -in @("NotSigned", "Valid")) `
    "Installer payload has an unsupported signature status: $payloadSignatureStatus"
Assert-InstallerBuildCondition `
    ([bool]$manifest.signed -eq ($payloadSignatureStatus -eq "Valid")) `
    "Installer payload signature status does not match its release manifest."

$authoringPath = Join-Path $repoRoot "packaging\windows\AirType.wxs"
Assert-InstallerBuildCondition (Test-Path -LiteralPath $authoringPath -PathType Leaf) `
    "AirType WiX authoring is missing."
$vcRuntimeLock = Get-Content `
    -LiteralPath (Join-Path $PSScriptRoot "local-asr-vc-runtime.lock.json") `
    -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot "wix-tool.ps1")
$wix = Get-PinnedWixExecutable -Version $vcRuntimeLock.wixToolVersion

$outputRoot = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$checksumPath = "$output.sha256"
Remove-Item -LiteralPath $output, $checksumPath -Force -ErrorAction SilentlyContinue

$tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')
$intermediateRoot = Join-Path $tempRoot ("AirTypeMsiBuild_" + [Guid]::NewGuid().ToString("N"))
Assert-InstallerBuildCondition `
    ($intermediateRoot.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) `
    "MSI build directory escaped the temporary directory."
New-Item -ItemType Directory -Force -Path $intermediateRoot | Out-Null

try {
    & $wix build `
        -arch x64 `
        -d "Version=$Version" `
        -b "Payload=$payloadRoot" `
        -b "Source=$repoRoot" `
        -intermediateFolder $intermediateRoot `
        -pdbtype none `
        $authoringPath `
        -out $output
    if ($LASTEXITCODE -ne 0) {
        throw "AirType Windows installer build failed."
    }

    Assert-InstallerBuildCondition (Test-Path -LiteralPath $output -PathType Leaf) `
        "WiX completed without producing the Windows installer."
    Normalize-WindowsAppSdkMsiLanguageMetadata -Path $output
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($output))" |
        Set-Content -LiteralPath $checksumPath -Encoding ASCII

    & powershell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "verify-windows-installer.ps1") `
        -PackagePath $output `
        -ExpectedVersion $Version `
        -ExpectedInstallerSignatureStatus NotSigned `
        -ExpectedPayloadSignatureStatus $payloadSignatureStatus `
        -ExpectedSourceCommit $manifest.sourceCommit `
        -ExpectedSourceBranch $manifest.sourceBranch
    if ($LASTEXITCODE -ne 0) {
        throw "AirType Windows installer verification failed."
    }

    Write-Host "AirType Windows installer created:"
    Write-Host "  Version:  $Version"
    Write-Host "  Payload:  $payloadRoot"
    Write-Host "  Package:  $output"
    Write-Host "  SHA-256:  $checksumPath"
}
finally {
    if (Test-Path -LiteralPath $intermediateRoot) {
        Remove-Item -LiteralPath $intermediateRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
