<#
.SYNOPSIS
    The packaged installer, run for real: installed, started, its folder
    replaced as an update replaces it, and removed again.

.DESCRIPTION
        tools\e2e\check-installer.ps1 [-Arch arm64|x64] [-Keep]

    Takes build\release\Heracles-<version>-win-<arch>-setup.exe as
    "tools\build.ps1 package" made it and

      1. has it refuse a folder that holds somebody else's files;
      2. installs it, silently, into build\e2e-install\Heracles, and compares
         every file with the packaged folder;
      3. reads what it told Windows: the entry under Installed apps, the
         Start menu shortcut, and where it keeps its uninstaller (not in the
         app's folder, which an update replaces whole);
      4. starts the installed copy on the fixture, and sees it put the
         version under Installed apps right;
      5. has the uninstaller refuse while the app runs;
      6. replaces the app's folder the way the app's updater does (a new
         folder beside it, two renames), starts the app again, and checks
         that Windows still knows it and can still remove it;
      7. uninstalls, and checks that the folder, the entry, the shortcut and
         the uninstaller are gone, and that a link scheme and a reminder
         identity that point at this copy went with them (planted for the
         check, and only when this account has none of its own: a run on a
         test library registers neither).

    The swap in step 6 is the script's, not the updater's: the updater's own
    is rehearsed by check-update.ps1, and an installed copy cannot be offered
    the version it already is.

    The installer is the one that ships, so for the length of the run this
    account has Heracles under Installed apps and in the Start menu. The
    check therefore refuses to start when Heracles is already installed
    here: it would be installing over, and then removing, a real copy.

    Needs tools\build.ps1 core package for the architecture. No mouse, no
    keys, no clipboard. Exit code: the number of checks that failed. -Keep
    leaves build\e2e-install.
#>
[CmdletBinding()]
param(
    [ValidateSet('', 'arm64', 'x64')]
    [string]$Arch = '',
    [switch]$Keep
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
$Name = "Heracles-$Version-win-$Arch"
$Release = Join-Path $Root 'build\release'
$Stage = Join-Path $Release $Name
$Setup = Join-Path $Release "$Name-setup.exe"
$Work = Join-Path $Root 'build\e2e-install'
$Target = Join-Path $Work 'Heracles'
$Exe = Join-Path $Target 'Heracles.exe'
# The installer's AppId (tools\installer\Heracles.iss).
$KeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E7666E6-6C97-4920-8107-E156E0BFCBB0}_is1'
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Heracles.lnk'
$Uninstall = Join-Path $env:LOCALAPPDATA 'Heracles\uninstall'
$Uninstaller = Join-Path $Uninstall 'unins000.exe'
$RealUpdates = Join-Path $env:LOCALAPPDATA 'Heracles\updates'
$SchemeKey = 'HKCU:\Software\Classes\heracles'
$IdentityKey = 'HKCU:\Software\Classes\AppUserModelId\com.heracles.notes.native'

if (-not (Test-Path $Setup) -or -not (Test-Path (Join-Path $Stage 'Heracles.exe'))) { throw "no installer for $Arch in build\release; run: tools\build.ps1 core package -Arch $Arch" }
if (Test-Path -LiteralPath $KeyPath) { throw 'Heracles is installed for this account. This check installs a copy and removes it again, and would do that to the real one: not run.' }
if (Test-Path -LiteralPath $Shortcut) { throw "there is a Heracles shortcut in the Start menu already ($Shortcut): not run." }
if (Test-Path -LiteralPath $Uninstall) { throw "$Uninstall is there already, which is where an installed copy keeps its uninstaller: not run." }
if ((Test-Path -LiteralPath (Join-Path $RealUpdates 'staged')) -or (Test-Path -LiteralPath (Join-Path $RealUpdates 'download.part'))) {
    throw "a copy of Heracles has an update waiting in $RealUpdates, which the uninstaller would remove: not run."
}
# What a real copy chose about updates is in that folder too, and must be as it was afterwards.
$Preferences = Join-Path $RealUpdates 'preferences.json'
$preferencesBefore = if (Test-Path -LiteralPath $Preferences) { (Get-FileHash -LiteralPath $Preferences -Algorithm SHA256).Hash } else { $null }

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

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

function Get-Log { @(Get-Content $LogFile -ErrorAction SilentlyContinue) }

function Invoke-Setup([string]$folder, [string]$logName) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$folder`"", "/LOG=`"$(Join-Path $Work $logName)`"")
    return (Start-Process -FilePath $Setup -ArgumentList $arguments -Wait -PassThru).ExitCode
}

