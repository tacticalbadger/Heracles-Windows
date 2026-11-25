<#
.SYNOPSIS
    The update key and what is done with it: dot-source this file.

.DESCRIPTION
        . tools\update-signing.ps1
        $key = New-UpdateKey                       # .Private and .Public, both base64
        New-UpdateFeed -Private $key.Private -Manifest $manifest -Path feed.json
        $manifest = Read-UpdateFeed -Path feed.json -Public $key.Public
        Get-StoredUpdateKey -Name heracles-windows-update-key

    Every installed copy of Heracles takes an update only if the feed that
    announces it carries a signature made with the update key. The key is an
    ECDSA key on the curve P-256, the signature is over the SHA-256 of the
    bytes, and both are computed by Windows itself: nothing here, and nothing
    in the app, is a library of anybody's.

    The public half is 64 bytes, the point's two coordinates, written in
    base64. It goes into tools\update-config.json and from there beside the
    program in every package. The private half is those 64 bytes and 32
    more, in base64. It lives in the Windows credential store of the account
    that makes releases (Control Panel > Credential Manager > Windows
    Credentials, under the name in update-config.json) and nowhere in the
    repository. tools\update-key.ps1 makes it, shows its public half, and
    writes it to a file to be kept somewhere safe.

    A feed is one small JSON file:

        {"format":1,"manifest":"<base64>","signature":"<base64>"}

    The manifest is JSON too, kept as the exact bytes that were signed. It
    names the version, the build number, the release notes, and for each
    processor the archive's address, length and SHA-256, so the one
    signature covers the archives as well.

    Nothing here prints a private key.
#>

Set-StrictMode -Version 2

$script:UpdateCurve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
$script:UpdateHash = [System.Security.Cryptography.HashAlgorithmName]::SHA256

# A key, from the bytes it is kept as: 64 for a public half, 96 with the
# private half after them.
function ConvertTo-UpdateKey([string]$text) {
    $raw = [Convert]::FromBase64String($text)
    if ($raw.Length -ne 64 -and $raw.Length -ne 96) { throw 'that is not an update key: it should be 64 or 96 bytes in base64' }
    $parameters = New-Object System.Security.Cryptography.ECParameters
    $parameters.Curve = $script:UpdateCurve
    $point = New-Object System.Security.Cryptography.ECPoint
    $point.X = [byte[]]$raw[0..31]
    $point.Y = [byte[]]$raw[32..63]
    $parameters.Q = $point
    if ($raw.Length -eq 96) { $parameters.D = [byte[]]$raw[64..95] }
    return [System.Security.Cryptography.ECDsa]::Create($parameters)
}

# A new key. Nothing is stored: the caller decides what becomes of it.
function New-UpdateKey {
    $key = [System.Security.Cryptography.ECDsa]::Create($script:UpdateCurve)
    try {
        $p = $key.ExportParameters($true)
        return @{
            Private = [Convert]::ToBase64String([byte[]]($p.Q.X + $p.Q.Y + $p.D))
            Public  = [Convert]::ToBase64String([byte[]]($p.Q.X + $p.Q.Y))
        }
    }
    finally { $key.Dispose() }
}

# The public half of a private key.
function Get-UpdatePublicKey([Parameter(Mandatory = $true)][string]$Private) {
    $raw = [Convert]::FromBase64String($Private)
    if ($raw.Length -ne 96) { throw 'that is not a private update key: it should be 96 bytes in base64' }
    return [Convert]::ToBase64String([byte[]]$raw[0..63])
}

function Get-UpdateSignature {
    param([Parameter(Mandatory = $true)][string]$Private, [Parameter(Mandatory = $true)][byte[]]$Bytes)
    if ([Convert]::FromBase64String($Private).Length -ne 96) { throw 'signing needs the private half of the update key' }
    $key = ConvertTo-UpdateKey $Private
    try { return [Convert]::ToBase64String($key.SignData($Bytes, $script:UpdateHash)) }
    finally { $key.Dispose() }
}

