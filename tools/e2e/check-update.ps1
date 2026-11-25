<#
.SYNOPSIS
    Rehearses an over-the-air update from start to finish with nothing real
    in it, and the updates that must be refused.

.DESCRIPTION
        tools\e2e\check-update.ps1 [-Arch arm64|x64] [-Port 8498] [-Look] [-Theme light|dark] [-Keep]

    Needs the package of this version for that processor
    (tools\build.ps1 core package). From it the rehearsal makes copies that
    say they are versions 9.9.1 and 9.9.2 (the app's own code compiled again
    under those numbers, in the packaged folder), with a signing key made
    for the run in place of the real one. A feed and an archive of 9.9.2 are
    served from this machine by tools\e2e\serve-folder.ps1, and the copies
    run on a library in the work folder. No release, no GitHub, not the real
    update key, not the real library, not what the real app remembers about
    updates.

    It checks:
      0. A copy with a library of its own and no feed named does not look
         for updates at all, so a trial copy is never offered the published
         app. Its two menu items for updates are greyed out.
      1. The old copy finds the new one in the feed and offers it with its
         notes. "Install Update" fetches and checks it, "Install and
         Restart" quits the app, and what comes back is the new version: the
         folder on disk is file for file the new one, and what the update
         left for later is cleared away.
      2. That copy, now the new version, finds nothing newer in the same
         feed. Check for Updates can be chosen and says so, and Check for
         Updates Automatically is ticked and can be unticked.
      3. With the archive altered after the feed was signed, nothing is
         installed, and the app says why.
      4. The same with the feed altered after it was signed: nothing in it
         is believed, and nothing is downloaded on its word.

    Windows of the copies come to the front while this runs. Everything is
    done through UI Automation patterns: no mouse, no keyboard, no
    clipboard. A run that passes removes its work folder (-Keep keeps it; a
    run that fails always does). Exit code: the number of checks that
    failed.

    -Look also saves pictures of the update window as it offers, as it is
    ready to install and as it refuses, to build\e2e\shots\update-*.png:
    colour is in no automation tree. -Theme shows the copies in light or in
    dark, whatever Windows is set to.

    The made-up build numbers are 9901 and 9902. A build number has to fit
    in a part of a Windows file version, which holds no more than 65535.
