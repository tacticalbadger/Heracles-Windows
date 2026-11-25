<#
.SYNOPSIS
    The update key of Heracles for Windows: makes it, shows its public half,
    and writes it out to be kept somewhere safe.

.DESCRIPTION
        tools\update-key.ps1                  say whether a key is stored here, and show its public half
        tools\update-key.ps1 -Create          make the key (once), store it, and write its public half
                                              into tools\update-config.json
        tools\update-key.ps1 -Export <file>   write the private key to a file: to keep a copy of, or to
                                              take to another PC
        tools\update-key.ps1 -Import <file>   take the private key from such a file

    Every copy of Heracles from 0.3.0 on takes an update only if the feed
    that announces it was signed with this key (see tools\update-signing.ps1
    for how). The public half is in tools\update-config.json and goes into
    every package. The private half is in the Windows credential store of
    the account that makes releases, under the name update-config.json
    gives, and never in the repository.

    LOSE THE PRIVATE KEY AND NO INSTALLED COPY CAN BE UPDATED AGAIN. There is
    no way round it: a new key means a build that everybody has to download
    and put in by hand, as before 0.3.0. So, once it is made: -Export it to
    a file, put what the file holds into a password manager, and delete the
    file. The credential store is this account's on this PC only, and goes
    with the account.

    Anybody who has the private key can sign an update that every copy will
    take. The exported file is that key, in the clear: do not leave it lying
    about, and never commit it.

    -Create refuses when a key is already stored, and -Import when a
    different one is, unless -Replace is given. Replacing the key strands
    every copy that trusts the old one.

    Nothing here prints the private key.
#>
[CmdletBinding()]
param(
    [switch]$Create,
    [string]$Export,
    [string]$Import,
    [switch]$Replace
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'update-signing.ps1')
$ConfigPath = Join-Path $Root 'tools\update-config.json'
$Config = Get-UpdateConfig $Root
$Name = $Config.keyName

# The public half, written into update-config.json where "key" is.
function Set-ConfiguredKey([string]$public) {
    $text = [System.IO.File]::ReadAllText($ConfigPath, [System.Text.Encoding]::UTF8)
    $pattern = '("key"\s*:\s*")[^"]*(")'
    if ($text -notmatch $pattern) { throw 'tools\update-config.json has no "key" to write the public key into' }
    $text = [regex]::Replace($text, $pattern, { param($match) $match.Groups[1].Value + $public + $match.Groups[2].Value })
    [System.IO.File]::WriteAllText($ConfigPath, $text, (New-Object System.Text.UTF8Encoding $false))
}

$stored = Get-StoredUpdateKey $Name

if ($Create) {
    if ($stored -and -not $Replace) {
        throw "a key is already stored as '$Name'. Making another would strand every copy that trusts this one. (To do it anyway: -Create -Replace.)"
    }
    $key = New-UpdateKey
    Set-StoredUpdateKey -Name $Name -Private $key.Private
    Set-ConfiguredKey $key.Public
    Write-Host "made the update key and stored it as '$Name' in this account's Windows credentials"
    Write-Host "public key: $($key.Public)"
    Write-Host 'written into tools\update-config.json'
    Write-Host ''
    Write-Host 'Now keep a copy of it: tools\update-key.ps1 -Export <file>, put what the file holds'
    Write-Host 'into a password manager, and delete the file. Without the key, no installed copy'
    Write-Host 'can be updated again.'
    return
}

if ($Import) {
    $private = ([System.IO.File]::ReadAllText($Import, [System.Text.Encoding]::ASCII)).Trim()
    $public = Get-UpdatePublicKey $private
    if ($stored -and $stored -ne $private -and -not $Replace) {
        throw "a different key is already stored as '$Name'. (To put this one in its place: -Import <file> -Replace.)"
    }
    Set-StoredUpdateKey -Name $Name -Private $private
    Write-Host "stored the key from $Import as '$Name' in this account's Windows credentials"
    Write-Host "public key: $public"
    if ($Config.key -ne $public) {
        Write-Host "note: tools\update-config.json names another public key ($($Config.key)). Packages are checked against that one."
    }
    return
}

if (-not $stored) {
    Write-Host "no update key is stored as '$Name' in this account's Windows credentials."
    if ($Config.key) { Write-Host "tools\update-config.json names the public key $($Config.key): its private half is somewhere else. Bring it here with -Import <file>." }
    else { Write-Host 'None has been made yet: tools\update-key.ps1 -Create' }
    exit 1
}

$public = Get-UpdatePublicKey $stored
if ($Export) {
    $target = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $Export))
    if (Test-Path $target) { throw "$target is already there; name a file that is not" }
    # Not under the repository: a private key has no business near a commit.
    if ($target.StartsWith($Root.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'that file would be inside the repository. Name one somewhere else.'
    }
    [System.IO.File]::WriteAllText($target, $stored + "`r`n", [System.Text.Encoding]::ASCII)
    Write-Host "wrote the private key to $target"
    Write-Host 'Put what it holds into a password manager, then delete the file.'
    return
}

Write-Host "the update key is stored as '$Name' in this account's Windows credentials"
Write-Host "public key: $public"
if ($Config.key -eq $public) { Write-Host 'tools\update-config.json names the same key' }
else {
    Write-Host "BUT tools\update-config.json names another: '$($Config.key)'. A release signed here would be refused by every copy built from it."
    exit 1
}
