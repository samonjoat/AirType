param(
    [string]$ProjectAssetsPath,
    [Parameter(Mandatory)] [string]$PythonRuntimeRoot,
    [Parameter(Mandatory)] [string]$RuntimeRequirementsPath,
    [string]$PublishedAppRoot,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [switch]$LocalAsrOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-SafePathSegment {
    param([Parameter(Mandatory)] [string]$Value)

    $invalid = [IO.Path]::GetInvalidFileNameChars()
    $characters = foreach ($character in $Value.ToCharArray()) {
        if ($character -in $invalid) { '_' } else { $character }
    }
    return (-join $characters)
}

function Get-XmlNodeText {
    param($Node)

    if ($null -eq $Node) {
        return $null
    }
    if ($Node -is [System.Xml.XmlNode]) {
        return $Node.InnerText.Trim()
    }
    return ([string]$Node).Trim()
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

function Get-MetadataValue {
    param(
        [string[]]$Lines,
        [Parameter(Mandatory)] [string]$Name
    )

    $prefix = "$Name`: "
    $line = $Lines | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($null -eq $line) {
        return $null
    }
    return $line.Substring($prefix.Length).Trim()
}

function Get-LicenseEvidenceFiles {
    param([Parameter(Mandatory)] [string]$Root)

    if (!(Test-Path -LiteralPath $Root)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object {
        $_.Name -match '^(licen[cs]e|copying|notice|copyright)(\..*)?$' -or
        $_.DirectoryName.Split([IO.Path]::DirectorySeparatorChar) -contains 'licenses'
    } | Sort-Object FullName -Unique)
}

function Copy-LicenseEvidenceFiles {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.IO.FileInfo[]]$Files,
        [Parameter(Mandatory)] [string]$SourceRoot,
        [Parameter(Mandatory)] [string]$DestinationRoot
    )

    $copied = @()
    foreach ($file in $Files) {
        $relative = Get-PortableRelativePath -BaseDirectory $SourceRoot -Path $file.FullName
        if ($relative.StartsWith("..", [StringComparison]::Ordinal)) {
            throw "License evidence escaped its package root: $($file.FullName)"
        }

        $destination = Join-Path $DestinationRoot (Join-Path "files" $relative)
        $destinationParent = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        $copied += $destination
    }
    return $copied
}

function Write-Declaration {
    param(
        [Parameter(Mandatory)] [string]$Destination,
        [string[]]$Lines
    )

    $Lines | Set-Content -LiteralPath $Destination -Encoding UTF8
}

$runtimeRoot = (Resolve-Path -LiteralPath $PythonRuntimeRoot).Path
$requirementsPath = (Resolve-Path -LiteralPath $RuntimeRequirementsPath).Path
$assetsPath = $null
$publishedRoot = $null
if (!$LocalAsrOnly) {
    if ([string]::IsNullOrWhiteSpace($ProjectAssetsPath) -or [string]::IsNullOrWhiteSpace($PublishedAppRoot)) {
        throw "ProjectAssetsPath and PublishedAppRoot are required for an app inventory."
    }
    $assetsPath = (Resolve-Path -LiteralPath $ProjectAssetsPath).Path
    $publishedRoot = (Resolve-Path -LiteralPath $PublishedAppRoot).Path
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($outputRoot) -ne "THIRD_PARTY_LICENSES") {
    throw "OutputDirectory must end in THIRD_PARTY_LICENSES."
}