#>
[CmdletBinding()]
param(
    [ValidateSet('', 'arm64', 'x64')]
    [string]$Arch = '',
    [int]$Port = 8498,
    [switch]$Look,
    [ValidateSet('', 'light', 'dark')]
    [string]$Theme = '',
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$E2e = $PSScriptRoot
$Root = Split-Path -Parent (Split-Path -Parent $E2e)
. (Join-Path $E2e 'ui-check.ps1')
. (Join-Path $Root 'tools\update-signing.ps1')

if (-not $Arch) {
    # The machine's architecture, as in build.ps1 (an emulated shell reports its own).
    $machine = [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE', 'Machine')
    if (-not $machine) { $machine = $env:PROCESSOR_ARCHITECTURE }
    $Arch = if ($machine -eq 'ARM64') { 'arm64' } else { 'x64' }
}
$Version = (Get-Content (Join-Path $Root 'VERSION') -Raw).Trim()
$Package = Join-Path $Root "build\release\Heracles-$Version-win-$Arch"
if (-not (Test-Path (Join-Path $Package 'Heracles.exe'))) { throw "no package at $Package; run: tools\build.ps1 core package -Arch $Arch" }

$Work = Join-Path $Root 'build\update-rehearsal'
$Web = Join-Path $Work 'web'
$Fixture = Join-Path $Work 'fixture'
$Data = Join-Path $Fixture 'data'
$LogFile = Join-Path $Fixture 'heracles.log'
$ServerLog = Join-Path $Work 'server.log'
$Base = "http://127.0.0.1:$Port"
$OldVersion = '9.9.1'; $OldBuild = 9901
$NewVersion = '9.9.2'; $NewBuild = 9902
$Archive = "Heracles-$NewVersion-win-$Arch.zip"

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
function Test-Log([string]$pattern) { [bool](@(Get-Log) -match $pattern) }

# Which line of the log is the first to match, counted from 0, or -1.
function Get-LogLine([string]$pattern) {
    $lines = Get-Log
    for ($index = 0; $index -lt $lines.Count; $index++) { if ($lines[$index] -match $pattern) { return $index } }
    return -1
}
function Get-ServerLog { @(Get-Content $ServerLog -ErrorAction SilentlyContinue) }

# Every file under a folder, with its SHA-256: "relative\path = hash".
function Get-FolderListing([string]$folder) {
    $folder = (Resolve-Path $folder).Path.TrimEnd('\')
    @(Get-ChildItem $folder -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
            '{0} = {1}' -f $_.FullName.Substring($folder.Length + 1), (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        })
}

# The version a copy on disk is, from the program's own code.
function Get-CopyVersion([string]$copy) {
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $copy 'Heracles.dll'))
    return "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart) ($($info.FilePrivatePart))"
}

# Other builds must not be about: the checks look at every window of the app.
Stop-ProcessesUnder (Join-Path $Root 'build')
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $Data, $Web, (Join-Path $Work 'logs') | Out-Null

$server = $null
$failed = 1
try {
    # --- a key for this run only -------------------------------------------------
    Step 'Making a signing key for the rehearsal'
    $key = New-UpdateKey
    $config = Get-UpdateConfig $Root
    if ($key.Public -eq $config.key) { throw 'the rehearsal''s key is the real update key, which cannot be' }
    Write-Host "public key: $($key.Public)"

    # --- the app's code, compiled as two versions --------------------------------
    # Only the program's own assembly differs between versions here; the rest
    # of each copy is the packaged folder as it is handed out.
    function Build-Relabelled([string]$version, [int]$build, [string]$into) {
        & dotnet build (Join-Path $Root 'app\Heracles\Heracles.csproj') -c Release -r "win-$Arch" --no-incremental `
            "-p:HeraclesVersion=$version" "-p:HeraclesBuild=$build" -p:DebugType=none -p:DebugSymbols=false -o $into --nologo -v quiet | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $into 'Heracles.dll'))) { throw "could not compile the app as $version" }
    }

    # A copy of the package that says it is a version, trusts the rehearsal
    # key, and would read its feed from nowhere unless told on the command line.
    function New-Copy([string]$name, [string]$assembly) {
        $copy = Join-Path $Work "$name\Heracles"
        New-Item -ItemType Directory -Force $copy | Out-Null
        & robocopy $Package $copy /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "could not copy the package to $copy" }
        Copy-Item (Join-Path $assembly 'Heracles.dll') (Join-Path $copy 'Heracles.dll') -Force
        $settings = [ordered]@{ feed = 'https://example.invalid/never-asked/update-feed.json'; key = $key.Public }
        [System.IO.File]::WriteAllText((Join-Path $copy 'Heracles.update.json'), (($settings | ConvertTo-Json) + "`n"), [System.Text.Encoding]::ASCII)
        return $copy
    }

    Step "Compiling the app as $OldVersion ($OldBuild) and as $NewVersion ($NewBuild)"
    Build-Relabelled $OldVersion $OldBuild (Join-Path $Work 'build-old')
    Build-Relabelled $NewVersion $NewBuild (Join-Path $Work 'build-new')

    Step "Making $NewVersion, its archive and three feeds"
    $new = New-Copy 'new' (Join-Path $Work 'build-new')
    $newListing = Get-FolderListing $new
    $goodArchive = Join-Path $Web "feed\$Archive"
    New-Item -ItemType Directory -Force (Join-Path $Web 'feed'), (Join-Path $Web 'bad-archive'), (Join-Path $Web 'bad-feed') | Out-Null
    New-UpdateArchive -Folder $new -Path $goodArchive

    function New-RehearsalFeed([string]$folder) {
        $zip = Join-Path $Web "$folder\$Archive"
        $manifest = [ordered]@{
            version  = $NewVersion
            build    = $NewBuild
            notes    = "- A rehearsal update. **Nothing** in it is real.`n- A second point, with ``code`` in it."
            archives = [ordered]@{
                $Arch = [ordered]@{
                    url    = "$Base/$folder/$Archive"
                    # The length and the checksum of the archive as it was made:
                    # what is served may have been altered since.
                    size   = [long](Get-Item $goodArchive).Length
                    sha256 = (Get-FileHash $goodArchive -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
        }
        New-UpdateFeed -Private $key.Private -Manifest $manifest -Path (Join-Path $Web "$folder\update-feed.json")
        if (-not (Test-Path $zip)) { Copy-Item $goodArchive $zip }
    }
    New-RehearsalFeed 'feed'

    # The archive altered after the feed was made: four bytes in its middle,
    # the same length, the same name.
    New-RehearsalFeed 'bad-archive'
    $bad = Join-Path $Web "bad-archive\$Archive"
    $stream = [System.IO.File]::Open($bad, 'Open', 'ReadWrite')
    try {
        $stream.Position = [long]($stream.Length / 2)
        $stream.Write([byte[]](0x00, 0x11, 0x22, 0x33), 0, 4)
    }
    finally { $stream.Dispose() }
    if ((Get-FileHash $bad).Hash -eq (Get-FileHash $goodArchive).Hash) { throw 'the archive did not change' }
    if ((Get-Item $bad).Length -ne (Get-Item $goodArchive).Length) { throw 'the altered archive is not the length it was' }

    # The feed altered after it was signed: the notes say something else.
    New-RehearsalFeed 'bad-feed'
    $badFeed = Join-Path $Web 'bad-feed\update-feed.json'
    $envelope = [System.IO.File]::ReadAllText($badFeed) | ConvertFrom-Json
    $said = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.manifest))
    $changed = $said.Replace('A rehearsal update.', 'An update from somewhere else.')
    if ($changed -eq $said) { throw 'the feed did not change' }
    $envelope.manifest = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($changed))
    [System.IO.File]::WriteAllText($badFeed, (($envelope | ConvertTo-Json -Compress) + "`n"), [System.Text.Encoding]::ASCII)

    # --- the feeds are read over HTTP from the work folder, on this machine only --
    $server = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $E2e 'serve-folder.ps1')`"",
        '-Folder', "`"$Web`"", '-Log', "`"$ServerLog`"", '-Port', "$Port")
    if (-not (Wait-Ui { (Get-ServerLog) -match 'listening' } 15000)) { throw "the local web server did not start on port $Port (in use? give -Port)" }

    # A copy started on the rehearsal's library. With a feed named it looks
    # at that feed, a moment after it starts. Each start is on a library that
    # last looked two days ago, as a copy in everyday use has.
    function Start-Copy([string]$copy, [string]$feed, [string]$name) {
        if (Test-Path $LogFile) { Move-Item $LogFile (Join-Path $Work "logs\before-$name.log") -Force }
        $updates = Join-Path $Data 'updates'
        New-Item -ItemType Directory -Force $updates | Out-Null
        $preferences = Join-Path $updates 'preferences.json'
        $kept = if (Test-Path $preferences) { [System.IO.File]::ReadAllText($preferences) | ConvertFrom-Json } else { [pscustomobject]@{ automatic = $true } }
        $automatic = if ($kept.PSObject.Properties['automatic']) { [bool]$kept.automatic } else { $true }
        $before = [ordered]@{ automatic = $automatic; lastCheck = (Get-Date).ToUniversalTime().AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ') }
        [System.IO.File]::WriteAllText($preferences, ($before | ConvertTo-Json), [System.Text.Encoding]::ASCII)
        $arguments = @()
        if ($feed) { $arguments = @('--updater-feed', "$Base/$feed/update-feed.json") }
        if ($Theme) { $arguments += "--theme-$Theme" }
        & (Join-Path $E2e 'launch.ps1') -Fixture $Fixture -Exe (Join-Path $copy 'Heracles.exe') -Wait 0 @arguments 6>&1 | Out-Null
    }

    # A picture of each of the app's windows as they are now, when asked for.
    function Save-Look([string]$name) {
        if (-not $Look) { return }
        Start-Sleep -Milliseconds 600
        $suffix = if ($Theme) { "-$Theme" } else { '' }
        & (Join-Path $E2e 'capture.ps1') -Prefix "update-$name$suffix" 6>&1 | Out-Null
    }

    # End the app the way its user does, and wait for it to be gone.
    function Exit-Copy([string]$copy) {
        if (@(Get-ProcessesUnder $copy).Count -gt 0) {
            try { Invoke-UiMenu 'File' 'Exit' } catch { }
            $null = Wait-Ui { @(Get-ProcessesUnder $copy).Count -eq 0 } 15000
        }
        Stop-ProcessesUnder $Work
    }

    # How the two menu items for updates stand, read with the Help menu open.
    function Get-UpdateMenu {
        & $script:UiScript expand -Name 'Help' 6>&1 | Out-Null
        Start-Sleep -Milliseconds 300
        $check = Find-Ui -Id 'UpdateCheck'
        $automatic = Find-Ui -Id 'UpdateAutomatic'
        try { & $script:UiScript collapse -Name 'Help' 6>&1 | Out-Null } catch { }
        Start-Sleep -Milliseconds 200
        return @{
            Check          = if (-not $check) { 'missing' } elseif ($check -match ' DISABLED') { 'off' } else { 'on' }
            Automatic      = if (-not $automatic) { 'missing' } elseif ($automatic -match ' DISABLED') { 'off' } else { 'on' }
            AutomaticState = if ($automatic -and $automatic -match " status='ticked'") { 'ticked' } else { 'not ticked' }
        }
    }

    $old = New-Copy 'old' (Join-Path $Work 'build-old')
    $oldListing = Get-FolderListing $old

    # --- 0. a trial run does not look --------------------------------------------
    Step '0. A copy with a library of its own and no feed named'
    Start-Copy $old '' 'trial'
    $null = Wait-Ui { Find-Ui -Id 'Sidebar' } 30000
    Test-Ui 'trial: the copy starts, and says it is the old version' { Test-Log "Heracles $([regex]::Escape($OldVersion)) \(build $OldBuild\)" }
    Test-Ui 'trial: it says that it does not look, and why' { Test-Log 'updates: off: .*HERACLES_DATA_DIR' }
    Start-Sleep -Seconds 5
    Test-Ui 'trial: nothing was looked for, and nothing asked of the server' {
        -not (Test-Log 'updates: (found|downloaded|nothing newer|stopped|on,)') -and @(Get-ServerLog | Where-Object { $_ -match '^GET ' }).Count -eq 0
    }
    $menu = Get-UpdateMenu
    Test-Ui 'trial: Check for Updates and Check for Updates Automatically are greyed out' {
        $menu.Check -eq 'off' -and $menu.Automatic -eq 'off' -and $menu.AutomaticState -eq 'not ticked'
    }
    Exit-Copy $old

    # --- 1. the update arrives and is installed ----------------------------------
    Step "1. $OldVersion looks at the feed"
    Start-Copy $old 'feed' 'update'
    $offered = Wait-Ui { Find-Ui -Id 'UpdateWindow' } 40000
    Test-Ui 'update: the new version is offered' { $offered }
    Test-Ui 'update: the log says what was found' { Test-Log "updates: found $([regex]::Escape($NewVersion)) \($NewBuild\)" }
    Test-Ui 'update: the window names both versions' { (Get-UiName 'UpdateVersions') -eq "Heracles $NewVersion is available. You have $OldVersion." }
    Test-Ui 'update: it shows the notes, as words' { (Get-UiName 'UpdateNotes') -eq 'A rehearsal update. Nothing in it is real. A second point, with code in it.' }
    Test-Ui 'update: nothing has been downloaded before it was agreed to' { @(Get-ServerLog | Where-Object { $_ -match "GET /feed/$([regex]::Escape($Archive))" }).Count -eq 0 }
    Save-Look 'offer'

    if ($offered) { Invoke-Ui -Id 'UpdateInstall' }
    $verified = Wait-Ui { Test-Log "updates: $([regex]::Escape($NewVersion)) is verified and will be installed when the app quits" } 180000
    Test-Ui 'update: Install Update fetches it and checks it' { $verified }
    Test-Ui 'update: the window then asks when to restart' { Find-UiShown -Id 'UpdateRestart' }
    Test-Ui 'update: until then the copy on disk is untouched' { (Get-CopyVersion $old) -eq "$OldVersion ($OldBuild)" }
    Save-Look 'ready'

    $oldProcess = @(Get-ProcessesUnder $old) | Select-Object -First 1
    if ($verified) { Invoke-Ui -Id 'UpdateRestart' }
    $installed = Wait-Ui { Test-Log "updates: installed $([regex]::Escape($NewVersion))" } 120000
    Test-Ui 'update: Install and Restart quits the app and the update is installed' { $installed }
    $back = Wait-Ui { (Test-Log "Heracles $([regex]::Escape($NewVersion)) \(build $NewBuild\)") -and (Find-Ui -Id 'Sidebar') } 60000
    Test-Ui 'update: the app comes back, as the new version' { $back }
    Test-Ui 'update: what runs now is the copy where the old one was, in another process' {
        $now = @(Get-ProcessesUnder $old)
        $now.Count -eq 1 -and $oldProcess -and $now[0].Id -ne $oldProcess.Id
    }
    Test-Ui 'update: the old app quit as it does from its menu, before anything was replaced' {
        $exit = Get-LogLine ' exit$'
        $swap = Get-LogLine 'updates: installed'
        $exit -ge 0 -and $swap -gt $exit
    }
    Test-Ui 'update: the copy on disk says it is the new version' { (Get-CopyVersion $old) -eq "$NewVersion ($NewBuild)" }
    Test-Ui 'update: it is file for file the new version' {
        $now = Get-FolderListing $old
        -not (Compare-Object $now $newListing)
    }
    $cleared = Wait-Ui { -not (Test-Path "$old.update-old") -and -not (Test-Path "$old.update-new") -and -not (Test-Path (Join-Path $Data 'updates\staged')) } 30000
    Test-Ui 'update: the old copy kept aside, and the folder the update was unpacked into, are cleared away' { $cleared }
    Test-Ui 'update: no unhandled exception on the way' { -not (Test-Log 'unhandled exception') }
    Exit-Copy $old

    # --- 2. the updated copy is content -------------------------------------------
    Step '2. The updated copy looks at the same feed'
    Start-Copy $old 'feed' 'updated'
    $null = Wait-Ui { Find-Ui -Id 'Sidebar' } 30000
    $content = Wait-Ui { Test-Log 'updates: nothing newer in the feed' } 30000
    Test-Ui "updated: nothing is offered to a copy that is already $NewVersion" { $content -and -not (Test-Log 'updates: found') -and -not (Find-Ui -Id 'UpdateWindow') }
    $menu = Get-UpdateMenu
    Test-Ui 'updated: Check for Updates can be chosen, and Check for Updates Automatically is ticked' {
        $menu.Check -eq 'on' -and $menu.Automatic -eq 'on' -and $menu.AutomaticState -eq 'ticked'
    }
    & $script:UiScript expand -Name 'Help' 6>&1 | Out-Null
    Start-Sleep -Milliseconds 300
    try { Invoke-Ui -Id 'UpdateCheck' } catch { }
    Test-Ui 'updated: asked from the menu, it says that it is up to date' { (Get-UiName 'MessageText') -eq "Heracles $NewVersion is the newest version there is." }
    Save-Look 'none'
    try { Invoke-Ui -Id 'MessageOk' } catch { }
    & $script:UiScript expand -Name 'Help' 6>&1 | Out-Null
    Start-Sleep -Milliseconds 300
    try { Invoke-Ui -Id 'UpdateAutomatic' } catch { }
    Start-Sleep -Milliseconds 300
    $menu = Get-UpdateMenu
    Test-Ui 'updated: unticked, the daily look is off, and that is kept' {
        $kept = [System.IO.File]::ReadAllText((Join-Path $Data 'updates\preferences.json')) | ConvertFrom-Json
        $menu.AutomaticState -eq 'not ticked' -and $menu.Automatic -eq 'on' -and $kept.automatic -eq $false
    }
    Exit-Copy $old
    # The next copy looks by itself again.
    Remove-Item (Join-Path $Data 'updates\preferences.json') -Force -ErrorAction SilentlyContinue

    # --- 3. an archive that was altered after signing is refused -----------------
    # The feed is intact, so the update is found and offered; the archive is
    # what stops it.
    Step "3. $OldVersion looks at a feed whose archive was altered after signing"
    $second = New-Copy 'old-2' (Join-Path $Work 'build-old')
    Start-Copy $second 'bad-archive' 'bad-archive'
    $offered = Wait-Ui { Find-Ui -Id 'UpdateWindow' } 40000
    Test-Ui 'altered archive: the update is found and offered, since the feed is as it was signed' { $offered -and (Test-Log "updates: found $([regex]::Escape($NewVersion))") }
    if ($offered) { Invoke-Ui -Id 'UpdateInstall' }
    $refused = Wait-Ui { Test-Log 'updates: stopped: The update is improperly signed' } 180000
    Test-Ui 'altered archive: it is fetched and refused, as improperly signed' { $refused }
    Test-Ui 'altered archive: the window says so, and offers nothing to install' {
        (Get-UiName 'UpdateStatus') -match 'The update is improperly signed' -and -not (Find-UiShown -Id 'UpdateRestart') -and -not (Find-UiShown -Id 'UpdateInstall')
    }
    Save-Look 'refused'
    try { Invoke-Ui -Id 'UpdateClose' } catch { }
    Exit-Copy $second
    Start-Sleep -Seconds 5   # as long as an installer would have needed
    Test-Ui 'altered archive: nothing was installed, and nothing is left of it' {
        (Get-CopyVersion $second) -eq "$OldVersion ($OldBuild)" -and -not (Compare-Object (Get-FolderListing $second) $oldListing) `
            -and -not (Test-Path "$second.update-old") -and -not (Test-Path "$second.update-new") `
            -and -not (Test-Path (Join-Path $Data 'updates\staged')) -and -not (Test-Path (Join-Path $Data 'updates\download.part')) `
            -and -not (Test-Log 'handed to its installer|updates: install')
    }

    # --- 4. a feed that was altered after signing is refused ---------------------
    # Nothing in such a feed is believed: no update is found, and nothing is
    # downloaded.
    Step "4. $OldVersion looks at a feed that was altered after signing"
    Start-Copy $second 'bad-feed' 'bad-feed'
    $null = Wait-Ui { Find-Ui -Id 'Sidebar' } 30000
    $refused = Wait-Ui { Test-Log 'updates: stopped: The update feed is improperly signed' } 40000
    Test-Ui 'altered feed: it is refused, as improperly signed' { $refused }
    Start-Sleep -Seconds 3
    Test-Ui 'altered feed: nothing is found, offered or downloaded on its word' {
        -not (Test-Log 'updates: (found|downloaded)') -and -not (Find-Ui -Id 'UpdateWindow') `
            -and @(Get-ServerLog | Where-Object { $_ -match "GET /bad-feed/$([regex]::Escape($Archive))" }).Count -eq 0
    }
    Exit-Copy $second
    Test-Ui 'altered feed: the copy is as it was' { (Get-CopyVersion $second) -eq "$OldVersion ($OldBuild)" -and -not (Compare-Object (Get-FolderListing $second) $oldListing) }

    Show-UiResults
    $failed = $script:UiFailed.Count
}
finally {
    Stop-ProcessesUnder $Work
    if ($server -and -not $server.HasExited) { $server | Stop-Process -Force }
    if ($failed -eq 0 -and -not $Keep) { Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue }
    else { Write-Host "left for a look: $Work" }
}
exit $failed
