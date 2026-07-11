param(
    [Parameter(Mandatory)] [string]$PackagePath,
    [Parameter(Mandatory)] [string]$ExpectedVersion,
    [ValidateSet("NotSigned", "Valid")] [string]$ExpectedInstallerSignatureStatus = "NotSigned",
    [ValidateSet("NotSigned", "Valid")] [string]$ExpectedPayloadSignatureStatus = "NotSigned",
    [string]$ExpectedSourceCommit,
    [string]$ExpectedSourceBranch,
    [switch]$KeepExtraction
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-InstallerCondition {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Invoke-WixChecked {
    param(
        [Parameter(Mandatory)] [string]$Wix,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$FailureMessage
    )

    & $Wix @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "ExpectedVersion must use x.y.z format."
}
if (![string]::IsNullOrWhiteSpace($ExpectedSourceCommit) -and
    $ExpectedSourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "ExpectedSourceCommit must be a full 40-character Git SHA."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$package = (Resolve-Path -LiteralPath $PackagePath).Path
Assert-InstallerCondition ([IO.Path]::GetExtension($package) -eq ".msi") `
    "Windows installer package must be an MSI."
$checksumPath = "$package.sha256"
Assert-InstallerCondition (Test-Path -LiteralPath $checksumPath -PathType Leaf) `
    "Windows installer SHA-256 sidecar is missing."

$expectedHash = (Get-Content -LiteralPath $checksumPath -Raw).
    Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-InstallerCondition ($expectedHash -eq $actualHash) `
    "Windows installer checksum does not match its sidecar."

$installerSignature = Get-AuthenticodeSignature -LiteralPath $package
Assert-InstallerCondition ($installerSignature.Status.ToString() -eq $ExpectedInstallerSignatureStatus) `
    "Windows installer signature status is $($installerSignature.Status); expected $ExpectedInstallerSignatureStatus."

$vcRuntimeLockPath = Join-Path $PSScriptRoot "local-asr-vc-runtime.lock.json"
$vcRuntimeLock = Get-Content -LiteralPath $vcRuntimeLockPath -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot "wix-tool.ps1")
$wix = Get-PinnedWixExecutable -Version $vcRuntimeLock.wixToolVersion

$tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')
$validationRoot = Join-Path $tempRoot ("ATMSI_" + [Guid]::NewGuid().ToString("N"))
Assert-InstallerCondition `
    ($validationRoot.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) `
    "MSI validation directory escaped the temporary directory."
$decompiledPath = Join-Path $validationRoot "d.wxs"
$intermediateRoot = Join-Path $validationRoot "i"
$administrativeRoot = Join-Path $validationRoot "a"
New-Item -ItemType Directory -Force -Path $validationRoot, $intermediateRoot, $administrativeRoot | Out-Null

try {
    Invoke-WixChecked `
        -Wix $wix `
        -Arguments @(
            "msi", "validate", $package,
            "-intermediateFolder", $intermediateRoot
        ) `
        -FailureMessage "Windows installer ICE validation failed."

    Invoke-WixChecked `
        -Wix $wix `
        -Arguments @(
            "msi", "decompile", $package,
            "-sui",
            "-intermediateFolder", $intermediateRoot,
            "-out", $decompiledPath
        ) `
        -FailureMessage "Windows installer decompilation failed."

    [xml]$decompiled = Get-Content -LiteralPath $decompiledPath -Raw
    $namespaces = [Xml.XmlNamespaceManager]::new($decompiled.NameTable)
    $namespaces.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")
    $packageNode = $decompiled.SelectSingleNode("/w:Wix/w:Package", $namespaces)
    Assert-InstallerCondition ($null -ne $packageNode) `
        "Decompiled installer is missing the WiX Package element."
    Assert-InstallerCondition ($packageNode.GetAttribute("Name") -eq "AirType") `
        "Windows installer product name is not AirType."
    Assert-InstallerCondition ($packageNode.GetAttribute("Manufacturer") -eq "AirType") `
        "Windows installer manufacturer is not AirType."
    Assert-InstallerCondition ($packageNode.GetAttribute("Version") -eq $ExpectedVersion) `
        "Windows installer version does not match the expected release version."
    Assert-InstallerCondition `
        ($packageNode.GetAttribute("UpgradeCode").Trim('{}') -eq "0CB1FBA8-B8E7-489F-8A10-56D1CBE441DF") `
        "Windows installer upgrade identity is incorrect."
    Assert-InstallerCondition ($packageNode.GetAttribute("ProductCode") -match '^\{[0-9A-Fa-f-]{36}\}$') `
        "Windows installer product code is missing or invalid."

    Assert-InstallerCondition `
        ($null -ne $decompiled.SelectSingleNode("//w:StandardDirectory[@Id='ProgramFiles64Folder']", $namespaces)) `
        "Windows installer does not target the x64 Program Files directory."
    Assert-InstallerCondition `
        ($null -ne $decompiled.SelectSingleNode("//w:MajorUpgrade", $namespaces)) `
        "Windows installer has no major-upgrade policy."
    Assert-InstallerCondition `
        ($null -ne $decompiled.SelectSingleNode("//w:Shortcut[@Name='AirType']", $namespaces)) `
        "Windows installer has no AirType Start menu shortcut."
    Assert-InstallerCondition `
        ($null -ne $decompiled.SelectSingleNode("//w:Property[@Id='ARPPRODUCTICON']", $namespaces)) `
        "Windows installer has no Add/Remove Programs icon metadata."
    Assert-InstallerCondition `
        ($decompiled.SelectNodes("//w:File", $namespaces).Count -gt 100) `
        "Windows installer payload is unexpectedly small."

    $administrativeProcess = Start-Process `
        -FilePath "msiexec.exe" `
        -ArgumentList @(
            "/a", ('"{0}"' -f $package),
            "/qn", "/norestart", ('TARGETDIR="{0}"' -f $administrativeRoot)
        ) `
        -Wait `
        -PassThru
    $administrativeExitCode = $administrativeProcess.ExitCode
    Assert-InstallerCondition ($administrativeExitCode -in @(0, 3010)) `
        "Windows installer administrative extraction failed with exit code $administrativeExitCode."

    $airTypeExecutables = @(
        Get-ChildItem -LiteralPath $administrativeRoot -Recurse -File -Filter "AirType.exe")
    Assert-InstallerCondition ($airTypeExecutables.Count -eq 1) `
        "Windows installer must contain exactly one AirType.exe."
    $airTypeExe = $airTypeExecutables[0].FullName
    $payloadRoot = Split-Path -Parent $airTypeExe
    $manifestPath = Join-Path $payloadRoot "release-manifest.json"
    Assert-InstallerCondition (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
        "Windows installer payload is missing release-manifest.json."
    Assert-InstallerCondition `
        (Test-Path -LiteralPath (Join-Path $payloadRoot "System.Private.CoreLib.dll") -PathType Leaf) `
        "Windows installer payload is missing the self-contained .NET runtime."
    Assert-InstallerCondition `
        (Test-Path -LiteralPath (Join-Path $payloadRoot "Microsoft.WindowsAppRuntime.Bootstrap.dll") -PathType Leaf) `
        "Windows installer payload is missing the self-contained Windows App SDK runtime."

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-InstallerCondition ($manifest.schemaVersion -eq 1) `
        "Windows installer payload has an unsupported release manifest schema."
    Assert-InstallerCondition ($manifest.product -eq "AirType") `
        "Windows installer payload manifest product is not AirType."
    Assert-InstallerCondition ($manifest.version -eq $ExpectedVersion) `
        "Windows installer payload manifest version does not match."
    Assert-InstallerCondition ($manifest.platform -eq "win-x64") `
        "Windows installer payload platform is not win-x64."
    Assert-InstallerCondition ($manifest.sourceCommit -match '^[0-9a-f]{40}$') `
        "Windows installer payload source commit is invalid."
    if (![string]::IsNullOrWhiteSpace($ExpectedSourceCommit)) {
        Assert-InstallerCondition `
            ($manifest.sourceCommit -eq $ExpectedSourceCommit.ToLowerInvariant()) `
            "Windows installer payload source commit does not match the expected commit."
    }
    if (![string]::IsNullOrWhiteSpace($ExpectedSourceBranch)) {
        Assert-InstallerCondition ($manifest.sourceBranch -eq $ExpectedSourceBranch) `
            "Windows installer payload source branch does not match the expected branch."
    }

    $versionInfo = (Get-Item -LiteralPath $airTypeExe).VersionInfo
    Assert-InstallerCondition ($versionInfo.ProductVersion -eq $ExpectedVersion) `
        "Installed executable product version does not match."
    Assert-InstallerCondition ($versionInfo.FileVersion -eq "$ExpectedVersion.0") `
        "Installed executable file version does not match."
    $payloadHash = (Get-FileHash -LiteralPath $airTypeExe -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-InstallerCondition ($manifest.executableSha256 -eq $payloadHash) `
        "Windows installer payload executable hash does not match its release manifest."

    $payloadSignature = Get-AuthenticodeSignature -LiteralPath $airTypeExe
    Assert-InstallerCondition ($payloadSignature.Status.ToString() -eq $ExpectedPayloadSignatureStatus) `
        "Windows installer payload signature status is $($payloadSignature.Status); expected $ExpectedPayloadSignatureStatus."
    Assert-InstallerCondition `
        ([bool]$manifest.signed -eq ($ExpectedPayloadSignatureStatus -eq "Valid")) `
        "Windows installer payload signature state does not match its release manifest."

    [pscustomobject][ordered]@{
        package = $package
        version = $ExpectedVersion
        sha256 = $actualHash
        installerSignatureStatus = $installerSignature.Status.ToString()
        payloadSignatureStatus = $payloadSignature.Status.ToString()
        sourceCommit = $manifest.sourceCommit
        productCode = $packageNode.GetAttribute("ProductCode")
        upgradeCode = $packageNode.GetAttribute("UpgradeCode")
        payloadFileCount = @(
            Get-ChildItem -LiteralPath $payloadRoot -Recurse -File -Force).Count
    } | ConvertTo-Json -Compress | Write-Host
}
finally {
    if (!$KeepExtraction -and (Test-Path -LiteralPath $validationRoot)) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    elseif ($KeepExtraction) {
        Write-Host "MSI validation extraction retained at $validationRoot"
    }
}