function Invoke-Uninstall([string]$logName) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $Work $logName)`"")
    return (Start-Process -FilePath $Uninstaller -ArgumentList $arguments -Wait -PassThru).ExitCode
}

# The files of a folder, as relative path -> SHA-256.
function Get-Hashes([string]$folder) {
    $hashes = @{}
    foreach ($file in [System.IO.Directory]::GetFiles($folder, '*', 'AllDirectories')) {
        $hashes[$file.Substring($folder.Length + 1)] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    }
    return $hashes
}

# What differs between the installed folder and the package.
function Compare-WithPackage {
    $wanted = Get-Hashes $Stage
    $found = Get-Hashes $Target
    $different = @()
    foreach ($name in $wanted.Keys) {
        if (-not $found.ContainsKey($name)) { $different += "missing: $name" }
        elseif ($found[$name] -ne $wanted[$name]) { $different += "not as packaged: $name" }
    }
    foreach ($name in $found.Keys) { if (-not $wanted.ContainsKey($name)) { $different += "not from the package: $name" } }
    return $different
}

function Start-Installed {
    & (Join-Path $E2e 'launch.ps1') -Exe $Exe -Wait 0 --e2e-seed 6>&1 | Out-Null
    return (Wait-Ui { Find-Ui -Id 'Sidebar' } 30000)
}

# Through the app's own File > Exit. Asked again if the app is still there:
# on a busy machine the menu is not always open by the time its item is
# invoked (seen once, while a release was uploading).
function Exit-Installed {
    for ($try = 0; $try -lt 3; $try++) {
        try { Invoke-UiMenu 'File' 'Exit' } catch { }
        if (Wait-Ui { @(Get-ProcessesUnder $Target).Count -eq 0 } 8000) { return $true }
    }
    return $false
}

# A fixture cannot be reset under a running build, and two builds must not
# run on it at once.
Stop-ProcessesUnder (Join-Path $Root 'build')
& (Join-Path $E2e 'make-fixture.ps1') 6>&1 | Out-Null
if (Test-Path $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Force $Work | Out-Null

# Whether this account has a link scheme or a reminder identity of its own
# (a real copy of the app made them). Only where it has none is one planted.
$plantScheme = -not (Test-Path -LiteralPath $SchemeKey)
$plantIdentity = -not (Test-Path -LiteralPath $IdentityKey)

try {
    Step "1. a folder with somebody else's files in it is refused"
    $foreign = Join-Path $Work 'Somebody'
    New-Item -ItemType Directory -Force $foreign | Out-Null
    Set-Content -LiteralPath (Join-Path $foreign 'theirs.txt') -Value 'not the app''s' -Encoding ascii
    $code = Invoke-Setup $foreign 'refused.log'
    Test-Ui "refused: the installer stops (exit code 7, was $code)" { $code -eq 7 }
    Test-Ui 'refused: nothing was put into that folder' { @(Get-ChildItem -LiteralPath $foreign -Force).Count -eq 1 }
    Test-Ui 'refused: nothing was told to Windows' { -not (Test-Path -LiteralPath $KeyPath) }

    Step "2. install into $Target"
    $code = Invoke-Setup $Target 'install.log'
    Test-Ui "install: the installer finishes (exit code 0, was $code)" { $code -eq 0 }
    $different = @(Compare-WithPackage)
    Test-Ui "install: the folder is the package, file for file $(($different | Select-Object -First 3) -join '; ')" { $different.Count -eq 0 }

    Step '3. what Windows was told'
    $entry = Get-ItemProperty -LiteralPath $KeyPath -ErrorAction SilentlyContinue
    Test-Ui 'windows: there is an entry under Installed apps' { $null -ne $entry }
    if ($null -ne $entry) {
        Test-Ui 'windows: it is called Heracles' { $entry.DisplayName -eq 'Heracles' }
        Test-Ui "windows: it says version $Version" { $entry.DisplayVersion -eq $Version }
        Test-Ui 'windows: it names the folder' { $entry.InstallLocation.TrimEnd('\') -ieq $Target }
        Test-Ui 'windows: it shows the app''s icon' { $entry.DisplayIcon -ieq $Exe }
        Test-Ui 'windows: its uninstaller is outside the app''s folder' { $entry.UninstallString -match [regex]::Escape($Uninstaller) }
    }
    Test-Ui 'windows: the uninstaller is where the entry says' { (Test-Path $Uninstaller) -and (Test-Path (Join-Path $Uninstall 'unins000.dat')) }
    Test-Ui 'windows: the Start menu has a shortcut to the installed app' {
        (Test-Path -LiteralPath $Shortcut) -and (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut).TargetPath -ieq $Exe
    }

    Step '4. the installed copy starts, and says which version it is'
    # As if an older version had been installed: the app has to put it right.
    Set-ItemProperty -LiteralPath $KeyPath -Name DisplayVersion -Value '0.0.1'
    $appeared = Start-Installed
    Test-Ui 'app: it starts from where it was installed and shows its window' { $appeared }
    Test-Ui 'log: it names this version' { (Get-Log) -match ("Heracles " + [regex]::Escape($Version) + " \(build \d+\)") }
    Test-Ui "windows: Installed apps says $Version again" { (Get-ItemProperty -LiteralPath $KeyPath).DisplayVersion -eq $Version }
    Test-Ui 'log: and the app says it wrote that' { (Get-Log) -match 'installed copy: Windows'' list of installed apps now says' }

    Step '5. the uninstaller refuses while the app runs'
    $code = Invoke-Uninstall 'uninstall-refused.log'
    Test-Ui "running: the uninstaller stops (exit code $code)" { $code -ne 0 }
    Test-Ui 'running: the app is still installed' { (Test-Path $Exe) -and (Test-Path -LiteralPath $KeyPath) }
    $gone = Exit-Installed
    Test-Ui 'app: File > Exit ends it' { $gone }

    Step '6. the folder is replaced, as an update replaces it'
    $incoming = "$Target.update-new"
    $previous = "$Target.update-old"
    Copy-Item -LiteralPath $Stage -Destination $incoming -Recurse
    # Tried again for a while, as the updater does: something may hold the
    # folder for a moment after the app has gone (seen with the x64 copy,
    # which runs under emulation here: "access is denied" on the first try).
    $moved = Wait-Ui {
        try { Rename-Item -LiteralPath $Target -NewName (Split-Path -Leaf $previous) -ErrorAction Stop; $true } catch { $false }
    } 15000
    Test-Ui 'swap: the app''s folder can be moved aside once the app has quit' { $moved }
    Rename-Item -LiteralPath $incoming -NewName (Split-Path -Leaf $Target)
    Remove-Item -LiteralPath $previous -Recurse -Force
    Set-ItemProperty -LiteralPath $KeyPath -Name DisplayVersion -Value '0.0.1'
    Test-Ui 'swap: the uninstaller is still there' { (Test-Path $Uninstaller) -and (Test-Path (Join-Path $Uninstall 'unins000.dat')) }
    Test-Ui 'swap: the Start menu shortcut still leads to the app' { (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut).TargetPath -ieq $Exe -and (Test-Path $Exe) }
    $appeared = Start-Installed
    Test-Ui 'swap: the app starts from the replaced folder' { $appeared }
    Test-Ui "swap: Installed apps says $Version again" { (Get-ItemProperty -LiteralPath $KeyPath).DisplayVersion -eq $Version }
    $gone = Exit-Installed
    Test-Ui 'swap: File > Exit ends it' { $gone }

    Step '7. uninstall'
    # What the app would have written for itself on a real library.
    if ($plantScheme) {
        New-Item -Path "$SchemeKey\shell\open\command" -Force | Out-Null
        Set-ItemProperty -LiteralPath $SchemeKey -Name 'URL Protocol' -Value ''
        Set-Item -LiteralPath "$SchemeKey\shell\open\command" -Value "`"$Exe`" `"%1`""
    }
    if ($plantIdentity) {
        New-Item -Path $IdentityKey -Force | Out-Null
        Set-ItemProperty -LiteralPath $IdentityKey -Name DisplayName -Value 'Heracles'
        Set-ItemProperty -LiteralPath $IdentityKey -Name IconUri -Value (Join-Path $Target 'Heracles.png')
    }
    $schemeBefore = if (Test-Path -LiteralPath "$SchemeKey\shell\open\command") { (Get-Item -LiteralPath "$SchemeKey\shell\open\command").GetValue('') } else { $null }
    $code = Invoke-Uninstall 'uninstall.log'
    Test-Ui "uninstall: the uninstaller finishes (exit code 0, was $code)" { $code -eq 0 }
    # The uninstaller's last steps run in a copy of itself that outlives it by a moment.
    $null = Wait-Ui { -not (Test-Path -LiteralPath $KeyPath) -and -not (Test-Path $Uninstaller) -and -not (Test-Path $Target) } 60000
    Test-Ui 'uninstall: the app''s folder is gone' { -not (Test-Path $Target) }
    Test-Ui 'uninstall: the entry under Installed apps is gone' { -not (Test-Path -LiteralPath $KeyPath) }
    Test-Ui 'uninstall: the Start menu shortcut is gone' { -not (Test-Path -LiteralPath $Shortcut) }
    Test-Ui 'uninstall: the uninstaller is gone' { -not (Test-Path $Uninstall) }
    Test-Ui 'uninstall: what a copy on this PC chose about updates is as it was' {
        $now = if (Test-Path -LiteralPath $Preferences) { (Get-FileHash -LiteralPath $Preferences -Algorithm SHA256).Hash } else { $null }
        $now -eq $preferencesBefore
    }
    if ($plantScheme) { Test-Ui 'uninstall: the link scheme that pointed at this copy is gone' { -not (Test-Path -LiteralPath $SchemeKey) } }
    else {
        Write-Host 'note: this account has a heracles:// link scheme of its own; none was planted, and it must be as it was'
        Test-Ui 'uninstall: the account''s own link scheme is as it was' {
            (Test-Path -LiteralPath "$SchemeKey\shell\open\command") -and (Get-Item -LiteralPath "$SchemeKey\shell\open\command").GetValue('') -eq $schemeBefore
        }
    }
    if ($plantIdentity) { Test-Ui 'uninstall: the reminder identity that pointed at this copy is gone' { -not (Test-Path -LiteralPath $IdentityKey) } }
    else {
        Write-Host 'note: this account has a reminder identity of its own; none was planted, and it must still be there'
        Test-Ui 'uninstall: the account''s own reminder identity is still there' { Test-Path -LiteralPath $IdentityKey }
    }
}
finally {
    Stop-ProcessesUnder $Work
    # Whatever happened above, nothing of the test copy stays with Windows.
    if ((Test-Path -LiteralPath $KeyPath) -and (Test-Path $Uninstaller)) {
        Write-Host 'cleaning up: uninstalling the test copy'
        Invoke-Uninstall 'uninstall-cleanup.log' | Out-Null
        $null = Wait-Ui { -not (Test-Path -LiteralPath $KeyPath) } 60000
    }
    if (Test-Path -LiteralPath $KeyPath) {
        $about = (Get-ItemProperty -LiteralPath $KeyPath).InstallLocation
        if ($about -and $about.StartsWith($Work, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $KeyPath -Recurse -Force }
    }
    if (Test-Path -LiteralPath $Shortcut) {
        $points = (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut).TargetPath
        if ($points -and $points.StartsWith($Work, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $Shortcut -Force }
    }
    # Planted keys that are still there point into this run's folder: they go.
    if ($plantScheme -and (Test-Path -LiteralPath $SchemeKey)) { Remove-Item -LiteralPath $SchemeKey -Recurse -Force }
    if ($plantIdentity -and (Test-Path -LiteralPath $IdentityKey)) { Remove-Item -LiteralPath $IdentityKey -Recurse -Force }
}

Show-UiResults
if ($script:UiFailed.Count -eq 0 -and -not $Keep -and (Test-Path $Work)) { Remove-Item -LiteralPath $Work -Recurse -Force }
exit $script:UiFailed.Count
