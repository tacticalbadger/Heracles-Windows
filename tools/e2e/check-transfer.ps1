<#
.SYNOPSIS
    Drives the built app through importing files and exporting a note, and
    says what passed.

.DESCRIPTION
        tools\e2e\check-transfer.ps1 [-Exe <path>]

    Resets the fixture, writes a Markdown file with a picture beside it and
    an HTML file under build\e2e\files, starts the app, and checks File >
    Import and File > Export as Markdown in the real window, through the
    system's own dialogs: both files come in as pages under the open note,
    the last is shown, the picture came with its page, the page is written
    out again as Markdown with its picture in a folder beside it, and a dialog
    that is cancelled brings nothing in.

    The app's own controls are driven by UI Automation patterns. The system's
    dialogs offer none, so their file-name box and their button are sent the
    window messages a program sends its own controls (see ui.ps1). Nothing
    here is keyboard or mouse input, and the clipboard is not touched.
    Windows of the app do come to the front. Exit code: the number of checks
    that failed.

    The dialogs' buttons are found by their English names (Open, Save).
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
$Files = Join-Path $Fixture 'files'
$LogFile = Join-Path $Fixture 'heracles.log'
$Ellipsis = [string][char]0x2026
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

# Answer one of the system's file dialogs: put a name in its box and press its button.
function Complete-FileDialog {
    param([string]$Title, [string]$NameBox, [string]$Button, [string]$Answer)
    if (-not (Wait-Ui { Find-Ui -Name $Title } 8000)) { throw "the dialog '$Title' did not open" }
    Start-Sleep -Milliseconds 400
    Set-UiValue $NameBox $Answer
    Start-Sleep -Milliseconds 200
    Invoke-Ui -Name $Button
    $null = Wait-Ui { -not (Find-Ui -Name $Title) } 8000
}

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

# The files to bring in. A one-pixel picture stands beside the Markdown file.
if (Test-Path $Files) { Remove-Item $Files -Recurse -Force }
New-Item -ItemType Directory -Force $Files | Out-Null
$ascii = [System.Text.Encoding]::ASCII
[System.IO.File]::WriteAllText((Join-Path $Files 'Ferry.md'), "# Ferry times`n`nSix and noon.`n`n![The harbour](harbour.png)`n", $ascii)
[System.IO.File]::WriteAllText((Join-Path $Files 'Tides.html'), '<h1>Tides</h1><p>High water at six.</p>', $ascii)
[System.IO.File]::WriteAllBytes((Join-Path $Files 'harbour.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='))

Start-App -AppArgs @('--e2e-seed')
if (-not (Wait-Ui { Find-Ui -Id 'Sidebar' } 15000)) { Write-Host 'FAIL  the window did not appear'; exit 1 }

# --- 1. Import, through the system's dialog ---
$trip = (Get-UiId -Name 'Trip' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Select-Ui "note:$trip"
$pane = @(Get-Panes)[0]
Invoke-UiMenu 'File' "Import$Ellipsis"
# Several files are named the way the dialog itself writes them: each in quotes.
Complete-FileDialog 'Import' '1148' 'Open' ('"{0}" "{1}"' -f (Join-Path $Files 'Ferry.md'), (Join-Path $Files 'Tides.html'))
$ferry = (Get-UiId -Name 'Ferry' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
$tides = (Get-UiId -Name 'Tides' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
Test-Ui 'import: both files are pages in the sidebar' { $ferry -and $tides }
# The dialog hands the files over in an order of its own, so either may be last.
Test-Ui 'import: the last of them is shown, and only that one is opened' { @($ferry, $tides | Where-Object { Find-Ui -Id "Tab:${pane}:$_" }).Count -eq 1 }
Select-Ui "note:$tides"
Test-Ui 'import: an HTML file''s words are the page''s' { Get-UiId -Name 'High water at six.' -Prefix 'Block:' -Within "NoteText:$pane" }
Select-Ui "note:$ferry"
Test-Ui 'import: a Markdown file''s words are the page''s' { Get-UiId -Name 'Six and noon.' -Prefix 'Block:' -Within "NoteText:$pane" }
Test-Ui 'import: the picture beside it is in the image store' { @(Get-ChildItem (Join-Path $Fixture 'data\images') -Recurse -File -ErrorAction SilentlyContinue).Count -ge 1 }

# --- 2. Export, through the system's dialog ---
$out = Join-Path $Files 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$written = Join-Path $out 'Ferry again.md'
Invoke-UiMenu 'File' "Export as Markdown$Ellipsis"
Complete-FileDialog 'Export as Markdown' '1001' 'Save' $written
Test-Ui 'export: the note is written as Markdown where it was asked for' { (Test-Path $written) -and ((Get-Content $written -Raw) -match 'Six and noon') }
Test-Ui 'export: its picture is in a folder beside it' { @(Get-ChildItem $out -Recurse -File | Where-Object { $_.Extension -eq '.png' }).Count -eq 1 }

# --- 3. A dialog that is cancelled ---
$before = @(Get-UiIds -Prefix 'note:' -Within 'Sidebar').Count
Invoke-UiMenu 'File' "Import$Ellipsis"
if (-not (Wait-Ui { Find-Ui -Name 'Import' } 8000)) { throw 'the Import dialog did not open' }
Invoke-Ui -Name 'Cancel'
Test-Ui 'import: cancelling the dialog brings nothing in' { -not (Find-Ui -Name 'Import') -and @(Get-UiIds -Prefix 'note:' -Within 'Sidebar').Count -eq $before }

Test-Ui 'the log holds no unhandled exception' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'unhandled exception') }
Test-Ui 'the log shows nothing going wrong' { -not ((Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'error shown') }
Exit-App

Show-UiResults
Stop-Fixture-Processes
exit $script:UiFailed.Count
