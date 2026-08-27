[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path $PSScriptRoot -Parent
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$standardName = "MediaFetch-$Version-win-x64-standard"
$dependenciesName = "MediaFetch-$Version-win-x64-dependencies"
$standardDirectory = Join-Path $artifactsRoot $standardName
$dependenciesDirectory = Join-Path $artifactsRoot $dependenciesName
$standardArchive = Join-Path $artifactsRoot "$standardName.zip"
$dependenciesArchive = Join-Path $artifactsRoot "$dependenciesName.zip"
$releaseMetadataUrl = 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Parent,
        [Parameter(Mandatory)]
        [string]$Child
    )

    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([char[]]'\/')
    $fullChild = [IO.Path]::GetFullPath($Child)
    $prefix = $fullParent + [IO.Path]::DirectorySeparatorChar

    if (-not $fullChild.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the artifacts directory: $fullChild"
    }
}

function Resolve-Executable {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $command = Get-Command $Name -CommandType Application -ErrorAction Stop
    $item = Get-Item -LiteralPath $command.Source

    if ($item.LinkType -and $item.Target) {
        return [IO.Path]::GetFullPath([string]$item.Target)
    }

    return $item.FullName
}

function Reset-PackageTarget {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Assert-ChildPath -Parent $artifactsRoot -Child $Path

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Copy-ReleaseDocuments {
    param(
        [Parameter(Mandatory)]
        [string]$Destination
    )

    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $Destination
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $Destination
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

@($standardDirectory, $dependenciesDirectory, $standardArchive, $dependenciesArchive) |
    ForEach-Object { Reset-PackageTarget -Path $_ }

dotnet publish `
    (Join-Path $projectRoot 'MediaFetch.Desktop.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $standardDirectory `
    -p:PublishProfile=FrameworkDependentWinX64 `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-ReleaseDocuments -Destination $standardDirectory

$toolsDirectory = Join-Path $dependenciesDirectory 'tools'
$dotnetDirectory = Join-Path $dependenciesDirectory 'dotnet'
$licensesDirectory = Join-Path $dependenciesDirectory 'licenses'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $dotnetDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null

$ytDlpPath = Resolve-Executable 'yt-dlp.exe'
$ffmpegPath = Resolve-Executable 'ffmpeg.exe'
$ffprobePath = Resolve-Executable 'ffprobe.exe'

Copy-Item -LiteralPath $ytDlpPath -Destination (Join-Path $toolsDirectory 'yt-dlp.exe')
Copy-Item -LiteralPath $ffmpegPath -Destination (Join-Path $toolsDirectory 'ffmpeg.exe')
Copy-Item -LiteralPath $ffprobePath -Destination (Join-Path $toolsDirectory 'ffprobe.exe')

$ffmpegRoot = Split-Path (Split-Path $ffmpegPath -Parent) -Parent
Copy-Item -LiteralPath (Join-Path $ffmpegRoot 'LICENSE') `
    -Destination (Join-Path $licensesDirectory 'FFmpeg-GPLv3.txt')
Copy-Item -LiteralPath (Join-Path $ffmpegRoot 'README.txt') `
    -Destination (Join-Path $licensesDirectory 'FFmpeg-build-info.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'DEPENDENCIES.md') -Destination $dependenciesDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $dependenciesDirectory

Write-Host 'Reading the current .NET 10 release metadata...'
$metadata = Invoke-RestMethod -Uri $releaseMetadataUrl -UseBasicParsing
$latestRuntimeVersion = [string]$metadata.'latest-runtime'
$latestRelease = @($metadata.releases) |
    Where-Object { $_.'release-version' -eq $latestRuntimeVersion } |
    Select-Object -First 1

if (-not $latestRelease) {
    throw "Could not find .NET $latestRuntimeVersion in the official release metadata."
}

$runtimeInstaller = @($latestRelease.windowsdesktop.files) |
    Where-Object {
        $_.name -eq 'windowsdesktop-runtime-win-x64.exe' -and
        $_.rid -eq 'win-x64'
    } |
    Select-Object -First 1

if (-not $runtimeInstaller) {
    throw "Could not find the Windows Desktop Runtime x64 installer for .NET $latestRuntimeVersion."
}

$installerFileName = [IO.Path]::GetFileName(([Uri]$runtimeInstaller.url).AbsolutePath)
$installerPath = Join-Path $dotnetDirectory $installerFileName

Write-Host "Downloading .NET Windows Desktop Runtime $latestRuntimeVersion..."
Invoke-WebRequest -Uri $runtimeInstaller.url -OutFile $installerPath -UseBasicParsing

$expectedHash = ([string]$runtimeInstaller.hash).ToUpperInvariant()
$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA512).Hash

if ($actualHash -ne $expectedHash) {
    Remove-Item -LiteralPath $installerPath -Force
    throw 'The downloaded .NET installer failed SHA512 verification.'
}

Compress-Archive `
    -Path (Join-Path $standardDirectory '*') `
    -DestinationPath $standardArchive `
    -CompressionLevel Optimal

Compress-Archive `
    -Path (Join-Path $dependenciesDirectory '*') `
    -DestinationPath $dependenciesArchive `
    -CompressionLevel Optimal

$standardFile = Get-Item -LiteralPath $standardArchive
$dependenciesFile = Get-Item -LiteralPath $dependenciesArchive
$standardHash = Get-FileHash -LiteralPath $standardArchive -Algorithm SHA256
$dependenciesHash = Get-FileHash -LiteralPath $dependenciesArchive -Algorithm SHA256

Write-Host ''
Write-Host "Standard package: $($standardFile.FullName)"
Write-Host "Size: $([math]::Round($standardFile.Length / 1MB, 1)) MB"
Write-Host "SHA256: $($standardHash.Hash)"
Write-Host ''
Write-Host "Dependencies package: $($dependenciesFile.FullName)"
Write-Host "Size: $([math]::Round($dependenciesFile.Length / 1MB, 1)) MB"
Write-Host "SHA256: $($dependenciesHash.Hash)"
Write-Host ".NET Windows Desktop Runtime: $latestRuntimeVersion"
