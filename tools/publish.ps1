<#
.SYNOPSIS
    Publishes the packages built by "tools\build.ps1 package" as a GitHub
    Release on this repository.

.DESCRIPTION
        tools\publish.ps1 [-Replace] [-Prerelease] [-DryRun]

    Tag v<version> (from the VERSION file), with these attached from
    build\release, for both processors:

      Heracles-<version>-win-<arch>.iso   what people download and install
      Heracles-<version>-win-<arch>.zip   what installed copies download to
                                          update themselves
      update-feed.json                    what tells installed copies that
                                          there is a new version
      SHA256SUMS

    Before anything is sent: the checksums are verified, the packages must
    have been built from the current commit (their build number is the commit
    count), and that commit must be on origin/main. The notes are the
    standard install text below plus docs\release-notes\<version>.md ("In
    this version") when that file exists. Keep names, hostnames and account
    details out of release notes.

    And a release is refused in any state that would silently stop updates:
    without a package for each processor; without the feed; with a feed that
    is not signed by the key the packaged copies check against, or that
    describes other archives than the ones being sent, or another version or
    build; with a build number that is not higher than the one already
    published. Once it is out, the feed is read back from the address
    installed copies read it from, and compared with the one that was built.

    -Replace     re-upload the files and notes of a release that already exists
                 (not possible where releases are immutable)
    -Prerelease  mark the release as a pre-release
    -DryRun      run every check and show what would be published; send
                 nothing. A check that would stop a real run is reported and
                 the dry run goes on, so all of them are seen at once.

    The release is created as a draft, the files are attached, and only then
    is it published. With "immutable releases" switched on for a repository,
    GitHub accepts no file once a release is out, and a tag that has had
    such a release can never be used for another one: a release published
    without its files costs the version number. A draft left behind by a
    run that did not finish is found and carried on with.

    No GitHub CLI is needed: the release is created through the GitHub API
    with the credential Git already uses for this repository (Git Credential
    Manager). The token is used for these calls only and is never printed or
    stored.

    The packages are not code-signed. The notes tell people how to get past
    the Windows warning.
#>
[CmdletBinding()]
param(
    [switch]$Replace,
    [switch]$Prerelease,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Root = Split-Path -Parent $PSScriptRoot
$Version = (Get-Content (Join-Path $Root 'VERSION') -Raw).Trim()
$Tag = "v$Version"
$Out = Join-Path $Root 'build\release'
$SumsPath = Join-Path $Out 'SHA256SUMS'
$FeedPath = Join-Path $Out 'update-feed.json'
# Reading a feed and checking its signature, as the app does.
. (Join-Path $PSScriptRoot 'update-signing.ps1')

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# Something that stops a real run. In a dry run it is said and the run goes
# on, so that every such thing is seen in one go.
$script:Refusals = 0
function Stop-Publish([string]$why) {
    if (-not $DryRun) { throw $why }
    $script:Refusals++
    Write-Host "would refuse: $why" -ForegroundColor Yellow
}

function Invoke-Git {
    # Whether git failed is its exit code. Windows PowerShell makes an error
    # of anything a program writes to stderr, sent to $null or not, and with
    # errors set to stop that ends the script: a warning from git, or a
    # message from the server while a tag is pushed, would end a release
    # half made. The setting is this function's own and goes when it returns.
    $ErrorActionPreference = 'Continue'
    $output = & git -C $Root @args 2>$null
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed (exit code $LASTEXITCODE)" }
    return $output
}

# ----- The files -----

$packages = @(Get-ChildItem $Out -Filter "Heracles-$Version-win-*.iso" -ErrorAction SilentlyContinue | Sort-Object Name)
$archives = @(Get-ChildItem $Out -Filter "Heracles-$Version-win-*.zip" -ErrorAction SilentlyContinue | Sort-Object Name)
if ($packages.Count -eq 0 -or -not (Test-Path $SumsPath)) {
    throw "no packages for $Version in build\release; run: tools\build.ps1 core package (and again with -Arch for the other architecture)"
}

Step 'Checking the files'
$expected = @{}
foreach ($line in Get-Content $SumsPath) {
    if ($line -match '^([0-9a-f]{64})\s+(\S+)$') { $expected[$Matches[2]] = $Matches[1] }
}
$head = (Invoke-Git rev-parse HEAD | Select-Object -First 1).Trim()
$commitCount = [int](Invoke-Git rev-list --count HEAD | Select-Object -First 1)
foreach ($package in $packages) {
    $actual = (Get-FileHash $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $expected.ContainsKey($package.Name) -or $expected[$package.Name] -ne $actual) {
        throw "$($package.Name) does not match SHA256SUMS; run the package step again"
    }
    # The staged folder next to the image says which commit count it was built from.
    $assembly = Join-Path $Out ($package.BaseName + '\Heracles.dll')
    if (-not (Test-Path $assembly)) { throw "the staged folder for $($package.Name) is gone; run the package step again" }
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assembly)
    if ($fileVersion.FilePrivatePart -ne $commitCount) {
        Stop-Publish "$($package.Name) is build $($fileVersion.FilePrivatePart), but the current commit is number $commitCount. Package again from this commit."
    }
    Write-Host ("{0}  {1:N0} MB  build {2}" -f $package.Name, ($package.Length / 1MB), $fileVersion.FilePrivatePart)
}
foreach ($archive in $archives) {
    $actual = (Get-FileHash $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $expected.ContainsKey($archive.Name) -or $expected[$archive.Name] -ne $actual) {
        throw "$($archive.Name) does not match SHA256SUMS; run the package step again"
    }
    Write-Host ("{0}  {1:N0} MB" -f $archive.Name, ($archive.Length / 1MB))
}

# ----- Updates -----

# Every copy that is out looks for the newest release's feed and takes what
# it names. A release that is wrong here is one nobody is offered, or one
# every copy refuses, and nothing would say so afterwards.
Step 'Checking the update feed'
$config = Get-UpdateConfig $Root
$feedManifest = $null
foreach ($arch in 'arm64', 'x64') {
    $name = "Heracles-$Version-win-$arch"
    if (-not (Test-Path (Join-Path $Out "$name.iso")) -or -not (Test-Path (Join-Path $Out "$name.zip"))) {
        Stop-Publish "there is no package for $arch. Copies on that processor would never be offered this version. Run: tools\build.ps1 core package -Arch $arch"
    }
}
if (-not (Test-Path $FeedPath)) {
    Stop-Publish 'there is no update-feed.json. Without it no installed copy learns of this version. Run: tools\build.ps1 feed'
}
else {
    foreach ($archive in $archives) {
        $arch = $archive.BaseName -replace '^.*-win-', ''
        # The key the copies in this package will check a feed against.
        $settingsPath = Join-Path $Out ($archive.BaseName + '\Heracles.update.json')
        if (-not (Test-Path $settingsPath)) { throw "the staged folder for $($archive.Name) has no Heracles.update.json; run the package step again" }
        $settings = [System.IO.File]::ReadAllText($settingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($settings.feed -ne $config.feed -or $settings.key -ne $config.key) {
            Stop-Publish "the package for $arch was made with another update key or feed address than tools\update-config.json has. Package again."
        }
        try { $feedManifest = Read-UpdateFeed -Path $FeedPath -Public $settings.key }
        catch {
            Stop-Publish "the feed is not one the copies in the $arch package would take: $($_.Exception.Message)"
            continue
        }
        if (-not $feedManifest.archives.PSObject.Properties[$arch]) {
            Stop-Publish "the feed names no archive for $arch"
            continue
        }
        $named = $feedManifest.archives.$arch
        $hash = (Get-FileHash $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([long]$named.size -ne $archive.Length -or $named.sha256 -ne $hash) {
            Stop-Publish "the feed describes another $($archive.Name) than the one here (its length or its checksum differs). Run: tools\build.ps1 feed"
        }
    }
    if ($null -ne $feedManifest) {
        if ($feedManifest.version -ne $Version) { Stop-Publish "the feed is for version $($feedManifest.version), not $Version. Run: tools\build.ps1 feed" }
        if ([int]$feedManifest.build -ne $commitCount) {
            Stop-Publish "the feed is for build $($feedManifest.build), but the current commit is number $commitCount. Package again from this commit, then: tools\build.ps1 feed"
        }
        Write-Host ("update-feed.json  version {0}, build {1}, signed with the key in the packages" -f $feedManifest.version, $feedManifest.build)
    }
}

# The tag points at the commit the release was built from: it has to be on GitHub.
Invoke-Git fetch -q origin | Out-Null
& git -C $Root merge-base --is-ancestor $head origin/main 2>$null
if ($LASTEXITCODE -ne 0) { Stop-Publish 'the current commit is not on origin/main; push first' }
if (@(Invoke-Git status --porcelain).Count -gt 0) {
    Write-Host 'note: the working tree has uncommitted changes; the tag will point at the last commit'
}

$remote = (Invoke-Git remote get-url origin | Select-Object -First 1).Trim()
if ($remote -notmatch 'github\.com[:/]+([^/]+)/([^/]+?)(\.git)?$') { throw "origin is not a GitHub repository: $remote" }
$Owner = $Matches[1]
$Repo = $Matches[2]

# The addresses built into the packages and written in the feed must be this
# repository's: the feed where the newest release serves it, each archive
# where this release will hold it.
$wantedFeed = "https://github.com/$Owner/$Repo/releases/latest/download/update-feed.json"
if ($config.feed -ne $wantedFeed) {
    Stop-Publish "the packages read their feed from $($config.feed), which is not where this release will serve it ($wantedFeed). See tools\update-config.json."
}
if ($null -ne $feedManifest) {
    foreach ($archive in $archives) {
        $arch = $archive.BaseName -replace '^.*-win-', ''
        $wanted = "https://github.com/$Owner/$Repo/releases/download/$Tag/$($archive.Name)"
        if ($feedManifest.archives.PSObject.Properties[$arch] -and $feedManifest.archives.$arch.url -ne $wanted) {
            Stop-Publish "the feed sends $arch copies to $($feedManifest.archives.$arch.url), which is not where this release will hold the archive ($wanted)"
        }
    }
    # A copy takes an update only if its build number is higher than its own.
    try {
        $out = Get-PublishedUpdateBuild $config
        if ($null -eq $out) { Write-Host "no feed is published yet: build $($feedManifest.build) will be the first with one" }
        elseif ([int]$feedManifest.build -gt $out) { Write-Host "build $($feedManifest.build) is higher than the published build $out" }
        elseif (-not $Replace) { Stop-Publish "build $($feedManifest.build) is not higher than the published build $out. Installed copies would never be offered this version." }
    }
    catch { Stop-Publish $_.Exception.Message }
}

# ----- The notes -----

$notes = @"
Heracles is a local-first, hierarchical notebook for Windows: pages in notebooks, a block editor, split panes and kanban boards, all in one database on your own PC.

## Install

1. Download the disc image for your PC below: **win-x64** for most PCs, **win-arm64** for Windows on ARM (Settings > System > About shows the system type).
2. Double-click the downloaded ``.iso`` file. Windows opens it as a disc. If it asks whether to open the file, choose **Open**.
3. Copy the **Heracles** folder from the disc to your PC, for example into ``%LOCALAPPDATA%\Programs``, and run **Heracles.exe** from the copy.
4. Eject the disc (right-click it in File Explorer and choose **Eject**). It is not needed again, and the downloaded file can be deleted.

This build is **not code-signed**. If Windows shows "Windows protected your PC", choose **More info**, then **Run anyway**. Nothing needs installing: the .NET runtime is inside the folder. Requires Windows 10 version 2004 or newer, or Windows 11.

## Updates

From version 0.3.0 on, Heracles updates itself: it looks for a new version once a day and from **Help > Check for Updates**, shows what changed, and replaces its own folder when you agree. So this download is only for putting Heracles on a PC for the first time, or over a version older than 0.3.0. Keep the folder in a place of your own: run from the disc, it cannot replace itself. The ``.zip`` files and ``update-feed.json`` below are what installed copies fetch; there is no need to download them.

## First run

- Your library is stored only on your PC, under ``%APPDATA%\Heracles``.
- If the original Heracles (version 0.1) was used on this PC, its library is brought across the first time this one starts. The old library is not changed, and a copy of it is kept.
- Reminders on tasks are delivered by Windows whether or not Heracles is running. Clicking one opens its list.
"@
$versionNotes = Join-Path $Root "docs\release-notes\$Version.md"
if (Test-Path $versionNotes) {
    # Read as UTF-8 by name: Windows PowerShell takes a file without a
    # byte-order mark for ANSI, which garbles anything beyond ASCII.
    $notes += "`n`n## In this version`n`n" + [System.IO.File]::ReadAllText($versionNotes, [System.Text.Encoding]::UTF8).Trim()
}
$notes += "`n`n``SHA256SUMS`` lists checksums for the downloads."
$title = "Heracles $Version"

if ($DryRun) {
    Step "Dry run: would publish $Tag to $Owner/$Repo at $($head.Substring(0, 7))"
    Write-Host "title: $title"
    Write-Host "files: $((@($packages) + @($archives) | ForEach-Object { $_.Name }) -join ', '), update-feed.json, SHA256SUMS"
    Write-Host '--- notes ---'
    Write-Host $notes
    Write-Host '--- end of notes ---'
    if ($script:Refusals -gt 0) { Write-Host "a real run would stop: $($script:Refusals) thing(s) above" -ForegroundColor Yellow }
    return
}

# ----- GitHub -----

# The token Git already holds for this repository, asked for the way Git asks.
function Get-GitHubToken {
    # The question goes through a file and cmd's "<": a redirected input
    # stream from Windows PowerShell starts with a byte-order mark, and Git
    # then does not recognise the first line. The file holds no secret.
    $question = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($question, "protocol=https`nhost=github.com`npath=$Owner/$Repo.git`n`n", [System.Text.Encoding]::ASCII)
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $env:ComSpec
        $psi.Arguments = "/d /c `"git credential fill < `"$question`"`""
        $psi.WorkingDirectory = $Root
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.CreateNoWindow = $true
        $process = [System.Diagnostics.Process]::Start($psi)
        $text = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
    }
    finally {
        Remove-Item -LiteralPath $question -Force -ErrorAction SilentlyContinue
    }
    foreach ($line in ($text -split "`n")) {
        if ($line.StartsWith('password=')) { return $line.Substring(9).Trim() }
    }
    throw 'Git has no credential for github.com; push once so Git Credential Manager signs in, then run this again'
}

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$headers = @{
    Authorization          = 'Bearer ' + (Get-GitHubToken)
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'heracles-publish'
}
$api = "https://api.github.com/repos/$Owner/$Repo"

function Invoke-GitHub([string]$method, [string]$uri, $body = $null) {
    $arguments = @{ Method = $method; Uri = $uri; Headers = $headers; TimeoutSec = 120 }
    if ($null -ne $body) {
        $arguments.Body = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5))
        $arguments.ContentType = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @arguments
}

function Get-StatusCode($errorRecord) {
    $response = $errorRecord.Exception.Response
    if ($null -ne $response) { return [int]$response.StatusCode }
    return 0
}

function Send-Asset($release, [System.IO.FileInfo]$file, [string]$contentType) {
    foreach ($asset in @($release.assets | Where-Object { $_.name -eq $file.Name })) {
        Invoke-GitHub 'Delete' "$api/releases/assets/$($asset.id)" | Out-Null
    }
    $upload = ($release.upload_url -replace '\{.*$', '') + '?name=' + [Uri]::EscapeDataString($file.Name)
    Write-Host ("uploading {0} ({1:N0} MB)" -f $file.Name, ($file.Length / 1MB))
    Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType $contentType -InFile $file.FullName -TimeoutSec 3600 | Out-Null
}

$existing = $null
try { $existing = Invoke-GitHub 'Get' "$api/releases/tags/$Tag" }
catch { if ((Get-StatusCode $_) -ne 404) { throw "GitHub refused the request ($(Get-StatusCode $_)): $($_.Exception.Message)" } }

function Send-Files($release) {
    foreach ($package in $packages) { Send-Asset $release $package 'application/x-iso9660-image' }
    foreach ($archive in $archives) { Send-Asset $release $archive 'application/zip' }
    # The feed goes up while the release is still a draft, with the archives
    # it names already there: the moment the release is out, it is what every
    # installed copy reads.
    Send-Asset $release (Get-Item $FeedPath) 'application/json'
    Send-Asset $release (Get-Item $SumsPath) 'text/plain'
}

if ($null -ne $existing) {
    if (-not $Replace) {
        throw "release $Tag already exists. Change VERSION and package again for a new one, or run with -Replace to re-upload this one."
    }
    if ($existing.PSObject.Properties['immutable'] -and $existing.immutable) {
        throw "release $Tag is immutable: GitHub accepts no changes to its files, and its tag cannot be used for another release. Change VERSION and package again."
    }
    Step "Replacing the files of $Tag"
    $release = Invoke-GitHub 'Patch' "$api/releases/$($existing.id)" @{ name = $title; body = $notes; prerelease = [bool]$Prerelease }
    $release = Invoke-GitHub 'Get' "$api/releases/$($existing.id)"
    Send-Files $release
}
else {
    # A draft is not found by its tag, only in the list. Windows PowerShell
    # hands a list from GitHub over as one object, not release by release
    # (and an empty list as one empty object, which has no "draft" to ask
    # for: the first release of a repository stopped here). Sent through a
    # script block it comes apart into its releases, or into nothing.
    $releases = @(Invoke-GitHub 'Get' "$api/releases?per_page=100" | ForEach-Object { $_ })
    $draft = $releases | Where-Object { $_.draft -and $_.tag_name -eq $Tag } | Select-Object -First 1
    if ($null -ne $draft) {
        Step "Carrying on with the draft of $Tag"
        $release = Invoke-GitHub 'Patch' "$api/releases/$($draft.id)" @{ name = $title; body = $notes; prerelease = [bool]$Prerelease }
        $release = Invoke-GitHub 'Get' "$api/releases/$($draft.id)"
    }
    else {
        Step "Creating release $Tag (as a draft until its files are attached)"
        # An annotated tag made here and pushed, so the local clone has it too.
        & git -C $Root rev-parse -q --verify "refs/tags/$Tag" 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Invoke-Git tag -a $Tag -m $title $head | Out-Null }
        Invoke-Git push -q origin "refs/tags/$Tag" | Out-Null
        $release = Invoke-GitHub 'Post' "$api/releases" @{
            tag_name = $Tag; name = $title; body = $notes; draft = $true; prerelease = [bool]$Prerelease
        }
    }
    Send-Files $release
    # Published last: nothing can be added to an immutable release.
    Step "Publishing $Tag"
    $release = Invoke-GitHub 'Patch' "$api/releases/$($release.id)" @{ draft = $false }
}

