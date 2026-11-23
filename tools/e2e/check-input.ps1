<#
.SYNOPSIS
    Uses the built app for a minute with a REAL mouse and keyboard, and says
    what passed.

.DESCRIPTION
        tools\e2e\check-input.ps1 [-Exe <path>]

    Every other check goes through UI Automation and never touches the mouse
    or the keys, so none of them reaches the last inch between Windows' input
    and the app. This one does: it moves the pointer, clicks, drags and types
    on the desktop, in the app's own window on a throwaway library.

    What it does: clicks into a paragraph and types; Enter for a new block;
    a Markdown heading as it is typed; the slash menu; Backspace in an empty
    block; Ctrl+Z; the Up arrow; Quick Open by its key; a click on a sidebar row; a click on a
    card's box; a card dragged onto another and onto a column; a card
    clicked open, its title typed over, Esc to close; a divider dragged; a
    tab dragged to another pane.

    BEFORE RUNNING: leave the mouse and the keyboard alone until it has
    finished (about a minute). It brings the app to the front, and stops, with
    nothing typed or clicked, the moment another program has the keyboard.
    It never copies or pastes: the clipboard is left as it was.

    Exit code: the number of checks that failed.
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
$Ui = Join-Path $E2e 'ui.ps1'
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

# Real input. Each of these stops the run if the app does not have the keyboard.
function Send-Click { param([string]$Id) & $Ui click -Id $Id 6>&1 | Out-Null; Start-Sleep -Milliseconds 250 }
function Send-Keys { param([string]$Keys) & $Ui keys -Keys $Keys 6>&1 | Out-Null; Start-Sleep -Milliseconds 200 }
function Send-Text { param([string]$Text) & $Ui text -Text $Text 6>&1 | Out-Null; Start-Sleep -Milliseconds 250 }
function Send-Drag { param([string]$From, [string]$To, [int]$Right = 0, [int]$Down = 0) & $Ui dragto -Id $From -ToId $To -ToX $Right -ToY $Down 6>&1 | Out-Null; Start-Sleep -Milliseconds 500 }

function Get-Blocks { param([string]$Pane) @(Get-UiIds -Prefix 'Block:' -Within "NoteText:$Pane" | Select-Object -Unique) }

# Where an element is on the screen, in pixels: left, top, width, height.
function Get-UiRect {
    param([string]$Id)
    $line = Find-Ui -Id $Id
    if ($line -and $line -match '\[(-?\d+),(-?\d+) (\d+)x(\d+)\]') { return @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4]) }
    return $null
}

function Get-CardNames { param([string]$Column) @(Get-UiIds -Prefix 'Card:' -Within "Column:$Column" | ForEach-Object { Get-UiName $_ }) }
function Get-Column { param([string]$Pane, [string]$Name) (Get-UiId -Name $Name -Prefix 'Column:' -Within "Board:$Pane") -replace '^Column:', '' }

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
& $Ui foreground 6>&1 | Out-Null

# --- 1. The sidebar under a real click ---
$trip = Get-UiId -Name 'Trip' -Prefix 'note:' -Within 'Sidebar'
Send-Click $trip
$pane = @(Get-Panes)[0]
Test-Ui 'sidebar: a click on a row opens the note' { Find-Ui -Id "Tab:${pane}:$($trip -replace '^note:', '')" }

# --- 2. Writing with real keys ---
$first = @(Get-Blocks $pane)[0]
Send-Click $first
Send-Keys '{END}'
Send-Text ' Twice over.'
Test-Ui 'typing: a click in a paragraph puts the caret there, and keys are typed into it' { (Get-UiName $first) -eq 'Pack the lantern. Twice over.' }
Send-Keys '{ENTER}'
Send-Text 'A second line.'
Test-Ui 'typing: Enter makes a new block, and typing goes on in it' {
    $blocks = @(Get-Blocks $pane)
    $blocks.Count -eq 2 -and (Get-UiName $blocks[1]) -eq 'A second line.' -and (Get-UiName $blocks[0]) -eq 'Pack the lantern. Twice over.'
}
Send-Keys '{ENTER}'
Send-Text '# '
Send-Text 'Heading'
Test-Ui 'typing: a hash and a space make a heading as they are typed' {
    $blocks = @(Get-Blocks $pane)
    $blocks.Count -eq 3 -and (Get-UiName $blocks[2]) -eq 'Heading' -and (Get-UiStatus $blocks[2]) -eq 'heading 1'
}
Send-Keys '{ENTER}'
Send-Text '/task'
Send-Keys '{ENTER}'
Send-Text 'Said and done.'
Test-Ui 'typing: the slash menu turns the block into what was chosen' {
    $blocks = @(Get-Blocks $pane)
    $blocks.Count -eq 4 -and (Get-UiStatus $blocks[3]) -eq 'todo' -and (Get-UiName $blocks[3]) -eq 'Said and done.'
}
Send-Keys '{ENTER}'
Send-Keys '{ENTER}'
Test-Ui 'typing: Enter twice at the end of a task list leaves it, into a paragraph' {
    $blocks = @(Get-Blocks $pane)
    $blocks.Count -ge 5 -and (Get-UiStatus $blocks[-1]) -eq 'paragraph'
}
$count = @(Get-Blocks $pane).Count
Send-Keys '{BACKSPACE}'
Test-Ui 'typing: Backspace in an empty block takes the block away' { @(Get-Blocks $pane).Count -eq ($count - 1) }
Send-Keys '^z'
Test-Ui 'typing: Ctrl+Z brings it back' { @(Get-Blocks $pane).Count -eq $count }
# Arrow keys walk from block to block: up from the last, and type at the end of the one above.
Send-Keys '{UP}'
Send-Keys '{END}'
Send-Text ' Indeed.'
Test-Ui 'typing: the Up arrow goes into the block above' { (Get-UiName (@(Get-Blocks $pane)[3])) -eq 'Said and done. Indeed.' }

