# Building Heracles

This document covers how to compile Heracles into a distributable application
for **macOS** and **Windows**. For day-to-day development setup see
[`README.md`](./README.md).

---

## Prerequisites

| Tool        | Version   | Notes |
|-------------|-----------|-------|
| Node.js     | 20.x LTS  | The native module `better-sqlite3` needs to match the Electron ABI; Node 20 is what `electron@41` was tested with. |
| npm         | 10.x      | Ships with Node 20. |
| Git         | any       | For cloning. |
| Python      | 3.x       | Required by `node-gyp` for native module rebuilds. macOS Xcode CLT and Windows Build Tools provide this. |

Install dependencies once:

```bash
git clone https://github.com/mattdobbs/Heracles.git
cd Heracles
npm install
```

`npm install` runs `electron-builder install-app-deps` indirectly, which
rebuilds `better-sqlite3` against the Electron ABI for your current platform.

---

## Building for macOS

> Build **on a Mac**. Cross-compiling to macOS from another OS is not
> supported.

### Quick path — unpacked `.app` bundle

```bash
npm run package:mac
```

What this does:

1. Runs `vite build` to produce `dist/renderer/`.
2. Runs `electron-builder --mac --dir` which copies the Electron binary,
   stitches in `electron/`, `dist/renderer/`, and `node_modules/`, and produces
   a `.app` directory at `app/macOS/mac-arm64/Heracles.app` (or `mac/` for
   Intel).