$published = Invoke-GitHub 'Get' "$api/releases/tags/$Tag"
$names = @($published.assets | ForEach-Object { $_.name })
$missing = @(@(@($packages) + @($archives) | ForEach-Object { $_.Name }) + 'update-feed.json' + 'SHA256SUMS' | Where-Object { $names -notcontains $_ })
if ($missing.Count -gt 0) { throw "release $Tag is out, but GitHub does not list: $($missing -join ', ')" }
Write-Host "published: $($published.html_url) ($($names.Count) files)"

# What installed copies will read from now on, fetched the way they fetch
# it. A pre-release is not "the newest release", so its feed is not served
# there, and nobody is offered it: that is what a pre-release is for.
if ($Prerelease) {
    Write-Host 'note: a pre-release is not offered to installed copies; the feed they read is still the one of the newest full release'
}
else {
    Step 'Reading the feed back from where installed copies read it'
    $local = (Get-FileHash $FeedPath -Algorithm SHA256).Hash
    $served = $null
    $temp = [System.IO.Path]::GetTempFileName()
    try {
        # GitHub takes a moment to send "latest" to a release that has just come out.
        for ($attempt = 0; $attempt -lt 12 -and $served -ne $local; $attempt++) {
            if ($attempt -gt 0) { Start-Sleep -Seconds 5 }
            try {
                Invoke-WebRequest -Uri $config.feed -OutFile $temp -UseBasicParsing -TimeoutSec 60 -Headers @{ 'Cache-Control' = 'no-cache' } | Out-Null
                $served = (Get-FileHash $temp -Algorithm SHA256).Hash
            }
            catch { $served = $null }
        }
    }
    finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    if ($served -ne $local) {
        throw "release $Tag is out, but $($config.feed) does not serve the feed that was built. Installed copies will not be offered this version until it does. Is this release marked as the latest?"
    }
    Write-Host "ok: $($config.feed) serves the feed that was built"
}
