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
  uniffi.toml             C# namespace Heracles.Core, class CoreApi
Cargo.toml, Cargo.lock    workspace with the one member; output in target\
app/
  Heracles/               the WPF project (net10.0-windows10.0.22621.0):
                          App.xaml(.cs) startup, MainWindow, Support/
                          (AppPaths, Log)
  Heracles.Tests/         xunit tests: the crossing into the core
                          (BindingTests), AppPaths, Log
  Generated/, runtimes/   produced by the core build, not in git
  Directory.Build.props   version from VERSION, build number, PathMap
  nuget.config            pins nuget.org
tools/build.ps1           targets core, app, test, run, package, clean
tools/e2e/                make-fixture, launch, capture, ui (UI Automation
                          and synthetic input), adapted from Toolbox
tools/                    (to come) publish.ps1
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
tools\build.ps1                 # core (release DLL + C# bindings), then the app
tools\build.ps1 app -Restart    # rebuild the app, close the running build, start the new one
tools\build.ps1 test            # cargo test --locked, then dotnet test on app\Heracles.Tests
tools\build.ps1 run             # starts it on the real library, not a fixture
tools\build.ps1 core package    # self-contained zip for this machine -> build\release
tools\build.ps1 clean
cargo test --locked             # the core alone: every test binary must say ok
```

Run `core` after ANY Rust API change (bindings and DLL are generated).

To keep a build's output in a file, start the script in a child shell and
let `cmd` redirect:
`cmd /c "powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 test > out.log 2>&1"`.
Redirecting inside PowerShell 5.1 (`*>` or `2>&1`) turns cargo's progress
lines, which go to stderr, into errors, and the script stops on the first.

## End-to-end checks

```powershell
tools\e2e\make-fixture.ps1            # an empty library folder and no log -> build\e2e
tools\e2e\launch.ps1                  # (re)start the built app on the fixture
tools\e2e\launch.ps1 -Exe build\release\Heracles-0.2.0-win-x64\Heracles.exe   # a packaged build instead
tools\e2e\launch.ps1 -DataDir <path>  # another library folder (to see what the app does with a bad one)
tools\e2e\capture.ps1                 # PNG of every Heracles window -> build\e2e\shots
tools\e2e\ui.ps1 tree                 # also: find, invoke, expand, select, setvalue, click, clickat, drag, keys, text
```

`launch.ps1` sets `HERACLES_DATA_DIR` (`build\e2e\data`) and
`HERACLES_LOG_PATH` (`build\e2e\heracles.log`) for the run; the real library
is never touched. It closes a previous instance of the same build first.
`make-fixture.ps1` refuses while a build started from `build\` is running.
Two different builds must not run on the fixture at once: stop the first by
path before launching the second.

How things are checked: assertions go through the log and the automation
tree (`ui.ps1 find -Id LibraryStatus`); screenshots are for looks.

- Log lines: `Heracles <version> (build <n>)` first, then
  `library: opened, <n> notes` or `library: could not be opened: <reason>`.
- Automation ids: `LibraryStatus` (its name is `1 note` or `<n> notes`),
  `StartupError` (its name is the reason; present only when the library did
  not open, and then `LibraryStatus` is absent).
- `invoke`, `setvalue`, `select`, `expand` touch neither mouse nor keyboard.
  `click`, `drag`, `keys`, `text` are real input on the owner's desktop.
- `ui.ps1 find` throws when the element is not there, which ends a chain of
  commands on one line. Put a check that is expected to find nothing last,
  or in a `try`.

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

Core changes made here that the Mac repository can take. Each is portable:
the core's own tests pass with it on Windows, and nothing is behind a
Windows-only switch except where a comment says so.

1. **`file://` picture URLs** (`core/src/transfer.rs`, one line of
   `core/tests/import_export.rs`). `file_url_path` reads the URL properly: a
   host other than this machine is not local (it used to be read as a
   relative path), and on Windows the slash before a drive letter is
   dropped. No change in behaviour for `file:///abs/path` on the Mac. Take
   as is.
2. **The exported task patch is flat** (`core/src/ffi.rs`,
   `core/src/model.rs`, `core/src/store/tasks.rs`). The Rust `TaskPatch`
   and every Rust test are unchanged. What crosses the binding is now
   `TaskPatchRecord`, exported under the name `TaskPatch`: `due_date`,
   `deadline` and `location` are plain optionals, each with a
   `clear_due_date` / `clear_deadline` / `clear_location` flag that wins.
   **The Mac's Swift must change where it builds a `TaskPatch`:**
   `dueDate: .some(x)` becomes `dueDate: x`, and `dueDate: .some(nil)`
   becomes `clearDueDate: true` (the same for `deadline` and `location`).
   Why: C# has no optional of an optional string, so the generated C# for
   the old shape did not compile.
3. `core/uniffi.toml` is new and only read by the C# generator.

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

Run 2026-11-22 on the ARM64 dev machine (Release build, .NET 10, 200 %
scaling, a 1200 by 800 window). Each block was one paragraph of about 200
characters in three runs. "Open" is from the start of building the page to
the first idle moment after it was drawn. Medians of three runs.

