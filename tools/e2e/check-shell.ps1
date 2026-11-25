<#
.SYNOPSIS
    Drives the built app through the window's behaviour and says what passed.

.DESCRIPTION
        tools\e2e\check-shell.ps1 [-Exe <path>]

    Resets the fixture, starts the app on it with a few seeded notes, and
    checks the sidebar, tabs and panes, search, Quick Open, renaming, the
    trash, a second window kept in step with the first, the hand-over from a
    second copy of the app, and the layout coming back after a restart.

    Everything goes through UI Automation patterns: the mouse and keyboard are
    not touched, and neither is the clipboard. Windows of the app do come to
    the front. Exit code: the number of checks that failed.

    Not covered, because they need a real mouse or keyboard: context menus,
    dragging tabs and dividers, and the keyboard shortcuts.
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
    param([string[]]$AppArgs = @(), [switch]$Again)
    $launch = @{ Wait = 0 }
    if ($Exe) { $launch.Exe = $Exe }
    if ($Again) { $launch.Again = $true }
    & (Join-Path $E2e 'launch.ps1') @launch @AppArgs 6>&1 | Out-Null
}

function Get-WindowTitles {
    @(& (Join-Path $E2e 'ui.ps1') windows 6>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match "^Window '(.*)' id=" } | ForEach-Object { $Matches[1] })
}

function Get-Panes { @(Get-UiIds -Prefix 'Pane:' | ForEach-Object { $_ -replace '^Pane:', '' } | Select-Object -Unique) }

function Exit-App {
    Invoke-UiMenu 'File' 'Exit'
    $null = Wait-Ui { @(Get-Fixture-Processes).Count -eq 0 } 5000
}

Stop-Fixture-Processes
& (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
Start-App -AppArgs @('--e2e-seed')
if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host 'FAIL  the window did not appear'; exit 1 }

