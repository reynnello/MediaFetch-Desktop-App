[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path $PSScriptRoot -Parent
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$packageName = "MediaFetch-$Version-win-x64-portable"
$packageDirectory = Join-Path $artifactsRoot $packageName
$archivePath = Join-Path $artifactsRoot "$packageName.zip"

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

Assert-ChildPath -Parent $artifactsRoot -Child $packageDirectory
Assert-ChildPath -Parent $artifactsRoot -Child $archivePath

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

dotnet publish `
    (Join-Path $projectRoot 'MediaFetch.Desktop.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $packageDirectory `
    -p:PublishProfile=PortableWinX64 `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$toolsDirectory = Join-Path $packageDirectory 'tools'
$licensesDirectory = Join-Path $packageDirectory 'licenses'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
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
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') `
    -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') `
    -Destination $packageDirectory

Compress-Archive `
    -Path (Join-Path $packageDirectory '*') `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$archive = Get-Item -LiteralPath $archivePath
$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256

Write-Host "Release package: $($archive.FullName)"
Write-Host "Size: $([math]::Round($archive.Length / 1MB, 1)) MB"
Write-Host "SHA256: $($hash.Hash)"
