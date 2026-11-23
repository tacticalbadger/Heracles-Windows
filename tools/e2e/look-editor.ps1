<#
.SYNOPSIS
    Takes pictures of the editor to be looked at: a note holding every kind
    of block, in light and in dark, with and without the caret in a block.

.DESCRIPTION
        tools\e2e\look-editor.ps1

    Resets the fixture, and for each theme starts the app on it, opens the
    seeded note "Every kind of block", and saves three pictures under
    build\e2e\shots: the top of the note with nothing selected, the same with
    the caret in its first paragraph, and the bottom of the note with the
    caret in its last. The pictures are named editor-<theme>-<n>.png.

    Colours cannot be asserted from the automation tree, so this checks
    nothing: the pictures are for a person, or a session, to look at. One
    showed the text box black on a dark page, and another showed emoji as
    white blobs, when every test was passing.

    Goes through UI Automation only. The app's window comes to the front.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$E2e = $PSScriptRoot
$Root = Split-Path -Parent (Split-Path -Parent $E2e)
$Shots = Join-Path $Root 'build\e2e\shots'
. (Join-Path $E2e 'ui-check.ps1')

function Stop-Fixture-Processes {
    $running = @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and $_.Path.StartsWith((Join-Path $Root 'build') + '\', [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
        })
    $running | Stop-Process -Force
    $running | ForEach-Object { try { $_.WaitForExit(5000) | Out-Null } catch { } }
}

function Save-Shot {
    param([string]$Name)
    & (Join-Path $E2e 'capture.ps1') 6>&1 | Out-Null
    $taken = Join-Path $Shots 'shot-1.png'
    if (Test-Path $taken) {
        Copy-Item $taken (Join-Path $Shots $Name) -Force
        Write-Host "   -> $(Join-Path $Shots $Name)"
    }
}

foreach ($theme in 'dark', 'light') {
    Stop-Fixture-Processes
    & (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
    & (Join-Path $E2e 'launch.ps1') -Wait 0 '--e2e-seed' "--theme-$theme" 6>&1 | Out-Null
    if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host "the window did not appear ($theme)"; continue }
    $note = Get-UiId -Name 'Every kind of block' -Prefix 'note:' -Within 'Sidebar'
    Select-Ui $note
    Start-Sleep -Milliseconds 700
    Write-Host "$theme"
    Save-Shot "editor-$theme-1.png"
    $blocks = @(Get-UiIds -Prefix 'Block:' | Select-Object -Unique)
    if ($blocks.Count -gt 1) {
        Set-UiFocus $blocks[1]
        Start-Sleep -Milliseconds 500
        Save-Shot "editor-$theme-2.png"
        Set-UiFocus $blocks[-1]
        Start-Sleep -Milliseconds 500
        Save-Shot "editor-$theme-3.png"
    }
}
Stop-Fixture-Processes