$outputParent = Split-Path -Parent $outputRoot
if (!(Test-Path -LiteralPath $outputParent)) {
    throw "The parent of OutputDirectory must already exist: $outputParent"
}
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$manifestEntries = @()
if (!$LocalAsrOnly) {
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    if ($packageFolders.Count -eq 0) {
        throw "NuGet package folders are missing from project.assets.json."
    }

    $nugetLibraries = @($assets.libraries.PSObject.Properties | Where-Object {
        $_.Value.type -eq "package"
    } | Sort-Object Name)

    foreach ($library in $nugetLibraries) {
    $separator = $library.Name.LastIndexOf('/')
    if ($separator -le 0) {
        throw "Unexpected NuGet library key: $($library.Name)"
    }

    $packageId = $library.Name.Substring(0, $separator)
    $version = $library.Name.Substring($separator + 1)
    $packagePath = $library.Value.path
    $packageRoot = $null
    foreach ($folder in $packageFolders) {
        $candidate = Join-Path $folder ($packagePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (Test-Path -LiteralPath $candidate) {
            $packageRoot = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
    if ($null -eq $packageRoot) {
        throw "NuGet package cache entry is missing for $($library.Name)."
    }

    $nuspecPath = Get-ChildItem -LiteralPath $packageRoot -File -Filter "*.nuspec" | Select-Object -First 1
    $declaredLicense = $null
    $licenseUrl = $null
    if ($null -ne $nuspecPath) {
        [xml]$nuspec = Get-Content -LiteralPath $nuspecPath.FullName -Raw
        $metadata = $nuspec.package.metadata
        $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
        $licenseUrlNode = $metadata.SelectSingleNode("*[local-name()='licenseUrl']")
        if ($null -ne $licenseNode) {
            $declaredLicense = Get-XmlNodeText $licenseNode
        }
        if ($null -ne $licenseUrlNode) {
            $licenseUrl = Get-XmlNodeText $licenseUrlNode
        }
    }

    $destinationRoot = Join-Path $outputRoot (Join-Path "nuget" (Join-Path (Get-SafePathSegment $packageId) (Get-SafePathSegment $version)))
    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
    $evidence = @(Get-LicenseEvidenceFiles -Root $packageRoot)
    $copied = @(Copy-LicenseEvidenceFiles -Files $evidence -SourceRoot $packageRoot -DestinationRoot $destinationRoot)
    $declarationPath = Join-Path $destinationRoot "PACKAGE.txt"
    Write-Declaration -Destination $declarationPath -Lines @(
        "Package: $packageId",
        "Version: $version",
        "Declared license: $(if ([string]::IsNullOrWhiteSpace($declaredLicense)) { 'Not declared in nuspec' } else { $declaredLicense })",
        "License URL: $(if ([string]::IsNullOrWhiteSpace($licenseUrl)) { 'Not declared in nuspec' } else { $licenseUrl })",
        "Supplied license/notice files: $($copied.Count)"
    )

    $allEvidence = @($copied + $declarationPath | ForEach-Object {
        (Get-PortableRelativePath -BaseDirectory $outputRoot -Path $_).Replace('\', '/')
    })
        $manifestEntries += [pscustomobject][ordered]@{
            ecosystem = "NuGet"
            name = $packageId
            version = $version
            declaredLicense = $declaredLicense
            licenseUrl = $licenseUrl
            evidenceFiles = $allEvidence
        }
    }
}

$sitePackages = Join-Path $runtimeRoot "Lib\site-packages"
if (!(Test-Path -LiteralPath $sitePackages) -or !(Test-Path -LiteralPath $requirementsPath)) {
    throw "Portable Python runtime or requirements lock is incomplete."
}

$lockedPackages = @{}
foreach ($line in Get-Content -LiteralPath $requirementsPath) {
    if ($line -match '^\s*([^#=\s]+)==([^\s;]+)') {
        $lockedPackages[$matches[1].Replace('-', '_').ToLowerInvariant()] = $matches[2]
    }
}

$installedPackages = @{}
foreach ($distInfo in Get-ChildItem -LiteralPath $sitePackages -Directory -Filter "*.dist-info" | Sort-Object Name) {
    $metadataPath = Join-Path $distInfo.FullName "METADATA"
    if (!(Test-Path -LiteralPath $metadataPath)) {
        throw "Python package metadata is missing: $($distInfo.FullName)"
    }

    $metadataLines = [IO.File]::ReadAllLines($metadataPath)
    $packageName = Get-MetadataValue -Lines $metadataLines -Name "Name"
    $version = Get-MetadataValue -Lines $metadataLines -Name "Version"
    if ([string]::IsNullOrWhiteSpace($packageName) -or [string]::IsNullOrWhiteSpace($version)) {
        throw "Python package name/version metadata is incomplete: $metadataPath"
    }

    $normalizedName = $packageName.Replace('-', '_').ToLowerInvariant()
    $installedPackages[$normalizedName] = $version
    $declaredLicense = Get-MetadataValue -Lines $metadataLines -Name "License-Expression"
    if ([string]::IsNullOrWhiteSpace($declaredLicense)) {
        $declaredLicense = Get-MetadataValue -Lines $metadataLines -Name "License"
    }
    $licenseUrl = Get-MetadataValue -Lines $metadataLines -Name "Home-page"
    if ([string]::IsNullOrWhiteSpace($licenseUrl)) {
        $projectUrl = $metadataLines | Where-Object {
            $_ -match '^Project-URL:\s*(Homepage|Source|Repository|Code),\s*'
        } | Select-Object -First 1
        if ($null -ne $projectUrl) {
            $licenseUrl = ($projectUrl -split ',\s*', 2)[1]
        }
    }

    $destinationRoot = Join-Path $outputRoot (Join-Path "python" (Join-Path (Get-SafePathSegment $packageName) (Get-SafePathSegment $version)))
    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
    $evidence = @(Get-LicenseEvidenceFiles -Root $distInfo.FullName)
    $copied = @(Copy-LicenseEvidenceFiles -Files $evidence -SourceRoot $distInfo.FullName -DestinationRoot $destinationRoot)
    $declarationPath = Join-Path $destinationRoot "PACKAGE.txt"
    Write-Declaration -Destination $declarationPath -Lines @(
        "Package: $packageName",
        "Version: $version",
        "Declared license: $(if ([string]::IsNullOrWhiteSpace($declaredLicense)) { 'Not declared in wheel metadata' } else { $declaredLicense })",
        "Project URL: $(if ([string]::IsNullOrWhiteSpace($licenseUrl)) { 'Not declared in wheel metadata' } else { $licenseUrl })",
        "Supplied license/notice files: $($copied.Count)"
    )

    $allEvidence = @($copied + $declarationPath | ForEach-Object {
        (Get-PortableRelativePath -BaseDirectory $outputRoot -Path $_).Replace('\', '/')
    })
    $manifestEntries += [pscustomobject][ordered]@{
        ecosystem = "Python"
        name = $packageName
        version = $version
        declaredLicense = $declaredLicense
        licenseUrl = $licenseUrl
        evidenceFiles = $allEvidence
    }
}

foreach ($lockedPackage in $lockedPackages.GetEnumerator()) {
    if (!$installedPackages.ContainsKey($lockedPackage.Key)) {
        throw "Locked Python package is missing from the portable runtime: $($lockedPackage.Key)==$($lockedPackage.Value)"
    }
    if ($installedPackages[$lockedPackage.Key] -ne $lockedPackage.Value) {
        throw "Portable Python package version differs from lock: $($lockedPackage.Key)"
    }
}

$pythonExe = Join-Path $runtimeRoot "python.exe"
$pythonLicense = Join-Path $runtimeRoot "LICENSE.txt"
if (!(Test-Path -LiteralPath $pythonExe) -or !(Test-Path -LiteralPath $pythonLicense)) {
    throw "Portable CPython executable or license is missing."
}
$pythonVersion = (& $pythonExe -c "import platform; print(platform.python_version())").Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($pythonVersion)) {
    throw "Unable to read the portable CPython version."
}
$cpythonDestination = Join-Path $outputRoot (Join-Path "python" "CPython-$pythonVersion")
New-Item -ItemType Directory -Force -Path $cpythonDestination | Out-Null
Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $cpythonDestination "LICENSE.txt") -Force
$manifestEntries += [pscustomobject][ordered]@{
    ecosystem = "Runtime"
    name = "CPython"
    version = $pythonVersion
    declaredLicense = "Python Software Foundation License"
    licenseUrl = "https://docs.python.org/3/license.html"
    evidenceFiles = @("python/CPython-$pythonVersion/LICENSE.txt")
}

$vcRuntimeManifestPath = Join-Path $runtimeRoot "visual-cpp-runtime.json"
if (!(Test-Path -LiteralPath $vcRuntimeManifestPath -PathType Leaf)) {
    throw "Portable runtime Visual C++ metadata is missing."
}
$vcRuntime = Get-Content -LiteralPath $vcRuntimeManifestPath -Raw | ConvertFrom-Json
$vcRuntimeLicense = Join-Path $runtimeRoot ([string]$vcRuntime.licenseRelativePath).Replace('/', '\')
if (!(Test-Path -LiteralPath $vcRuntimeLicense -PathType Leaf)) {
    throw "Portable runtime Visual C++ license is missing."
}
$vcRuntimeDestination = Join-Path $outputRoot (Join-Path "runtime" "Microsoft-Visual-Cpp-Runtime-$($vcRuntime.version)")
New-Item -ItemType Directory -Force -Path $vcRuntimeDestination | Out-Null
$vcRuntimeManifestDestination = Join-Path $vcRuntimeDestination "visual-cpp-runtime.json"
$vcRuntimeLicenseDestination = Join-Path $vcRuntimeDestination "license.rtf"
Copy-Item -LiteralPath $vcRuntimeManifestPath -Destination $vcRuntimeManifestDestination -Force
Copy-Item -LiteralPath $vcRuntimeLicense -Destination $vcRuntimeLicenseDestination -Force
$manifestEntries += [pscustomobject][ordered]@{
    ecosystem = "Runtime"
    name = [string]$vcRuntime.product
    version = [string]$vcRuntime.version
    declaredLicense = "Microsoft Software License Terms"
    licenseUrl = [string]$vcRuntime.documentationUrl
    evidenceFiles = @(
        (Get-PortableRelativePath -BaseDirectory $outputRoot -Path $vcRuntimeManifestDestination).Replace('\', '/'),
        (Get-PortableRelativePath -BaseDirectory $outputRoot -Path $vcRuntimeLicenseDestination).Replace('\', '/')
    )
}

if (!$LocalAsrOnly) {
    $runtimeConfigPath = Join-Path $publishedRoot "AirType.runtimeconfig.json"
    if (!(Test-Path -LiteralPath $runtimeConfigPath)) {
        throw "Published AirType runtime configuration is missing."
    }
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $includedFrameworksProperty = $runtimeConfig.runtimeOptions.PSObject.Properties["includedFrameworks"]
    $frameworkProperty = $runtimeConfig.runtimeOptions.PSObject.Properties["framework"]
    $frameworks = if ($null -ne $includedFrameworksProperty) {
        @($includedFrameworksProperty.Value)
    } elseif ($null -ne $frameworkProperty) {
        @($frameworkProperty.Value)
    } else {
        @()
    }
    $frameworkVersions = @($frameworks | ForEach-Object {
        "$($_.name) $($_.version)"
    })
    if ($frameworkVersions.Count -eq 0) {
        throw "Published AirType package does not declare self-contained .NET frameworks."
    }

    $dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source
    $dotnetLicense = Join-Path $dotnetRoot "LICENSE.txt"
    $dotnetNotices = Join-Path $dotnetRoot "ThirdPartyNotices.txt"
    if (!(Test-Path -LiteralPath $dotnetLicense) -or !(Test-Path -LiteralPath $dotnetNotices)) {
        throw ".NET SDK license or third-party notices are missing."
    }
    $dotnetDestination = Join-Path $outputRoot "runtime\dotnet"
    New-Item -ItemType Directory -Force -Path $dotnetDestination | Out-Null
    Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $dotnetDestination "LICENSE.txt") -Force
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $dotnetDestination "ThirdPartyNotices.txt") -Force
    $manifestEntries += [pscustomobject][ordered]@{
        ecosystem = "Runtime"
        name = ".NET self-contained frameworks"
        version = ($frameworkVersions -join "; ")
        declaredLicense = "MIT and included third-party terms"
        licenseUrl = "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"
        evidenceFiles = @(
            "runtime/dotnet/LICENSE.txt",
            "runtime/dotnet/ThirdPartyNotices.txt"
        )
    }

    $interSourceRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $assetsPath)) "Fonts\Inter"
    $interLicense = Join-Path $publishedRoot "Fonts\Inter\OFL.txt"
    $interFonts = @(
        [pscustomobject]@{
            name = "Inter-Italic-VariableFont_opsz,wght.ttf"
            sha256 = "6136f73372fedc37b80fd1a8ec3e21734073ff17376f3672718f186779672e7a"
        },
        [pscustomobject]@{
            name = "Inter-VariableFont_opsz,wght.ttf"
            sha256 = "0be2399ea925f1f83ff974764761da9860ec50742ed29a5d4c1ffd0c5c7ac3a8"
        }
    )
    if (!(Test-Path -LiteralPath $interLicense)) {
        throw "Published Inter OFL license is missing."
    }
    foreach ($interFont in $interFonts) {
        $fontPath = Join-Path $interSourceRoot $interFont.name
        if (!(Test-Path -LiteralPath $fontPath)) {
            throw "Inter variable font source is missing: $($interFont.name)"
        }
        $actualFontHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fontPath).Hash.ToLowerInvariant()
        if ($actualFontHash -ne $interFont.sha256) {
            throw "Inter variable font hash changed without inventory review: $($interFont.name)"
        }
    }

    $interDestination = Join-Path $outputRoot "assets\Inter"
    New-Item -ItemType Directory -Force -Path $interDestination | Out-Null
    Copy-Item -LiteralPath $interLicense -Destination (Join-Path $interDestination "OFL.txt") -Force
    $interDeclaration = Join-Path $interDestination "ASSET.txt"
    Write-Declaration -Destination $interDeclaration -Lines @(
        "Asset: Inter variable fonts",
        "Declared license: SIL Open Font License 1.1",
        "Upstream: https://github.com/google/fonts/tree/main/ofl/inter",
        "Inter-Italic-VariableFont_opsz,wght.ttf SHA-256: $($interFonts[0].sha256)",
        "Inter-VariableFont_opsz,wght.ttf SHA-256: $($interFonts[1].sha256)"
    )
    $manifestEntries += [pscustomobject][ordered]@{
        ecosystem = "Asset"
        name = "Inter variable fonts"
        version = "sha256-pinned"
        declaredLicense = "SIL Open Font License 1.1"
        licenseUrl = "https://github.com/google/fonts/blob/main/ofl/inter/OFL.txt"
        evidenceFiles = @(
            "assets/Inter/ASSET.txt",
            "assets/Inter/OFL.txt"
        )
    }
}

$modelDestination = Join-Path $outputRoot "model\Systran-faster-whisper-small.en"
New-Item -ItemType Directory -Force -Path $modelDestination | Out-Null
$modelDeclaration = Join-Path $modelDestination "MODEL.txt"
Write-Declaration -Destination $modelDeclaration -Lines @(
    "Model: Systran/faster-whisper-small.en",
    "Declared license: MIT",
    "Model card: https://huggingface.co/Systran/faster-whisper-small.en",
    "Upstream model: OpenAI Whisper small.en",
    "Upstream license: https://github.com/openai/whisper/blob/main/LICENSE",
    $(if ($LocalAsrOnly) {
        "The model weights are included in this Local ASR bundle."
    } else {
        "The model is downloaded on demand and is not present in the base app package."
    })
)
$modelLicense = Join-Path $modelDestination "LICENSE.txt"
Write-Declaration -Destination $modelLicense -Lines @(
    "MIT License",
    "",
    "Copyright (c) 2022 OpenAI",
    "",
    "Permission is hereby granted, free of charge, to any person obtaining a copy",
    'of this software and associated documentation files (the "Software"), to deal',
    "in the Software without restriction, including without limitation the rights",
    "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell",
    "copies of the Software, and to permit persons to whom the Software is",
    "furnished to do so, subject to the following conditions:",
    "",
    "The above copyright notice and this permission notice shall be included in all",
    "copies or substantial portions of the Software.",
    "",
    'THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR',
    "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,",
    "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE",
    "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER",
    "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,",
    "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE",
    "SOFTWARE."
)
$manifestEntries += [pscustomobject][ordered]@{
    ecosystem = "Model"
    name = "Systran/faster-whisper-small.en"
    version = "small.en"
    declaredLicense = "MIT"
    licenseUrl = "https://huggingface.co/Systran/faster-whisper-small.en"
    evidenceFiles = @(
        "model/Systran-faster-whisper-small.en/MODEL.txt",
        "model/Systran-faster-whisper-small.en/LICENSE.txt"
    )
}

$manifest = [ordered]@{
    schemaVersion = 1
    inventoryScope = $(if ($LocalAsrOnly) {
        "Local ASR runtime dependencies and included model"
    } else {
        "Dependencies and optional model used by AirType"
    })
    entries = @($manifestEntries | Sort-Object ecosystem, name, version)
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot "manifest.json") -Encoding UTF8

Write-Host "Collected $($manifestEntries.Count) third-party dependency records in $outputRoot"
