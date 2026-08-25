@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 goto :no_dotnet

set "PROJECT=%CD%\DesktopOrganizer.csproj"
set "OUTPUT=%CD%\release\win-x64"

if not exist "%PROJECT%" goto :no_project

if exist "%OUTPUT%" (
    rmdir /s /q "%OUTPUT%" 2>nul
)

if exist "%OUTPUT%" goto :output_locked
mkdir "%OUTPUT%" >nul 2>&1
if errorlevel 1 goto :output_failed

echo Building DesktopOrganizer for Windows x64...
echo Output: "%OUTPUT%"
echo.

dotnet publish "%PROJECT%" ^
  -c Release ^
  -f net10.0-windows ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -p:PublishTrimmed=false ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -warnaserror ^
  -o "%OUTPUT%"

if errorlevel 1 goto :publish_failed
if not exist "%OUTPUT%\DesktopOrganizer.exe" goto :exe_missing

echo.
echo Build completed successfully.
echo You can now double-click DesktopOrganizer.exe.
start "" explorer.exe "%OUTPUT%"
pause
exit /b 0

:no_dotnet
echo ERROR: dotnet was not found.
echo Install the .NET 10 SDK, then run this file again.
pause
exit /b 1

:no_project
echo ERROR: DesktopOrganizer.csproj was not found next to this script.
pause
exit /b 1

:output_locked
echo ERROR: The old output directory could not be removed.
echo Close DesktopOrganizer.exe and any Explorer window using this folder, then try again.
pause
exit /b 1

:output_failed
echo ERROR: The output directory could not be created.
pause
exit /b 1

:publish_failed
echo.
echo ERROR: dotnet publish failed. Read the error messages above.
pause
exit /b 1

:exe_missing
echo.
echo ERROR: Publish finished without creating DesktopOrganizer.exe.
pause
exit /b 1
