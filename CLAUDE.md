# Heracles (Windows) — working notes for Claude

Read this first on any machine. It is the handoff between sessions and
machines; memory files do not travel, this file does.
**Update the "Current state" section at the end in every commit** (write it
before committing, so the commit describes itself).

## What this is

Heracles is a local-first, hierarchical notebook: pages in notebooks, a
block editor, split panes and kanban boards, all in one SQLite database.
This repository is its native Windows app: a C# (WPF) shell over the Rust
core `heracles-core`.

It began as an Electron app. The Mac side was rebuilt as a native app
(Swift over the Rust core, repo https://github.com/tacticalbadger/Heracles,
version 0.3.0). On 2026-11-22 the owner decided the Windows side does the
same: no Electron, the same core, feature parity with the Mac app.

**The Mac app is the behavioural reference, at its commit `5dd0252`.** When
the design is silent, do what the Mac app does. Two places to read it:

- The first commit of this repository is that tree, unchanged:
  `git show <first commit>:macos/Sources/HeraclesApp/PaneTree.swift`,
  `git ls-tree -r --name-only <first commit> macos/`. Find the commit with
  `git rev-list --max-parents=0 HEAD`.
- The Mac repository itself, cloned beside this one (`..\Heracles`). Read
  it; never add it as a remote here and never push to it.

The design is `docs/superpowers/specs/2026-11-22-native-windows-app-design.md`.
The plans are in `docs/superpowers/plans/`, one per stage.

**One branch.** Commit when the owner asks. Push when the owner says. (While
the history is being rebuilt the branch is `port`; it becomes `main` when
the owner says to replace `main` on GitHub.)

## Decisions

Owner, 2026-11-22:

- No Electron. A native Windows app with feature parity with the Mac app.
- The Rust core, the same crate the Mac uses.
- The decisions of the Toolbox Windows port stand (`..\Toolbox_Windows`):
  WPF on .NET 10 in C#, bindings from `uniffi-bindgen-cs` v0.12.0 (UniFFI
  0.32), PowerShell build and release scripts, unsigned self-contained zips
  for ARM64 and x64, nothing of the owner's own setup in git.
- The editor is native: one rich-text box per block, the Mac editor's
  design translated. Not a web view.
- The GitHub repository is rebuilt around the new app.
- Commits are authored as `tacticalbadger`.

Claude's calls, open to change:

- An existing Electron library is imported on first start, as the Mac does.
- The library lives in `%APPDATA%\Heracles`, the Electron app's folder.
- The core stays at `core/`, the path it has in the Mac repository, and its
  crate types are left as they are.
- The app is single-instance: a second launch hands over to the first. The
  change feed only works inside one process.
- No `--self-test` or `--snapshot` flags; the `tools\e2e` harness instead.
- A click opens a link, as on the Mac.
- Shortcuts are the Mac's with Ctrl for Command and Alt for Option.
- Windows versions are numbered on their own. The first native one is 0.2.0.
- The exported `TaskPatch` is flat (a value plus a `clear_…` flag) because
  C# cannot express the nested optional the Mac's binding uses.
- Supported from Windows 10 version 2004.
- The application identity is `com.heracles.notes.native`.

Out of scope, because the Mac has not built them either: updating itself,
syntax colouring in code blocks, find within a note, selecting text across
blocks, resizing an image, per-section sort modes and dragging notes in the
sidebar, moving a subtask under another task, HTML export. Also out: code
signing, an installer, an app icon (the Mac has none).

## Layout

```
core/                     heracles-core (Rust, edition 2024, UniFFI 0.32)
  src/doc/                block document model, HTML and Markdown conversion
  src/store/              SQLite schema, notes, boards, search, image store
  src/transfer.rs         import and export of files
  src/legacy.rs           the Electron library importer
  src/changes.rs          the change feed
  src/ffi.rs              what crosses to the interface
  uniffi.toml             C# namespace Heracles.Core, class CoreApi (to come)
Cargo.toml, Cargo.lock    workspace with the one member; output in target\
app/                      (to come)
  Heracles/               the WPF project (net10.0-windows10.0.22621.0)
  Heracles.Tests/         xunit tests
  Generated/, runtimes/   produced by the core build, not in git
tools/                    (to come) build.ps1, publish.ps1, e2e/
docs/                     the design, the plans, the Mac's rewrite notes
VERSION                   marketing version; build number = commit count
```

