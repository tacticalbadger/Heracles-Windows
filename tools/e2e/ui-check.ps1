<#
.SYNOPSIS
    Helpers for scripted end-to-end checks: dot-source this file.

.DESCRIPTION
        . tools\e2e\ui-check.ps1
        $id = Get-UiId -Name 'Trip' -Prefix 'note:'      # the automation id of the first element so named
        Test-Ui 'the tab is there' { Find-Ui -Id "Tab:$pane:$note" }
        Wait-Ui { Find-Ui -Id 'Switcher' }                # until it is there, or two seconds pass
        Show-UiResults                                     # how many checks passed, and which did not

    Everything here goes through tools\e2e\ui.ps1 and UI Automation patterns.
    Nothing moves the mouse or types on the keyboard.
#>

$script:UiScript = Join-Path $PSScriptRoot 'ui.ps1'
$script:UiPassed = 0
$script:UiFailed = @()

# The line ui.ps1 prints for an element, or $null if it is not there.
function Find-Ui {
    param([string]$Id, [string]$Name)
    try {
        if ($Id) { return (& $script:UiScript find -Id $Id 6>&1 | ForEach-Object { "$_" } | Select-Object -First 1) }
        return (& $script:UiScript find -Name $Name 6>&1 | ForEach-Object { "$_" } | Select-Object -First 1)
    }
    catch { return $null }
}

# Every line of the automation tree under an element.
function Get-UiTree {
    param([string]$Within, [int]$Depth = 8)
    try {
        if ($Within) { return @(& $script:UiScript tree -Within $Within -Depth $Depth 6>&1 | ForEach-Object { "$_" }) }
        return @(& $script:UiScript tree -Depth $Depth 6>&1 | ForEach-Object { "$_" })
    }
    catch { return @() }
}

# The automation id of the first element with this name whose id starts with a prefix.
function Get-UiId {
    param([Parameter(Mandatory = $true)][string]$Name, [string]$Prefix = '', [string]$Within)
    foreach ($line in (Get-UiTree -Within $Within)) {
        if ($line -match "'$([regex]::Escape($Name))' id='($([regex]::Escape($Prefix))[^']*)'") { return $Matches[1] }
    }
    return $null
}

# Every automation id under an element that starts with a prefix.
function Get-UiIds {
    param([Parameter(Mandatory = $true)][string]$Prefix, [string]$Within)
    $ids = @()
    foreach ($line in (Get-UiTree -Within $Within)) {
        if ($line -match " id='($([regex]::Escape($Prefix))[^']*)'") { $ids += $Matches[1] }
    }
    return $ids
}

# The name of the element with this id (what a screen reader would say).
function Get-UiName {
    param([Parameter(Mandatory = $true)][string]$Id)
    $line = Find-Ui -Id $Id
    if ($line -and $line -match "^\s*\S+ '(.*)' id='") { return $Matches[1] }
    return $null
}

function Invoke-Ui { param([string]$Id, [string]$Name) if ($Id) { & $script:UiScript invoke -Id $Id 6>&1 | Out-Null } else { & $script:UiScript invoke -Name $Name 6>&1 | Out-Null } }
function Select-Ui { param([Parameter(Mandatory = $true)][string]$Id) & $script:UiScript select -Id $Id 6>&1 | Out-Null }
function Set-UiValue { param([Parameter(Mandatory = $true)][string]$Id, [string]$Text) & $script:UiScript setvalue -Id $Id -Text $Text 6>&1 | Out-Null }

# Choose an item of the menu bar: Invoke-UiMenu 'View' 'Split Right'.
function Invoke-UiMenu {
    param([Parameter(Mandatory = $true)][string]$Menu, [Parameter(Mandatory = $true)][string]$Item)
    & $script:UiScript expand -Name $Menu 6>&1 | Out-Null
    Start-Sleep -Milliseconds 250
    & $script:UiScript invoke -Name $Item 6>&1 | Out-Null
    Start-Sleep -Milliseconds 250
}

# Wait until a condition holds, for at most a few seconds. Returns whether it did.
function Wait-Ui {
    param([Parameter(Mandatory = $true)][scriptblock]$Condition, [int]$Milliseconds = 3000)
    $until = (Get-Date).AddMilliseconds($Milliseconds)
    do {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $until)
    return $false
}

# One check: a name, and a condition that is waited for.
function Test-Ui {
    param([Parameter(Mandatory = $true)][string]$What, [Parameter(Mandatory = $true)][scriptblock]$Condition)
    if (Wait-Ui $Condition) {
        $script:UiPassed++
        Write-Host "PASS  $What"
    }
    else {
        $script:UiFailed += $What
        Write-Host "FAIL  $What"
    }
}

function Show-UiResults {
    Write-Host "checks: $($script:UiPassed) passed, $($script:UiFailed.Count) failed"
    foreach ($what in $script:UiFailed) { Write-Host "  failed: $what" }
}

# The line for an element that is there AND showing. An element that is
# hidden (a collapsed overlay, say) is still in the automation tree, marked
# as off the screen; so is a row scrolled out of view. Use Find-Ui to ask
# whether something exists, and this to ask whether it can be seen.
function Find-UiShown {
    param([string]$Id, [string]$Name)
    $line = if ($Id) { Find-Ui -Id $Id } else { Find-Ui -Name $Name }
    if ($line -and $line -notmatch '\[offscreen\]') { return $line }
    return $null
}

# Give an element the keyboard, as tabbing to it would. What had it loses it,
# which is how a typed title is committed.
function Set-UiFocus {
    param([Parameter(Mandatory = $true)][string]$Id)
    & $script:UiScript focus -Id $Id 6>&1 | Out-Null
}
