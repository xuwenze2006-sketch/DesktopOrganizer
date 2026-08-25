@echo off
setlocal EnableExtensions
cd /d "%~dp0"

call "%~dp0publish-win-x64.cmd"
if errorlevel 1 exit /b 1

set "APP=%~dp0release\win-x64\DesktopOrganizer.exe"
if not exist "%APP%" (
    echo ERROR: DesktopOrganizer.exe was not found.
    pause
    exit /b 1
)

start "" "%APP%"
exit /b 0
