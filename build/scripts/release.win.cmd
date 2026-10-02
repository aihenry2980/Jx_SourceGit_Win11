@echo off
setlocal EnableExtensions EnableDelayedExpansion
if /I "%~1"=="--non-interactive" set "NON_INTERACTIVE=1"

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%\..\.." >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Failed to switch to repository root.
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

set "RUNTIME=win-x64"
set "CONFIGURATION=Release"
set "OUTPUT_DIR=build\SourceGit"

if not defined DATE_TAG for /f %%D in ('powershell -NoProfile -Command "(Get-Date).ToString('yyyyMMdd')"') do set "DATE_TAG=%%D"
if not defined DATE_TAG (
  echo [ERROR] Failed to generate date-based version tag.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

set "RELEASE_INDEX=1"
:resolve_version
if !RELEASE_INDEX! EQU 1 (
  set "VERSION=!DATE_TAG!"
) else (
  call :ordinal !RELEASE_INDEX! ORDINAL
  set "VERSION=!DATE_TAG!-!ORDINAL!"
)

set "ZIP_FILE=build\sourcegit_!VERSION!.%RUNTIME%.zip"
set "SOURCE_ZIP_FILE=build\sourcegit_!VERSION!.source-with-submodules.zip"
if exist "!ZIP_FILE!" (
  set /a RELEASE_INDEX+=1
  goto :resolve_version
)
if exist "!SOURCE_ZIP_FILE!" (
  set /a RELEASE_INDEX+=1
  goto :resolve_version
)

echo.
echo ======================================
echo SourceGit Windows Release Packaging
echo ======================================
echo Version: !VERSION!
echo Runtime: %RUNTIME%
echo Output : !ZIP_FILE!
echo.

if exist "%OUTPUT_DIR%" (
  echo [INFO] Cleaning old publish output...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$expected = Join-Path (Resolve-Path -LiteralPath 'build').ProviderPath 'SourceGit'; $item = Get-Item -LiteralPath 'build\SourceGit' -Force; if ($item.FullName -ne $expected -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe publish output directory.' }; Remove-Item -LiteralPath $item.FullName -Recurse -Force"
  if errorlevel 1 (
    popd
    if not defined NON_INTERACTIVE pause
    exit /b 1
  )
)

echo [INFO] Publishing...
dotnet publish src\SourceGit.csproj -c %CONFIGURATION% -r %RUNTIME% -o %OUTPUT_DIR%
if errorlevel 1 (
  echo [ERROR] dotnet publish failed.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

echo [INFO] Adding Windows updater...
copy /y build\scripts\update-sourcegit.win.ps1 "%OUTPUT_DIR%\update-sourcegit.win.ps1" >nul
if errorlevel 1 (
  echo [ERROR] Failed to copy updater PowerShell script.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

copy /y build\scripts\update-sourcegit.win.cmd "%OUTPUT_DIR%\update-sourcegit.win.cmd" >nul
if errorlevel 1 (
  echo [ERROR] Failed to copy updater command script.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

copy /y build\scripts\install-sourcegit.win.cmd "%OUTPUT_DIR%\install-sourcegit.win.cmd" >nul
if errorlevel 1 (
  echo [ERROR] Failed to copy installer command script.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

echo [INFO] Writing release marker...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$marker = [ordered]@{ PackageVersion = $env:VERSION; Runtime = $env:RUNTIME; AssetName = ('sourcegit_{0}.{1}.zip' -f $env:VERSION, $env:RUNTIME); CreatedAt = (Get-Date).ToString('o') }; $marker | ConvertTo-Json | Set-Content -Path '%OUTPUT_DIR%\sourcegit-release.json' -Encoding UTF8"
if errorlevel 1 (
  echo [ERROR] Failed to write release marker.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

echo [INFO] Packaging zip...
powershell -NoProfile -ExecutionPolicy Bypass -File build\scripts\package.win.ps1
if errorlevel 1 (
  echo [ERROR] Zip packaging failed.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

echo [INFO] Packaging source with submodules...
powershell -NoProfile -ExecutionPolicy Bypass -File build\scripts\package.source.ps1
if errorlevel 1 (
  echo [ERROR] Source packaging failed.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

if exist "!ZIP_FILE!" (
  echo [OK] Release zip generated:
  echo      %CD%\!ZIP_FILE!
  if not defined NON_INTERACTIVE explorer /select,"%CD%\!ZIP_FILE!" >nul 2>&1
) else (
  echo [ERROR] Packaging command finished but zip file was not found.
  popd
  if not defined NON_INTERACTIVE pause
  exit /b 1
)

popd
echo.
if not defined NON_INTERACTIVE pause
exit /b 0

:ordinal
setlocal
set /a N=%1
set /a MOD100=N%%100
set /a MOD10=N%%10
set "SUFFIX=th"

if !MOD100! GEQ 11 if !MOD100! LEQ 13 goto ordinal_done
if !MOD10! EQU 1 set "SUFFIX=st"
if !MOD10! EQU 2 set "SUFFIX=nd"
if !MOD10! EQU 3 set "SUFFIX=rd"

:ordinal_done
endlocal & set "%2=%1%SUFFIX%"
goto :eof