# --- 0. Updates: a run on a test library must never look for the real app's ---
Test-Ui 'updates: this run does not look for them, and says why' { (Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'updates: off: ' }
& $script:UiScript expand -Name 'Help' 6>&1 | Out-Null
Start-Sleep -Milliseconds 300
Test-Ui 'updates: the two items for them are in the Help menu, greyed out' {
    (Find-Ui -Id 'UpdateCheck') -match 'DISABLED' -and (Find-Ui -Id 'UpdateAutomatic') -match 'DISABLED'
}
try { & $script:UiScript collapse -Name 'Help' 6>&1 | Out-Null } catch { }
Start-Sleep -Milliseconds 200

# --- 1. The sidebar ---
$trip = (Get-UiId -Name 'Trip' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
$budget = (Get-UiId -Name 'Budget' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Test-Ui 'sidebar: Trip is listed under Favorites and in its section' { $trip -and (Find-Ui -Id "favorite:$trip") -and (Find-Ui -Id "note:$trip") }
Test-Ui 'sidebar: the notebook, Plans, Budget and Packing are listed' {
    @(Get-UiIds -Prefix 'notebook:' -Within 'Sidebar').Count -eq 1 -and (Get-UiId -Name 'Plans' -Prefix 'note:' -Within 'Sidebar') -and $budget -and (Get-UiId -Name 'Packing' -Prefix 'note:' -Within 'Sidebar')
}
Test-Ui 'sidebar: a note with no title is called Untitled' { Get-UiId -Name 'Untitled' -Prefix 'note:' -Within 'Sidebar' }
Test-Ui 'sidebar: the archive and the trash list their notes' { (Get-UiId -Name 'Old ideas' -Prefix 'archived:' -Within 'Sidebar') -and (Get-UiId -Name 'Scrap' -Prefix 'trashed:' -Within 'Sidebar') }

# --- 2. Opening a note ---
Select-Ui "note:$trip"
$pane = @(Get-Panes)[0]
Test-Ui 'open: choosing Trip gives it a tab' { Find-Ui -Id "Tab:${pane}:$trip" }
Test-Ui 'open: its text is shown' { (Get-UiName "NoteText:$pane") -eq 'Pack the lantern.' }
Test-Ui 'open: the window is named after it' { @(Get-WindowTitles) -contains 'Trip' }

# --- 2b. The icon ---
Invoke-Ui -Id "NoteIcon:$pane"
Test-Ui 'icon: the picker opens' { Find-UiShown -Id 'IconPicker' }
Invoke-Ui -Id 'IconRemove'
Invoke-Ui -Id "NoteIcon:$pane"
Test-Ui 'icon: Remove takes the icon away' { (Find-Ui -Id 'IconRemove') -match 'DISABLED' }
$someIcon = @(Get-UiIds -Prefix 'Icon:')[0]
Invoke-Ui -Id $someIcon
Invoke-Ui -Id "NoteIcon:$pane"
Test-Ui 'icon: choosing one gives the note an icon' { $line = Find-Ui -Id 'IconRemove'; $line -and $line -notmatch 'DISABLED' }
Invoke-Ui -Id $someIcon

# --- 3. A second pane ---
Invoke-UiMenu 'View' 'Split Right'
Test-Ui 'split: Split Right gives a second pane' { @(Get-Panes).Count -eq 2 }
$right = @(Get-Panes | Where-Object { $_ -ne $pane })[0]
Select-Ui "note:$budget"
Test-Ui 'split: a note chosen now opens in the new pane' { Find-Ui -Id "Tab:${right}:$budget" }

# --- 4. Closing a tab ---
Invoke-Ui -Id "TabClose:${right}:$budget"
Invoke-Ui -Id "TabClose:${right}:$trip"
Test-Ui 'tabs: a pane whose last tab is closed goes away' { @(Get-Panes).Count -eq 1 }

# --- 5. Search ---
Set-UiValue 'SidebarSearch' 'lant'
Test-Ui 'search: notes whose text matches are listed' { (Find-Ui -Id "hit:$trip") -and (Find-Ui -Id "hit:$budget") }
Set-UiValue 'SidebarSearch' ''
Test-Ui 'search: clearing it brings the tree back' { (Find-Ui -Id "note:$trip") -and -not (Find-Ui -Id "hit:$trip") }

# --- 6. Quick Open ---
Invoke-UiMenu 'File' "Quick Open$([char]0x2026)"
Test-Ui 'quick open: it appears' { Find-UiShown -Id 'Switcher' }
Set-UiValue 'SwitcherQuery' 'bud'
Test-Ui 'quick open: typing finds Budget' { Find-Ui -Id "SwitcherResult:$budget" }
Select-Ui "SwitcherResult:$budget"
Test-Ui 'quick open: choosing it opens it and puts Quick Open away' { (Find-Ui -Id "Tab:${pane}:$budget") -and -not (Find-UiShown -Id 'Switcher') }

# --- 7. Renaming ---
Set-UiValue "NoteTitle:$pane" 'Budget 2027'
Set-UiFocus 'SidebarSearch'
Test-Ui 'rename: a new title reaches the sidebar and the tab' { (Get-UiName "note:$budget") -eq 'Budget 2027' -and (Get-UiName "Tab:${pane}:$budget") -eq 'Budget 2027' }

# --- 8. The trash ---
Invoke-UiMenu 'Note' 'Move to Trash'
Test-Ui 'trash: the note moves to the Trash section' { (Find-Ui -Id "trashed:$budget") -and -not (Find-Ui -Id "note:$budget") }
Test-Ui 'trash: it stays open, and its title can no longer be changed' { (Find-Ui -Id "NoteTitle:$pane") -match 'DISABLED' }

# --- 9. A second copy of the app, and a second window ---
Start-App -AppArgs @('--new-window') -Again
Test-Ui 'second copy: it hands over and a second window opens' { @(Get-Panes).Count -eq 2 -and @(Get-WindowTitles).Count -ge 2 }
Test-Ui 'second copy: one process is left' { @(Get-Fixture-Processes).Count -eq 1 }
Test-Ui 'second copy: the log says it handed over' { (Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'instance: handed over to the running app' }
$other = @(Get-Panes | Where-Object { $_ -ne $pane })[0]
# The newest window is found first, so this choice lands in the new window.
Select-Ui "note:$trip"
Test-Ui 'two windows: Trip opens in the new window' { Find-Ui -Id "Tab:${other}:$trip" }
Set-UiValue "NoteTitle:$other" 'Trip to the coast'
Set-UiFocus 'SidebarSearch'
Test-Ui 'two windows: renamed in one, it is renamed in the other' { (Get-UiName "Tab:${pane}:$trip") -eq 'Trip to the coast' -and (Get-UiName "Tab:${other}:$trip") -eq 'Trip to the coast' }

# --- 10. The layout comes back ---
Exit-App
Test-Ui 'exit: the app ends when asked to' { @(Get-Fixture-Processes).Count -eq 0 }
Start-App
Test-Ui 'restart: both windows come back with their tabs' { (Find-Ui -Id "Tab:${pane}:$trip") -and (Find-Ui -Id "Tab:${other}:$trip") }

# --- 11. A saved layout that cannot be read ---
Exit-App
Set-Content -Path (Join-Path $Fixture 'data\windows.json') -Value 'not json' -Encoding ascii
Start-App
Test-Ui 'damaged layout: the app opens one plain window' { (Find-Ui -Id 'Sidebar') -and @(Get-Panes).Count -eq 1 -and -not (Find-UiShown -Id 'StartupError') }
Test-Ui 'the log holds no unhandled exception' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'unhandled exception') }
Exit-App

Show-UiResults
Stop-Fixture-Processes
exit $script:UiFailed.Count
