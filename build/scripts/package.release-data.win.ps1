param(
    [string]$SourceDir = "",
    [string]$OutputZip = "build/release-data.zip",
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

function Resolve-SourceDir {
    param(
        [string]$SourceDir,
        [string]$Configuration,
        [string]$Framework,
        [string]$Runtime
    )

    if (-not [string]::IsNullOrWhiteSpace($SourceDir)) {
        return (Resolve-Path $SourceDir).Path
    }

    $candidates = @(
        "src/bin/$Configuration/$Framework/$Runtime/publish",
        "src/bin/$Configuration/$Framework/$Runtime",
        "build/SourceGit"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate -PathType Container) {
            $resolved = (Resolve-Path $candidate).Path
            if (Test-Path (Join-Path $resolved "SourceGit.exe") -PathType Leaf) {
                return $resolved
            }
        }
    }

    $latestExe = Get-ChildItem -Path "src/bin/$Configuration" -Recurse -Filter "SourceGit.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latestExe -ne $null) {
        return $latestExe.Directory.FullName
    }

    throw "Failed to locate release binaries. Please pass -SourceDir explicitly."
}

$resolvedSourceDir = Resolve-SourceDir -SourceDir $SourceDir -Configuration $Configuration -Framework $Framework -Runtime $Runtime

$requiredFiles = @(
    "av_libglesv2.dll",
    "libHarfBuzzSharp.dll",
    "libonigwrap.dll",
    "libSkiaSharp.dll",
    "SourceGit.exe"
)

$missing = @()
foreach ($file in $requiredFiles) {
    $full = Join-Path $resolvedSourceDir $file
    if (-not (Test-Path $full -PathType Leaf)) {
        $missing += $file
    }
}

if ($missing.Count -gt 0) {
    throw "Missing files in '$resolvedSourceDir': $($missing -join ', ')"
}

$outputDir = Split-Path -Path $OutputZip -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$stageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("sourcegit-release-data-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item -Path (Join-Path $resolvedSourceDir $file) -Destination (Join-Path $stageDir $file) -Force
}

if (Test-Path $OutputZip -PathType Leaf) {
    Remove-Item -Path $OutputZip -Force
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $OutputZip -Force
Remove-Item -Path $stageDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Created release data zip: $OutputZip"
Write-Host "Source: $resolvedSourceDir"
