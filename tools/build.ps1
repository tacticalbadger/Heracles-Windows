<#
.SYNOPSIS
    Builds Heracles for Windows.

.DESCRIPTION
    tools\build.ps1 [targets...] [-Configuration Release|Debug] [-Arch arm64|x64]

    Targets (default: core app):
      core     Rust core (release) -> app\runtimes\win-<arch>\native\heracles_core.dll
               and the UniFFI C# bindings -> app\Generated\heracles_core.cs
      app      the WPF app -> build\app\win-<arch>\Heracles.exe
      test     Rust tests, then the .NET tests
      run      start the built app (on the real library, not a fixture)
      package  self-contained build for handing out: a disc image (.iso) per
               architecture and SHA256SUMS under build\release (needs "core"
               for that architecture first)
      clean    remove build output and the generated bindings

    Run "core" again after any change to the Rust API: the bindings and the
    DLL are generated, not in git. "app" refuses to overwrite a build that is
    running; pass -Restart to close that instance and start the new build.

    The version comes from the VERSION file; the build number is the commit
    count.

.EXAMPLE
    tools\build.ps1                 # core and app, for this machine's architecture
    tools\build.ps1 app -Restart    # rebuild the app and relaunch it
    tools\build.ps1 test
    tools\build.ps1 core package            # a package for this machine's architecture
    tools\build.ps1 core package -Arch x64  # and one for the other
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Targets,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [ValidateSet('', 'arm64', 'x64')]
    [string]$Arch = '',
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Root = Split-Path -Parent $PSScriptRoot
$Core = Join-Path $Root 'core'
$App = Join-Path $Root 'app'
$Build = Join-Path $Root 'build'

if (-not $Targets -or $Targets.Count -eq 0) { $Targets = @('core', 'app') }
$known = @('core', 'app', 'test', 'run', 'package', 'clean')
foreach ($t in $Targets) {
    if ($known -notcontains $t) { throw "unknown target '$t' (expected: $($known -join ', '))" }
}

