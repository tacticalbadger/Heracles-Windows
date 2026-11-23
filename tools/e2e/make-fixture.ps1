<#
.SYNOPSIS
    Resets the fixture the end-to-end checks run against.

.DESCRIPTION
        tools\e2e\make-fixture.ps1 [-Fixture <dir>] [-Legacy]

    The fixture (build\e2e by default) holds an empty "data" folder, which the
    app turns into a new library when tools\e2e\launch.ps1 starts it there,
    and the log of that run. The real library is never touched.

    -Legacy puts a small library of the Electron app's in the folder
    (heracles.sqlite), which the app imports the first time it starts there.

    Refuses to run while a build of the app started from build\ is running:
    it has the fixture's database open.
#>
[CmdletBinding()]
param(
    [string]$Fixture,
    # Leave the library of the Electron app in the folder, for the app to import when it first starts.
    [switch]$Legacy
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildDir = Join-Path $Root 'build'
if (-not $Fixture) { $Fixture = Join-Path $BuildDir 'e2e' }

$running = @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($BuildDir + '\', [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
if ($running.Count -gt 0) {
    throw "Heracles is running from $BuildDir (pid $($running.Id -join ', ')) and may have the fixture open. Close it first."
}

foreach ($name in 'data', 'heracles.log') {
    $path = Join-Path $Fixture $name
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
New-Item -ItemType Directory -Force (Join-Path $Fixture 'data') | Out-Null
if ($Legacy) {
    # The library the unit tests import too: see app\Heracles.Tests\Fixtures\README.md.
    Copy-Item (Join-Path $Root 'app\Heracles.Tests\Fixtures\legacy.sqlite') (Join-Path $Fixture 'data\heracles.sqlite')
    Write-Host "ok: $Fixture (a library folder holding only the Electron app's library, no log)"
}
else {
    Write-Host "ok: $Fixture (an empty library folder, no log)"
}
