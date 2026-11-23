<#
.SYNOPSIS
    Drives the built app through its first start on a library the Electron
    app left behind, and says what passed.

.DESCRIPTION
        tools\e2e\check-legacy.ps1 [-Exe <path>]

    Puts a small Electron library in the fixture (see
    app\Heracles.Tests\Fixtures\README.md), starts the app on it, and checks
    in the real window: a summary says what came across and where the copy of
    the old library is, and lists what had to be adjusted; the old notes are
    in the sidebar, with their place, their favorite and their trash; the old
    board has its columns and cards; the old library file is byte for byte as
    it was; a second start imports nothing and shows no summary. Then, on a
    fixture whose old library is not a database at all: the app starts on an
    empty library and says the old one was not changed.

    Everything goes through UI Automation patterns: the mouse and keyboard are
    not touched, and neither is the clipboard. Windows of the app do come to
    the front. Exit code: the number of checks that failed.
#>
[CmdletBinding()]
param(
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$E2e = $PSScriptRoot
$Root = Split-Path -Parent (Split-Path -Parent $E2e)
$Fixture = Join-Path $Root 'build\e2e'
$Data = Join-Path $Fixture 'data'
$Old = Join-Path $Data 'heracles.sqlite'
$LogFile = Join-Path $Fixture 'heracles.log'
. (Join-Path $E2e 'ui-check.ps1')

function Get-Fixture-Processes {
    @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and $_.Path.StartsWith((Join-Path $Root 'build') + '\', [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
        })
}

function Stop-Fixture-Processes {
    $running = Get-Fixture-Processes
    $running | Stop-Process -Force
    $running | ForEach-Object { try { $_.WaitForExit(5000) | Out-Null } catch { } }
}

function Start-App {
    param([string[]]$AppArgs = @())
    $launch = @{ Wait = 0 }
    if ($Exe) { $launch.Exe = $Exe }
    & (Join-Path $E2e 'launch.ps1') @launch @AppArgs 6>&1 | Out-Null
}

function Get-Panes { @(Get-UiIds -Prefix 'Pane:' | ForEach-Object { $_ -replace '^Pane:', '' } | Select-Object -Unique) }

function Exit-App {
    Invoke-UiMenu 'File' 'Exit'
    $null = Wait-Ui { @(Get-Fixture-Processes).Count -eq 0 } 5000
}

function Get-Log { @(Get-Content $LogFile -ErrorAction SilentlyContinue) }

# Something that should be there and is not ends the run. The app is not
# left running for that: the next build could not replace it.
trap {
    Write-Host "FAIL  the check stopped: $_"
    Show-UiResults
    Stop-Fixture-Processes
    exit ($script:UiFailed.Count + 1)
}

# --- 1. The first start brings the old library across ---
Stop-Fixture-Processes
& (Join-Path $E2e 'make-fixture.ps1') -Legacy 6>&1 | Out-Null
$before = (Get-FileHash $Old -Algorithm SHA256).Hash
Start-App
if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host 'FAIL  the window did not appear'; exit 1 }

