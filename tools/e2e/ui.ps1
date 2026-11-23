<#
.SYNOPSIS
    Drives the running app through Windows UI Automation and synthetic input.

.DESCRIPTION
        tools\e2e\ui.ps1 windows                       top-level windows of the app
        tools\e2e\ui.ps1 tree [-Depth 8] [-Within <automationId>]
        tools\e2e\ui.ps1 find    -Id <automationId> | -Name <name>
        tools\e2e\ui.ps1 invoke  -Id <automationId> | -Name <name>     button, menu item, tree item
        tools\e2e\ui.ps1 expand  -Id ... | -Name ...   /  collapse / select
        tools\e2e\ui.ps1 setvalue -Id ... -Text <text>                 text boxes
        tools\e2e\ui.ps1 scroll  -Id ... -Y <0 to 100>                  how far down a scrolling area is
        tools\e2e\ui.ps1 click   -Id ... | -Name ... [-Right] [-Double]  real mouse click on its centre
        tools\e2e\ui.ps1 clickat -X <px> -Y <px> [-Right] [-Double]      relative to the main window
        tools\e2e\ui.ps1 drag    -X <px> -Y <px> -ToX <px> -ToY <px>      relative to the main window
        tools\e2e\ui.ps1 dragto  -Id <from> -ToId <to> [-ToX <px>] [-ToY <px>]   real drag from the middle of one element to the middle of another, plus an offset
        tools\e2e\ui.ps1 keys    -Keys '<SendKeys syntax>'               e.g. '^+{]}' for Ctrl+Shift+]
        tools\e2e\ui.ps1 text    -Text 'echo hello'                      typed literally
        tools\e2e\ui.ps1 foreground

    "invoke", "expand", "select" and "setvalue" use automation patterns and
    touch neither the mouse nor the keyboard. "click", "drag", "keys" and
    "text" are real input: they bring the app to the front first and refuse
    to type when another program has the keyboard.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('windows', 'tree', 'find', 'invoke', 'expand', 'collapse', 'select', 'setvalue', 'focus', 'scroll', 'click', 'clickat', 'drag', 'dragto', 'keys', 'text', 'foreground')]
    [string]$Action,
    [string]$Id,
    [string]$Name,
    [string]$Within,
    [string]$Text,
    [string]$Keys,
    [string]$ToId,
    [int]$Depth = 8,
    [int]$X,
    [int]$Y,
    [int]$ToX,
    [int]$ToY,
    [switch]$Right,
    [switch]$Double
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
if (-not ('HeraclesE2E.Input' -as [type])) {
    Add-Type -Namespace HeraclesE2E -Name Input -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@
}
[HeraclesE2E.Input]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

# The system's own dialogs (open a file, save a file) are made of plain
# Windows controls, which offer no automation patterns here. They are sent
# window messages, each to the one control by its handle: "select what you
# hold", "this character was typed in you", and, to the dialog, "your button
# was pressed". None of it is keyboard or mouse input: nothing is typed
# into whatever has the keyboard, and nothing can land in another program.
#
# Two things found the hard way. A Save dialog takes its file name only
# from characters typed in its box: text put there whole (WM_SETTEXT) shows
# in the box and is then ignored, and Save writes the name the dialog
# offered. And a button told it was clicked (BM_CLICK) does nothing unless
# its dialog is the active window; the dialog told its button was pressed
# (WM_COMMAND) always listens.
if (-not ('HeraclesE2E.Native' -as [type])) {
    Add-Type -Namespace HeraclesE2E -Name Native -MemberDefinition @'
[DllImport("user32.dll", EntryPoint = "SendMessageW")] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
[DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hwnd);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
'@
}
$EM_SETSEL = 0x00B1
$WM_CHAR = 0x0102
$WM_COMMAND = 0x0111

# Replace what a plain text box holds, as typing over all of it would.
function Set-NativeText([IntPtr]$edit, [string]$text) {
    [HeraclesE2E.Native]::SendMessage($edit, $EM_SETSEL, [IntPtr]::Zero, [IntPtr](-1)) | Out-Null
    foreach ($character in $text.ToCharArray()) {
        [HeraclesE2E.Native]::SendMessage($edit, $WM_CHAR, [IntPtr][int]$character, [IntPtr]::Zero) | Out-Null
    }
}

# Tell a dialog its button was pressed. Posted, not sent: the dialog may close before it answers.
function Send-NativePress([IntPtr]$button) {
    $dialog = [HeraclesE2E.Native]::GetParent($button)
    [HeraclesE2E.Native]::PostMessage($dialog, $WM_COMMAND, [IntPtr][HeraclesE2E.Native]::GetDlgCtrlID($button), $button) | Out-Null
}

