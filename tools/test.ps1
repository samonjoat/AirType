param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testProject = Join-Path $repoRoot "AirType.Tests\AirType.Tests.csproj"
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$testStorageRoot = [IO.Path]::GetFullPath(
    (Join-Path $tempRoot ("AirType.Tests\" + [Guid]::NewGuid().ToString("N"))))
$testStoragePrefix = $tempRoot + '\'

if (!$testStorageRoot.StartsWith($testStoragePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Test storage must remain under the system temporary directory."
}

$storageVariable = "AIRTYPE_STORAGE_ROOT"
$hadStorageVariable = Test-Path "Env:$storageVariable"
$previousStorageRoot = [Environment]::GetEnvironmentVariable($storageVariable)
$windowsAppSdkTestProperties = @(
    "-p:WindowsAppSdkBootstrapInitialize=false",
    "-p:WindowsAppSdkUndockedRegFreeWinRTInitialize=false"
)

function Invoke-CheckedDotNet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

Push-Location $repoRoot
try {
    [Environment]::SetEnvironmentVariable($storageVariable, $testStorageRoot)

    if (!$NoRestore) {
        Invoke-CheckedDotNet `
            -Arguments (@("restore", $testProject) + $windowsAppSdkTestProperties) `
            -FailureMessage "AirType test restore failed."
    }

    Invoke-CheckedDotNet `
        -Arguments (@(
            "clean", $testProject,
            "--configuration", $Configuration,
            "--verbosity", "minimal"
        ) + $windowsAppSdkTestProperties) `
        -FailureMessage "AirType test clean failed."

    Invoke-CheckedDotNet `
        -Arguments (@(
            "test", $testProject,
            "--configuration", $Configuration,
            "--no-restore",
            "--verbosity", "minimal",
            "--logger", "console;verbosity=minimal",
            "--blame-hang-timeout", "300s",
            "--blame-hang-dump-type", "none"
        ) + $windowsAppSdkTestProperties) `
        -FailureMessage "AirType xUnit tests failed."
}
finally {
    Pop-Location

    if ($hadStorageVariable) {
        [Environment]::SetEnvironmentVariable($storageVariable, $previousStorageRoot)
    }
    else {
        [Environment]::SetEnvironmentVariable($storageVariable, $null)
    }

    $verifiedStorageRoot = [IO.Path]::GetFullPath($testStorageRoot)
    if (!$verifiedStorageRoot.StartsWith($testStoragePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test storage outside the system temporary directory."
    }
    if (Test-Path -LiteralPath $verifiedStorageRoot) {
        Remove-Item -LiteralPath $verifiedStorageRoot -Recurse -Force
    }
}
