<#
.SYNOPSIS
    Drives the built app through a board and a task, and says what passed.

.DESCRIPTION
        tools\e2e\check-boards.ps1 [-Exe <path>]

    Resets the fixture, starts the app on it with a few seeded notes, and
    checks a todo list in the real window: its board shows its columns and
    cards, a column and a card can be added, a column renamed and moved, a
    card ticked; a card opens as a task, which can be renamed, given a
    priority, a due date, a label, a reminder, a subtask and a comment; a
    subtask opens in the same window with a way back, and can be made a card;
    a second pane on the same list follows; it is all there after a restart;
    and a list in the trash cannot be changed.

    Everything goes through UI Automation patterns: the mouse and keyboard are
    not touched, and neither is the clipboard. Windows of the app do come to
    the front. Exit code: the number of checks that failed.

    Not covered, because it needs a real mouse: dragging a card. The unit
    tests reach what a drop decides.
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

# A column's id by its name, in one pane's board.
function Get-Column { param([string]$Pane, [string]$Name) (Get-UiId -Name $Name -Prefix 'Column:' -Within "Board:$Pane") -replace '^Column:', '' }

# The names of a board's columns, left to right.
function Get-ColumnNames { param([string]$Pane) @(Get-UiIds -Prefix 'Column:' -Within "Board:$Pane" | ForEach-Object { Get-UiName $_ }) }

# A card's id by its title, in one pane's board.
function Get-Card { param([string]$Pane, [string]$Name) (Get-UiId -Name $Name -Prefix 'Card:' -Within "Board:$Pane") -replace '^Card:', '' }

# Put something new in one of the boxes that take new things (a column, a card,
# a label, a subtask, a comment), and hand it over by the button that appears.
function Add-UiEntry {
    param([string]$Box, [string]$Text)
    Set-UiValue $Box $Text
    $null = Wait-Ui { Find-UiShown -Id "Add:$Box" }
    Invoke-Ui -Id "Add:$Box"
}

# Change what a box holds, and move the keyboard on, which is when it is kept.
function Set-UiStored {
    param([string]$Box, [string]$Text, [string]$Then)
    Set-UiValue $Box $Text
    Set-UiFocus $Then
}

# The titles of a column's cards, top to bottom.
function Get-CardNames { param([string]$Column) @(Get-UiIds -Prefix 'Card:' -Within "Column:$Column" | ForEach-Object { Get-UiName $_ }) }

# Something that should be there and is not ends the run. The app is not
# left running for that: the next build could not replace it.
trap {
    Write-Host "FAIL  the check stopped: $_"
    Show-UiResults
    Stop-Fixture-Processes
    exit ($script:UiFailed.Count + 1)
}

Stop-Fixture-Processes
& (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
Start-App -AppArgs @('--e2e-seed')
if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host 'FAIL  the window did not appear'; exit 1 }

