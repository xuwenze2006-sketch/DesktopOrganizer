@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet was not found. Install the .NET 10 SDK first.
    pause
    exit /b 1
)

if not exist "%~dp0DesktopOrganizer.csproj" (
    echo ERROR: DesktopOrganizer.csproj was not found next to this script.
    pause
    exit /b 1
)

dotnet run --project "%~dp0DesktopOrganizer.csproj"
set "EXIT_CODE=%errorlevel%"
if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%