> `npm run package:mac` (`--dir`) is a **quick local build, ad-hoc signed only** —
> no Developer ID, not notarized. For a distributable build use
> `npm run release:mac` (see [Distribution → macOS](#macos)).

### Producing a `.dmg` installer

```bash
npm run package:mac:dmg
```

Generates `app/macOS/Heracles-0.1.0-arm64.dmg` (ad-hoc). The **signed + notarized**
`.dmg` (and `.zip`) come from `npm run release:mac`.

### Targeting Intel + Apple Silicon

By default, `electron-builder` builds for the host architecture. To build
universal or Intel-only on an Apple Silicon Mac:

```bash
# Intel only
npx electron-builder --mac --dir --x64

# Universal (both architectures, ~2× size)
npx electron-builder --mac --dir --universal
```

### Native module note

`better-sqlite3` is recompiled for the bundled Electron version via
`@electron/rebuild`, which `electron-builder` runs automatically inside the
`--mac` step. If you see runtime errors about NODE_MODULE_VERSION mismatches,
run:

```bash
npx electron-builder install-app-deps
```

---

## Building for Windows

There are two viable approaches.

### Approach A — build on Windows (recommended)

This is the only path that's fully reliable, because `better-sqlite3` must be
compiled against the Windows Electron ABI and Microsoft's C++ toolchain.

**Setup once:**

1. Install **Node.js 20 LTS** from <https://nodejs.org>. During the installer,
   tick the **"Automatically install the necessary tools"** checkbox — this
   pulls in Python + Visual Studio Build Tools via `windows-build-tools`.
   (If you skipped that, run `npm install --global --production windows-build-tools`
   in an elevated PowerShell.)
2. Install **Git for Windows** from <https://git-scm.com>.

**Build:**

```powershell
git clone https://github.com/mattdobbs/Heracles.git
cd Heracles
npm install
npm run package:win
```

Output:

```
app\windows\Heracles Setup 0.1.0.exe         # NSIS installer
app\windows\win-unpacked\Heracles.exe        # Unpacked binary (debug / portable)
```

For an unpacked folder without the installer wrapper:

```powershell
npm run package:win:dir
# → app\windows\win-unpacked\
```

### Approach B — cross-compile from macOS or Linux via Docker

`electron-builder` supports cross-platform builds, but native modules like
`better-sqlite3` need Windows binaries. The most reliable container is
`electronuserland/builder:wine`:

```bash
docker run --rm -ti \
  --env ELECTRON_CACHE="/root/.cache/electron" \
  --env ELECTRON_BUILDER_CACHE="/root/.cache/electron-builder" \
  -v ${PWD}:/project \
  -v ${PWD##*/}-node-modules:/project/node_modules \
  -v ~/.cache/electron:/root/.cache/electron \
  -v ~/.cache/electron-builder:/root/.cache/electron-builder \
  electronuserland/builder:wine \
  /bin/bash -c "npm install && npm run package:win"
```

Notes:

- The container ships with Wine + the Windows build prerequisites needed to
  rebuild `better-sqlite3` for the win32 target.
- Build is noticeably slower than a native Windows build (~3–5×).
- The installer it produces is functionally identical to the native one but
  unsigned.

### Approach C — CI / GitHub Actions

For repeatable signed builds on both platforms, use GitHub Actions with a
matrix on `macos-latest` and `windows-latest` runners and upload the artifacts
to a GitHub Release. Out of scope for this doc, but `electron-builder`'s
[GitHub Actions guide](https://www.electron.build/multi-platform-build) is the
canonical reference.

---

## Distribution

Distributable builds are **signed and published to a Cloudflare R2 bucket**, and
the app **auto-updates** from it via `electron-updater`. See the README's
[Releases & auto-update](./README.md#releases--auto-update) section for the
end-to-end flow: bump version → build → `npm run release:upload`.

`release:upload` (`scripts/upload-release.cjs`) discovers the current version's
artifacts in `app/macOS` or `app/windows`, uploads them plus the update feed to
the bucket root over R2's S3 API (feeds last, so clients never see a version
whose artifacts aren't up yet; feeds get `Cache-Control: no-cache`), and
verifies the public URL responds. It needs an R2 API token in
`R2_ACCESS_KEY_ID` / `R2_SECRET_ACCESS_KEY` (env vars or a git-ignored
`.env.r2` at the repo root); `-- --dry-run` previews without uploading.

### macOS

`npm run release:mac` produces a **Developer ID-signed, notarized, and stapled**
`.zip` and `.dmg`. It requires the signing identity in your keychain and notary
credentials in the environment:

```bash
export APPLE_API_KEY="$HOME/.appstoreconnect/private_keys/AuthKey_XXXXXXXXXX.p8"
export APPLE_API_KEY_ID="XXXXXXXXXX"
export APPLE_API_ISSUER="<issuer-uuid>"
npm run release:mac
```

Config lives in `package.json` → `build.mac` (`identity`, `hardenedRuntime`,
`entitlements` → `build/entitlements.mac.plist`, `notarize`). An
`afterAllArtifactBuild` hook (`build/notarize-dmg.cjs`) additionally notarizes +
staples the DMG. Verify with `spctl -a -t install Heracles.app` (→ *accepted,
Notarized Developer ID*) and `xcrun stapler validate`.

> For a quick local run you don't need any of this — `npm run package:mac`
> produces an ad-hoc build; clear quarantine with
> `xattr -dr com.apple.quarantine /Applications/Heracles.app` or right-click →
> **Open**.

### Windows

`npm run release:win` produces the NSIS installer + `latest.yml`. For an
**official, SmartScreen-clean, auto-update-capable** Windows build you must
code-sign it — full instructions (certificate options, electron-builder wiring,
verification, and uploading to the bucket) are in
[`windowsInstructions.md`](./windowsInstructions.md).

---

## Troubleshooting

### `Error: The module 'better-sqlite3' was compiled against a different Node.js version`

The native binding was built for plain Node, not Electron's bundled Node.
Re-run:

```bash
npx electron-builder install-app-deps
```

### Build fails with `gyp ERR! find Python`

Install Python 3 and either re-run `npm install` or set:

```bash
npm config set python "$(which python3)"
```

### Vite warns about chunks larger than 500 kB

Cosmetic only — the editor pulls in TipTap + CodeMirror + ProseMirror, which
naturally exceeds the default chunk-size warning. Suppress with
`build.chunkSizeWarningLimit` in `vite.config.ts` if it bothers you.

### macOS bundle won't launch when copied to another Mac

Quarantine flag — see [Distribution → macOS](#macos).

### Windows installer immediately closes

Usually a missing VC++ runtime on the target machine. `electron-builder`'s
NSIS template bundles the runtime, but if you used `--win dir`, the user
needs to install VC++ Redistributable themselves.

---

## Reference: script summary

| Script                      | Output                                                |
|-----------------------------|-------------------------------------------------------|
| `npm run dev`               | Dev server on `:5173` + Electron with HMR             |
| `npm run build`             | `dist/renderer/` (renderer bundle only)               |
| `npm run package:mac`       | `app/macOS/mac-arm64/Heracles.app` (unpacked)         |
| `npm run package:mac:dmg`   | `app/macOS/Heracles-<version>-arm64.dmg`              |
| `npm run package:win`       | `app\windows\Heracles Setup <version>.exe` (NSIS)     |
| `npm run package:win:dir`   | `app\windows\win-unpacked\`                           |
| `npm run preview`           | Preview built renderer in a browser (no Electron)     |

All packaged outputs land under `app/` and are gitignored.