# --- 1. A todo list is a board ---
$packing = (Get-UiId -Name 'Packing' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Select-Ui "note:$packing"
$pane = @(Get-Panes | Where-Object { Find-Ui -Id "Tab:${_}:$packing" })[0]
Test-Ui 'board: a todo list shows a board' { $pane -and (Find-Ui -Id "Board:$pane") }
Test-Ui 'board: its columns are there, left to right' { ((Get-ColumnNames $pane) -join '|') -eq 'Up Next|Active|Done' }
$upNext = Get-Column $pane 'Up Next'
Test-Ui 'board: a column holds its cards, top to bottom' { ((Get-CardNames $upNext) -join '|') -eq 'Tent|Stove' }
$tent = Get-Card $pane 'Tent'
Test-Ui 'board: a card says its priority, its subtasks and its labels' {
    $status = Get-UiStatus "Card:$tent"
    $status -match 'P2' -and $status -match '1/2' -and $status -match 'camping' -and $status -match 'due '
}
$lantern = Get-Card $pane 'Lantern'
Test-Ui 'board: a card that is done says so' { (Get-UiStatus "Card:$lantern") -match '^done' }

# --- 2. Columns ---
Add-UiEntry "NewColumn:$pane" 'Waiting'
Test-Ui 'columns: a new column is added at the right' { ((Get-ColumnNames $pane) -join '|') -eq 'Up Next|Active|Done|Waiting' }
Test-Ui 'columns: the box is empty again for the next' { (Get-UiValue "NewColumn:$pane") -eq '' }
$waiting = Get-Column $pane 'Waiting'
Set-UiStored "ColumnName:$waiting" 'Blocked' 'SidebarSearch'
Test-Ui 'columns: a column is renamed where its name is' { ((Get-ColumnNames $pane) -join '|') -eq 'Up Next|Active|Done|Blocked' }
Invoke-Ui -Id "ColumnMenu:$waiting"
Start-Sleep -Milliseconds 250
Invoke-Ui -Id "ColumnLeft:$waiting"
Test-Ui 'columns: a column moves left' { ((Get-ColumnNames $pane) -join '|') -eq 'Up Next|Active|Blocked|Done' }

# --- 3. Cards ---
Add-UiEntry "NewCard:$waiting" 'Passports'
Test-Ui 'cards: a new card is added to its column' { ((Get-CardNames $waiting) -join '|') -eq 'Passports' }
$stove = Get-Card $pane 'Stove'
Invoke-Ui -Id "CardDone:$stove"
Test-Ui 'cards: ticking a card marks it done' { (Get-UiStatus "Card:$stove") -match '^done' }
Invoke-Ui -Id "ColumnMenu:$waiting"
Start-Sleep -Milliseconds 250
Invoke-Ui -Id "ColumnDelete:$waiting"
Test-Ui 'columns: a deleted column hands its cards to the first' {
    ((Get-ColumnNames $pane) -join '|') -eq 'Up Next|Active|Done' -and ((Get-CardNames $upNext) -join '|') -eq 'Tent|Stove|Passports'
}

# --- 4. A card opens as a task ---
Invoke-Ui -Id "Card:$tent"
Test-Ui 'task: a card opens in a window of its own' { (Find-Ui -Id 'TaskDetail') -and (Get-UiValue 'TaskTitle') -eq 'Tent' }
Test-Ui 'task: its notes, subtasks and comments are shown' {
    (Get-UiValue 'TaskNotes') -eq 'The green one.' -and (Get-UiId -Name 'Pegs' -Prefix 'Subtask:' -Within 'TaskDetail') -and (Get-UiId -Name 'Borrow the big one?' -Prefix 'Comment:' -Within 'TaskDetail')
}
Set-UiStored 'TaskTitle' 'Big tent' 'TaskClose'
Test-Ui 'task: a new title reaches the card' { (Get-UiName "Card:$tent") -eq 'Big tent' }
Invoke-Ui -Id 'TaskPriority:1'
Test-Ui 'task: a priority reaches the card' { (Get-UiStatus "Card:$tent") -match 'P1' }
Add-UiEntry 'TaskNewLabel' 'borrowed'
Test-Ui 'task: a label is added, and reaches the card' { (Find-Ui -Id 'Label:borrowed') -and (Get-UiStatus "Card:$tent") -match 'borrowed' }
Invoke-Ui -Id 'LabelRemove:camping'
Test-Ui 'task: a label is taken off' { -not (Find-Ui -Id 'Label:camping') -and (Get-UiStatus "Card:$tent") -notmatch 'camping' }
Invoke-Ui -Id 'TaskDeadlineSet'
Test-Ui 'task: a deadline is set, and can be taken off again' { Find-UiShown -Id 'TaskDeadlineClear' }
Invoke-Ui -Id 'TaskDeadlineClear'
Test-Ui 'task: with no deadline, one can be set' { Find-UiShown -Id 'TaskDeadlineSet' }
Set-UiStored 'TaskLocation' 'The shed' 'TaskClose'
Set-UiValue 'TaskReminderTime' '09:30'
Invoke-Ui -Id 'TaskAddReminder'
Test-Ui 'task: a reminder is added' { @(Get-UiIds -Prefix 'Reminder:' -Within 'TaskDetail').Count -eq 1 }
Add-UiEntry 'TaskNewSubtask' 'Groundsheet'
$groundsheet = (Get-UiId -Name 'Groundsheet' -Prefix 'Subtask:' -Within 'TaskDetail') -replace '^Subtask:', ''
Test-Ui 'task: a subtask is added, and the card counts it' { $groundsheet -and (Get-UiStatus "Card:$tent") -match '1/3' }
Add-UiEntry 'TaskNewComment' 'Ask Sam.'
Test-Ui 'task: a comment is added under the first' { @(Get-UiIds -Prefix 'Comment:' -Within 'TaskDetail').Count -eq 2 -and (Get-UiId -Name 'Ask Sam.' -Prefix 'Comment:' -Within 'TaskDetail') }
Set-UiValue 'TaskNotes' 'The green one, and its poles.'

# --- 5. A subtask, in the same window ---
Invoke-Ui -Id "Subtask:$groundsheet"
Test-Ui 'subtask: it opens in the same window, under its task' { (Get-UiValue 'TaskTitle') -eq 'Groundsheet' -and (Get-UiName 'TaskPath') -eq 'Big tent' -and (Find-UiShown -Id 'TaskBack') }
Invoke-Ui -Id 'TaskBack'
Test-Ui 'subtask: the way back leads to the task' { (Get-UiValue 'TaskTitle') -eq 'Big tent' -and -not (Find-UiShown -Id 'TaskBack') }
Test-Ui 'notes: what was written is still there after going down and back' { (Get-UiValue 'TaskNotes') -eq 'The green one, and its poles.' }
Invoke-Ui -Id "Subtask:$groundsheet"
$null = Wait-Ui { (Get-UiValue 'TaskTitle') -eq 'Groundsheet' }
$active = Get-Column $pane 'Active'
Invoke-Ui -Id 'TaskMakeCard'
Start-Sleep -Milliseconds 250
Invoke-Ui -Id "MakeCard:$active"
Test-Ui 'subtask: made a card, it is on the board and no longer under its task' {
    ((Get-CardNames $active) -join '|') -eq 'Map|Groundsheet' -and (Get-UiStatus "Card:$tent") -match '1/2' -and -not (Find-UiShown -Id 'TaskBack')
}
Invoke-Ui -Id 'TaskClose'
Test-Ui 'task: the window closes' { -not (Find-Ui -Id 'TaskDetail') }

# --- 6. Two panes on one list keep in step ---
Invoke-UiMenu 'View' 'Split Right'
$right = @(Get-Panes | Where-Object { $_ -ne $pane })[0]
Test-Ui 'two panes: the second shows the same board' { $right -and (Get-Card $right 'Big tent') -eq $tent }
# A card has the same id in both panes, so the first found is the one ticked.
# Whichever it is, both boards must show it done.
Invoke-Ui -Id "CardDone:$tent"
Test-Ui 'two panes: a card ticked in one is done in the other' {
    $in = @(Get-UiTree -Within "Board:$pane") + @(Get-UiTree -Within "Board:$right") | Where-Object { $_ -match "id='Card:$tent'" }
    @($in).Count -eq 2 -and @($in | Where-Object { $_ -match "status='done" }).Count -eq 2
}

# --- 7. A task deleted from its window ---
Invoke-Ui -Id "Card:$stove"
$null = Wait-Ui { Find-Ui -Id 'TaskDetail' }
Invoke-Ui -Id 'TaskDelete'
Test-Ui 'delete: the task goes, and its window with it' { -not (Find-Ui -Id 'TaskDetail') -and -not (Get-Card $pane 'Stove') }

# --- 8. It is all kept ---
Exit-App
Start-App
$null = Wait-Ui { Find-Ui -Id "Board:$pane" } 15000
Test-Ui 'kept: the board is as it was left' {
    ((Get-ColumnNames $pane) -join '|') -eq 'Up Next|Active|Done' -and ((Get-CardNames $upNext) -join '|') -eq 'Big tent|Passports' -and ((Get-CardNames $active) -join '|') -eq 'Map|Groundsheet'
}
Test-Ui 'kept: the card says what the task was given' {
    $status = Get-UiStatus "Card:$tent"
    $status -match '^done' -and $status -match 'P1' -and $status -match 'borrowed'
}
Invoke-Ui -Id "Card:$tent"
Test-Ui 'kept: the task has its notes, its place, its reminder and its comments' {
    (Get-UiValue 'TaskNotes') -eq 'The green one, and its poles.' -and (Get-UiValue 'TaskLocation') -eq 'The shed' -and @(Get-UiIds -Prefix 'Reminder:' -Within 'TaskDetail').Count -eq 1 -and @(Get-UiIds -Prefix 'Comment:' -Within 'TaskDetail').Count -eq 2
}

# --- 9. A list in the trash cannot be changed ---
Invoke-UiMenu 'Note' 'Move to Trash'
Test-Ui 'trash: the task window of a list in the trash closes' { -not (Find-Ui -Id 'TaskDetail') }
try { Add-UiEntry "NewCard:$upNext" 'Changed in the trash' } catch { }
try { Invoke-Ui -Id "CardDone:$tent" } catch { }
Start-Sleep -Milliseconds 400
Test-Ui 'trash: a list in the trash keeps its cards as they were' {
    ((Get-CardNames $upNext) -join '|') -eq 'Big tent|Passports' -and (Get-UiStatus "Card:$tent") -match '^done'
}

# The task was given a reminder above. A run on a test library must not
# hand it to Windows, nor take the link scheme for itself.
Test-Ui 'a test library takes nothing of Windows: no links, no reminders' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'windows: links|reminders:') }
Test-Ui 'the log holds no unhandled exception' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'unhandled exception') }
Test-Ui 'the log shows nothing going wrong' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'error shown') }
Exit-App

Show-UiResults
Stop-Fixture-Processes
exit $script:UiFailed.Count