Swift tree to C# tree (`macos/Sources/HeraclesApp/` to `app/Heracles/`):

| Mac | Windows |
|---|---|
| `HeraclesApp.swift`, `main.swift` | `App.xaml(.cs)`, `SingleInstance.cs` |
| `AppModel*.swift`, `Library.swift`, `LegacyImport.swift` | `Model/` |
| `Workspace.swift`, `PaneTree.swift`, `PaneViews.swift` | `Workspaces/` |
| `RootView.swift`, `Commands.swift` | `MainWindow.xaml(.cs)` |
| `SidebarView.swift`, `SwitcherView.swift` | `Sidebar/` |
| `NoteDetailView.swift`, `EditorCoordinator.swift` | `Notes/` |
| `Editor/*` | `Editor/` (file for file) |
| `BoardView.swift`, `TaskDetailView.swift`, `TaskSupport.swift` | `Boards/` |
| `Navigation.swift` | `Model/Navigation.cs` |
| `SelfTest.swift`, `Snapshot.swift` | not ported; `tools/e2e` instead |

Types and methods keep the Mac's names where C# allows.

## Dev machine and toolchain

Windows 11 on **ARM64** (Rust host `aarch64-pc-windows-msvc`), 200 % display
scaling, the account is not an administrator. Installed: Git, rustup with
stable 1.100 (ARM64 and x64 targets), Visual Studio 2022 with the C++
tools, .NET SDK 10 and 11 (`dotnet` picks 11; the app targets .NET 10),
`uniffi-bindgen-cs` v0.12.0+v0.32.0. Not installed and not needed: Node,
`gh`, PowerShell 7. This core needs neither clang nor CMake (Toolbox's
does).

Things that bite on this machine:

- The harness runs Windows PowerShell 5.1 (`&&` is a parse error) and Git
  Bash. Keep scripts ASCII: 5.1 reads BOM-less files as ANSI. `2>&1` on a
  native command turns stderr lines into error records.
- Git Bash here is an x64 program running under emulation, and so is a
  `powershell.exe` started from it: `$env:PROCESSOR_ARCHITECTURE` says
  AMD64 there while cargo still builds for ARM64. Start the build scripts
  from PowerShell.
- The user's NuGet config has no package sources; a `nuget.config` in the
  repository must supply nuget.org.
- **Never pipe the output of something that leaves a process running**
  unless that process was started through the shell: a child started with
  inherited handles keeps the pipe open and the caller hangs until it exits.
- Bash `cd` and PowerShell `Set-Location` persist in the harness; use
  absolute paths.
- **The owner may be at this PC while a session works.** Test windows come
  to the front. Prefer UI Automation patterns to real keys and clicks, and
  do not write to the clipboard.
