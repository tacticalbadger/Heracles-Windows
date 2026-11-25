<#
.SYNOPSIS
    Checks a packaged disc image the way someone meets it: opened as a drive,
    looked at, and the app started from the disc itself.

.DESCRIPTION
        tools\e2e\check-disc.ps1 [-Arch arm64|x64]

    Mounts build\release\Heracles-<version>-win-<arch>.iso with a drive
    letter, as a double-click on it does, and checks what is on it (the
    Heracles folder, the installer and a README). Then it
    starts the app from the disc, on the fixture, opens a note and closes the
    app again. A disc cannot be written to, so this is also the check that
    the app keeps nothing beside itself.

    That every file on the disc is the file that was packaged is checked by
    the package step itself (tools\build.ps1 package), each time.

    A drive named "Heracles <version>" is in File Explorer while this runs,
    and the app's window comes to the front. Everything goes through UI
    Automation patterns: no mouse, no keyboard, no clipboard. Exit code: the
    number of checks that failed.
#>
[CmdletBinding()]
param(
    [ValidateSet('', 'arm64', 'x64')]
    [string]$Arch = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$E2e = $PSScriptRoot
$Root = Split-Path -Parent (Split-Path -Parent $E2e)
$Fixture = Join-Path $Root 'build\e2e'
$LogFile = Join-Path $Fixture 'heracles.log'
. (Join-Path $E2e 'ui-check.ps1')

if (-not $Arch) {
    # The machine's architecture, as in build.ps1 (an emulated shell reports its own).
    $machine = [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE', 'Machine')
    if (-not $machine) { $machine = $env:PROCESSOR_ARCHITECTURE }
    $Arch = if ($machine -eq 'ARM64') { 'arm64' } else { 'x64' }
}
$Version = (Get-Content (Join-Path $Root 'VERSION') -Raw).Trim()
$Iso = Join-Path $Root "build\release\Heracles-$Version-win-$Arch.iso"
if (-not (Test-Path $Iso)) { throw "no disc image at $Iso; run: tools\build.ps1 core package -Arch $Arch" }

# Instances of the app started from under a folder.
function Get-ProcessesUnder([string]$folder) {
    @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and $_.Path.StartsWith($folder.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
        })
}

function Stop-ProcessesUnder([string]$folder) {
    $running = Get-ProcessesUnder $folder
    $running | Stop-Process -Force
    $running | ForEach-Object { try { $_.WaitForExit(5000) | Out-Null } catch { } }
}

# Which processor a program is for, from its header.
function Get-PeArchitecture([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $machine = [BitConverter]::ToUInt16($bytes, [BitConverter]::ToInt32($bytes, 0x3c) + 4)
    if ($machine -eq 0xAA64) { return 'arm64' }
    if ($machine -eq 0x8664) { return 'x64' }
    return ('0x{0:X4}' -f $machine)
}

# A fixture cannot be reset under a running build, and two builds must not
# run on it at once.
Stop-ProcessesUnder (Join-Path $Root 'build')
& (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null

$script:Drive = $null
Mount-DiskImage -ImagePath $Iso -StorageType ISO | Out-Null
try {
    $null = Wait-Ui {
        $volume = Get-DiskImage -ImagePath $Iso | Get-Volume
        if ($volume -and $volume.DriveLetter) { $script:Drive = "$($volume.DriveLetter):" }
        $script:Drive -and (Test-Path "$script:Drive\")
    } 15000
    if (-not $script:Drive) { Write-Host 'FAIL  the disc image did not open as a drive'; exit 1 }
    $drive = $script:Drive
    $exe = "$drive\Heracles\Heracles.exe"

    # --- 1. The disc ---
    Test-Ui "disc: it is called 'Heracles $Version'" { (Get-DiskImage -ImagePath $Iso | Get-Volume).FileSystemLabel -ceq "Heracles $Version" }
    $setupName = "Heracles-$Version-win-$Arch-setup.exe"
    Test-Ui 'disc: at its top are the Heracles folder, the installer and README.txt, and nothing else' {
        (@(Get-ChildItem "$drive\" -Force | ForEach-Object { $_.Name } | Sort-Object) -join ', ') -ceq (@('Heracles', $setupName, 'README.txt' | Sort-Object) -join ', ')
    }
    Test-Ui 'disc: the README begins with this version and processor' {
        (Get-Content "$drive\README.txt" -TotalCount 1) -ceq "Heracles $Version for Windows ($Arch)"
    }
    Test-Ui 'disc: the README names the installer that is on the disc' {
        [System.IO.File]::ReadAllText("$drive\README.txt").Contains($setupName)
    }
    Test-Ui 'disc: the installer on it is the one beside the image' {
        $beside = Join-Path $Root "build\release\$setupName"
        (Test-Path $beside) -and (Get-FileHash -LiteralPath "$drive\$setupName" -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $beside -Algorithm SHA256).Hash
    }
    Test-Ui 'disc: the README is plain text that Notepad shows as lines' {
        $bytes = [System.IO.File]::ReadAllBytes("$drive\README.txt")
        $text = [System.Text.Encoding]::ASCII.GetString($bytes)
        @($bytes | Where-Object { $_ -gt 126 }).Count -eq 0 -and $text.Contains("`r`n") -and -not ($text -replace "`r`n", '').Contains("`n")
    }
    Test-Ui "disc: Heracles.exe is a program for $Arch" { (Get-PeArchitecture $exe) -eq $Arch }
    Test-Ui 'disc: the core is beside it, for the same processor' { (Get-PeArchitecture "$drive\Heracles\heracles_core.dll") -eq $Arch }
    # Without these two a copy never updates itself, and nothing would say so.
    Test-Ui 'disc: the app carries the address of its update feed, and the update key' {
        $carried = [System.IO.File]::ReadAllText("$drive\Heracles\Heracles.update.json") | ConvertFrom-Json
        $config = [System.IO.File]::ReadAllText((Join-Path $Root 'tools\update-config.json')) | ConvertFrom-Json
        $config.key -and $carried.key -ceq $config.key -and $carried.feed -ceq $config.feed
    }
    Test-Ui 'disc: the picture of its icon is beside it, for its reminders' { (Get-Item "$drive\Heracles\Heracles.png").Length -gt 1000 }

    # --- 2. The app, started from the disc ---
    & (Join-Path $E2e 'launch.ps1') -Exe $exe -Wait 0 --e2e-seed 6>&1 | Out-Null
    $appeared = Wait-Ui { Find-Ui -Id 'Sidebar' } 30000
    Test-Ui 'app: it starts from the disc and shows its window' { $appeared }
    Test-Ui 'app: the one running is the one on the disc' { @(Get-ProcessesUnder $drive).Count -eq 1 }
    Test-Ui 'log: it names this version' { (Get-Content $LogFile -ErrorAction SilentlyContinue) -match ("Heracles " + [regex]::Escape($Version) + " \(build \d+\)") }
    Test-Ui 'log: the library was made and opened' {
        $log = Get-Content $LogFile -ErrorAction SilentlyContinue
        ($log -match 'seed: created \d+ notes') -and ($log -match 'library: opened, \d+ notes')
    }
    # A copy on a library of a test's own must not look for the published
    # app, even with the real feed and key beside it.
    Test-Ui 'log: on a test library it does not look for updates' { (Get-Content $LogFile -ErrorAction SilentlyContinue) -match 'updates: off: .*HERACLES_DATA_DIR' }
    $trip = (Get-UiId -Name 'Trip' -Prefix 'note:' -Within 'Sidebar') -replace '^note:', ''
    Test-Ui 'app: the notes are in the sidebar' { $trip }
    if ($trip) { Select-Ui "note:$trip" }
    Test-Ui 'app: a note opens in a tab' { $trip -and @(Get-UiIds -Prefix 'Tab:' | Where-Object { $_ -like "*:$trip" }).Count -ge 1 }
    Test-Ui 'app: the note shows its words' { @(Get-UiIds -Prefix 'Block:').Count -ge 1 }

    Invoke-UiMenu 'File' 'Exit'
    $gone = Wait-Ui { @(Get-ProcessesUnder $drive).Count -eq 0 } 10000
    Test-Ui 'app: File > Exit ends it' { $gone }
    Test-Ui 'log: no unhandled exception, and it ends with exit' {
        $log = @(Get-Content $LogFile -ErrorAction SilentlyContinue)
        -not ($log -match 'unhandled exception') -and $log.Count -gt 0 -and ($log[-1] -match 'exit\s*$')
    }
}
finally {
    # Nothing may still be running from the disc when it goes.
    if ($script:Drive) { Stop-ProcessesUnder $script:Drive }
    Dismount-DiskImage -ImagePath $Iso | Out-Null
}

Test-Ui 'disc: it is ejected again' { -not (Get-DiskImage -ImagePath $Iso).Attached }

Show-UiResults
exit $script:UiFailed.Count