| Page | Open | Private memory at open | Frames in a 3 s scroll |
|---|---|---|---|
| 100 rich-text boxes, spelling on | 2.2 s | 167 MB | 325 |
| 500 rich-text boxes, spelling on | 9.9 s | 480 MB | 185 |
| 500 rich-text boxes, spelling off | 1.5 s | 242 MB | 275 |
| 2000 rich-text boxes, spelling on | 43.6 s | 1680 MB | 85 |
| 500 static text blocks | 0.34 s | 86 MB | 278 |
| 2000 static text blocks | 0.99 s | 120 MB | 157 |
| 500 blocks: 40 text boxes, the rest static, spelling off | 0.46 s | 100 MB | 280 |
| 500 blocks: 40 text boxes, the rest static, spelling on | 1.3 s | 128 MB | 259 |
| 2000 blocks: 40 text boxes, the rest static, spelling off | 1.1 s | 139 MB | 159 |
| (an empty window) | 0.13 s | 52 MB | |

**Decision: windowed.** A `RichTextBox` for every block fails the target
(at 500 blocks: open within 0.5 s, 250 MB, 120 frames) by a factor of
twenty with spelling on and three with it off. The editor gives a text box
only to blocks near the viewport and draws the rest as static text.

What the editor-views plan must take from this:

- A `RichTextBox` costs about 3 ms and 0.4 MB to create and lay out. A
  `TextBlock` with the same runs costs about 0.6 ms and 0.07 MB.
- **Spell-checking is most of the cost**: about 17 ms and 0.5 MB more per
  box. Even 40 boxes with spelling on push a 500-block page to 1.3 s. Turn
  spelling on for the block being typed in, not for every box.
- With about two screens of text boxes (40) and the rest static, a
  500-block note opens in under half a second and a 2000-block one in about
  a second. Scrolling stayed smooth in every windowed case.
- **The two kinds of block must measure the same height.** In the spike
  they did not: the same paragraph was 60 px tall as a `RichTextBox` and
  43 px as a `TextBlock`. Unless that is made equal (line height, padding),
  the page jumps when a block changes from one to the other.
- Beyond a few thousand blocks even static text is slow to lay out all at
  once. That is not a problem to solve now.

Not measured: the cost of swapping blocks between the two kinds while
scrolling, and typing latency in a long note. Memory after scrolling the
whole page was two to seven times the figure at open, but that number
includes garbage not yet collected and says little.

### Offsets and marks

Not run yet.

### Colour emoji

Not run yet.

### Scheduled toasts

Not run yet.

## Not verified

- The x64 build has only run under emulation, on the ARM64 dev machine.
  It has never run on x64 hardware.
- Windows 10. Everything so far ran on Windows 11.
- A PC with nothing installed. The packages are self-contained and have
  only been started on the machine that built them.

## Current state (update in every commit)

As of 2026-11-22, on the branch `port`.

- The history was restarted. The first commit is the Mac repository's tree
  at `5dd0252`, unchanged; the second removes what Windows does not use and
  adds these notes, the README, the design and the first plan.
- **The core is ready for Windows.** `cargo test --locked`: 203 tests, all
  passing (the Mac's 195, with the one that failed here fixed, plus 8 new).
  Two changes, both listed under "For the Mac repository": `file://`
  picture URLs, and a task patch that C# can express.
- **There is an app, and it does one thing.** `tools\build.ps1` builds the
  core, generates the C# bindings and builds `Heracles.exe`. The window
  opens the library and says how many notes it holds (`LibraryStatus`), or
  says why it could not be opened (`StartupError`). Paths come from
  `AppPaths` (`HERACLES_DATA_DIR`, `HERACLES_LOG_PATH` override them). The
  log's first line is `Heracles <version> (build <n>)`, then
  `library: opened, <n> notes` or `library: could not be opened: <reason>`.
- Verified: 203 Rust tests and 14 .NET tests pass (`tools\build.ps1 test`).
  The .NET tests go through the bindings: records, optionals, the clear
  flags, bytes, errors by case, the change feed, the feed arriving on the
  thread that wrote, a library folder with spaces and non-ASCII letters, a
  file where the folder should be. The app was started once on a scratch
  library and its log read. A core build started from Git Bash (an emulated
  x64 shell) still put an ARM64 DLL in the ARM64 slot.
- `main` on GitHub still holds the Electron app. It is replaced only when
  the owner says so.
- A bundle of the old history is beside this checkout
  (`..\Heracles-Windows-old-history.bundle`), for the owner to keep or
  delete. The old history must never be pushed again.

- **The harness works** (`tools\e2e`). Checked through it: the window's
  `LibraryStatus` reads `1 note` on a new fixture; pointed at a file where
  the folder should be, the window shows `StartupError` with the reason
  ("file error: Cannot create a file when that file already exists") and
  keeps running; `make-fixture.ps1` refuses while that instance is up.

- **Packages build for both processors.** `tools\build.ps1 core package`
  and `tools\build.ps1 core package -Arch x64` give self-contained zips of
  about 60 MB each and `SHA256SUMS` under `build\release`. The x64 core
  cross-compiles from ARM64 with the installed tools (the bundled SQLite
  included) and needs nothing extra. Each packaged build was started on the
  fixture and its window read (`1 note`), the x64 one under emulation, and
  the headers of `Heracles.exe` and `heracles_core.dll` in each package name
  the right processor. Neither package names the build machine's profile
  folder (the package step checks). After packaging for x64, run
  `tools\build.ps1 core` once more so the bindings match this machine's DLL.
  There is no `publish.ps1` yet: nothing is released until the app does
  something.

- **Spike 1 of 4 is done** (see "Spike findings"): a text box for every
  block is far too slow, so the editor gives text boxes only to blocks near
  the viewport, and spell-checks only the block being typed in.

Next: the other three spikes
(`docs/superpowers/plans/2026-11-22-windows-stage-0-1-repo-skeleton-spikes.md`).
