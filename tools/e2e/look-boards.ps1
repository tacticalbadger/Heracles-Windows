<#
.SYNOPSIS
    Takes pictures of a board and of a task to be looked at, in dark and in
    light.

.DESCRIPTION
        tools\e2e\look-boards.ps1

    Resets the fixture, and for each theme starts the app on it, opens the
    seeded todo list "Packing", and saves pictures under build\e2e\shots: the
    board (board-<theme>-1.png), then with the task "Tent" open in its window
    (task-<theme>-1.png is the task, -2 the board behind it), then the foot
    of the task, where its subtasks and comments are (task-foot-<theme>-1.png).

    Colours cannot be asserted from the automation tree, so this checks
    nothing: the pictures are for a person, or a session, to look at.

    Goes through UI Automation only. The app's windows come to the front.
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

# capture.ps1 saves one picture for each window of the app, newest first.
function Save-Shots {
    param([string]$Name)
    Get-ChildItem $Shots -Filter 'shot-*.png' -ErrorAction SilentlyContinue | Remove-Item -Force
    & (Join-Path $E2e 'capture.ps1') 6>&1 | Out-Null
    $number = 0
    foreach ($taken in @(Get-ChildItem $Shots -Filter 'shot-*.png' | Sort-Object Name)) {
        $number++
        $to = Join-Path $Shots ("{0}-{1}.png" -f $Name, $number)
        Copy-Item $taken.FullName $to -Force
        Write-Host "   -> $to"
    }
}

foreach ($theme in 'dark', 'light') {
    Stop-Fixture-Processes
    & (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
    & (Join-Path $E2e 'launch.ps1') -Wait 0 '--e2e-seed' "--theme-$theme" 6>&1 | Out-Null
    if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host "the window did not appear ($theme)"; continue }
    $list = Get-UiId -Name 'Packing' -Prefix 'note:' -Within 'Sidebar'
    Select-Ui $list
    Start-Sleep -Milliseconds 700
    Write-Host "$theme"
    Save-Shots "board-$theme"
    $tent = Get-UiId -Name 'Tent' -Prefix 'Card:'
    if ($tent) {
        Invoke-Ui -Id $tent
        $null = Wait-Ui { Find-Ui -Id 'TaskDetail' }
        Set-UiValue 'TaskReminderTime' '09:30'
        Invoke-Ui -Id 'TaskAddReminder'
        Invoke-Ui -Id 'TaskDeadlineSet'
        Start-Sleep -Milliseconds 700
        Save-Shots "task-$theme"
        # And the foot of it: subtasks and comments.
        & (Join-Path $E2e 'ui.ps1') scroll -Id 'TaskFields' -Y 100 6>&1 | Out-Null
        Start-Sleep -Milliseconds 500
        Save-Shots "task-foot-$theme"
    }
}
Stop-Fixture-Processes
