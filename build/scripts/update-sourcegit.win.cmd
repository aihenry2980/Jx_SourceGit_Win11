@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%update-sourcegit.win.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if "%EXIT_CODE%"=="0" (
  echo SourceGit update finished.
) else (
  echo SourceGit update failed.
)
pause
exit /b %EXIT_CODE%