# --- 3. Quick Open by its key ---
Send-Keys '^k'
Test-Ui 'quick open: Ctrl+K shows it' { Find-UiShown -Id 'Switcher' }
Send-Text 'bud'
Send-Keys '{ENTER}'
$budget = (Get-UiId -Name 'Budget' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Test-Ui 'quick open: typing a name and Enter opens the note' { (Find-Ui -Id "Tab:${pane}:$budget") -and -not (Find-UiShown -Id 'Switcher') }
Test-Ui 'saved: what was typed is the note''s, read back from the sidebar''s search' {
    # The words were saved when the pane moved on to another note.
    Set-UiValue 'SidebarSearch' 'Indeed'
    $hit = Wait-Ui { @(Get-UiIds -Prefix 'hit:' -Within 'Sidebar').Count -ge 1 }
    Set-UiValue 'SidebarSearch' ''
    $hit
}

# --- 4. A board under a real hand ---
$packing = Get-UiId -Name 'Packing' -Prefix 'note:' -Within 'Sidebar'
Send-Click $packing
$null = Wait-Ui { Find-Ui -Id "Board:$pane" }
$upNext = Get-Column $pane 'Up Next'
$active = Get-Column $pane 'Active'
$done = Get-Column $pane 'Done'
$stove = Get-UiId -Name 'Stove' -Prefix 'Card:' -Within "Board:$pane"
$map = Get-UiId -Name 'Map' -Prefix 'Card:' -Within "Board:$pane"
$tent = Get-UiId -Name 'Tent' -Prefix 'Card:' -Within "Board:$pane"
Send-Click ($stove -replace '^Card:', 'CardDone:')
Test-Ui 'board: a click on a card''s box ticks it' { (Get-UiStatus $stove) -match '^done' }
Send-Drag $stove $map
Test-Ui 'board: a card dragged onto a card lands in front of it' { ((Get-CardNames $active) -join '|') -eq 'Stove|Map' -and ((Get-CardNames $upNext) -join '|') -eq 'Tent' }
Send-Drag $stove "NewCard:$done"
Test-Ui 'board: a card dragged onto a column goes to its end' { ((Get-CardNames $done) -join '|') -eq 'Lantern|Stove' -and ((Get-CardNames $active) -join '|') -eq 'Map' }
Send-Click $tent
Test-Ui 'board: a click on a card opens its task' { Find-Ui -Id 'TaskDetail' }
Send-Click 'TaskTitle'
Send-Keys '{END}'
Send-Text ', the big one'
Send-Keys '{ENTER}'
Test-Ui 'task: a title typed over is kept on Enter, and the card shows it' { (Get-UiName $tent) -eq 'Tent, the big one' }
# The box for a new subtask is below what the window shows: scrolled to, then clicked.
& $Ui scroll -Id 'TaskFields' -Y 100 6>&1 | Out-Null
Start-Sleep -Milliseconds 300
Send-Click 'TaskNewSubtask'
Send-Text 'Groundsheet'
Send-Keys '{ENTER}'
Send-Text 'Poles'
Send-Keys '{ENTER}'
Test-Ui 'task: two subtasks typed one after the other, Enter after each' {
    (Get-UiId -Name 'Groundsheet' -Prefix 'Subtask:' -Within 'TaskDetail') -and (Get-UiId -Name 'Poles' -Prefix 'Subtask:' -Within 'TaskDetail') -and (Get-UiValue 'TaskNewSubtask') -eq ''
}
Send-Keys '{ESC}'
Test-Ui 'task: Esc closes the window' { -not (Find-Ui -Id 'TaskDetail') }

# --- 5. Panes under a real hand ---
Invoke-UiMenu 'View' 'Split Right'
$right = @(Get-Panes | Where-Object { $_ -ne $pane })[0]
$divider = @(Get-UiIds -Prefix 'Divider:')[0]
$before = Get-UiRect "Pane:$pane"
Send-Drag $divider $divider -Right 240
Test-Ui 'panes: a divider dragged to the right makes the left pane wider' {
    $after = Get-UiRect "Pane:$pane"
    $before -and $after -and ($after[2] - $before[2]) -gt 120
}
# The right pane shows the same note. Its tab is dragged across to the left pane's tabs.
$note = $packing -replace '^note:', ''
Select-Ui "note:$budget"
$null = Wait-Ui { Find-Ui -Id "Tab:${right}:$budget" }
Send-Drag "Tab:${right}:$budget" "Tab:${pane}:$note"
Test-Ui 'panes: a tab dragged onto another pane''s tabs moves there' { (Find-Ui -Id "Tab:${pane}:$budget") -and -not (Find-Ui -Id "Tab:${right}:$budget") }

Test-Ui 'the log holds no unhandled exception' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'unhandled exception') }
Test-Ui 'the log shows nothing going wrong' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'error shown') }
Invoke-UiMenu 'File' 'Exit'
$null = Wait-Ui { @(Get-Fixture-Processes).Count -eq 0 } 5000

Show-UiResults
Stop-Fixture-Processes
exit $script:UiFailed.Count
