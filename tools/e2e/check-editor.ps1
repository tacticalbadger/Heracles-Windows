<#
.SYNOPSIS
    Drives the built app through writing in a note and says what passed.

.DESCRIPTION
        tools\e2e\check-editor.ps1 [-Exe <path>]

    Resets the fixture, starts the app on it with a few seeded notes, and
    checks the editor in the real window: a note's blocks are there to be
    found, words set in a block become the note's, the Format and Edit menus
    act on the block being written in, a task can be ticked, two panes on one
    note keep in step, what was written is there after a restart, and a note
    in the trash cannot be changed.

    Everything goes through UI Automation patterns: the mouse and keyboard are
    not touched, and neither is the clipboard. Windows of the app do come to
    the front. Exit code: the number of checks that failed.

    Not covered, because they need a real mouse or keyboard: typing itself,
    every key, clicks and drags in a note, the block handle, and pasting.
    The unit tests reach the code behind each of those, with real text boxes
    in no window.
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

# The automation ids of a pane's blocks, in order down the page.
function Get-Blocks { param([string]$Pane) @(Get-UiIds -Prefix 'Block:' -Within "NoteText:$Pane" | Select-Object -Unique) }

# What a block says it is: 'paragraph', 'heading 2', 'todo done'.
function Get-BlockKind {
    param([string]$Id)
    $line = Find-Ui -Id $Id
    if ($line -and $line -match "status='([^']*)'") { return $Matches[1] }
    return $null
}

Stop-Fixture-Processes
& (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
Start-App -AppArgs @('--e2e-seed')
if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host 'FAIL  the window did not appear'; exit 1 }

# --- 1. A note is shown in the editor ---
$trip = (Get-UiId -Name 'Trip' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Select-Ui "note:$trip"
$pane = @(Get-Panes)[0]
Test-Ui 'shown: the note is a document to a screen reader' { (Find-Ui -Id "NoteText:$pane") -match '^\s*Document ' }
$block = @(Get-Blocks $pane)[0]
Test-Ui 'shown: its paragraph is a block, with its words and its kind' { $block -and (Get-UiName $block) -eq 'Pack the lantern.' -and (Get-BlockKind $block) -eq 'paragraph' }

# --- 2. Writing ---
Set-UiValue $block 'Pack the lantern and the stove.'
Test-Ui 'writing: words set in a block are the note''s words' { (Get-UiName "NoteText:$pane") -eq 'Pack the lantern and the stove.' }

# --- 3. The Format and Edit menus act on the block being written in ---
Invoke-UiMenu 'Format' 'Heading 2'
Test-Ui 'format: Heading 2 makes the block a heading' { (Get-BlockKind $block) -eq 'heading 2' }
Invoke-UiMenu 'Edit' 'Undo'
Test-Ui 'undo: the block is a paragraph again, with its words' { (Get-BlockKind $block) -eq 'paragraph' -and (Get-UiName $block) -eq 'Pack the lantern and the stove.' }
Invoke-UiMenu 'Edit' 'Undo'
Test-Ui 'undo: and before that, the words it had' { (Get-UiName $block) -eq 'Pack the lantern.' }
Invoke-UiMenu 'Edit' 'Redo'
Invoke-UiMenu 'Edit' 'Redo'
Test-Ui 'redo: both steps come back' { (Get-BlockKind $block) -eq 'heading 2' -and (Get-UiName $block) -eq 'Pack the lantern and the stove.' }

# --- 4. A task ---
Invoke-UiMenu 'Format' 'Task List'
Test-Ui 'task: Task List makes the block a task, not yet done' { (Get-BlockKind $block) -eq 'todo' }
Invoke-Ui -Id ($block -replace '^Block:', 'Check:')
Test-Ui 'task: ticking its box marks it done' { (Get-BlockKind $block) -eq 'todo done' }

# --- 5. Two panes on one note keep in step ---
Invoke-UiMenu 'View' 'Split Right'
$right = @(Get-Panes | Where-Object { $_ -ne $pane })[0]
Test-Ui 'two panes: the second shows the same note' { $right -and (Get-UiName "NoteText:$right") -eq 'Pack the lantern and the stove.' }
# The block has the same id in both panes, so the first found is written in.
# Whichever it is, the other pane must follow once the words are saved.
Set-UiValue $block 'Pack the lantern, the stove and a map.'
Test-Ui 'two panes: words written in one reach the other' {
    Wait-Ui { (Get-UiName "NoteText:$pane") -eq 'Pack the lantern, the stove and a map.' -and (Get-UiName "NoteText:$right") -eq 'Pack the lantern, the stove and a map.' } 5000
}

# --- 6. What was written is kept ---
Exit-App
Start-App
$null = Wait-Ui { Find-Ui -Id "NoteText:$pane" } 15000
Test-Ui 'kept: the words and the task are there after a restart' { (Get-UiName "NoteText:$pane") -eq 'Pack the lantern, the stove and a map.' -and (Get-BlockKind (@(Get-Blocks $pane)[0])) -eq 'todo done' }

# --- 7. A note in the trash cannot be changed ---
Invoke-UiMenu 'Note' 'Move to Trash'
$trashed = @(Get-Blocks $pane)[0]
try { Set-UiValue $trashed 'Changed in the trash.' } catch { }
Test-Ui 'trash: a note in the trash keeps its words' { (Get-UiName "NoteText:$pane") -eq 'Pack the lantern, the stove and a map.' }

# --- 8. A todo list still waits for its board ---
$packing = (Get-UiId -Name 'Packing' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Select-Ui "note:$packing"
$shown = @(Get-Panes | Where-Object { Find-Ui -Id "Tab:${_}:$packing" })[0]
Test-Ui 'todo list: it says its board is still to come' { $shown -and (Get-UiName "NoteText:$shown") -match 'Boards arrive' }

Test-Ui 'the log holds no unhandled exception' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'unhandled exception') }
Exit-App

Show-UiResults
Stop-Fixture-Processes
exit $script:UiFailed.Count
