function Test-PinnedWixVersion {
    param(
        [Parameter(Mandatory)] [string]$Actual,
        [Parameter(Mandatory)] [string]$Expected
    )

    return $Actual.Equals($Expected, [StringComparison]::Ordinal) -or
        $Actual.StartsWith("$Expected+", [StringComparison]::Ordinal)
}

function Get-PinnedWixExecutable {
    param([Parameter(Mandatory)] [string]$Version)

    $LASTEXITCODE = 0
    $installed = Get-Command wix.exe -ErrorAction SilentlyContinue
    if ($null -ne $installed) {
        $installedVersion = (& $installed.Source --version | Select-Object -First 1).Trim()
        if ($LASTEXITCODE -eq 0 -and
            (Test-PinnedWixVersion -Actual $installedVersion -Expected $Version)) {
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
        $installOutput = @(& dotnet tool install wix --tool-path $toolRoot --version $Version --no-cache)
        $installExitCode = $LASTEXITCODE
        foreach ($line in $installOutput) {
            Write-Host $line
        }
        if ($installExitCode -ne 0) {
            throw "Failed to install pinned WiX Toolset $Version."
        }
    }

    $toolVersion = (& $toolExecutable --version | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0 -or
        !(Test-PinnedWixVersion -Actual $toolVersion -Expected $Version)) {
        throw "Pinned WiX Toolset version validation failed."
    }

    return $toolExecutable
}