# The plain Windows control of a class at, or under, an element: its window handle, or zero.
function Get-NativeControl($element, [string]$class) {
    $candidates = @($element) + @($element.FindAll($TreeScope::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($Auto::ClassNameProperty, $class))))
    foreach ($candidate in $candidates) {
        try {
            if ($candidate.Current.ClassName -eq $class -and $candidate.Current.NativeWindowHandle -ne 0) { return [IntPtr]$candidate.Current.NativeWindowHandle }
        }
        catch { }
    }
    return [IntPtr]::Zero
}

$Auto = [System.Windows.Automation.AutomationElement]
$TreeScope = [System.Windows.Automation.TreeScope]
$app = @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue)
if ($app.Count -eq 0) { Write-Host 'Heracles is not running'; exit 1 }
$processIds = @($app | ForEach-Object { $_.Id })

function Get-AppWindows {
    $all = $Auto::RootElement.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($w in $all) {
        try { if ($processIds -contains $w.Current.ProcessId) { $w } } catch { }
    }
}

function Get-MainWindow {
    $main = $app | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    if (-not $main) { throw 'the app has no main window' }
    return $main.MainWindowHandle
}

function Find-Element {
    $conditions = @()
    if ($Id) { $conditions += New-Object System.Windows.Automation.PropertyCondition ($Auto::AutomationIdProperty, $Id) }
    if ($Name) { $conditions += New-Object System.Windows.Automation.PropertyCondition ($Auto::NameProperty, $Name) }
    if ($conditions.Count -eq 0) { throw 'give -Id or -Name' }
    $condition = if ($conditions.Count -eq 1) { $conditions[0] } else { New-Object System.Windows.Automation.AndCondition ($conditions) }
    # Dialogs and menus are separate top-level windows; the newest comes first.
    foreach ($window in @(Get-AppWindows)) {
        $root = $window
        if ($Within) {
            $root = $window.FindFirst($TreeScope::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($Auto::AutomationIdProperty, $Within)))
            if (-not $root) { continue }
        }
        $found = $root.FindFirst(($TreeScope::Element -bor $TreeScope::Descendants), $condition)
        if ($found) { return $found }
    }
    throw "no element with $(if ($Id) { "id '$Id'" }) $(if ($Name) { "name '$Name'" })"
}

function Describe($element) {
    # An element that has gone (a chosen menu item, a closed window) answers
    # with an error, or with nothing where its properties were.
    try { return (Describe-Live $element) } catch { return "(element is gone)" }
}

function Describe-Live($element) {
    $c = $element.Current
    # A menu item that was just chosen is gone by the time it is described,
    # and sometimes answers with nothing at all in place of an error.
    if (-not $c.ControlType) { return "(element is gone)" }
    $r = $c.BoundingRectangle
    # An element that was shown and then hidden keeps its last rectangle, so
    # the rectangle alone does not say whether it can be seen.
    $rect = if ($c.IsOffscreen -or -not ($r -is [System.Windows.Rect]) -or $r.IsEmpty) { 'offscreen' } else { '{0},{1} {2}x{3}' -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height }
    $extra = ''
    if ($c.HelpText) { $extra += " help='$($c.HelpText)'" }
    if ($c.ItemStatus) { $extra += " status='$($c.ItemStatus)'" }
    if (-not $c.IsEnabled) { $extra += ' DISABLED' }
    # What a text box holds, on one line and not too much of it. It comes
    # last, so nothing before it is harder to pick out.
    $held = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$held)) {
        $value = "$($held.Current.Value)" -replace '[\r\n]+', ' '
        if ($value.Length -gt 200) { $value = $value.Substring(0, 200) }
        $extra += " value='$value'"
    }
    "{0} '{1}' id='{2}' [{3}]{4}" -f ($c.ControlType.ProgrammaticName -replace 'ControlType\.', ''), $c.Name, $c.AutomationId, $rect, $extra
}

function Show-Tree($element, [int]$level) {
    Write-Host ((' ' * ($level * 2)) + (Describe $element))
    if ($level -ge $Depth) { return }
    $children = $element.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($child in $children) { Show-Tree $child ($level + 1) }
}

