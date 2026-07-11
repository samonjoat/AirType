param(
    [string]$OutputDirectory,
    [switch]$AllowDirty,
    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".temp"))
$developerProfileName = Split-Path $env:USERPROFILE -Leaf
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $tempRoot "public-snapshot"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)

if (!$outputRoot.StartsWith($tempRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Public snapshot output must remain under $tempRoot."
}
if ($outputRoot -eq $repoRoot -or $outputRoot -eq $tempRoot) {
    throw "Refusing unsafe public snapshot output path: $outputRoot"
}

function Remove-SafeSnapshotDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (!$fullPath.StartsWith($tempRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove snapshot path outside .temp: $fullPath"
    }
    if ($fullPath -eq $tempRoot) {
        throw "Refusing to remove the .temp root."
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string]$FailureMessage
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw $FailureMessage
        }
    }
    finally {
        Pop-Location
    }
}

function Clear-GeneratedValidationArtifacts {
    $generatedDirectoryNames = @(".pytest_cache", "__pycache__", "bin", "obj")
    $generatedDirectories = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -Directory -Force | Where-Object {
        $_.Name -in $generatedDirectoryNames
    } | Sort-Object { $_.FullName.Length } -Descending)

    foreach ($directory in $generatedDirectories) {
        $fullPath = [IO.Path]::GetFullPath($directory.FullName)
        if (!$fullPath.StartsWith($outputRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove generated validation path outside snapshot: $fullPath"
        }
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Test-PublicSourcePath {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $normalized = $RelativePath.Replace('\', '/')
    $exactFiles = @(
        ".editorconfig",
        ".gitattributes",
        "AirType.sln",
        "CODE_OF_CONDUCT.md",
        "CODE_SIGNING_POLICY.md",
        "CONTRIBUTING.md",
        "DCO.txt",
        "global.json",
        "LICENSE",
        "PRIVACY.md",
        "README.md",
        "SECURITY.md",
        "THIRD_PARTY_NOTICES.md",
        "TRADEMARKS.md",
        "docs/ARCHITECTURE.md",
        "docs/INSTALL.md",
        "docs/RELEASING.md"
    )
    if ($normalized -in $exactFiles) {
        return $true
    }

    foreach ($prefix in @(
        ".github/", "AirType/", "AirType.LocalAsrWorker/", "AirType.Tests/",
        "docs/release-acceptance/", "packaging/", "tools/"
    )) {
        if ($normalized.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

$gitStatus = @(git -C $repoRoot status --porcelain)
if (!$AllowDirty -and $gitStatus.Count -ne 0) {
    throw "Public snapshot generation requires a clean worktree unless -AllowDirty is explicit."
}

$candidateFiles = @(git -C $repoRoot ls-files)
if ($AllowDirty) {
    $candidateFiles += @(git -C $repoRoot ls-files --others --exclude-standard)
}
$candidateFiles = @($candidateFiles | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0 -or $candidateFiles.Count -eq 0) {
    throw "Unable to enumerate source files from Git."
}

$selectedFiles = @($candidateFiles | Where-Object {
    (Test-PublicSourcePath -RelativePath $_) -and
    (Test-Path -LiteralPath (Join-Path $repoRoot $_) -PathType Leaf)
})
if ($selectedFiles.Count -eq 0) {
    throw "Public snapshot allowlist selected no files."
}

Remove-SafeSnapshotDirectory -Path $outputRoot
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

foreach ($relativePath in $selectedFiles) {
    $source = Join-Path $repoRoot $relativePath
    $destination = Join-Path $outputRoot $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$publicGitIgnore = @'
.vs/
**/bin/
**/obj/
*.user
*.suo
*.rsuser

.temp/
release-artifacts/

AirType.LocalAsrWorker/.venv/
AirType.LocalAsrWorker/runtime/
AirType.LocalAsrWorker/models/
AirType.LocalAsrWorker/local-asr-manifest.json
AirType.LocalAsrWorker/.pytest_cache/
AirType.LocalAsrWorker/**/__pycache__/
AirType.LocalAsrWorker/build/
AirType.LocalAsrWorker/*.egg-info/

*.db
*.db-shm
*.db-wal
*.log
*.key
*.pem
.env
appsettings.Production.json
'@
[IO.File]::WriteAllText(
    (Join-Path $outputRoot ".gitignore"),
    $publicGitIgnore.TrimStart() + "`n",
    [Text.UTF8Encoding]::new($false))

$allowedTopLevel = @(
    ".editorconfig", ".gitattributes", ".github", ".gitignore",
    "AirType", "AirType.LocalAsrWorker", "AirType.Tests", "AirType.sln",
    "CODE_OF_CONDUCT.md", "CODE_SIGNING_POLICY.md", "CONTRIBUTING.md", "DCO.txt", "docs", "global.json",
    "LICENSE", "packaging", "PRIVACY.md", "README.md", "SECURITY.md",
    "THIRD_PARTY_NOTICES.md", "tools", "TRADEMARKS.md"
)
$unexpectedTopLevel = @(Get-ChildItem -LiteralPath $outputRoot -Force | Where-Object {
    $_.Name -notin $allowedTopLevel
})
if ($unexpectedTopLevel.Count -gt 0) {
    throw "Unexpected public snapshot root entries: $($unexpectedTopLevel.Name -join ', ')"
}

$forbiddenSegments = @(
    ".git", ".temp", ".venv", ".vs", "__pycache__", "bin", "mockups", "obj"
)
$forbiddenTopLevel = @(
    "benchmark-results", "design-assets", "implementation-plans", "planning",
    "prompts", "release-artifacts", "scripts"
)
$forbiddenPathPrefixes = @(
    "AirType.LocalAsrWorker/.venv/",
    "AirType.LocalAsrWorker/models/",
    "AirType.LocalAsrWorker/runtime/"
)
$forbiddenExtensions = @(
    ".db", ".dll", ".exe", ".log", ".onnx", ".pyd", ".pyc", ".zip"
)
$binaryExtensions = @(
    ".dll", ".exe", ".ico", ".jpeg", ".jpg", ".onnx", ".png", ".pyd",
    ".ttf", ".wav", ".zip"
)
$snapshotFiles = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File -Force)

foreach ($file in $snapshotFiles) {
    $relativePath = [IO.Path]::GetRelativePath($outputRoot, $file.FullName)
    $normalizedRelativePath = $relativePath.Replace('\', '/')
    $forbiddenPrefix = $forbiddenPathPrefixes | Where-Object {
        $normalizedRelativePath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -ne $forbiddenPrefix) {
        throw "Forbidden generated path '$forbiddenPrefix' in public snapshot: $relativePath"
    }
    $segments = $relativePath.Split([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ($segments[0] -in $forbiddenTopLevel) {
        throw "Forbidden private root '$($segments[0])' in public snapshot: $relativePath"
    }
    $forbiddenSegment = $segments | Where-Object { $_ -in $forbiddenSegments } | Select-Object -First 1
    if ($null -ne $forbiddenSegment) {
        throw "Forbidden path segment '$forbiddenSegment' in public snapshot: $relativePath"
    }
    if ($file.Extension.ToLowerInvariant() -in $forbiddenExtensions) {
        throw "Forbidden generated/binary file type in public snapshot: $relativePath"
    }
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse points are not permitted in the public snapshot: $relativePath"
    }

    if ($file.Extension.ToLowerInvariant() -notin $binaryExtensions) {
        $content = [IO.File]::ReadAllText($file.FullName)
        foreach ($forbiddenText in @($env:USERPROFILE, $repoRoot, $developerProfileName)) {
            if (![string]::IsNullOrWhiteSpace($forbiddenText) -and
                $content.IndexOf($forbiddenText, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Developer-specific content found in public snapshot: $relativePath"
            }
        }
        foreach ($secretPattern in @(
            'AIza[0-9A-Za-z_-]{30,}',
            '(?i)\bsk-(?:or-v1-)?[a-z0-9_-]{20,}\b',
            '(?i)\bgsk_[a-z0-9]{20,}\b'
        )) {
            if ($content -match $secretPattern) {
                throw "Potential credential pattern found in public snapshot: $relativePath"
            }
        }
    }
}

foreach ($requiredPath in @(
    "AirType.sln",
    "AirType/AirType.csproj",
    "AirType.LocalAsrWorker/pyproject.toml",
    "AirType.Tests/AirType.Tests.csproj",
    "AirType/Fonts/Inter/OFL.txt",
    "AirType/Sounds/PROVENANCE.md",
    "CODE_SIGNING_POLICY.md",
    ".github/CODEOWNERS",
    ".github/workflows/codeql.yml",
    ".github/workflows/sign-release.yml",
    "packaging/windows/AirType.wxs",
    "tools/build-release.ps1",
    "tools/build-windows-installer.ps1",
    "tools/finalize-signpath-installer.ps1",
    "tools/finalize-signpath-release.ps1",
    "tools/release-archive.ps1",
    "tools/verify-windows-installer.ps1",
    "tools/wix-tool.ps1",
    "tools/build-public-snapshot.ps1",
    "tools/test.ps1"
)) {
    if (!(Test-Path -LiteralPath (Join-Path $outputRoot $requiredPath) -PathType Leaf)) {
        throw "Required public snapshot file is missing: $requiredPath"
    }
}

if (Test-Path -LiteralPath (Join-Path $outputRoot ".git")) {
    throw "Public snapshot must not contain Git history or refs."
}

if (!$SkipValidation) {
    Invoke-CheckedCommand -FilePath "dotnet" `
        -Arguments @("restore", ".\AirType.sln") `
        -WorkingDirectory $outputRoot `
        -FailureMessage "Public snapshot restore failed."
    Invoke-CheckedCommand -FilePath "dotnet" `
        -Arguments @("clean", ".\AirType\AirType.csproj", "--verbosity", "minimal") `
        -WorkingDirectory $outputRoot `
        -FailureMessage "Public snapshot clean failed."
    Invoke-CheckedCommand -FilePath "dotnet" `
        -Arguments @("build", ".\AirType\AirType.csproj", "--no-restore", "--verbosity", "minimal") `
        -WorkingDirectory $outputRoot `
        -FailureMessage "Public snapshot build failed."
    Invoke-CheckedCommand -FilePath "pwsh" `
        -Arguments @("-NoProfile", "-File", ".\tools\test.ps1", "-NoRestore") `
        -WorkingDirectory $outputRoot `
        -FailureMessage "Public snapshot xUnit tests failed."
    Invoke-CheckedCommand -FilePath "python" `
        -Arguments @(".\tools\generate-notification-sound-candidates.py", "--verify-production") `
        -WorkingDirectory $outputRoot `
        -FailureMessage "Public snapshot notification asset verification failed."
    Invoke-CheckedCommand -FilePath "python" `
        -Arguments @("-m", "pytest", ".\tests", "-q") `
        -WorkingDirectory (Join-Path $outputRoot "AirType.LocalAsrWorker") `
        -FailureMessage "Public snapshot Local ASR worker tests failed."
}

$snapshotFiles = $null
Clear-GeneratedValidationArtifacts
$remainingGeneratedDirectories = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -Directory -Force | Where-Object {
    $_.Name -in @(".pytest_cache", "__pycache__", "bin", "obj")
})
if ($remainingGeneratedDirectories.Count -gt 0) {
    throw "Generated validation directories remain in the public snapshot."
}
$snapshotFiles = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File -Force)
$totalBytes = ($snapshotFiles | Measure-Object -Property Length -Sum).Sum
Write-Host "Public snapshot created and verified."
Write-Host "  Files:  $($snapshotFiles.Count)"
Write-Host "  Bytes:  $totalBytes"
Write-Host "  Output: $outputRoot"
