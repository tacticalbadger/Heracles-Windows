# Heracles for Windows, Stage 7: Package and Release — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Everything needed to put 0.2.0 out is in place and checked, so that publishing it is one command the owner runs when he says so.

**Architecture:** Nothing new in the app. A release script taken from the Toolbox port (`..\Toolbox_Windows\tools\publish.ps1`), release notes, a README that describes the app as it is, and both packages built from the final commit and started.

**Tech Stack:** PowerShell 5.1, the GitHub REST API (through the credential Git already holds), `tools\build.ps1 core package`.

**Spec:** `docs/superpowers/specs/2026-11-22-native-windows-app-design.md` ("Build, package and release"; stage 7 of "Order of work": both architectures, release notes, a parity pass against the Mac README).

## Global Constraints

- **Nothing is published and nothing is pushed by this plan.** The release goes out when the owner asks: `tools\publish.ps1` is his to run. The plan ends with the packages built, the script's dry run read, and the notes saying what is left to him.
- Packages are self-contained zips for `win-arm64` and `win-x64` with `SHA256SUMS`, built from the final commit (the build number is the commit count, and the release script refuses packages built from another).
- A package must not name the build machine's profile folder (`tools\build.ps1 package` already refuses one that does).
- Release notes and the README carry no names, hostnames or account details.
- The first native Windows version is 0.2.0.
- Commit locally, as `tacticalbadger`, message ending `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Update `CLAUDE.md` in every commit.

## Review Focus

1. **A release published without its files** (GitHub can make releases immutable). Expected: the script makes a draft, attaches the files, and publishes last. Read in the script; not run.
2. **Packages from an older commit.** Expected: the script refuses them. Dry run.
3. **A package started on a PC with nothing installed.** Not possible here: under "Not verified".
4. **The x64 package on x64 hardware.** Not possible here: it runs under emulation. Under "Not verified".
5. **The README promising what has not been tried.** Expected: it says nobody has used the app day to day, and points at what is not verified.

---

### Task 1: The release script, the notes and the README

**Files:** Create `tools/publish.ps1`, `docs/release-notes/0.2.0.md`; Modify `README.md`.

- [ ] **Step 1:** `tools\publish.ps1`, from the Toolbox port's, renamed: `Heracles-<version>-win-<arch>.zip`, `Heracles.dll` for the build number, this app's install text. `-DryRun` reports every check that would stop a real run and goes on, so all are seen at once.
- [ ] **Step 2:** `docs\release-notes\0.2.0.md`: what this version is, for someone downloading it.
- [ ] **Step 3:** `README.md`: status and download, features (against the Mac README, line for line), keys, building, the scripted checks, where data is kept and what the first start does.
- [ ] **Step 4:** Commit: `A release script, release notes, and a README for the app as it is`.

### Task 2: Build both packages, start them, and read the dry run

- [ ] **Step 1:** From the final commit: `tools\build.ps1 clean`, `tools\build.ps1`, `tools\build.ps1 test`, every `tools\e2e\check-*.ps1`; expected: all pass, no warnings.
- [ ] **Step 2:** `tools\build.ps1 core package -Arch x64`, then `tools\build.ps1 core package` (this machine's last, so the bindings match its DLL). Expected: two zips and `SHA256SUMS` under `build\release`, and no refusal for a profile folder's name.
- [ ] **Step 3:** `tools\e2e\check-shell.ps1 -Exe build\release\Heracles-0.2.0-win-arm64\Heracles.exe`, and the same for `win-x64` (under emulation). Expected: 30 of 30 each. `check-boards.ps1 -Exe` for the ARM64 package.
- [ ] **Step 4:** `tools\publish.ps1 -DryRun`. Expected: the files and notes it would publish, and one thing it would refuse: the commit is not on `origin/main`.
- [ ] **Step 5:** Update `CLAUDE.md`: the parity checklist against the Mac README, Not verified, Current state, and **what the owner does to release** (push, then `tools\publish.ps1`). Commit: `Stage 7: packages for both processors, checked; ready to release`.
