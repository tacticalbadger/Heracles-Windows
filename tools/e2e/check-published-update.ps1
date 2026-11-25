<#
.SYNOPSIS
    Takes the update that is published: an old-looking copy, with the real
    update key in it, reads the feed where installed copies read it and
    replaces itself with what that feed names.

.DESCRIPTION
        tools\e2e\check-published-update.ps1 [-Arch arm64|x64] [-Keep]

    For after a release. tools\e2e\check-update.ps1 rehearses an update with
    nothing real in it; this is the other half, with everything real but the
    old copy's version. It needs the package of the version that was just
    published, for that processor, still in build\release.

    The old copy is that package with the app's own code compiled again as
    version 0.0.9, build 1. Nothing else in it is changed: it carries the
    real feed address and the real update key, as every installed copy
    does. Started on a library of its own, it is told to read the published
    feed (a copy on a test library reads no feed unless one is named, and
    the one named here is the real one). It then has to do what a copy on
    somebody's PC will do: believe the feed because the update key signed
    it, fetch the archive from GitHub, take it because it is the one the
    feed describes, quit, and come back as the published version, file for
    file what was packaged. Asked again, it is offered nothing.

    It talks to GitHub, and downloads the archive (some 60 MB). What it
    sends is what any copy sends: a request for two files, with the app's
    version and processor.

    Windows of the copy come to the front while this runs. Everything is
    done through UI Automation patterns: no mouse, no keyboard, no
    clipboard. Exit code: the number of checks that failed.
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
if (-not (Test-Path (Join-Path $Package 'Heracles.exe'))) { throw "no package at ${Package}. This needs the package that was published" }

$Work = Join-Path $Root 'build\update-published'
$Fixture = Join-Path $Work 'fixture'
$Data = Join-Path $Fixture 'data'
$LogFile = Join-Path $Fixture 'heracles.log'
$OldVersion = '0.0.9'; $OldBuild = 1

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