# Whether a signature is this key's signature of exactly these bytes: the
# check every installed copy makes.
function Test-UpdateSignature {
    param([Parameter(Mandatory = $true)][string]$Public, [Parameter(Mandatory = $true)][string]$Signature, [Parameter(Mandatory = $true)][byte[]]$Bytes)
    try {
        $signed = [Convert]::FromBase64String($Signature)
        $key = ConvertTo-UpdateKey ([Convert]::ToBase64String([byte[]]([Convert]::FromBase64String($Public))[0..63]))
        try { return $key.VerifyData($Bytes, $signed, $script:UpdateHash) }
        finally { $key.Dispose() }
    }
    catch { return $false }
}

# ----- The feed -----

# Write a feed: what it says (a hashtable or an ordered dictionary), and the
# key's signature of exactly the bytes it is said in. The file is read back
# and checked against the key before this returns.
function New-UpdateFeed {
    param(
        [Parameter(Mandatory = $true)][string]$Private,
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $said = [System.Text.Encoding]::UTF8.GetBytes(($Manifest | ConvertTo-Json -Depth 6))
    $feed = [ordered]@{
        format    = 1
        manifest  = [Convert]::ToBase64String($said)
        signature = Get-UpdateSignature -Private $Private -Bytes $said
    }
    [System.IO.File]::WriteAllText($Path, (($feed | ConvertTo-Json -Compress) + "`n"), [System.Text.Encoding]::ASCII)
    Read-UpdateFeed -Path $Path -Public (Get-UpdatePublicKey $Private) | Out-Null
}

# What a feed says, if the key signed it. Anything else is an error that
# says which: not a feed, or not signed with this key.
function Read-UpdateFeed {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Public)
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    try { $feed = $text | ConvertFrom-Json }
    catch { throw "$Path is not a feed: it is not JSON" }
    foreach ($name in 'format', 'manifest', 'signature') {
        if (-not $feed.PSObject.Properties[$name]) { throw "$Path is not a feed: it has no $name" }
    }
    $said = [Convert]::FromBase64String($feed.manifest)
    if (-not (Test-UpdateSignature -Public $Public -Signature $feed.signature -Bytes $said)) {
        throw "$Path is not signed with the update key $($Public.Substring(0, 12))..."
    }
    if ($feed.format -ne 1) { throw "$Path is a feed of form $($feed.format), which these scripts do not write" }
    return ([System.Text.Encoding]::UTF8.GetString($said) | ConvertFrom-Json)
}

# ----- The credential store -----

if (-not ('HeraclesUpdateKeyStore' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

// The current user's own credentials, as Credential Manager shows them
// under "Windows Credentials". A generic credential, kept on this PC only.
public static class HeraclesUpdateKeyStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    private const int Generic = 1;
    private const int LocalMachine = 2;
    private const int NotFound = 1168;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static void Write(string target, byte[] secret, string comment)
    {
        var blob = Marshal.AllocHGlobal(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var credential = new CREDENTIAL
            {
                Type = Generic, TargetName = target, Comment = comment, UserName = target,
                CredentialBlobSize = secret.Length, CredentialBlob = blob, Persist = LocalMachine,
            };
            if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(blob); }
    }

    public static byte[] Read(string target)
    {
        IntPtr pointer;
        if (!CredReadW(target, Generic, 0, out pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error);
        }
        try
        {
            var credential = (CREDENTIAL)Marshal.PtrToStructure(pointer, typeof(CREDENTIAL));
            var secret = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
            return secret;
        }
        finally { CredFree(pointer); }
    }

    public static bool Delete(string target)
    {
        return CredDeleteW(target, Generic, 0);
    }
}
'@
}

# The private key kept under a name in the credential store, in base64, or
# $null when there is none.
function Get-StoredUpdateKey([Parameter(Mandatory = $true)][string]$Name) {
    $secret = [HeraclesUpdateKeyStore]::Read($Name)
    if ($null -eq $secret) { return $null }
    if ($secret.Length -ne 96) { throw "what is kept as '$Name' in the credential store is not an update key ($($secret.Length) bytes)" }
    return [Convert]::ToBase64String($secret)
}

