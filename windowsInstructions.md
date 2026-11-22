# Windows: sign and ship the official build

> Audience: an AI coding agent (or developer) on a **Windows** machine, tasked
> with producing a **code-signed, auto-update-capable** Windows build of Heracles
> and publishing it. macOS signing/notarization is already wired and documented
> in the README; this file is the Windows counterpart.

## What "official" means here
1. The installer (`.exe`) is **Authenticode code-signed** with a trusted
   certificate, so Windows SmartScreen doesn't block it and users see a real
   publisher name.
2. The signed installer + update feed are uploaded to the project's release
   bucket, so existing installs **auto-update** to it.

`electron-updater` on Windows verifies that a downloaded update's signature
matches the **publisher name** of the currently-installed app. So every release
must be signed with a certificate whose subject/publisher name stays consistent.

## Already configured in this repo (do not re-do)
- `package.json` → `build.win.target = ["nsis"]`, `build.nsis` (assisted
  installer, per-user, choose install dir).
- `build.publish` → `generic` provider pointing at the Cloudflare R2 public URL
  (the same feed macOS uses). This bakes `app-update.yml` into the build.
- Script: `npm run release:win` → `npm run build && electron-builder --win nsis --config.directories.output=app/windows`.
- Output lands in `app/windows/` (gitignored).

You only need to **add signing credentials** and **upload the artifacts**.

## Prerequisites
- Node.js LTS + npm, repo cloned, `npm install` run.
- Windows SDK (provides `signtool.exe`) for verification.
- A **code-signing certificate** (see next section). As of 2023 the CA/Browser
  Forum requires OV/EV private keys to live on hardware (USB token) or a cloud
  HSM — you can no longer get a plain downloadable `.pfx` for a *new* OV cert.

## Step 1 — get a code-signing certificate (pick one)

### Option A — Azure Trusted Signing (recommended; cleanest automation)
Microsoft's managed signing service (~US$10/mo). electron-builder has built-in
support. Requires an Azure subscription and a Trusted Signing account + identity
validation. Once set up, add to `package.json` → `build.win`:
```jsonc
"azureSignOptions": {
  "publisherName": "<name on your certificate>",
  "endpoint": "https://<region>.codesigning.azure.net",
  "codeSigningAccountName": "<your-trusted-signing-account>",
  "certificateProfileName": "<your-cert-profile>"
}
```
and provide a service principal via env vars before building:
```powershell
$env:AZURE_TENANT_ID="..."; $env:AZURE_CLIENT_ID="..."; $env:AZURE_CLIENT_SECRET="..."
```

### Option B — USB hardware token (OV/EV from DigiCert, Sectigo, SSL.com, …)
The cert + private key live on a physical token plugged into this PC. Signing is
driven by `signtool` using the token's CSP and the cert's SHA-1 thumbprint. Use a
custom sign hook so electron-builder calls it (interactive PIN may be required):
`package.json` → `build.win.sign = "build/sign-win.cjs"`, with a hook like:
```js
// build/sign-win.cjs
const { execFileSync } = require('child_process');
exports.default = async function (cfg) {
  execFileSync('signtool', [
    'sign', '/sha1', process.env.WIN_CERT_THUMBPRINT,
    '/fd', 'sha256', '/tr', 'http://timestamp.digicert.com', '/td', 'sha256',
    '/d', 'Heracles', cfg.path
  ], { stdio: 'inherit' });
};
```
Set `$env:WIN_CERT_THUMBPRINT` to the token cert's thumbprint. Also set
`build.win.publisherName` to the cert's subject common name.

### Option C — legacy `.pfx` file (only if you already hold one)
electron-builder signs automatically when these env vars are set:
```powershell
$env:CSC_LINK="C:\path\to\cert.pfx"   # or a base64 string of the .pfx
$env:CSC_KEY_PASSWORD="<pfx-password>"
```

## Step 2 — build the signed installer
```powershell
# bump "version" in package.json first (must be > the currently installed version)
npm run release:win
```
Produces in `app/windows/`:
- `Heracles Setup <version>.exe`  (the signed installer)
- `Heracles Setup <version>.exe.blockmap`
- `latest.yml`  (the Windows update feed)

## Step 3 — verify the signature
```powershell
signtool verify /pa /v "app\windows\Heracles Setup <version>.exe"
# or:
powershell Get-AuthenticodeSignature "app\windows\Heracles Setup <version>.exe" | Format-List
```
Status must be **Valid** and the signer name must match the publisher you expect.

## Step 4 — publish to the release bucket (Cloudflare R2)
Upload these to the **bucket root** so they sit next to the macOS feed:
- `latest.yml`
- `Heracles Setup <version>.exe`
- `Heracles Setup <version>.exe.blockmap`

Bucket details:
- **Public download URL** (what clients read): `https://pub-622aa86bc99340e3ac5b01cb86b7d752.r2.dev/`
- **S3-compatible upload endpoint**: `https://b9d2073cb737c168f67215026b0c9b64.r2.cloudflarestorage.com` , bucket name `heracles`

Upload with the bundled script (needs an R2 API token — Access Key ID + Secret,
kept out of the repo):
```powershell
$env:R2_ACCESS_KEY_ID="<r2-access-key-id>"
$env:R2_SECRET_ACCESS_KEY="<r2-secret>"
npm run release:upload
```
It finds the current version's artifacts in `app\windows`, uploads them plus
`latest.yml` to the bucket root (feed last, with `Cache-Control: no-cache` so
clients see the new version promptly), and verifies the public URL. Add
`-- --dry-run` to preview. The AWS CLI (`aws s3 cp … --endpoint-url
https://b9d2073cb737c168f67215026b0c9b64.r2.cloudflarestorage.com`) or
`npx wrangler r2 object put` still work as manual alternatives.

> macOS and Windows share one bucket. `latest-mac.yml` serves Mac clients,
> `latest.yml` serves Windows clients — never overwrite one with the other.

## Release checklist
- [ ] `version` bumped in `package.json` (and committed) — must exceed installed.
- [ ] Signing credentials present in the environment (Option A/B/C).
- [ ] `npm run release:win` succeeded.
- [ ] `signtool verify /pa` reports **Valid** with the expected publisher.
- [ ] `npm run release:upload` succeeded (`latest.yml`, `.exe`, `.exe.blockmap` at the bucket root).
- [ ] `latest.yml` not cached stale.

## Security notes
- Never commit the `.pfx`, token PIN, Azure secret, or R2 API token. Use env vars
  / a secrets store. The `.gitignore` already excludes the `app/` output.
- Keep the **same certificate / publisher name** across releases, or
  `electron-updater` will refuse to apply the update on installed clients.