# --- what is published -----------------------------------------------------------
$config = Get-UpdateConfig $Root
$settings = [System.IO.File]::ReadAllText((Join-Path $Package 'Heracles.update.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
if ($settings.key -ne $config.key -or $settings.feed -ne $config.feed) { throw 'the package does not carry the feed address and the key that tools\update-config.json has' }
Step "Reading $($config.feed)"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$fetched = [System.IO.Path]::GetTempFileName()
try {
    Invoke-WebRequest -Uri $config.feed -OutFile $fetched -UseBasicParsing -TimeoutSec 60 -Headers @{ 'Cache-Control' = 'no-cache' } | Out-Null
    $published = Read-UpdateFeed -Path $fetched -Public $config.key
}
finally { Remove-Item -LiteralPath $fetched -Force -ErrorAction SilentlyContinue }
if ($published.version -ne $Version) { throw "what is published is $($published.version), not ${Version}. There is nothing here to check it against" }
$NewBuild = [int]$published.build
if (-not $published.archives.PSObject.Properties[$Arch]) { throw "the published feed names no archive for $Arch" }
Write-Host "published: $Version, build $NewBuild, signed with the update key"

# Other builds must not be about: the checks look at every window of the app.
Stop-ProcessesUnder (Join-Path $Root 'build')
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $Data | Out-Null

$failed = 1
try {
    # --- the old copy: the package, saying it is an older version -----------------
    Step "Compiling the app as $OldVersion ($OldBuild)"
    $assembly = Join-Path $Work 'build-old'
    & dotnet build (Join-Path $Root 'app\Heracles\Heracles.csproj') -c Release -r "win-$Arch" --no-incremental `
        "-p:HeraclesVersion=$OldVersion" "-p:HeraclesBuild=$OldBuild" -p:DebugType=none -p:DebugSymbols=false -o $assembly --nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $assembly 'Heracles.dll'))) { throw "could not compile the app as $OldVersion" }
    $old = Join-Path $Work 'old\Heracles'
    New-Item -ItemType Directory -Force $old | Out-Null
    & robocopy $Package $old /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "could not copy the package to $old" }
    Copy-Item (Join-Path $assembly 'Heracles.dll') (Join-Path $old 'Heracles.dll') -Force
    $wanted = Get-FolderListing $Package

    function Start-Copy([string]$name) {
        if (Test-Path $LogFile) { Move-Item $LogFile (Join-Path $Work "before-$name.log") -Force }
        & (Join-Path $E2e 'launch.ps1') -Fixture $Fixture -Exe (Join-Path $old 'Heracles.exe') -Wait 0 '--updater-feed' $config.feed 6>&1 | Out-Null
    }

    function Exit-Copy {
        if (@(Get-ProcessesUnder $old).Count -gt 0) {
            try { Invoke-UiMenu 'File' 'Exit' } catch { }
            $null = Wait-Ui { @(Get-ProcessesUnder $old).Count -eq 0 } 15000
        }
        Stop-ProcessesUnder $Work
    }

    # --- 1. it finds what is published, and takes it ------------------------------
    Step "1. $OldVersion, with the real update key, looks at the published feed"
    Test-Ui 'the copy is the old version, with the real key and feed address in it' {
        $carried = [System.IO.File]::ReadAllText((Join-Path $old 'Heracles.update.json')) | ConvertFrom-Json
        (Get-CopyVersion $old) -eq "$OldVersion ($OldBuild)" -and $carried.key -ceq $config.key -and $carried.feed -ceq $config.feed
    }
    Start-Copy 'update'
    $offered = Wait-Ui { Find-Ui -Id 'UpdateWindow' } 90000
    Test-Ui 'the published version is offered' { $offered -and (Test-Log "updates: found $([regex]::Escape($Version)) \($NewBuild\)") }
    Test-Ui 'the window names both versions' { (Get-UiName 'UpdateVersions') -eq "Heracles $Version is available. You have $OldVersion." }
    Test-Ui 'it shows the release notes' { "$(Get-UiName 'UpdateNotes')".Length -gt 40 }
    if ($offered) { Invoke-Ui -Id 'UpdateInstall' }
    $verified = Wait-Ui { Test-Log "updates: $([regex]::Escape($Version)) is verified and will be installed when the app quits" } 900000
    Test-Ui 'the archive is fetched from GitHub and is the one the feed describes' { $verified }
    if ($verified) { Invoke-Ui -Id 'UpdateRestart' }
    $installed = Wait-Ui { Test-Log "updates: installed $([regex]::Escape($Version))" } 120000
    Test-Ui 'Install and Restart quits the app and the update is installed' { $installed }
    $back = Wait-Ui { (Test-Log "Heracles $([regex]::Escape($Version)) \(build $NewBuild\)") -and (Find-Ui -Id 'Sidebar') } 60000
    Test-Ui 'the app comes back, as the published version' { $back }
    Test-Ui 'the copy on disk is file for file what was packaged and published' { -not (Compare-Object (Get-FolderListing $old) $wanted) }
    $cleared = Wait-Ui { -not (Test-Path "$old.update-old") -and -not (Test-Path "$old.update-new") -and -not (Test-Path (Join-Path $Data 'updates\staged')) } 30000
    Test-Ui 'what the update left for later is cleared away' { $cleared }
    Test-Ui 'no unhandled exception on the way' { -not (Test-Log 'unhandled exception') }
    Exit-Copy

    # --- 2. and is then content ----------------------------------------------------
    Step '2. The updated copy looks at the published feed'
    # As a copy in everyday use: it last looked two days ago.
    $before = [ordered]@{ automatic = $true; lastCheck = (Get-Date).ToUniversalTime().AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ') }
    [System.IO.File]::WriteAllText((Join-Path $Data 'updates\preferences.json'), ($before | ConvertTo-Json), [System.Text.Encoding]::ASCII)
    Start-Copy 'updated'
    $null = Wait-Ui { Find-Ui -Id 'Sidebar' } 30000
    $content = Wait-Ui { Test-Log 'updates: nothing newer in the feed' } 60000
    Test-Ui 'nothing is offered to a copy that is the published version' { $content -and -not (Test-Log 'updates: found') -and -not (Find-Ui -Id 'UpdateWindow') }
    Exit-Copy

    Show-UiResults
    $failed = $script:UiFailed.Count
}
finally {
    Stop-ProcessesUnder $Work
    if ($failed -eq 0 -and -not $Keep) { Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue }
    else { Write-Host "left for a look: $Work" }
}
exit $failed