function Set-StoredUpdateKey {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Private)
    $secret = [Convert]::FromBase64String($Private)
    if ($secret.Length -ne 96) { throw 'that is not a private update key: it should be 96 bytes in base64' }
    [HeraclesUpdateKeyStore]::Write($Name, $secret, 'Signs the update feed of Heracles for Windows. Keep a copy somewhere safe: without it no installed copy can be updated.')
}

function Remove-StoredUpdateKey([Parameter(Mandatory = $true)][string]$Name) {
    [HeraclesUpdateKeyStore]::Delete($Name) | Out-Null
}

# ----- Where updates come from -----

# tools\update-config.json: the repository, the feed's address, the name the
# private key is kept under, and the key's public half.
function Get-UpdateConfig([Parameter(Mandatory = $true)][string]$Root) {
    $path = Join-Path $Root 'tools\update-config.json'
    $config = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($name in 'repository', 'feed', 'keyName', 'key') {
        if (-not $config.PSObject.Properties[$name]) { throw "tools\update-config.json has no $name" }
    }
    return $config
}

# The build number of the feed that installed copies read now, or $null when
# no feed is published yet. An error when it cannot be told: a feed that
# cannot be fetched, or one this key did not sign.
function Get-PublishedUpdateBuild($Config) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $file = [System.IO.Path]::GetTempFileName()
    try {
        try { Invoke-WebRequest -Uri $Config.feed -OutFile $file -UseBasicParsing -TimeoutSec 60 -Headers @{ 'Cache-Control' = 'no-cache' } | Out-Null }
        catch {
            $response = $_.Exception.Response
            if ($null -ne $response -and [int]$response.StatusCode -eq 404) { return $null }
            throw "the published feed could not be read, so nothing is known about what is out ($($Config.feed)): $($_.Exception.Message)"
        }
        $published = Read-UpdateFeed -Path $file -Public $Config.key
        return [int]$published.build
    }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}

# ----- The archive -----

# A folder as a zip, its files at the top: what a copy that updates itself
# downloads. Read back before this returns: every file that went in must
# come out the same, under the same name, and nothing else.
function New-UpdateArchive {
    param([Parameter(Mandatory = $true)][string]$Folder, [Parameter(Mandatory = $true)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $Path) { Remove-Item -Force $Path }
    $Folder = (Resolve-Path $Folder).Path.TrimEnd('\')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($Folder, $Path, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $expected = @{}
    foreach ($file in Get-ChildItem $Folder -Recurse -File -Force) {
        $expected[$file.FullName.Substring($Folder.Length + 1).Replace('\', '/')] = $file.FullName
    }
    $different = @()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            # A folder's own entry: nothing to compare.
            if ($entry.FullName.EndsWith('/')) { continue }
            $seen[$entry.FullName] = $true
            if (-not $expected.ContainsKey($entry.FullName)) { $different += "in the archive and not put there: $($entry.FullName)"; continue }
            $stream = $entry.Open()
            try { $packed = [BitConverter]::ToString($sha.ComputeHash($stream)) }
            finally { $stream.Dispose() }
            $source = [System.IO.File]::OpenRead($expected[$entry.FullName])
            try { $original = [BitConverter]::ToString($sha.ComputeHash($source)) }
            finally { $source.Dispose() }
            if ($packed -ne $original) { $different += "not as it was put in: $($entry.FullName)" }
        }
        foreach ($name in $expected.Keys) { if (-not $seen.ContainsKey($name)) { $different += "not in the archive: $name" } }
    }
    finally {
        $zip.Dispose()
        $sha.Dispose()
    }
    if ($different.Count -gt 0) {
        Remove-Item -Force $Path
        throw "the archive does not hold what was put in it: $(($different | Select-Object -First 5) -join '; ')"
    }
}
