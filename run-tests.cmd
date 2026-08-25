@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

where pwsh >nul 2>&1
if not errorlevel 1 (
    pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ci.ps1" -SkipPublish
    set "EXIT_CODE=%errorlevel%"
    if not "!EXIT_CODE!"=="0" pause
    exit /b !EXIT_CODE!
)

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ci.ps1" -SkipPublish
set "EXIT_CODE=%errorlevel%"
if not "!EXIT_CODE!"=="0" pause
exit /b !EXIT_CODE!
