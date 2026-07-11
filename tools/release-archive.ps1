function Get-AirTypePortableRelativePath {
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

function New-AirTypeDeterministicZip {
    param(
        [Parameter(Mandatory)] [string]$SourceDirectory,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    $sourceRoot = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
    if (!(Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        throw "Release archive source directory does not exist: $sourceRoot"
    }

    $destination = [IO.Path]::GetFullPath($DestinationPath)
    if ([IO.Path]::GetExtension($destination) -ne ".zip") {
        throw "Release archive destination must use the .zip extension."
    }
    if ($destination.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release archive destination must be outside its source directory."
    }

    $destinationDirectory = [IO.Path]::GetDirectoryName($destination)
    if (![string]::IsNullOrWhiteSpace($destinationDirectory)) {
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    }
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
                Sort-Object FullName |
                ForEach-Object {
                    $relativePath = (Get-AirTypePortableRelativePath `
                        -BaseDirectory $sourceRoot `
                        -Path $_.FullName).Replace('\', '/')
                    $entry = $archive.CreateEntry(
                        $relativePath,
                        [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $fixedTimestamp
                    $entryStream = $entry.Open()
                    try {
                        $fileStream = [IO.File]::OpenRead($_.FullName)
                        try {
                            $fileStream.CopyTo($entryStream)
                        }
                        finally {
                            $fileStream.Dispose()
                        }
                    }
                    finally {
                        $entryStream.Dispose()
                    }
                }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}