function Test-Foreground {
    $front = [HeraclesE2E.Input]::GetForegroundWindow()
    $frontProcess = 0
    [HeraclesE2E.Input]::GetWindowThreadProcessId($front, [ref]$frontProcess) | Out-Null
    $hosts = @(Get-Process -Name 'msedgewebview2', 'CredentialUIBroker' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    return ($processIds -contains $frontProcess) -or ($hosts -contains $frontProcess)
}

function Set-Foreground {
    if (Test-Foreground) { return }
    $hwnd = Get-MainWindow
    if ([HeraclesE2E.Input]::IsIconic($hwnd)) { [HeraclesE2E.Input]::ShowWindow($hwnd, 9) | Out-Null }
    # UI Automation may activate a window where a plain SetForegroundWindow
    # from a background process is refused. No key is tapped to get the
    # right: a stray Alt would open the menu bar and swallow what follows.
    try { $Auto::FromHandle($hwnd).SetFocus() } catch { }
    [HeraclesE2E.Input]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 250
}

function Assert-Foreground {
    if (-not (Test-Foreground)) { throw 'another program has the keyboard; nothing was typed or clicked' }
}

# A real click or drag must begin and end over one of the app's own windows.
# An element scrolled out of view still says where it would be, and that
# place can be past the edge of its window, over another program: a click
# sent there once landed outside the window it was meant for.
function Assert-OverApp([int]$px, [int]$py) {
    $point = New-Object HeraclesE2E.Native+POINT
    $point.X = $px; $point.Y = $py
    $under = [HeraclesE2E.Native]::WindowFromPoint($point)
    $owner = 0
    [HeraclesE2E.Native]::GetWindowThreadProcessId($under, [ref]$owner) | Out-Null
    if ($processIds -notcontains $owner) { throw "the pointer at $px,$py would not be over the app; nothing was clicked" }
}

function Assert-InView($element) {
    if ($element.Current.IsOffscreen) { throw "not in view (scroll to it first): $(Describe $element)" }
}

function Invoke-Click([int]$px, [int]$py) {
    Assert-OverApp $px $py
    [HeraclesE2E.Input]::SetCursorPos($px, $py) | Out-Null
    Start-Sleep -Milliseconds 80
    $down = if ($Right) { 0x0008 } else { 0x0002 }
    $up = if ($Right) { 0x0010 } else { 0x0004 }
    $times = if ($Double) { 2 } else { 1 }
    for ($i = 0; $i -lt $times; $i++) {
        [HeraclesE2E.Input]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero)
        [HeraclesE2E.Input]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
    }
}

function Get-WindowOrigin {
    $rect = New-Object HeraclesE2E.Input+RECT
    [HeraclesE2E.Input]::GetWindowRect((Get-MainWindow), [ref]$rect) | Out-Null
    return $rect
}

