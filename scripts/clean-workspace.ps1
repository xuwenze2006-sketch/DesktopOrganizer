[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot ".git") -PathType Container)) {
    throw "Run this script from the existing DesktopOrganizer main checkout."
}
$gitRoot = & git -C $repositoryRoot rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne $repositoryRoot) {
    throw "The script directory does not belong to the repository root."
}
$branch = & git -C $repositoryRoot branch --show-current
if ($LASTEXITCODE -ne 0 -or $branch -ne "main") {
    throw "Workspace cleanup is only supported on main."
}

# Explicit build locations only. Never scan artifacts or release for cleanup.
$relativePaths = @(
    "bin", "obj",
    "tests/DesktopOrganizer.Tests/bin", "tests/DesktopOrganizer.Tests/obj",
    "artifacts/build/bin", "artifacts/build/obj"
)

function Get-CheckedBuildDirectory {
    param([string]$RelativePath)

    $fullPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $RelativePath))
    if (-not $fullPath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build output must be inside the repository: $fullPath"
    }

    # Check ancestors before enumeration so junctions cannot redirect cleanup.
    $ancestor = $fullPath
    while ($ancestor.Length -ge $repositoryRoot.Length) {
        $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Cleanup must not traverse a link or junction: $ancestor"
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw "Expected a build directory: $fullPath"
    }

    $trackedFiles = @(& git -C $repositoryRoot ls-files -- $RelativePath)
    if ($LASTEXITCODE -ne 0 -or $trackedFiles.Count -gt 0) {
        throw "Cannot clean a directory containing tracked files: $RelativePath"
    }
    & git -C $repositoryRoot check-ignore -q -- ($RelativePath + "/")
    if ($LASTEXITCODE -ne 0) { throw "Build directory is not ignored: $RelativePath" }

    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($fullPath)
    [long]$bytes = 0
    [long]$files = 0
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Build directory contains a link or junction: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            else { $bytes += $entry.Length; $files++ }
        }
    }
    [pscustomobject]@{ Path = $RelativePath; FullPath = $fullPath; Files = $files; Bytes = $bytes }
}

# Validate every target before recycling any target.
$targets = @($relativePaths | ForEach-Object { Get-CheckedBuildDirectory $_ })
if ($targets.Count -eq 0) {
    Write-Host "No build caches to clean."
    return
}
$targets | Select-Object Path, Files, @{Name = "MiB"; Expression = { [math]::Round($_.Bytes / 1MB, 2) }} |
    Format-Table -AutoSize | Out-Host

if (-not $Apply) {
    Write-Host "Preview only. Stop project builds/tests, then rerun with -Apply to recycle these caches."
    return
}

# Refuse to delete the output directory of a running application or test host.
$processes = @(Get-CimInstance Win32_Process)
foreach ($target in $targets) {
    foreach ($process in $processes) {
        if ($process.ExecutablePath -and $process.ExecutablePath.StartsWith(
                $target.FullPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "A process is running from $($target.Path). Close it before cleanup."
        }
        if ($process.Name -in @("dotnet.exe", "MSBuild.exe", "testhost.exe") -and
            $process.CommandLine -and ($process.CommandLine.IndexOf(
                $repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $process.CommandLine -match 'DesktopOrganizer(?:\.Tests)?\.(?:slnx|csproj)')) {
            throw "A project build or test process is still running. Wait for it before cleanup."
        }
    }
}

Add-Type -AssemblyName Microsoft.VisualBasic
[long]$removedBytes = 0
foreach ($target in $targets) {
    if ($PSCmdlet.ShouldProcess($target.FullPath, "Move rebuildable build cache to Recycle Bin")) {
        # Recheck after preview/process inspection, immediately before recycling.
        $checked = Get-CheckedBuildDirectory $target.Path
        if ($null -ne $checked) {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
                $checked.FullPath,
                [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
                [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
                [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
            $removedBytes += $checked.Bytes
        }
    }
}
Write-Host ("Moved {0:N2} MiB of build caches to Recycle Bin." -f ($removedBytes / 1MB))
