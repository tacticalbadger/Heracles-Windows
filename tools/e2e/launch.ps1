<#
.SYNOPSIS
    Relaunches the built app against the fixture.

.DESCRIPTION
        tools\e2e\launch.ps1 [-Fixture <dir>] [-Wait <secs>] [-Exe <path>] [-DataDir <path>] [app arguments...]
        tools\e2e\launch.ps1
        tools\e2e\launch.ps1 -Exe build\release\Heracles-0.2.0-win-x64\Heracles.exe

    The run uses the fixture's library folder (<fixture>\data) and log
    (<fixture>\heracles.log); the real library is not touched. A previous
    instance started from the same build is closed first. -Exe runs another
    build than build\app, such as a packaged one. -DataDir points the app at
    another library folder, for checking what it does with one it cannot use.

    The app takes no arguments yet.
#>
[CmdletBinding()]
param(
    [string]$Fixture,
    [int]$Wait = 3,
    [string]$Exe,
    [string]$DataDir,
    # The only positional parameter: without that, an app argument typed at a
    # PowerShell prompt lands in -Fixture and -Wait.
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$AppArgs
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Fixture) { $Fixture = Join-Path $Root 'build\e2e' }
if (-not (Test-Path (Join-Path $Fixture 'data'))) { throw "no fixture at $Fixture; run tools\e2e\make-fixture.ps1 first" }
$Fixture = (Resolve-Path $Fixture).Path

# The app is started from its own folder, so a relative path would mean
# something else to it than it does here. Combine leaves a full path as it is.
if ($DataDir) { $DataDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $DataDir)) }
else { $DataDir = Join-Path $Fixture 'data' }

if ($Exe) {
    if (-not (Test-Path $Exe)) { throw "no build at $Exe" }
    $Exe = (Resolve-Path $Exe).Path
}
else {
    # The machine's architecture, as in build.ps1 (an emulated shell reports its own).
    $machine = [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE', 'Machine')
    if (-not $machine) { $machine = $env:PROCESSOR_ARCHITECTURE }
    $arch = if ($machine -eq 'ARM64') { 'arm64' } else { 'x64' }
    $Exe = Join-Path $Root "build\app\win-$arch\Heracles.exe"
    if (-not (Test-Path $Exe)) { throw "no build at $Exe; run tools\build.ps1 first" }
}
$OutDir = Split-Path -Parent $Exe

$running = @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and ($_.Path -ieq $Exe) } catch { $false }
    })
if ($running.Count -gt 0) {
    $running | Stop-Process -Force
    $running | ForEach-Object { try { $_.WaitForExit(5000) | Out-Null } catch { } }
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
$psi.WorkingDirectory = $OutDir
# Started through the shell so the app does not inherit this script's output
# handles: a caller piping the script's output would otherwise wait for the
# app to exit. The shell cannot pass an environment, so the overrides are set
# here for the moment of the launch and put back afterwards.
$psi.UseShellExecute = $true
if ($AppArgs) {
    $psi.Arguments = ($AppArgs | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}
$overrides = @{
    HERACLES_DATA_DIR = $DataDir
    HERACLES_LOG_PATH = Join-Path $Fixture 'heracles.log'
}
$previous = @{}
foreach ($name in $overrides.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $overrides[$name], 'Process')
}
try { $process = [System.Diagnostics.Process]::Start($psi) }
finally {
    foreach ($name in $overrides.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
Write-Host "launched pid $($process.Id) on $DataDir with: $($psi.Arguments)"
if ($Wait -gt 0) { Start-Sleep -Seconds $Wait }
if ($process.HasExited) { Write-Host "the app has exited (code $($process.ExitCode)); see $(Join-Path $Fixture 'heracles.log')" }