Test-Ui 'first start: a summary is shown' { Find-UiShown -Id 'ImportSummary' }
Test-Ui 'summary: it counts what came' { (Get-UiName 'ImportSummaryCounts') -eq '7 notes, 3 tasks and 1 image were brought across from the previous version of Heracles.' }
Test-Ui 'summary: it says where the copy of the old library is, and the copy is there' {
    $copy = Get-UiValue 'ImportSummaryBackup'
    $copy -and $copy.StartsWith((Join-Path $Data 'backups'), [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path $copy)
}
Test-Ui 'summary: it lists what had to be adjusted' { @(Get-UiIds -Prefix 'ImportSummaryRepair:' -Within 'ImportSummary').Count -ge 1 }
Test-Ui 'the log says what was imported' { (Get-Log) -match 'legacy: imported 7 notes, 3 tasks, 1 images' }
Test-Ui 'the old library file is as it was, byte for byte' { (Get-FileHash $Old -Algorithm SHA256).Hash -eq $before }

# The app can be used with the summary still up: it is not modal.
Test-Ui 'old notes: the section and its page are in the sidebar' {
    (Get-UiId -Name 'Old plans' -Prefix 'note:' -Within 'Sidebar') -and (Get-UiId -Name 'Old trip' -Prefix 'note:' -Within 'Sidebar')
}
Test-Ui 'old notes: the favorite, the archived and the trashed are where they were' {
    (Get-UiId -Name 'Old trip' -Prefix 'favorite:' -Within 'Sidebar') -and (Get-UiId -Name 'Ferry times' -Prefix 'archived:' -Within 'Sidebar') -and (Get-UiId -Name 'Old scrap' -Prefix 'trashed:' -Within 'Sidebar')
}
Test-Ui 'old notes: the page that had lost its place has one' { Get-UiId -Name 'Lost page' -Prefix 'note:' -Within 'Sidebar' }

Invoke-Ui -Id 'ImportSummaryOk'
Test-Ui 'summary: OK puts it away' { -not (Find-Ui -Id 'ImportSummary') }

$page = Get-UiId -Name 'Old trip' -Prefix 'note:' -Within 'Sidebar'
Select-Ui $page
$pane = @(Get-Panes)[0]
Test-Ui 'old page: its words are blocks now' {
    (Get-UiId -Name 'Trip plan' -Prefix 'Block:' -Within "NoteText:$pane") -and (Get-UiId -Name 'Pack the lantern' -Prefix 'Block:' -Within "NoteText:$pane")
}
$list = Get-UiId -Name 'Old packing' -Prefix 'note:' -Within 'Sidebar'
Select-Ui $list
Test-Ui 'old board: its columns and cards came with it' {
    $tent = Get-UiId -Name 'Old tent' -Prefix 'Card:' -Within "Board:$pane"
    $stove = Get-UiId -Name 'Old stove' -Prefix 'Card:' -Within "Board:$pane"
    $tent -and $stove -and (Get-UiStatus $tent) -match 'P2' -and (Get-UiStatus $tent) -match '0/1 subtasks' -and (Get-UiStatus $tent) -match 'camping' -and (Get-UiStatus $stove) -match '^done'
}

# --- 2. A second start imports nothing ---
Exit-App
Start-App
$null = Wait-Ui { Find-Ui -Id 'Sidebar' } 15000
Start-Sleep -Milliseconds 800
Test-Ui 'second start: no summary' { -not (Find-Ui -Id 'ImportSummary') }
Test-Ui 'second start: the notes are still there, once' { @(Get-UiIds -Prefix 'note:' -Within 'Sidebar' | Where-Object { (Get-UiName $_) -eq 'Old trip' }).Count -eq 1 }
Test-Ui 'second start: the log shows one import, and no error' { @((Get-Log) -match 'legacy: imported').Count -eq 1 -and -not ((Get-Log) -match 'error shown|unhandled exception') }
Exit-App

# --- 3. An old library that cannot be read ---
Stop-Fixture-Processes
& (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
[System.IO.File]::WriteAllText($Old, 'This is not a database.', [System.Text.Encoding]::ASCII)
$before = (Get-FileHash $Old -Algorithm SHA256).Hash
Start-App
Test-Ui 'unreadable: the app starts all the same' { Find-Ui -Id 'Sidebar' }
Test-Ui 'unreadable: it says the old library was not changed' { (Get-UiName 'ErrorText') -match 'could not be imported' -and (Get-UiName 'ErrorText') -match 'has not been changed' }
Invoke-Ui -Id 'ErrorOk'
Test-Ui 'unreadable: there is no summary, and the library is the empty one' {
    -not (Find-Ui -Id 'ImportSummary') -and @(Get-UiIds -Prefix 'notebook:' -Within 'Sidebar').Count -eq 1 -and @(Get-UiIds -Prefix 'note:' -Within 'Sidebar').Count -eq 0
}
Test-Ui 'unreadable: the file is as it was, and the log says why nothing came' { (Get-FileHash $Old -Algorithm SHA256).Hash -eq $before -and ((Get-Log) -match 'legacy: could not be imported') }
Test-Ui 'the log holds no unhandled exception' { -not ((Get-Log) -match 'unhandled exception') }
Exit-App

Show-UiResults
Stop-Fixture-Processes
exit $script:UiFailed.Count
