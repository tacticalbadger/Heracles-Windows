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

> The bundle is **ad-hoc signed only** — no Developer ID. macOS Gatekeeper
> will warn on first launch (see [Distribution → macOS](#macos) below).

### Producing a `.dmg` installer

```bash
npm run package:mac:dmg
```

Generates `app/macOS/Heracles-0.1.0-arm64.dmg`. Same signing caveat as above.

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

### macOS

The packaged `.app` is unsigned (ad-hoc), so Gatekeeper will block it on
download. Two ways to publish:

1. **Zip it correctly** (preserves the bundle's extended attributes — a normal
   `zip` will corrupt the `.app`):

   ```bash
   ditto -c -k --keepParent \
     app/macOS/mac-arm64/Heracles.app \
     app/macOS/Heracles-0.1.0-mac-arm64.zip
   ```

2. **Instruct users** to clear the quarantine flag after unzipping:

   ```bash
   xattr -dr com.apple.quarantine /Applications/Heracles.app
   ```

   Or right-click → **Open** the first time instead of double-clicking.

For a proper signed + notarized distribution, you'd need:

- An Apple Developer ID Application certificate (`$99/year`)
- `electron-builder` config with `mac.identity` and `notarize: true`
- App-specific password or notarytool credentials in env

### Windows

The NSIS installer (`Heracles Setup 0.1.0.exe`) is also unsigned. On launch,
Windows SmartScreen will show a "Windows protected your PC" warning. Users
must click **More info → Run anyway**.

To sign properly you need an EV or OV code-signing certificate from a CA
(Sectigo, DigiCert, etc.) and the `electron-builder` `win.certificateFile` /
`win.certificatePassword` settings (or `CSC_LINK` / `CSC_KEY_PASSWORD` env
vars).

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
