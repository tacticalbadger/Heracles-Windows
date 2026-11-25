<#
.SYNOPSIS
    Serves a folder over HTTP on this machine only, for the update rehearsal.

.DESCRIPTION
        tools\e2e\serve-folder.ps1 -Folder <dir> -Log <file> [-Port 8498]

    Answers GET requests for files under the folder, on 127.0.0.1 and
    nowhere else, and writes one line per request to the log ("GET /path
    200"), which is how a check knows what was asked for and what was not.
    Runs until it is stopped: tools\e2e\check-update.ps1 starts it hidden
    and ends it by its process id. Needs no administrator.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [Parameter(Mandatory = $true)][string]$Log,
    [int]$Port = 8498
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Folder = (Resolve-Path $Folder).Path.TrimEnd('\')
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
[System.IO.File]::AppendAllText($Log, "listening on 127.0.0.1:$Port`n")

while ($listener.IsListening) {
    $context = $listener.GetContext()
    $asked = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath)
    $status = 404
    try {
        $file = [System.IO.Path]::GetFullPath((Join-Path $Folder $asked.TrimStart('/').Replace('/', '\')))
        # Nothing outside the folder, whatever is asked for.
        if ($context.Request.HttpMethod -eq 'GET' -and $file.StartsWith($Folder + '\', [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $file -PathType Leaf)) {
            $status = 200
            $context.Response.StatusCode = 200
            $context.Response.ContentType = 'application/octet-stream'
            $stream = [System.IO.File]::OpenRead($file)
            try {
                $context.Response.ContentLength64 = $stream.Length
                $stream.CopyTo($context.Response.OutputStream)
            }
            finally { $stream.Dispose() }
        }
        else { $context.Response.StatusCode = 404 }
    }
    # A caller that went away half way through.
    catch { $status = 0 }
    finally {
        try { $context.Response.Close() } catch { }
        [System.IO.File]::AppendAllText($Log, "$($context.Request.HttpMethod) $asked $status`n")
    }
}