# The machine's architecture, not this process's: an x64 PowerShell running
# under emulation on ARM64 (Git Bash starts one) reports AMD64 for itself,
# while cargo still builds for ARM64 when no target is named.
$machine = [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE', 'Machine')
if (-not $machine) { $machine = $env:PROCESSOR_ARCHITECTURE }
$hostArch = if ($machine -eq 'ARM64') { 'arm64' } else { 'x64' }
if (-not $Arch) { $Arch = $hostArch }
$Rid = "win-$Arch"
$HostRid = "win-$hostArch"
$Triple = if ($Arch -eq 'arm64') { 'aarch64-pc-windows-msvc' } else { 'x86_64-pc-windows-msvc' }
$OutDir = Join-Path $Build "app\$Rid"
$Exe = Join-Path $OutDir 'Heracles.exe'
$Version = (Get-Content (Join-Path $Root 'VERSION') -Raw).Trim()

# New-DiscImage and Compare-DiscImage, for the package.
. (Join-Path $PSScriptRoot 'disc-image.ps1')

# A shell opened before the toolchain was installed does not have these on
# PATH yet.
$extra = @(
    (Join-Path $env:USERPROFILE '.cargo\bin'),
    'C:\Program Files\dotnet'
) | Where-Object { (Test-Path $_) -and (($env:PATH -split ';') -notcontains $_) }
if ($extra) { $env:PATH = ($extra -join ';') + ';' + $env:PATH }

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Invoke-Native([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

function Assert-Tool([string]$name, [string]$hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { throw "$name not found. $hint" }
}

# Instances of the app started from this build's output folder.
function Get-RunningBuild {
    Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and ($_.Path -ieq $Exe) } catch { $false }
    }
}

# The 8.3 spelling of a folder. Where short names are switched off the path
# comes back unchanged.
function Get-ShortPath([string]$path) {
    try { return (New-Object -ComObject Scripting.FileSystemObject).GetFolder($path).ShortPath }
    catch { return $path }
}

# The Rust compiler bakes source paths into the DLL, as panic locations. They
# start with the profile folder, which is the account name, and the DLL is
# handed to other people. So the prefixes are rewritten: cargo's folder
# becomes "cargo", this checkout "heracles". Returns the environment variables
# that make cargo do it.
function Get-PathScrubEnvironment {
    $cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    $cargoHome = $cargoHome.TrimEnd('\')
    $separator = [string][char]0x1f

    $rustFlags = @()
    if ($env:CARGO_ENCODED_RUSTFLAGS) { $rustFlags += @($env:CARGO_ENCODED_RUSTFLAGS -split $separator) }
    elseif ($env:RUSTFLAGS) { $rustFlags += @($env:RUSTFLAGS -split '\s+' | Where-Object { $_ }) }
    $rustFlags += "--remap-path-prefix=$cargoHome=cargo"
    $rustFlags += "--remap-path-prefix=$Root=heracles"

    return @{ CARGO_ENCODED_RUSTFLAGS = ($rustFlags -join $separator) }
}

# Which processor a DLL or EXE is for, from its header.
function Get-PeArchitecture([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $machine = [BitConverter]::ToUInt16($bytes, [BitConverter]::ToInt32($bytes, 0x3c) + 4)
    if ($machine -eq 0xAA64) { return 'arm64' }
    if ($machine -eq 0x8664) { return 'x64' }
    return ('0x{0:X4}' -f $machine)
}

# The files among these that still name this machine's profile folder, in
# either spelling, as text or as UTF-16.
function Find-PrivatePaths([string[]]$files) {
    $leaves = @((Split-Path -Leaf $env:USERPROFILE), (Split-Path -Leaf (Get-ShortPath $env:USERPROFILE))) | Select-Object -Unique
    $needles = @()
    foreach ($leaf in $leaves) {
        foreach ($slash in '\', '/') {
            $needle = "Users$slash$leaf"
            $needles += $needle
            $needles += ($needle.ToCharArray() -join [string][char]0) + [string][char]0
        }
    }
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $found = @()
    foreach ($file in $files) {
        $text = $latin1.GetString([System.IO.File]::ReadAllBytes($file))
        foreach ($needle in $needles) {
            if ($text.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $found += (Split-Path -Leaf $file)
                break
            }
        }
    }
    return $found
}

function Assert-CoreBuilt([string]$rid, [string]$archHint) {
    if (-not (Test-Path (Join-Path $App 'Generated\heracles_core.cs'))) { throw 'no generated bindings; run: tools\build.ps1 core' }
    if (-not (Test-Path (Join-Path $App "runtimes\$rid\native\heracles_core.dll"))) { throw "no core DLL for $rid; run: tools\build.ps1 core$archHint" }
}

function Build-Core {
    Assert-Tool 'cargo' 'Install Rust: winget install Rustlang.Rustup'
    Assert-Tool 'uniffi-bindgen-cs' 'cargo install uniffi-bindgen-cs --git https://github.com/NordSecurity/uniffi-bindgen-cs --tag v0.12.0+v0.32.0'
    Step "core: cargo build --release ($Triple)"
    $scrub = Get-PathScrubEnvironment
    $before = @{}
    foreach ($name in $scrub.Keys) {
        $before[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $scrub[$name], 'Process')
    }
    # The Cargo workspace is the repository root, so build output is in
    # target\ there.
    Push-Location $Root
    try {
        if ($Arch -eq $hostArch) {
            Invoke-Native 'cargo build' { cargo build --release --locked --lib -p heracles-core }
            $dll = Join-Path $Root 'target\release\heracles_core.dll'
        }
        else {
            Invoke-Native 'rustup target add' { rustup target add $Triple }
            Invoke-Native 'cargo build' { cargo build --release --locked --lib -p heracles-core --target $Triple }
            $dll = Join-Path $Root "target\$Triple\release\heracles_core.dll"
        }
    }
    finally {
        Pop-Location
        foreach ($name in $scrub.Keys) { [Environment]::SetEnvironmentVariable($name, $before[$name], 'Process') }
    }
    # A DLL for the wrong processor would only fail when the app loads it.
    $built = Get-PeArchitecture $dll
    if ($built -ne $Arch) { throw "cargo built a DLL for $built where $Arch was asked for ($dll)" }
    if (@(Find-PrivatePaths @($dll)).Count -gt 0) {
        Write-Warning 'heracles_core.dll still names this machine''s profile folder; "package" will refuse it. See Get-PathScrubEnvironment.'
    }

    $native = Join-Path $App "runtimes\$Rid\native"
    New-Item -ItemType Directory -Force $native | Out-Null
    Copy-Item $dll (Join-Path $native 'heracles_core.dll') -Force

    # Bindings come from the metadata embedded in the DLL (UniFFI library
    # mode). The generator asks cargo about the crate, so it runs from there.
    Step 'core: C# bindings'
    $generated = Join-Path $App 'Generated'
    New-Item -ItemType Directory -Force $generated | Out-Null
    Push-Location $Core
    try {
        Invoke-Native 'uniffi-bindgen-cs' { uniffi-bindgen-cs --library $dll --config uniffi.toml --no-format --out-dir $generated }
    }
    finally { Pop-Location }
    Write-Host "ok: $native\heracles_core.dll"
    Write-Host "ok: $generated\heracles_core.cs"
}

function Build-App {
    Assert-Tool 'dotnet' 'Install the .NET SDK: winget install Microsoft.DotNet.SDK.10'
    Assert-CoreBuilt $Rid " -Arch $Arch"

    $running = @(Get-RunningBuild)
    if ($running.Count -gt 0) {
        if (-not $Restart) {
            throw "Heracles is running from $OutDir (pid $($running.Id -join ', ')), so its files are locked. Close it, or pass -Restart to close and relaunch it."
        }
        Step "app: closing the running build (pid $($running.Id -join ', '))"
        $running | Stop-Process -Force
        $running | ForEach-Object { try { $_.WaitForExit(5000) | Out-Null } catch { } }
    }

    $number = Get-BuildNumber
    Step "app: dotnet build ($Configuration, $Rid, $Version build $number)"
    $project = Join-Path $App 'Heracles\Heracles.csproj'
    Invoke-Native 'dotnet build' { dotnet build $project -c $Configuration -r $Rid -o $OutDir --no-incremental "-p:HeraclesBuild=$number" --nologo -v minimal }
    Write-Host "ok: $Exe"
    if ($Restart) { Start-App }
}

# The build number is the commit count, so two builds of one version can be told apart.
function Get-BuildNumber {
    $count = 0
    try {
        $text = & git -C $Root rev-list --count HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { $count = [int]$text }
    }
    catch { }
    return $count
}

# What the disc says of itself, in a file at its top, for whoever opens it
# without the release page in front of them.
function Get-DiscReadMe {
    return @(
        "Heracles $Version for Windows ($Arch)",
        '',
        '1. Copy the Heracles folder from this disc to your PC, for example into',
        '   %LOCALAPPDATA%\Programs.',
        '2. Run Heracles.exe from the copy.',
        '3. Eject this disc. It is not needed again.',
        '',
        'Run it from the copy, not from the disc: Windows opens links to notes,',
        'and reminders, with the copy of Heracles that was started last.',
        '',
        'The app is not code-signed. If Windows shows "Windows protected your PC",',
        'choose More info, then Run anyway.',
        '',
        'Your library is kept on your PC, under %APPDATA%\Heracles. Nothing is',
        'written to the folder the app runs from, so a newer version is put in by',
        'replacing that folder.',
        '',
        'https://github.com/tacticalbadger/Heracles-Windows'
    )
}

# A folder that runs on a PC with nothing installed (the .NET runtime is
# inside), on a disc image for the Releases page: Windows opens an .iso as a
# disc when it is double-clicked. Nothing in it is code-signed yet, so
# Windows asks before running it the first time.
function New-Package {
    Assert-Tool 'dotnet' 'Install the .NET SDK: winget install Microsoft.DotNet.SDK.10'
    Assert-CoreBuilt $Rid " -Arch $Arch"

    $number = Get-BuildNumber
    $name = "Heracles-$Version-win-$Arch"
    $release = Join-Path $Build 'release'
    $stage = Join-Path $release $name
    $iso = Join-Path $release "$name.iso"
    $top = Join-Path $release "$name.top"
    New-Item -ItemType Directory -Force $release | Out-Null
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    if (Test-Path $top) { Remove-Item -Recurse -Force $top }
    # Gone before anything is built: an image left from an earlier build must
    # not sit beside a folder it was not made from.
    Remove-DiscImage -Path $iso

    Step "package: dotnet publish (self-contained, $Rid, $Version build $number)"
    $project = Join-Path $App 'Heracles\Heracles.csproj'
    # Always compiled afresh: an assembly left by the "app" target at the same
    # commit would be reused as it is, debug information and all, whatever
    # this step asks for.
    $intermediate = Join-Path $App 'Heracles\obj\Release'
    if (Test-Path $intermediate) {
        Get-ChildItem $intermediate -Directory -Recurse -Filter $Rid | ForEach-Object { Remove-Item -Recurse -Force $_.FullName }
    }
    Invoke-Native 'dotnet publish' {
        dotnet publish $project -c Release -r $Rid --self-contained true "-p:HeraclesBuild=$number" `
            -p:DebugType=none -p:DebugSymbols=false -o $stage --nologo -v minimal
    }
    foreach ($needed in 'Heracles.exe', 'Heracles.dll', 'heracles_core.dll', 'Emoji.Wpf.dll') {
        if (-not (Test-Path (Join-Path $stage $needed))) { throw "the package is missing $needed" }
    }
    $packed = Get-PeArchitecture (Join-Path $stage 'heracles_core.dll')
    if ($packed -ne $Arch) { throw "the package for $Arch holds a core DLL for $packed. Run: tools\build.ps1 core -Arch $Arch, then package again." }
    # What is built here must not tell the recipient where it was built.
    $telling = @(Find-PrivatePaths @('Heracles.exe', 'Heracles.dll', 'heracles_core.dll' | ForEach-Object { Join-Path $stage $_ }))
    if ($telling.Count -gt 0) {
        throw "$($telling -join ', ') contains a path under this machine's profile folder. Run: tools\build.ps1 core -Arch $Arch (it rewrites those paths), then package again."
    }

    # The disc: the app in a folder named Heracles, and a few words beside it.
    Step "package: $name.iso"
    $label = "Heracles $Version"
    $holds = @{ Heracles = $stage }
    New-Item -ItemType Directory -Force $top | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $top 'README.txt'), ((Get-DiscReadMe) -join "`r`n") + "`r`n", [System.Text.Encoding]::ASCII)
    try {
        New-DiscImage -Path $iso -Label $label -Top $top -Folders $holds
        # Read back as Windows reads it, before anyone else does: every file
        # that went in must come out the same, and nothing else.
        $different = @(Compare-DiscImage -Path $iso -Label $label -Top $top -Folders $holds)
    }
    finally {
        Remove-Item -Recurse -Force $top
    }
    if ($different.Count -gt 0) {
        Remove-Item -Force $iso
        throw "the disc image does not hold what was put in it: $(($different | Select-Object -First 5) -join '; ')"
    }
    # The whole image this time, so every file in the package is looked at,
    # and whatever the disc says of itself.
    if (@(Find-PrivatePaths @($iso)).Count -gt 0) {
        Remove-Item -Force $iso
        throw "$name.iso contains a path under this machine's profile folder."
    }

    # Checksums for every package of this version that is there.
    $lines = Get-ChildItem $release -Filter "Heracles-$Version-win-*.iso" | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    [System.IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS'), (($lines -join "`n") + "`n"), [System.Text.Encoding]::ASCII)
    Write-Host ("ok: {0} ({1:N0} MB)" -f $iso, ((Get-Item $iso).Length / 1MB))
    Write-Host "ok: $(Join-Path $release 'SHA256SUMS')"
}

function Start-App {
    if (-not (Test-Path $Exe)) { throw "no build at $Exe; run: tools\build.ps1 app" }
    Step "run: $Exe"
    Start-Process -FilePath $Exe -WorkingDirectory $OutDir | Out-Null
}

function Test-All {
    Assert-Tool 'cargo' 'Install Rust: winget install Rustlang.Rustup'
    Assert-Tool 'dotnet' 'Install the .NET SDK: winget install Microsoft.DotNet.SDK.10'
    # The .NET tests go through the bindings into the DLL built for this machine.
    Assert-CoreBuilt $HostRid ''
    Step 'test: cargo test --locked'
    Push-Location $Root
    try { Invoke-Native 'cargo test' { cargo test --locked } }
    finally { Pop-Location }
    Step 'test: dotnet test'
    $tests = Join-Path $App 'Heracles.Tests\Heracles.Tests.csproj'
    # Compiled afresh every time: this machine's clock steps backwards now and
    # then, and an incremental build then runs the old tests without a word.
    Invoke-Native 'dotnet build' { dotnet build $tests --no-incremental --nologo -v quiet }
    Invoke-Native 'dotnet test' { dotnet test $tests --no-build --nologo -v minimal }
}

function Clear-All {
    Step 'clean'
    $paths = @(
        $Build,
        (Join-Path $Root 'target'),
        (Join-Path $App 'Generated'),
        (Join-Path $App 'runtimes'),
        (Join-Path $App 'Heracles\bin'),
        (Join-Path $App 'Heracles\obj'),
        (Join-Path $App 'Heracles.Tests\bin'),
        (Join-Path $App 'Heracles.Tests\obj')
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { Remove-Item -Recurse -Force $p; Write-Host "removed $p" }
    }
}

foreach ($t in $Targets) {
    switch ($t) {
        'core' { Build-Core }
        'app' { Build-App }
        'test' { Test-All }
        'run' { Start-App }
        'package' { New-Package }
        'clean' { Clear-All }
    }
}
