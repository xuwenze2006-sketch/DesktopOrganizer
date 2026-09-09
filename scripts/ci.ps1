[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidatePattern('^[a-z0-9]+(?:-[a-z0-9]+)+$')]
    [string]$RuntimeIdentifier = "win-x64",

    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot "DesktopOrganizer.slnx"
$appProject = Join-Path $repositoryRoot "DesktopOrganizer.csproj"
$testProject = Join-Path $repositoryRoot "tests\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj"
$artifacts = Join-Path $repositoryRoot "artifacts"
$testResults = Join-Path $artifacts "test-results"
$publishDirectory = Join-Path $artifacts "publish\$RuntimeIdentifier"
$archivePath = Join-Path $artifacts "DesktopOrganizer-$RuntimeIdentifier.zip"
$hashPath = Join-Path $artifacts "DesktopOrganizer-$RuntimeIdentifier.sha256"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Install .NET 10 SDK and retry."
}

$sdkVersionLines = & dotnet --version
$sdkVersionOutput = @($sdkVersionLines) | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$sdkVersionOutput)) {
    throw "Unable to query the installed .NET SDK version."
}

[string]$sdkVersion = ([string]$sdkVersionOutput).Trim()
if (-not $sdkVersion.StartsWith("10.", [StringComparison]::Ordinal)) {
    throw "DesktopOrganizer requires .NET 10 SDK. Current SDK: '$sdkVersion'."
}

foreach ($requiredFile in @($solution, $appProject, $testProject)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required file not found: $requiredFile"
    }
}

function Remove-CiOutput {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $artifactRoot = [IO.Path]::GetFullPath($artifacts)
    if (-not $fullPath.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "CI output must be inside the artifacts directory: $fullPath"
    }

    # Never follow directory junctions or symbolic links outside the output area.
    $ancestor = $fullPath
    while ($ancestor.Length -ge $artifactRoot.Length) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "CI output must not traverse a reparse point: $ancestor"
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

Set-Location $repositoryRoot
# Preserve art sources and backups in artifacts; remove only CI-owned outputs.
Remove-CiOutput -Path $testResults
if (-not $SkipPublish) {
    Remove-CiOutput -Path $publishDirectory
    Remove-CiOutput -Path $archivePath
    Remove-CiOutput -Path $hashPath
}
New-Item -ItemType Directory -Path $testResults -Force | Out-Null

Write-Host "Using .NET SDK $sdkVersion" -ForegroundColor Green
Invoke-DotNet -Arguments @(
    "restore", $solution,
    "--nologo"
)

Invoke-DotNet -Arguments @(
    "build", $solution,
    "--configuration", $Configuration,
    "--no-restore",
    "--nologo",
    "-warnaserror"
)

Invoke-DotNet -Arguments @(
    "test", $testProject,
    "--configuration", $Configuration,
    "--no-build",
    "--no-restore",
    "--nologo",
    "--logger", "trx;LogFileName=DesktopOrganizer.Tests.trx",
    "--results-directory", $testResults,
    "--collect", "XPlat Code Coverage"
)

if ($SkipPublish) {
    Write-Host "Build and tests completed. Publish was skipped." -ForegroundColor Green
    exit 0
}

Invoke-DotNet -Arguments @(
    "restore", $appProject,
    "--runtime", $RuntimeIdentifier,
    "--nologo"
)

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
Invoke-DotNet -Arguments @(
    "publish", $appProject,
    "--configuration", $Configuration,
    "--framework", "net10.0-windows",
    "--runtime", $RuntimeIdentifier,
    "--self-contained", "true",
    "--no-restore",
    "--nologo",
    "-warnaserror",
    "-p:PublishSingleFile=true",
    "-p:PublishReadyToRun=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "--output", $publishDirectory
)

$executable = Join-Path $publishDirectory "DesktopOrganizer.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Publish completed without producing DesktopOrganizer.exe."
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

$exeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashLines = @(
    "$exeHash  artifacts/publish/$RuntimeIdentifier/DesktopOrganizer.exe",
    "$zipHash  artifacts/DesktopOrganizer-$RuntimeIdentifier.zip"
)
[System.IO.File]::WriteAllLines(
    $hashPath,
    $hashLines,
    (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false))

Write-Host "Build, tests and publish completed successfully." -ForegroundColor Green
Write-Host "Executable: $executable"
Write-Host "Archive:    $archivePath"
Write-Host "Checksums:  $hashPath"
