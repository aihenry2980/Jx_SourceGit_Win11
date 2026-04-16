@echo off
setlocal EnableExtensions

set "SOURCE_DIR=%~dp0"
set "TARGET_DIR=%USERPROFILE%\Downloads\Jx_SourceGit"

echo.
echo ======================================
echo SourceGit Windows Installer
echo ======================================
echo Source: "%SOURCE_DIR%"
echo Target: "%TARGET_DIR%"
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop'; trap { Write-Host ('[ERROR] ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }" ^
  "$source = [System.IO.Path]::GetFullPath('%SOURCE_DIR%').TrimEnd('\');" ^
  "$target = [System.IO.Path]::GetFullPath('%TARGET_DIR%').TrimEnd('\');" ^
  "if (-not (Test-Path -LiteralPath (Join-Path $source 'SourceGit.exe') -PathType Leaf)) { throw 'SourceGit.exe was not found beside this installer. Please run this script from the unzipped SourceGit release folder.' }" ^
  "if ($source.Equals($target, [System.StringComparison]::OrdinalIgnoreCase)) { Write-Host '[OK] SourceGit is already in the stable folder.' -ForegroundColor Green; Start-Process -FilePath (Join-Path $target 'SourceGit.exe'); exit 0 }" ^
  "Write-Host '[INFO] Closing running SourceGit instances...';" ^
  "Get-Process -Name 'SourceGit' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue;" ^
  "Start-Sleep -Milliseconds 500;" ^
  "if (Test-Path -LiteralPath $target -PathType Container) { Write-Host '[INFO] Clearing previous install files...'; Get-ChildItem -LiteralPath $target -Force | Remove-Item -Recurse -Force -ErrorAction Stop } else { New-Item -ItemType Directory -Path $target -Force | Out-Null }" ^
  "Write-Host '[INFO] Copying files to stable folder...';" ^
  "Get-ChildItem -LiteralPath $source -Force | Where-Object { -not $_.FullName.TrimEnd('\').Equals($target, [System.StringComparison]::OrdinalIgnoreCase) } | Copy-Item -Destination $target -Recurse -Force;" ^
  "$exe = Join-Path $target 'SourceGit.exe';" ^
  "if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Install finished but SourceGit.exe was not found in the target folder.' }" ^
  "Write-Host '[OK] Installed SourceGit to' $target -ForegroundColor Green;" ^
  "Write-Host '[INFO] Starting SourceGit...';" ^
  "Start-Process -FilePath $exe"

set "EXIT_CODE=%ERRORLEVEL%"
echo.
if "%EXIT_CODE%"=="0" (
  echo SourceGit install finished.
  echo You can pin "%TARGET_DIR%\SourceGit.exe" to the taskbar once.
) else (
  echo SourceGit install failed.
)
pause
exit /b %EXIT_CODE%
