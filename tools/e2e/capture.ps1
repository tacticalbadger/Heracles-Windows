<#
.SYNOPSIS
    Lists the app's windows and saves a screenshot of each visible one.

.DESCRIPTION
        tools\e2e\capture.ps1 [-Out <folder>] [-Prefix <name>] [-Screen]

    Windows are rendered with PrintWindow, so they need not be in front.
    -Screen copies the window's area of the screen instead, which also shows
    content that draws outside the normal path (bring the window forward
    first). System dialogs that belong to the app's flow (Windows Security)
    are reported as well.
#>
[CmdletBinding()]
param(
    [string]$Out,
    [string]$Prefix = 'shot',
    [switch]$Screen
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Out) { $Out = Join-Path $Root 'build\e2e\shots' }
New-Item -ItemType Directory -Force $Out | Out-Null

Add-Type -AssemblyName System.Drawing
if (-not ('HeraclesE2E.Win' -as [type])) {
    Add-Type -Namespace HeraclesE2E -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@
}
# Real pixels, whatever the display scaling.
[HeraclesE2E.Win]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

# Only copies that are a test's: started from under this checkout's build
# folder, or from a disc. A copy started from anywhere else is the owner's
# own, and a picture of it would be a picture of his notes.
$testRoot = (Join-Path $Root 'build').TrimEnd('\') + '\'
$app = @(Get-Process -Name 'Heracles' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ($_.Path.StartsWith($testRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                (New-Object System.IO.DriveInfo ([System.IO.Path]::GetPathRoot($_.Path))).DriveType -eq [System.IO.DriveType]::CDRom)
        }
        catch { $false }
    })
if ($app.Count -eq 0) { Write-Host 'Heracles is not running (no copy started from build\ or from a disc)'; exit 1 }
$appIds = $app | ForEach-Object { [uint32]$_.Id }
$dialogHosts = @(Get-Process -Name 'CredentialUIBroker' -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })

$windows = New-Object System.Collections.Generic.List[object]
$callback = [HeraclesE2E.Win+EnumProc] {
    param($hwnd, $lParam)
    $processId = 0
    [HeraclesE2E.Win]::GetWindowThreadProcessId($hwnd, [ref]$processId) | Out-Null
    if ((($appIds -contains $processId) -or ($dialogHosts -contains $processId)) -and [HeraclesE2E.Win]::IsWindowVisible($hwnd)) {
        $title = New-Object System.Text.StringBuilder 512
        [HeraclesE2E.Win]::GetWindowTextW($hwnd, $title, 512) | Out-Null
        $class = New-Object System.Text.StringBuilder 256
        [HeraclesE2E.Win]::GetClassNameW($hwnd, $class, 256) | Out-Null
        $rect = New-Object HeraclesE2E.Win+RECT
        [HeraclesE2E.Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $windows.Add([pscustomobject]@{
                Handle = $hwnd; Title = $title.ToString(); Class = $class.ToString()
                Left = $rect.Left; Top = $rect.Top; Width = $rect.Right - $rect.Left; Height = $rect.Bottom - $rect.Top
                System = ($dialogHosts -contains $processId); Minimized = [HeraclesE2E.Win]::IsIconic($hwnd)
            })
    }
    return $true
}
[HeraclesE2E.Win]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

$n = 0
foreach ($w in $windows) {
    if ($w.Width -lt 40 -or $w.Height -lt 40 -or $w.Minimized) { continue }
    $kind = if ($w.System) { 'system dialog' } else { 'window' }
    $n++
    $file = Join-Path $Out "$Prefix-$n.png"
    $bitmap = New-Object System.Drawing.Bitmap $w.Width, $w.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        if ($Screen) {
            $graphics.CopyFromScreen($w.Left, $w.Top, 0, 0, (New-Object System.Drawing.Size $w.Width, $w.Height))
        }
        else {
            $hdc = $graphics.GetHdc()
            # 2 = PW_RENDERFULLCONTENT: include content composed off the GDI path.
            try { [HeraclesE2E.Win]::PrintWindow($w.Handle, $hdc, 2) | Out-Null }
            finally { $graphics.ReleaseHdc($hdc) }
        }
        $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    Write-Host ("{0}: `"{1}`" [{2}] {3}x{4} at {5},{6}" -f $kind, $w.Title, $w.Class, $w.Width, $w.Height, $w.Left, $w.Top)
    Write-Host "   -> $file"
}
if ($n -eq 0) { Write-Host 'no visible windows' }