switch ($Action) {
    'windows' {
        foreach ($w in @(Get-AppWindows)) { Write-Host (Describe $w) }
    }
    'tree' {
        foreach ($w in @(Get-AppWindows)) {
            $root = $w
            if ($Within) {
                $root = $w.FindFirst($TreeScope::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($Auto::AutomationIdProperty, $Within)))
                if (-not $root) { continue }
            }
            Show-Tree $root 0
        }
    }
    'find' { Write-Host (Describe (Find-Element)) }
    'invoke' {
        $element = Find-Element
        $pattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
        }
        elseif ($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
            $pattern.Toggle()
        }
        elseif ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
        }
        elseif ($element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
            $pattern.Expand()
        }
        elseif ((Get-NativeControl $element 'Button') -ne [IntPtr]::Zero) {
            # A button of a system dialog.
            Send-NativePress (Get-NativeControl $element 'Button')
        }
        else { throw "nothing to invoke on: $(Describe $element)" }
        Write-Host "invoked: $(Describe $element)"
    }
    'expand' {
        $element = Find-Element
        $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Write-Host "expanded: $(Describe $element)"
    }
    'collapse' {
        $element = Find-Element
        $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
        Write-Host "collapsed: $(Describe $element)"
    }
    'select' {
        $element = Find-Element
        $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Write-Host "selected: $(Describe $element)"
    }
    'focus' {
        $element = Find-Element
        $element.SetFocus()
        Write-Host "focused: $(Describe $element)"
    }
    'scroll' {
        # How far down, from 0 (the top) to 100 (the bottom). Across is left as it is.
        $element = Find-Element
        $element.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, $Y)
        Write-Host "scrolled: $(Describe $element)"
    }
    'setvalue' {
        $element = Find-Element
        $pattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            $pattern.SetValue($Text)
        }
        elseif ((Get-NativeControl $element 'Edit') -ne [IntPtr]::Zero) {
            # The file-name box of a system dialog.
            Set-NativeText (Get-NativeControl $element 'Edit') $Text
        }
        else { throw "nothing to set a value on: $(Describe $element)" }
        Write-Host "set: $(Describe $element)"
    }
    'click' {
        $element = Find-Element
        $r = $element.Current.BoundingRectangle
        if ($r.IsEmpty) { throw "not on screen: $(Describe $element)" }
        Assert-InView $element
        Set-Foreground
        # A click goes wherever the pointer is. If the app could not be
        # brought forward, it would land in someone else's window.
        Assert-Foreground
        Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
        Write-Host "clicked: $(Describe $element)"
    }
    'dragto' {
        if (-not $Id -or -not $ToId) { throw 'give -Id (what to pick up) and -ToId (where to let go)' }
        $from = (Find-Element).Current.BoundingRectangle
        $target = $null
        foreach ($window in @(Get-AppWindows)) {
            $target = $window.FindFirst(($TreeScope::Element -bor $TreeScope::Descendants), (New-Object System.Windows.Automation.PropertyCondition ($Auto::AutomationIdProperty, $ToId)))
            if ($target) { break }
        }
        if (-not $target) { throw "no element with id '$ToId'" }
        $to = $target.Current.BoundingRectangle
        if ($from.IsEmpty -or $to.IsEmpty) { throw 'one end of the drag is not on screen' }
        Assert-InView (Find-Element)
        Assert-InView $target
        Set-Foreground
        Assert-Foreground
        $fx = [int]($from.X + $from.Width / 2); $fy = [int]($from.Y + $from.Height / 2)
        $tx = [int]($to.X + $to.Width / 2) + $ToX; $ty = [int]($to.Y + $to.Height / 2) + $ToY
        Assert-OverApp $fx $fy
        Assert-OverApp $tx $ty
        [HeraclesE2E.Input]::SetCursorPos($fx, $fy) | Out-Null
        Start-Sleep -Milliseconds 150
        [HeraclesE2E.Input]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        # A drag is taken for one only once the pointer has really moved with
        # the button down, so it is moved through the system's own input, a
        # little at first and then the rest of the way.
        $steps = 30
        for ($i = 1; $i -le $steps; $i++) {
            $x = [int]($fx + ($tx - $fx) * $i / $steps); $y = [int]($fy + ($ty - $fy) * $i / $steps)
            [HeraclesE2E.Input]::SetCursorPos($x, $y) | Out-Null
            [HeraclesE2E.Input]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 25
        }
        Start-Sleep -Milliseconds 300
        [HeraclesE2E.Input]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Write-Host "dragged $Id -> $ToId"
    }
    'clickat' {
        Set-Foreground
        Assert-Foreground
        $o = Get-WindowOrigin
        Invoke-Click ($o.Left + $X) ($o.Top + $Y)
        Write-Host "clicked at $X,$Y"
    }
    'drag' {
        Set-Foreground
        Assert-Foreground
        $o = Get-WindowOrigin
        $fx = $o.Left + $X; $fy = $o.Top + $Y; $tx = $o.Left + $ToX; $ty = $o.Top + $ToY
        Assert-OverApp $fx $fy
        Assert-OverApp $tx $ty
        [HeraclesE2E.Input]::SetCursorPos($fx, $fy) | Out-Null
        Start-Sleep -Milliseconds 120
        [HeraclesE2E.Input]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        $steps = 24
        for ($i = 1; $i -le $steps; $i++) {
            [HeraclesE2E.Input]::SetCursorPos([int]($fx + ($tx - $fx) * $i / $steps), [int]($fy + ($ty - $fy) * $i / $steps)) | Out-Null
            Start-Sleep -Milliseconds 25
        }
        Start-Sleep -Milliseconds 250
        [HeraclesE2E.Input]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Write-Host "dragged $X,$Y -> $ToX,$ToY"
    }
    'keys' {
        Set-Foreground
        Assert-Foreground
        [System.Windows.Forms.SendKeys]::SendWait($Keys)
        Write-Host "sent keys: $Keys"
    }
    'text' {
        Set-Foreground
        Assert-Foreground
        $escaped = [regex]::Replace($Text, '[+^%~(){}\[\]]', { param($m) '{' + $m.Value + '}' })
        [System.Windows.Forms.SendKeys]::SendWait($escaped)
        Write-Host "typed: $Text"
    }
    'foreground' {
        Set-Foreground
        Write-Host 'brought to the front'
    }
}
