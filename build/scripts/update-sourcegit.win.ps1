param(
    [string]$Repo = "aihenry2980/Jx_SourceGit_Win11",
    [string]$InstallDir = "",
    [string]$AssetPattern = "sourcegit_*.win-x64.zip",
    [string]$Token = $env:GITHUB_TOKEN,
    [switch]$IncludePrerelease,
    [switch]$NoRestart,
    [switch]$KeepBackup,
    [switch]$SkipSelfRelaunch
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host "[INFO] $Message"
}

function Write-Ok([string]$Message) {
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Resolve-InstallDir {
    if (-not [string]::IsNullOrWhiteSpace($InstallDir)) {
        return $InstallDir
    }

    if ($PSScriptRoot -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot "SourceGit.exe"))) {
        return $PSScriptRoot
    }

    return (Join-Path $env:LOCALAPPDATA "Programs\SourceGit")
}

function Test-IsChildPath([string]$Child, [string]$Parent) {
    if ([string]::IsNullOrWhiteSpace($Child) -or [string]::IsNullOrWhiteSpace($Parent)) {
        return $false
    }

    $childFull = [System.IO.Path]::GetFullPath($Child).TrimEnd('\')
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\')
    return $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Copy-DirectoryContent([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

$InstallDir = Resolve-InstallDir
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

if (-not $SkipSelfRelaunch -and $PSCommandPath -and (Test-IsChildPath $PSCommandPath $InstallDir)) {
    $tempScriptDir = Join-Path ([System.IO.Path]::GetTempPath()) "SourceGitUpdater"
    $tempScript = Join-Path $tempScriptDir "update-sourcegit.win.ps1"
    New-Item -ItemType Directory -Path $tempScriptDir -Force | Out-Null
    Copy-Item -LiteralPath $PSCommandPath -Destination $tempScript -Force

    Write-Step "Updater is inside the install folder. Relaunching from temp..."
    $powershell = (Get-Process -Id $PID).Path
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $tempScript,
        "-Repo", $Repo,
        "-InstallDir", $InstallDir,
        "-AssetPattern", $AssetPattern,
        "-SkipSelfRelaunch"
    )

    if ($IncludePrerelease) { $arguments += "-IncludePrerelease" }
    if ($NoRestart) { $arguments += "-NoRestart" }
    if ($KeepBackup) { $arguments += "-KeepBackup" }
    if (-not [string]::IsNullOrWhiteSpace($Token) -and $Token -ne $env:GITHUB_TOKEN) {
        $arguments += @("-Token", $Token)
    }

    & $powershell @arguments
    exit $LASTEXITCODE
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SourceGitUpdate_" + [System.Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $tempRoot "sourcegit.zip"
$extractDir = Join-Path $tempRoot "extract"
$backupDir = "$InstallDir.__backup_$(Get-Date -Format 'yyyyMMddHHmmss')"
$markerPath = Join-Path $InstallDir "sourcegit-release.json"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    Write-Step "Looking for latest release in $Repo..."
    $headers = @{
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "SourceGit-Windows-Updater"
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers["Authorization"] = "Bearer $Token"
    }

    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases" -Headers $headers
    $release = $releases |
        Where-Object { -not $_.draft -and ($IncludePrerelease -or -not $_.prerelease) } |
        Select-Object -First 1

    if ($null -eq $release) {
        throw "No published release found in $Repo."
    }

    $asset = $release.assets |
        Where-Object { $_.name -like $AssetPattern } |
        Select-Object -First 1

    if ($null -eq $asset) {
        throw "Release '$($release.tag_name)' has no asset matching '$AssetPattern'."
    }

    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        try {
            $installed = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if ($installed.AssetName -eq $asset.name -or $installed.ReleaseTag -eq $release.tag_name) {
                Write-Ok "SourceGit is already up to date ($($release.tag_name), $($asset.name))."
                exit 0
            }
        } catch {
            Write-Step "Installed release marker could not be read. Continuing with update..."
        }
    }

    Write-Step "Downloading $($asset.name) from release $($release.tag_name)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $zipPath

    Write-Step "Extracting package..."
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force

    $exe = Get-ChildItem -LiteralPath $extractDir -Filter "SourceGit.exe" -Recurse -File |
        Select-Object -First 1

    if ($null -eq $exe) {
        throw "The downloaded package does not contain SourceGit.exe."
    }

    $sourceDir = $exe.Directory.FullName

    Write-Step "Closing running SourceGit instances..."
    Get-Process -Name "SourceGit" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    if (Test-Path -LiteralPath $InstallDir) {
        Write-Step "Backing up current install..."
        Move-Item -LiteralPath $InstallDir -Destination $backupDir -Force
    }

    try {
        Write-Step "Installing to $InstallDir..."
        Copy-DirectoryContent -Source $sourceDir -Destination $InstallDir
    } catch {
        if (Test-Path -LiteralPath $InstallDir) {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path -LiteralPath $backupDir) {
            Move-Item -LiteralPath $backupDir -Destination $InstallDir -Force
        }

        throw
    }

    if (-not $KeepBackup -and (Test-Path -LiteralPath $backupDir)) {
        Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    $marker = [ordered]@{
        ReleaseTag = $release.tag_name
        AssetName = $asset.name
        Runtime = "win-x64"
        DownloadUrl = $asset.browser_download_url
        InstalledAt = (Get-Date).ToString("o")
    }
    $marker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallDir "sourcegit-release.json") -Encoding UTF8

    Write-Ok "SourceGit updated to $($release.tag_name)."

    if (-not $NoRestart) {
        $installedExe = Join-Path $InstallDir "SourceGit.exe"
        if (Test-Path -LiteralPath $installedExe) {
            Write-Step "Starting SourceGit..."
            Start-Process -FilePath $installedExe
        }
    }
} catch {
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