- With `core.autocrlf` on (this machine's setting), git warns that LF will
  become CRLF. `.gitattributes` decides: LF everywhere, CRLF for `.ps1`.

## Everyday commands

```powershell
cargo test --locked            # the core: every test binary must say ok
```

The app's commands (`tools\build.ps1`, `tools\e2e\*.ps1`) are added here as
they are written.

## Conventions

- Commit only when the owner asks. Commit messages end with the co-author
  line for the Claude model doing the work.
- Before committing: scan the diff for keys and tokens, make sure no
  temporary probe code is left, run the tests, and update **Current state**.
- Never commit `target/`, `build/`, `app/**/bin|obj`, `app/Generated/` or
  `app/runtimes/`.
- **Nothing of the owner's own setup goes in git**: no real names, account
  emails, hostnames, employer, or paths under the profile folder. Use
  `corp.example`, `jdoe`, `ACME` in docs, tests and fixtures, and write "the
  owner" in these notes. The same goes for what is handed out: builds must
  not carry the build machine's paths.
- Scripts are PowerShell (`.ps1`, CRLF, ASCII). Everything else is LF.
- Never run the app on the owner's real library in a test. `HERACLES_DATA_DIR`
  points it at another folder.
- Never stop every `Heracles` process: filter by the build's path.
- Spike code is throwaway. It lives under `build\spikes\` and is never
  committed. What a spike found is written under "Spike findings" below.
- Core changes are written so the Mac repository can take them unchanged,
  and listed under "For the Mac repository".

## Gotchas carried from the Toolbox port

Each cost real time there. They apply here unchanged.

- **Static field order.** C# initialises static fields in file order. A
  static initialiser that needs another static field must come after it, or
  it silently sees null.
- `uniffi-bindgen-cs` must run from inside the crate folder (it calls
  `cargo metadata`). Generated enums with data are nested records
  (`Change.NotesChanged`), so those names shadow top-level records inside
  the enum type only.
- **Build paths end up in binaries.** Rust puts source paths in panic
  messages and the C# compiler records the path of its debug file. Both
  start with the profile folder, which is the account name. The build
  script must rewrite them (`--remap-path-prefix`, `PathMap`) and the
  package step must refuse a build that still names the profile folder.
- A `ComboBox` loses its selection when its `ItemsSource` is replaced and
  writes null back through a two-way binding.
- A `ListView` in `GridView` mode with an `ItemContainerStyle` based on the
  plain `ListViewItem` style loses its columns under the Fluent theme. Do
  not set one.
- Under the Fluent theme a `MenuItem` shows its check mark only when it is
  `IsCheckable`.
- A drag source must capture the mouse on button-down.
- UI Automation: a `ListBoxItem` in a `Popup` is selectable, not invokable.

Not carried, because nothing here hosts a native child window: the airspace
rules, the Mica backdrop problem, and everything about the RDP control.

## For the Mac repository

Core changes made here that the Mac repository can take. None yet.

## Parity with the Mac app

The checklist. Nothing is built yet.

- [ ] Sidebar: notebooks, nested notes, favorites, archive, trash, search
- [ ] Panes and tabs: splits within splits, draggable dividers, tabs that
      move between panes, Ctrl-click to open beside, layout kept per window
- [ ] Multiple windows on one shared model, kept in step by the change feed
- [ ] The native block editor: paragraph, headings, bullet, numbered and
      task lists, quote, callout, toggle, code block, divider, image, columns
- [ ] Editor behaviour: Markdown as you type, slash menu, block handle,
      drag to move and to make columns, whole-block selection, the editor's
      own undo, marks and links, paste through the core's converter
- [ ] Pictures in the image store; remote pictures fetched on open
- [ ] Kanban boards with drag between columns
- [ ] Task detail: notes, priority, dates, location, labels, reminders,
      nested subtasks, comments
- [ ] Reminders delivered by Windows, whether or not the app is running
- [ ] Import of Markdown and HTML files; export as Markdown
- [ ] The Electron library imported on first start, with a summary
- [ ] Quick Open, the icon picker, the menu bar with a Format menu
- [ ] `heracles://note/<id>` links, with `#block=<id>`
- [ ] Nothing lost on quit: closing and quitting wait for every editor
- [ ] Packages for ARM64 and x64, and a release script

## Spike findings

Filled in by the first plan's spikes. The later plans read these.

### Many rich-text boxes

Not run yet.

### Offsets and marks

Not run yet.

### Colour emoji

Not run yet.

### Scheduled toasts

Not run yet.

## Not verified

Nothing is listed yet because nothing has been built.

## Current state (update in every commit)

As of 2026-11-22, on the branch `port`.

- The history was restarted. The first commit is the Mac repository's tree
  at `5dd0252`, unchanged; this commit removes what Windows does not use and
  adds these notes, the README, the design and the first plan.
- **Nothing builds an app yet.** The core builds and 194 of its 195 tests
  pass on Windows; the one that fails is a `file://` path the core reads the
  Unix way, and is the next thing fixed.
- `main` on GitHub still holds the Electron app. It is replaced only when
  the owner says so.
- A bundle of the old history is beside this checkout
  (`..\Heracles-Windows-old-history.bundle`), for the owner to keep or
  delete. The old history must never be pushed again.

Next: the core's Windows fixes, then the build script and an empty window
(`docs/superpowers/plans/2026-11-22-windows-stage-0-1-repo-skeleton-spikes.md`).
