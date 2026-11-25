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
version 0.4.0). On 2026-11-22 the owner decided the Windows side does the
same: no Electron, the same core, feature parity with the Mac app.

**The Mac app is the behavioural reference, at its commit `91076bf`**
(2026-11-24: its 0.4.0, and the original app removed from its repository).
The app was first built against `5dd0252`; on 2026-11-25 the sixteen
commits after that were read and brought across (see "Since stage 7" in
"Current state"). When the design is silent, do what the Mac app does. Two
places to read it:

- The Mac repository itself, cloned beside this one (`..\Heracles`). Read
  it; never add it as a remote here and never push to it. **Its working
  tree may be behind**: `git fetch origin` there, then read from
  `origin/main` (`git show origin/main:macos/Sources/HeraclesApp/Updates.swift`,
  `git log --oneline 91076bf..origin/main` for what is new since). Fetching
  changes nothing of the owner's; do not pull or check out in his clone.
- The first commit of this repository is the Mac's tree at `5dd0252`,
  unchanged, for what the app was first built from:
  `git show <first commit>:macos/Sources/HeraclesApp/PaneTree.swift`. Find
  the commit with `git rev-list --max-parents=0 HEAD`. What the Mac gained
  after that is only in its own repository.

The design is `docs/superpowers/specs/2026-11-22-native-windows-app-design.md`.
The plans are in `docs/superpowers/plans/`, one per stage.

**One branch: `main`.** Commit when the owner asks. Push when the owner
says.

## Decisions

Owner, 2026-11-22:

- No Electron. A native Windows app with feature parity with the Mac app.
- The Rust core, the same crate the Mac uses.
- The decisions of the Toolbox Windows port stand (`..\Toolbox_Windows`):
  WPF on .NET 10 in C#, bindings from `uniffi-bindgen-cs` v0.12.0 (UniFFI
  0.32), PowerShell build and release scripts, unsigned self-contained zips
  for ARM64 and x64 (disc images here, from 0.2.1: see below), nothing of
  the owner's own setup in git.
- The editor is native: one rich-text box per block, the Mac editor's
  design translated. Not a web view.
- The GitHub repository is rebuilt around the new app.
- Commits are authored as `tacticalbadger`.

Owner, 2026-11-23:

- A release is handed out as a disc image (an ISO), from 0.2.1 on. 0.2.0
  was two zips.

Owner, 2026-11-25:

- The Windows app is brought to parity with the Mac app as it now stands
  (its 0.4.0), tested, and released. What the Mac had gained: an app icon,
  updating itself, "the original app" where it said "Electron", and a
  proprietary licence.
- **The licence is the Mac's**: proprietary, all rights reserved, the
  source published to be read. He was asked, and answered "Match the
  Mac". **It had already been done**: his decision of 2026-11-23 (see
  Conventions) was carried out that day by a commit made on the Mac
  (`4946a75`), which the session that asked had not fetched. The two
  answers agree, and `LICENSE` is as that commit left it. 0.2.0 and 0.2.1
  went out under MIT and stay so for whoever has them; 0.3.0 is the first
  release under the new terms.

Owner, 2026-11-25, later the same day (said in a Toolbox session, of both
apps: "both should be packaged installers as well. go ahead and build
releases for heracles and toolbox as msi or exe installers. install the
dependencies now"):

- **A release also carries an installer for each processor**, from 0.3.1
  on. This takes "an installer" off the out-of-scope list below. What
  Claude chose within that, open to change:
  - **An EXE made with Inno Setup 7, not an MSI.** The app replaces its
    own folder when it updates, and Windows Installer repairs or resents
    files it did not put there. The installer is per user
    (`PrivilegesRequired=lowest`, into `%LOCALAPPDATA%\Programs\Heracles`),
    so the app can change the place it lives in and nobody needs an
    administrator.
  - **The uninstaller is kept outside the app's folder**
    (`%LOCALAPPDATA%\Heracles\uninstall`), because an update replaces
    that folder whole and would take the uninstaller with it. For the
    same reason the installer treats the app's folder as the app's alone,
    as the updater does: it refuses a folder that holds other things,
    empties one that holds Heracles before installing, and the
    uninstaller removes the folder whole. It leaves the library
    (`%APPDATA%\Heracles`) alone.
  - **The disc image now holds the installer too**, beside the `Heracles`
    folder and `README.txt`, so a disc is enough either way.
  - **The archive (`.zip`) and the feed are as they were.** Nothing about
    updates changed, and a copy of 0.3.0 takes 0.3.1 as any other update.
  - **The app keeps the version under "Installed apps" right**
    (`Support/InstalledCopy.cs`): the installer writes the version it
    installed, and the app, which then updates with no installer
    involved, writes its own at start into the entry of the folder it
    runs from. Unlike links and reminders this is done on a test library
    too: it claims nothing, and can only touch the entry of the very
    folder the copy is in.
  - Inno Setup is free to use; its authors ask commercial users to buy a
    licence (https://jrsoftware.org/isorder.php), which is not required.
    Whether this use calls for one is the owner's to decide.

Claude's calls about updates, open to change, **each easy to change before
a version is out and hard after**, since every copy that is out goes on
doing what it was built to do:

- **The updater is the app's own code**, where the Mac uses Sparkle. Nothing
  like Sparkle fits an app with no installer: WinSparkle and NetSparkle run
  an installer and show notes in a web view, and Velopack wants its own
  packaging and has no signature of the developer's. What it has to do is
  small (read a feed, check a signature, fetch a zip, swap a folder), and
  the Mac's design names what must hold: an update is taken only if the
  owner signed it; a release cannot go out in a state that silently stops
  updates; a test run never finds the real release; no web view.
- **The feed is `update-feed.json` on every release**, read from
  `https://github.com/tacticalbadger/Heracles-Windows/releases/latest/download/update-feed.json`,
  which always serves the newest full release's copy. No server. Its form
  is in `Updates/UpdateFeed.cs`: an envelope holding a manifest (version,
  build number, notes, and for each processor the archive's address,
  length and SHA-256) as the exact bytes that were signed, and the
  signature. One signature therefore covers the archives too.
- **The signature is ECDSA on P-256 over SHA-256**, not Sparkle's Ed25519:
  it is what Windows itself computes, in the app (.NET) and in the release
  scripts (Windows PowerShell) alike, so neither needs a library.
- **Heracles for Windows has an update key of its own**, not the Mac's
  (which is in the Mac's keychain, and of another kind). The private half
  is in the Windows credential store of the account that makes releases,
  as `heracles-windows-update-key`; the public half is in
  `tools\update-config.json`. `tools\update-key.ps1` makes it, shows it,
  and writes it out to be kept. **Lose it and no copy can be updated
  again**: there is no provision for a lost key (Sparkle has one; nothing
  here does), so the way back is a build everyone puts in by hand.
- **The feed's address and the key are in a file beside the program**,
  `Heracles.update.json`, as they are in the Mac's Info.plist. Only the
  package step writes it, so a build made any other way never looks for
  updates.
- **The app's folder is replaced by the new version itself.** On quit the
  app starts the new version from where it was unpacked, with
  `--install-update`; that copy waits for the app to be gone, copies
  itself beside the app's folder, and swaps the two folders with two
  renames (`Updates/UpdateInstaller.cs`). No helper program, nothing
  running from a folder that is moved, and the long work done before
  anything is changed. Folders beside the app named `<name>.update-new`
  and `<name>.update-old` are the updater's while they carry its marker
  file; one without the marker is somebody's and is never touched.
- **The copy that installs does not take its command line at its word.**
  Anything that can start the app can write its arguments (Windows starts
  it with a link as its argument, and a link can be made to say
  anything), and `--install-update --target <folder>` names a folder to
  replace. So only a copy running from the updater's own unpacking folder
  (`<updates>\staged`, where nothing arrives but what was checked against
  the signed feed) installs anything, and only over a folder that holds
  `Heracles.exe`. The app as it is installed, started with those
  arguments, does nothing. Found by reading the code back for misuse
  before the release, not by a test; each guard then got a test that
  fails without it.
- The build number decides what is newer, as on the Mac, and it is the
  commit count, so it goes up by itself. It must stay below 65536: it is
  the last part of a Windows file version.
- A look sends the app's version and processor (the request's
  `User-Agent`) and nothing else.
- The menu items are in Help (the Mac has them in its app menu, which
  Windows has not). The daily look is on by default, three seconds after
  the first window and then once a day, and asks before anything is
  fetched.
- What the app remembers about updates is in
  `%LOCALAPPDATA%\Heracles\updates` (beside the log: it is about this copy,
  not about the library). A run on a test library keeps its own, inside
  that library's folder.
- Updates came in with 0.3.0. (0.3.1 is the same app with an installer.)

Claude's other calls of 2026-11-25:

- The icon is drawn from the Mac's own layers (`app\Heracles\Icon\*.svg`
  and `icon.json`, copied from `macos/Icon/Heracles.icon`) by
  `tools\make-icon.ps1`, with WPF, since Icon Composer is the Mac's. Windows
  draws an icon one way whatever the theme, so only the light style is
  used. Below 40 pixels the page is drawn larger and without its shadow.
- Reminders carry the icon: the identity's `IconUri` names `Heracles.png`
  beside the program.
- `docs/review-and-native-rewrite.md` is left as it was. The Mac has deleted
  its copy; here the owner's word of 2026-11-22 was to leave it, and the
  question put to him on 2026-11-25 was about the licence only.
- The app, its scripts and the README say "the original app" where they
  said "Electron", as the Mac's now do, and the core's copy of an imported
  library is `heracles-original-<time>.sqlite`. These notes keep the old
  word where they record what was decided or done at the time.

Claude's calls, open to change:

- What "as an ISO" means, which the owner did not say. One image for each
  processor, in place of the zip and not beside it
  (`Heracles-<version>-win-<arch>.iso`). On the disc: the app in a folder
  named `Heracles`, and a `README.txt` that says to copy the folder to the
  PC and run it from the copy (from 0.3.1 the installer is on the disc as
  well, and the README names it first). The disc is named `Heracles <version>`. The
  image is made by the part of Windows that writes discs (IMAPI2), so
  nothing is installed for it. The other reading was one image holding both
  processors: twice the download for everyone, so not taken.

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
- A task opens in a window of its own, not a sheet over the board: it is
  not modal, there is one per task, and it closes when its list goes to
  the trash. Its button says Close, where the Mac's says Done (a board's
  last column is called Done).
- A column is renamed where its name is, and its menu moves it left and
  right. The Mac's board can do neither; the design asks for both.
- A reminder is a scheduled toast under the identity above, and it opens
  its list by a `heracles://` link with no code of the app's running. The
  link also carries the reminder's identifier (`?reminder=…`), because the
  tag Windows knows a toast by is too short to hold one.
- **A run on a test library (`HERACLES_DATA_DIR` set) takes nothing of
  Windows for itself**: it does not register the link scheme or the
  identity, and it schedules no reminders. So no test, and no fixture,
  can put a notification on the owner's screen or take his links.
- A reminder whose task is renamed is scheduled again under the new name.
  (On the Mac the notification keeps the old one.)
- How the Mac editor's Swift value types are carried into C#
  (`app/Heracles/Editor/`): `RichText`, `TextStyle`, `TextLink`,
  `JsonValue` and `EditorSelection` are immutable with structural equality
  (a Swift `mutating func` on `RichText` returns a new one); `Block` and
  `EditorDocument` are mutable classes changed in place as on the Mac, with
  `Clone()` (deep) wherever Swift copies by assigning and `SameAs()` for
  comparison; `BlockIDs` passed `inout` is the class `BlockIds`; a Swift
  `Range<Int>` is `int start, int end`. A Swift function told apart by an
  argument label carries the label in its name (`insert(_:after:)` is
  `InsertAfter`, `stop(before:)` is `StopBefore`, `path(to:)` is `PathTo`,
  and `blocks(from:to:)` is `BlocksBetween`, since `Blocks` is taken). A
  Swift enum with a payload is a record struct with members that make it
  (`BlockTarget.Heading(2)`, `DropTarget.Before(id)`).
- What follows from blocks being classes, for anyone writing against the
  model: a block found in a document (`Block(id)`, `Stops`) **is** the
  block, not a copy, so a value needed from before a change is read before
  the change; one `Block` object must never sit in two places in a tree
  (`Fresh` and `Clone` make copies); and `Tidy` makes the lists of blocks
  anew, so no `Blocks` or `Children` list is kept across an edit.

Out of scope, because the Mac has not built them either: syntax colouring
in code blocks, find within a note, selecting text across blocks, resizing
an image, per-section sort modes and dragging notes in the sidebar, moving
a subtask under another task, HTML export. Also out: code signing, delta
updates and beta channels (the Mac rules those two out too). An installer
was on this list until the owner asked for one (2026-11-25, above).

## Layout

```
core/                     heracles-core (Rust, edition 2024, UniFFI 0.32)
  src/doc/                block document model, HTML and Markdown conversion
  src/store/              SQLite schema, notes, boards, search, image store
  src/transfer.rs         import and export of files
  src/legacy.rs           the importer of the original (Electron) app's library
  src/changes.rs          the change feed
  src/ffi.rs              what crosses to the interface
  uniffi.toml             C# namespace Heracles.Core, class CoreApi
Cargo.toml, Cargo.lock    workspace with the one member; output in target\
app/
  Heracles/               the WPF project (net10.0-windows10.0.22621.0)
    App.xaml(.cs)         startup, single instance, the windows, errors
    ImportSummaryWindow.cs  what came across from the Electron app's library
    SingleInstance.cs     one process per library; hand-over by named pipe
    MainWindow.xaml(.cs)  one window: menus, shortcuts, saved layout
    Model/                AppModel (the change feed; .Boards.cs: board
                          actions; .Legacy.cs: the Electron app's library,
                          and the summary's words), Library, Navigation (AppLink,
                          NavigationPolicy), Transfer (which files a dialog
                          offers, what an exported note is called)
    Workspaces/           PaneTree (immutable), Workspace, PaneNodeView
                          (PaneHost), PaneLeafView, TabDrag
    Sidebar/              SidebarRows (logic), SidebarView, SwitcherView
                          (Quick Open), NoteIcon
    Notes/                NoteDetailView (a note's title, and its editor),
                          EditorCoordinator (the app's end of an editor:
                          loading, saving, pictures, links), IconPicker,
                          ReadOnlyDocument (a note's words, for tests)
    Editor/               the editor. Its document model, no WPF in it:
                          JsonValue, RichText, EditorBlock (Block,
                          EditorDocument and its stored JSON, BlockIds),
                          EditorSelection, EditorDocument.Tree (finding
                          blocks, the order the caret meets them in),
                          EditorDocument.Editing (every edit that changes
                          a note's shape, BlockTarget, DropTarget, and
                          RichText's typing shortcuts). Its views:
                          EditorTheme (fonts, sizes, a block's text
                          as inlines and read back), BlockTextBox (one
                          block's text while it is typed in), BlockViews
                          (a view for each kind of block), BlockEditorView
                          (the page), BlockEditorController (the document,
                          the selection; .Typing.cs: editing and keys;
                          .Actions.cs: block actions, clipboard, pictures;
                          BlockEditorView.Mouse.cs: the handle and drags),
                          EditorAutomation (what UI Automation sees of it)
    Boards/               a todo list. What is decided, with no view in it:
                          TaskSupport (TaskDates, ReminderPlan), Board
                          (columns, cards, what a card says, where a drop
                          lands), TaskDetail (the task window's task: the
                          way down into subtasks, every field edit, notes
                          saved once typing pauses), ReminderScheduler
                          (IReminderCenter, ReminderToast). Its views:
                          BoardView (columns and cards, kept by id from one
                          reload to the next), TaskDetailWindow (one window
                          per task), Fields (EntryBox, StoredBox),
                          ToastCenter (Windows' scheduled notifications)
    Updates/              updates over the air. What is decided, with no
                          view in it: UpdatePolicy (whether this run looks,
                          and UpdateSettings: the file beside the program),
                          UpdateFeed (the signed feed, UpdateSignature,
                          UpdateAddress), UpdatePreferences (the daily
                          look), Updater (looks and fetches: the part that
                          talks to the web), UpdateInstaller (the swap of
                          the app's folder, and the command line of the
                          copy that does it), UpdateNotes (release notes
                          read for showing). Then UpdateCenter (the app's
                          end: the timer, what the menu follows, the
                          hand-over on quit) and UpdateWindow (the offer;
                          InstallingWindow)
    Icon/                 the icon: the Mac's layers (*.svg, icon.json),
                          and Heracles.ico and Heracles.png drawn from them
    Support/              AppPaths, AppVersion (version and build number),
                          Log, WindowStates (SavedWindow), Ui (shared view
                          helpers, LandmarkPeer), Prompts, E2eSeed,
                          LinkRegistration (the link scheme, the app's
                          identity and its icon for reminders,
                          WindowsIntegration: what the app writes in the
                          registry for itself at start), InstalledCopy
                          (the version under "Installed apps", for a copy
                          the installer put there)
  Heracles.Tests/         xunit tests: the crossing into the core
                          (BindingTests) and every class with logic.
                          Fixtures/legacy.sqlite is a small library as the
                          Electron app kept it (its README says what is in
                          it and how it was made)
  Generated/, runtimes/   produced by the core build, not in git
  Directory.Build.props   version from VERSION, build number, PathMap
  nuget.config            pins nuget.org
tools/build.ps1           targets core, app, test, run, package, feed, clean
tools/disc-image.ps1      New-DiscImage, Compare-DiscImage, Remove-DiscImage:
                          the package's ISO made, and read back by mounting
                          it (dot-sourced by build.ps1)
tools/installer/Heracles.iss
                          the Inno Setup script "package" compiles into
                          Heracles-<version>-win-<arch>-setup.exe; its
                          header says what follows from the app replacing
                          its own folder
tools/update-signing.ps1  the update key and what is done with it: making
                          and checking signatures, writing and reading a
                          feed, the credential store, the update archive
                          made and read back, the published build number
                          (dot-sourced by build.ps1, publish.ps1,
                          update-key.ps1 and the rehearsal)
tools/update-key.ps1      the update key: -Create, -Export, -Import, and
                          with nothing, whether it is here and is the one
                          the packages carry. Never prints the private half
tools/update-config.json  the repository, the feed's address, the name the
                          key is kept under, and its public half
tools/make-icon.ps1       draws Heracles.ico and Heracles.png from the
                          layers in app\Heracles\Icon (-Sheet: every size
                          in one picture, to look at)
tools/e2e/                make-fixture, launch, capture, ui (UI Automation
                          and synthetic input), adapted from Toolbox;
                          ui-check (helpers), check-shell, check-editor,
                          check-boards and check-transfer (the scripted
                          checks of the window, of writing in a note, of a
                          board and a task, and of files in and out),
                          check-legacy (the first start beside a library of
                          the original app), check-disc (a packaged disc
                          image opened as a drive, and the app started from
                          it), check-installer (a packaged installer run
                          for real: installed, started, its folder
                          replaced as an update replaces it, removed
                          again), check-update (a whole update rehearsed with
                          nothing real in it, and the updates that must be
                          refused; serve-folder is its web server),
                          check-published-update (after a release: an
                          old-looking copy takes the real update),
                          check-input (a minute of REAL mouse and
                          keyboard), look-editor and look-boards (pictures,
                          light and dark)
tools/publish.ps1         a GitHub release from the packages: checks, a
                          draft, the files and the feed, then published,
                          and the feed read back from where copies read
                          it. -DryRun sends nothing. Run when the owner
                          says
docs/                     the design, the plans, the Mac's rewrite notes,
                          release-notes/<version>.md ("In this version")
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
| `Updates.swift` (over Sparkle) | `Updates/` (the app's own: see Decisions) |
| `macos/Icon/` | `Icon/`, drawn by `tools/make-icon.ps1` |
| `update-config.sh`, `release.sh`, `publish.sh`, `update-rehearsal.sh` | `tools/update-config.json`, `tools/build.ps1` (`package`, `feed`), `tools/publish.ps1`, `tools/e2e/check-update.ps1` |
| `SelfTest.swift`, `Snapshot.swift` | not ported; `tools/e2e` instead |

Types and methods keep the Mac's names where C# allows.

## Dev machine and toolchain

Windows 11 on **ARM64** (Rust host `aarch64-pc-windows-msvc`), 200 % display
scaling, the account is not an administrator. Installed: Git, rustup with
stable 1.100 (ARM64 and x64 targets), Visual Studio 2022 with the C++
tools, .NET SDK 10 and 11 (`dotnet` picks 11; the app targets .NET 10),
`uniffi-bindgen-cs` v0.12.0+v0.32.0, and since 2026-11-25 Inno Setup
7.1.1 for the package step (for this account only: `winget install --id
JRSoftware.InnoSetup.7 -e --custom /CURRENTUSER`, which lands in
`%LOCALAPPDATA%\Programs\Inno Setup 7`, where `build.ps1` finds
`ISCC.exe`; its help is `ISetup.chm` there, which `hh.exe -decompile
<folder> ISetup.chm` turns into pages that can be searched). Not installed
and not needed: Node, `gh`, PowerShell 7. This core needs neither clang
nor CMake (Toolbox's does).

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
  do not write to the clipboard. One script uses real input,
  `tools\e2e\check-input.ps1`, for what nothing else can reach: run it when
  the handling of keys or the mouse has changed and before a release, not
  as a matter of course, and say so when it was run.
- With `core.autocrlf` on (this machine's setting), git warns that LF will
  become CRLF. `.gitattributes` decides: LF everywhere, CRLF for `.ps1`.
- **The clock steps backwards now and then** (two minutes, seen
  2026-11-22; seven, in the Toolbox notes). A file edited just after such a
  step is older than the output built just before it, so `dotnet build`
  decides nothing changed and **runs the old program** with no warning. If
  a change seems to have had no effect, compare the times of the source and
  the output, and rebuild with `--no-incremental` (or `tools\build.ps1
  clean`). Do not read much into gaps between log timestamps either.
  **And it steps forwards when it is put right** (seven minutes, seen
  2026-11-23, twice in one day counting the step back). A wait that reads
  the clock is over the instant that happens: a check said "the window did
  not appear" one second before it did. The harness's waits are timed by a
  stopwatch now (`Wait-Ui`); anything new that waits should be too.

## Everyday commands

```powershell
tools\build.ps1                 # core (release DLL + C# bindings), then the app
tools\build.ps1 app -Restart    # rebuild the app, close the running build, start the new one
tools\build.ps1 test            # cargo test --locked, then dotnet test on app\Heracles.Tests
tools\build.ps1 run             # starts it on the real library, not a fixture
tools\build.ps1 core package    # for this machine -> build\release: an installer (-setup.exe), a disc image (.iso) holding it and the app's folder, and the same folder as an archive (.zip) for copies that update themselves
tools\build.ps1 core package -Arch x64   # and for the other processor (then "core" again, for this one's bindings)
tools\build.ps1 feed            # update-feed.json for both packages, signed with the update key (needs both, and the key)
tools\update-key.ps1            # is the update key here, and is it the one the packages carry? (-Create, -Export <file>, -Import <file>)
tools\make-icon.ps1             # draw the icon again after a change to app\Heracles\Icon (-Sheet <png> to look at every size)
tools\publish.ps1 -DryRun       # what a release would send, and what would stop it; sends nothing
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
tools\e2e\launch.ps1 -Exe build\release\Heracles-0.2.1-win-x64\Heracles.exe   # a packaged build instead
tools\e2e\launch.ps1 -DataDir <path>  # another library folder (to see what the app does with a bad one)
tools\e2e\launch.ps1 --e2e-seed       # ...with a few notes of every kind in it
tools\e2e\launch.ps1 -Again --new-window   # a second copy while one runs: it hands over
tools\e2e\capture.ps1                 # PNG of every Heracles window -> build\e2e\shots
tools\e2e\ui.ps1 tree                 # also: find, invoke, expand, select, setvalue, focus, scroll, click, clickat, drag, keys, text
tools\e2e\check-shell.ps1             # the scripted check of the window: 32 checks, exit code = failures
tools\e2e\check-editor.ps1            # the scripted check of writing in a note: 15 checks
tools\e2e\check-boards.ps1            # the scripted check of a board and a task: 39 checks
tools\e2e\check-transfer.ps1          # the scripted check of File > Import and Export, through the system's dialogs: 10 checks
tools\e2e\make-fixture.ps1 -Legacy    # a library folder holding only the Electron app's library, to be imported
tools\e2e\check-legacy.ps1            # the scripted check of the first start on an Electron library: 20 checks
tools\e2e\check-disc.ps1              # a packaged disc image, opened as a drive, and the app started from it: 21 checks (-Arch x64 for the other one)
tools\e2e\check-installer.ps1         # a packaged installer run for real: installed, started, its folder replaced as an update does, removed again (-Arch x64; -Keep keeps its logs). REFUSES when Heracles is installed for this account
tools\e2e\check-update.ps1            # a whole update rehearsed from a package, with nothing real in it, and the two that must be refused: 31 checks, about six minutes (-Arch x64; -Look for pictures; -Theme light)
tools\e2e\check-published-update.ps1  # AFTER a release: an old-looking copy with the real key takes the published update from GitHub: 11 checks (-Arch x64)
tools\e2e\check-input.ps1             # REAL mouse and keyboard, for a minute: typing, keys, clicks, drags. 23 checks. Hands off while it runs
tools\e2e\look-editor.ps1             # pictures of a note of every kind of block, dark and light -> build\e2e\shots\editor-*.png
tools\e2e\look-boards.ps1             # pictures of a board and of a task, dark and light -> build\e2e\shots\board-*.png, task-*.png
tools\e2e\launch.ps1 --e2e-seed --theme-light   # the app in light (or --theme-dark) whatever Windows is set to
```

**Run `tools\e2e\check-shell.ps1` after any change to the window,
`tools\e2e\check-editor.ps1` after any change to the editor or to how a
note is shown or saved, and `tools\e2e\check-boards.ps1` after any change
to a board, a task or reminders.** The second opens a note, writes in a
block by automation ("set value"), uses the Format and Edit menus, ticks a
task, watches a second pane follow, restarts the app to see the words kept,
and tries to change a note in the trash. The third adds, renames, moves and
deletes a column, adds and ticks cards, opens a card as a task and gives it
a title, a priority, a deadline, a label, a place, a reminder, a subtask
and a comment, goes down into the subtask and back, makes it a card,
watches a second pane follow, deletes a task from its window, restarts, and
tries to change a list in the trash.

**`tools\e2e\check-input.ps1` is the one check that uses the real mouse
and keyboard**, because nothing else reaches the last inch between Windows'
input and the app: no automation pattern presses a key or drags. It moves
the pointer, clicks, drags and types, in the app's own window on the
fixture, for about a minute. Run it after any change to how keys or the
mouse are handled (the editor's typing, drags of blocks, cards, tabs and
dividers), and before a release; not as a matter of course, since the
owner may be at the PC. It never copies or pastes. Its guards, all in
`ui.ps1`: nothing is typed or clicked unless the app is the foreground
program; a click or a drag must begin and end over one of the app's own
windows; and an element scrolled out of view is refused (scroll to it
first: its rectangle still says where it would be, which can be past the
edge of its window).

**`tools\e2e\check-update.ps1` rehearses an update from start to finish,
after any change to updates, to quitting, or to the package, and before a
release.** It needs the package for the processor (`tools\build.ps1 core
package`). From it the rehearsal makes copies that say they are versions
9.9.1 and 9.9.2 (the app's own assembly compiled again under those
numbers, in the packaged folder), with a key made for the run where the
real one's public half was, a feed and an archive served from this machine
by `serve-folder.ps1`, and a library in its work folder
(`build\update-rehearsal`). Nothing real is in it: no release, no GitHub,
not the update key, not the real library, not what the real app remembers
about updates. It checks that a trial copy does not look at all; that the
old copy is offered the new one, fetches and checks it, quits through its
own Exit and comes back as the new version, file for file; that the new
one is offered nothing; and that an archive and a feed altered after
signing are each refused, for that reason, with nothing installed and (for
the feed) nothing downloaded. All by automation: no mouse, no keys.

**`tools\e2e\check-published-update.ps1` is its other half, for after a
release**: everything real but the old copy's version. The package that
was just published, with the app's own assembly compiled again as 0.0.9
(build 1) and nothing else changed, so it carries the real feed address
and the real update key. Told to read the published feed (a copy on a test
library reads none unless one is named), it has to believe that feed
because the key signed it, fetch the archive from GitHub, quit, and come
back as the published version, file for file what was packaged. It talks
to GitHub and downloads some 60 MB.

**Then look.** `tools\e2e\look-editor.ps1` takes six pictures, and
`look-boards.ps1` ten (the board, a task, and the foot of the task). Twice in one
day a picture showed what 436 passing tests could not: emoji drawn as white
blobs, and the block being typed in black on a dark page. Colour is in no
automation tree.

About `check-shell.ps1`: it
resets the fixture, seeds it, and drives the sidebar, tabs and panes,
search, Quick Open, the icon picker, renaming, the trash, a second window,
the hand-over from a second copy, and the layout coming back after a
restart, all through UI Automation patterns (no mouse, no keys, no
clipboard). It found a crash on Split Right that 139 unit tests did not.
New checks use the helpers in `tools\e2e\ui-check.ps1` (`Test-Ui`,
`Find-Ui`, `Find-UiShown`, `Get-UiId`, `Invoke-UiMenu`, `Set-UiFocus`).

App arguments: a `heracles://` link, `--new-window`, `--e2e-seed` (a
testing aid that does nothing unless `HERACLES_DATA_DIR` is set and the
library is empty; among its notes are "Every kind of block" and the todo
list "Packing", a board with cards of every sort),
`--theme-light` or `--theme-dark` (a testing aid: the theme, whatever
Windows is set to), `--updater-feed <address>` (the feed to read, taken
only in a run on a test library: the everyday app takes no feed from the
command line), and `--install-update --pid <n> --target <folder> --version
<v> [--restart]` (what the app starts the new version with when it quits:
that run installs the update and does nothing else).

A build cannot replace a program that is running: `tools\build.ps1 app`
refuses while an instance started from `build\` is up. A test instance
left open after a look at the screen is the usual cause. Stop it by path
(the check scripts do, at their end).

`launch.ps1` sets `HERACLES_DATA_DIR` (`build\e2e\data`) and
`HERACLES_LOG_PATH` (`build\e2e\heracles.log`) for the run; the real library
is never touched. It closes a previous instance of the same build first.
`make-fixture.ps1` refuses while a build started from `build\` is running.
Two different builds must not run on the fixture at once: stop the first by
path before launching the second.

How things are checked: assertions go through the log and the automation
tree (`ui.ps1 find -Id Sidebar`); screenshots are for looks, and are worth
looking at (one showed Quick Open white on white in dark mode).

- Log lines: `Heracles <version> (build <n>)` first; `instance: first` or
  `instance: handed over to the running app`; `seed: created <n> notes`;
  `library: opened, <n> notes` or `library: could not be opened: <reason>`;
  `window: opened (<n> open)`, `window: closed (<n> open)`;
  `instance: second launch: <args>`; `error shown: <text>`; `exit`. On the
  real library only (never on a fixture): `windows: links and reminders
  registered`, and `reminders: <n> scheduled, <m> taken back` when a sync
  changed anything. At a first start beside an Electron library: `legacy:
  imported <n> notes, <n> tasks, <n> images`, or `legacy: could not be
  imported: <reason>`.
- Log lines of updates, each beginning `updates: `, in the Mac's words where
  it says the same: `off: <why>` or `on, reading <feed>` once at start;
  `found <version> (<build>)`, `nothing newer in the feed` or `stopped:
  <reason>` for a look (`stopped: The update feed is improperly signed`);
  `downloaded <version>`, then `<version> is verified and will be installed
  when the app quits` or `stopped: The update is improperly signed`;
  `<version> was handed to its installer` as the app quits; and from the
  copy that installs, `installing <version>` and `installed <version>` or
  `stopped: <reason>`. **A run that only installs writes no `Heracles
  <version>` line and no `exit`.**
- Automation ids. Sidebar: `Sidebar` (the list), rows `note:<id>` (in the
  tree), `favorite:<id>`, `archived:<id>`, `trashed:<id>`, headers
  `notebook:<id>`, `section:favorites|archive|trash`, `toggle:<id>`,
  `SidebarSearch`, results `hit:<id>`, `NewMenu`. Panes: `Pane:<leafId>`,
  `Tab:<leafId>:<noteId>`, `TabClose:<leafId>:<noteId>`,
  `EmptyPane:<leafId>`, `Divider:<splitId>:<n>`. A note: `NoteTitle:<leafId>`,
  `NoteIcon:<leafId>`, `NoteText:<leafId>` (the editor's page, a Document
  whose name is the note's words; a todo list has a board in its place).
  In a note: `Block:<blockId>` for each block with text
  (an Edit: its name and value are its words, **its value can be set**, its
  item status is its kind: `paragraph`, `heading 2`, `bullet`, `todo`,
  `todo done`, `code rust`; `focus` puts the caret at its end),
  `Check:<blockId>` (a task's box; `invoke` ticks it), `Fold:<blockId>` (a
  toggle's arrow), `Language:<blockId>` (a code block's language),
  `HandleAdd`, `LinkAddress`, `LinkRemove`. A block's id is the same in
  every pane that shows the note, so `find -Id Block:…` gives the first.
  Quick Open: `Switcher`, `SwitcherQuery`, `SwitcherResult:<noteId>`. Icon
  picker: `IconPicker`, `Icon:<emoji>`, `IconTyped`, `IconRemove`. Dialogs:
  `PromptTitle`, `PromptInput`, `PromptOk`, `PromptCancel`, `ErrorText`,
  `ErrorOk`. `StartupError` shows only when the library did not open. The
  summary of an import: `ImportSummary`, `ImportSummaryCounts`,
  `ImportSummaryBackup` (a text box: its value is the path),
  `ImportSummaryRepair:<n>`, `ImportSummaryOk`. Menu
  items go by name (`ui.ps1 expand -Name View`, then `invoke -Name 'Split
  Right'`).
- Automation ids of a board: `Board:<leafId>`, `Column:<columnId>` (named
  by the column; its item status is `2 cards`), `ColumnName:<columnId>`
  (a text box: the name is changed where it is), `ColumnMenu:<columnId>`
  and, once that is invoked, `ColumnLeft:`, `ColumnRight:` and
  `ColumnDelete:<columnId>`; `NewColumn:<leafId>`, `NewCard:<columnId>`;
  `Card:<taskId>` (a button named by the card: `invoke` opens the task; its
  item status is the card in words, `done, P1, due Nov 26, 1/2 subtasks,
  camping`), `CardDone:<taskId>`. A card's id is the same in every pane.
- Of the task window: `TaskDetail` (its content; look `-Within` it),
  `TaskFields` (the part that scrolls), `TaskTitle`, `TaskDone`,
  `TaskNotes`, `TaskPriority:<1 to 4>` (item status `chosen`),
  `TaskDueSet`, `TaskDue`, `TaskDueClear` and the same three for
  `Deadline`, `TaskLocation`, `TaskMakeCard` and then `MakeCard:<columnId>`,
  `TaskNewLabel`, `Label:<text>`, `LabelRemove:<text>`, `TaskReminderDay`,
  `TaskReminderTime`, `TaskAddReminder`, `TaskReminderProblem`,
  `Reminder:<stored>`, `ReminderRemove:<stored>`, `TaskNewSubtask`,
  `Subtask:<taskId>` (`invoke` opens it), `SubtaskDone:`, `SubtaskRemove:`,
  `TaskNewComment`, `Comment:<commentId>`, `CommentEdit:`, `CommentDelete:`,
  `TaskPath` (the tasks above this one), `TaskBack`, `TaskDelete`,
  `TaskClose`.
- Of updates: in the Help menu, once it is expanded, `UpdateCheck` and
  `UpdateAutomatic` (item status `ticked`; both `DISABLED` in a run that
  does not look). The offer: `UpdateWindow` (look `-Within` it),
  `UpdateVersions` (named by its words: "Heracles 9.9.2 is available. You
  have 9.9.1."), `UpdateNotes` (named by the notes, as words),
  `UpdateStatus` (what the window now says: downloading, ready, or why
  not), `UpdateProgress`, and the buttons `UpdateInstall`, `UpdateLater`,
  `UpdateSkip`, `UpdateCancel`, `UpdateRestart`, `UpdateClose`, of which
  only those that apply are showing. `UpdateInstalling` is the window of
  the copy that installs. A plain message (`Prompts.Message`: "Heracles is
  up to date") is `MessageText` and `MessageOk`.
- **A box that takes something new** (`NewColumn:`, `NewCard:`,
  `TaskNewLabel`, `TaskNewSubtask`, `TaskNewComment`) hands it over on
  Enter, or by the button `Add:<the box's id>` that appears once the box
  holds text. A check sets the value and invokes that button
  (`Add-UiEntry` in `check-boards.ps1`). **A box that holds something
  stored** (`TaskTitle`, `TaskLocation`, `ColumnName:`, `NoteTitle:`) keeps
  it on Enter or when the keyboard leaves it: a check sets the value and
  then focuses something else (`Set-UiStored`).
- `ui.ps1 find` prints what a text box holds as `value='...'` at the end
  of its line (`Get-UiValue`), and an item status as `status='...'`
  (`Get-UiStatus`).
- A hidden element is still in the tree, marked `[offscreen]`; so is a row
  scrolled out of view. `Find-Ui` asks whether something exists,
  `Find-UiShown` whether it can be seen.
- `invoke`, `setvalue`, `select`, `expand`, `scroll` touch neither mouse nor
  keyboard. (`setvalue` does move the keyboard focus into the box.)
- **The system's file dialogs can be driven too**, without real input.
  Their controls are plain Windows controls and offer no automation
  patterns here, so `setvalue` and `invoke` fall back to window messages
  sent to the one control by its handle (`ui.ps1`, "Native"). The file-name
  box is id `1148` in an Open dialog and `1001` in a Save dialog; the
  buttons go by name (`Open`, `Save`, `Cancel`: English Windows). Several
  files are named in one box, each in double quotes. `check-transfer.ps1`
  has `Complete-FileDialog`.
  `click`, `clickat`, `drag`, `dragto`, `keys`, `text` are real input on the owner's desktop.
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
- **The license is proprietary** (the owner, 2026-11-23, for every public
  repository on the account: "the most restricting license my code can
  have"). `LICENSE` is all rights reserved, with one permission: to run
  the builds the owner hands out. Its copyright line carries the account
  name `tacticalbadger`, which the owner chose over a real name so that
  the rule above holds. Do not add an open-source license or a `license`
  field to a manifest, and keep the README's License section in step with
  `LICENSE`. Third-party components keep their own licenses.
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

## Found on Windows (each cost real time)

- **A view moved to a new parent must leave the old one first**, whatever
  kind of parent it had. `PaneHost` took panes out of grids but not out of
  its own `Child`, and the app died on the first Split Right ("already the
  logical child of another element").
- **Panels and borders do not exist for UI Automation.** A `DockPanel`,
  `Border` or `Grid` has no automation peer, so an id put on one is never
  found. A container that must be findable overrides
  `OnCreateAutomationPeer` to return a `LandmarkPeer` (`Support/Ui.cs`), or
  is a `Landmark`.
- **A hidden element keeps its last rectangle** in UI Automation. Whether
  it can be seen is its `IsOffscreen` flag, not its rectangle.
- **`SystemColors.WindowBrushKey` is white in dark mode too**, and a
  `Popup` does not inherit the window's text colour. Anything that floats
  (Quick Open, a popup) takes `Ui.AsSurface`, which uses the Fluent theme's
  own brushes (`SolidBackgroundFillColorBaseBrush`,
  `TextFillColorPrimaryBrush`). Lines between things use `Ui.Hairline`.
- **A list gives no selection change for the row already selected.** A
  click on the highlighted row therefore did nothing in Quick Open. Quick
  Open keeps its own highlight and treats any selection as a choice; the
  sidebar handles a click on the selected row by hand.
- **A value set through UI Automation puts the keyboard in the box, and
  leaves it there.** (These notes once said the opposite. Seen 2026-11-23:
  after `setvalue` the box is the focused element, and it already was when
  its text changed.) So nothing set that way is ever "committed" by the
  keyboard leaving, and a rule of the form "taken at once when changed
  without the keyboard" never fires. A box for something new has a button
  to hand it over; a box for something stored is kept when a check moves
  the focus on.
- A list row under the Fluent theme is about 44 px high by default.
  `Ui.Row` makes the rows this app uses.
- `Emoji.Wpf` clashes with anything of ours named `Emoji`: write
  `global::Emoji.Wpf.…`. WPF's `WindowState` clashes with anything of ours
  of that name: the saved one is `SavedWindow`.
- xunit's `Assert.Equal` on an `ImmutableArray<T>` compares the structs, not
  their items. Compare `.ToArray()`.
- **Backslashes and `\u` escapes in a file written through a shell
  here-document do not arrive as typed**: one level is taken off on the
  way, so a test meant to hold the six characters backslash-u-D-8-3-D held
  an emoji, and failed for a reason that was not in the code. Write such
  files with the editor tools, and build a string that must contain a
  backslash from its character code (`(char)92`).
- **What is dragged must not be carried as plain text.** A text box takes
  any text dropped on it for itself. Tabs and cards were carried as text
  ("so they can cross between panes"), and a card let go over "Add a
  card", or a tab over a note's title, typed `heracles-card:…` into the
  box. They go under names of their own now (`TabDrag.Format`,
  `CardDrag.Format`), which cross between panes and windows just as well.
  No test could have found this; the first minute of real input did.
- **A text box does not pass a drag on to what it sits in.** Over a text
  box, the box answers "not here" to anything that is not text, and what
  is underneath never hears of it. And `AllowDrop` set to false on the box
  does not help: it is inherited by everything inside the box, and a
  target that does not allow drops is no target at all, not a way through
  to its parent. A container that must take a drop anywhere within it
  listens for the drag on its way down (`PreviewDragOver`,
  `PreviewDrop`), as `BoardColumnView` does.
- **The view tests share one thread, and a test that pumped it used to let
  another test run inside itself.** Every typing test lets the thread do
  what it has put off after each key; a test of another class, asked for
  meanwhile from another of the runner's threads, was among the things put
  off, and ran whole, in the middle. Nothing noticed until a slow test (two
  seconds waiting on a web server) landed inside the test that times its
  own typing, which then failed, at random, for something it never did.
  `Sta.Run` now lets one test on that thread at a time (`StaTests`). A
  timing that fails once and passes alone is worth an hour, not a retry.
- **A Save dialog takes its file name only from characters typed in its
  box.** Text put there whole (`WM_SETTEXT`) shows in the box and is then
  ignored: Save writes the name the dialog first offered, in the folder it
  opened in. (An Open dialog reads its box either way, which is how this
  went unnoticed for an hour.) The harness sends the box one `WM_CHAR` for
  each character. And a button sent `BM_CLICK` does nothing unless its
  dialog is the active window; send the dialog `WM_COMMAND` with the
  button's id. **A check must never answer "replace it?" with Yes**: the
  name offered is the note's, and the folder may hold the file it came from.
- The same goes for a replacement made by `perl` or `sed` from the shell:
  `@"Software\Classes"` arrived as `SoftwareClasses`, in the line that
  opens the registry key the app registers itself under. It compiled. It
  was seen by reading the line back, before it ever ran. **Read back any
  line with a backslash in it that a shell command wrote**, and give such
  a string a test (`LinkRegistration.ClassesKey` has one). A `$(...)` in a
  double-quoted shell string is run by the shell, too: that emptied three
  lines of `ui.ps1` once.
- **A `\u` escape typed into a file through the session's own editing
  tools arrives as the character itself**, not as the six characters. Most
  of the time that is harmless (an em dash is an em dash). U+2028 and
  U+2029 are not: in C# source they end the line they are on, and the file
  no longer compiles. Write such characters by number: `(char)0x2028`.
- **Our `Block` and WPF's `System.Windows.Documents.Block` clash** in any
  file outside the `Heracles.Editor` namespace that uses both (every view
  test). `using Block = Heracles.Editor.Block;` settles it.
- **`RoutedCommand.Execute` does not ask `CanExecute` first**, and a
  `CommandBinding` on an element whose `CanExecute` says no is passed over
  for the class's own handler, which then runs. To stop a `RichTextBox`
  doing something of its own (bold, a bullet list, a new paragraph), handle
  the routed events on it: `PreviewCanExecute` (say no, **and set
  `ContinueRouting`**, or the key is swallowed and never reaches the
  window's shortcuts) and `PreviewExecuted` (mark it handled).
- `Key.Enter` and `Key.Return` are one value, and it prints as `Return`.
- **`UIElement.IsVisible` is false for everything that is not in a shown
  window**, so it says nothing in a test. Read `Visibility` up the tree
  (`BlockEditorView.FrameOf` does).
- **A scroll view measures what it scrolls with no limit on height**, so
  the page inside cannot learn how much of it is on show from its own
  measuring. `EditorScrollViewer` notes the height it is offered and asks
  the page to measure again when that changes.
- **An `EmojiInline` that takes the colour of the text around it is drawn
  as a flat shape of that colour**: in dark mode, a white blob. The
  library's own text block pins each one's `Foreground` to black, and so
  must anything else that makes one (`EditorTheme.ColourEmoji`). In a bare
  test program, with no theme, the same inline drew in colour unpinned, so
  only the window showed it.
- **A `RichTextBox`'s document takes its text colour from nowhere.** With
  the Fluent theme in dark mode the box's text and caret were black on the
  dark page, while static text beside it, which inherits from the window,
  was white. `BlockTextBox` points its own `Foreground`, its `CaretBrush`
  and its document's `Foreground` at `TextFillColorPrimaryBrush`.
- **Private-use characters (the icon font's glyphs) do not survive a file
  being read and written back by the session's tools**: they read as an
  empty string, and a rewrite of the whole file then stores an empty
  string. `NoteDetailView` lost its placeholder icon that way and
  `git show HEAD:<file> | cat -A` found it. Write such a character by
  number (`((char)0xE76E).ToString()`), and change files that hold them
  with targeted edits.
- `System.Text.Json` writes every character outside the basic plane (an
  emoji) as two escapes, whatever encoder it is given. `JsonValue.Text`
  puts them back, to match what the Mac and the core write.
- In PowerShell 5.1 under `Set-StrictMode`, `.Count` on a function's result
  fails when the function returned one thing. Wrap it: `@(Get-Things).Count`.
  A function named like an alias (`Diff`) never runs. `Write-Host` output is
  captured only with `6>&1`.
- **`Invoke-RestMethod` in PowerShell 5.1 hands a JSON list over as one
  object**, not item by item, and an empty list as one empty object. Piped
  straight into `Where-Object`, the filter is asked once, about the whole
  list: under `Set-StrictMode` an empty list is an error ("the property
  cannot be found"), and a list of several comes back whole or not at all.
  Take it apart first: `@(Invoke-RestMethod ... | ForEach-Object { $_ })`.
  The first real run of `tools\publish.ps1` stopped on this, at the list of
  releases, which is empty before a repository's first release. Nothing had
  been sent yet. (The Toolbox port's script has the same line.)
- **Under `$ErrorActionPreference = 'Stop'`, a program that writes to stderr
  ends the script, even with `2>$null`**, whatever its exit code. A script
  that must outlive a warning from git sets the preference to `Continue`
  inside the function that calls git and reads `$LASTEXITCODE`
  (`Invoke-Git` in `tools\publish.ps1`).
- **A disc image is made and read back with what Windows has, and no
  administrator** (2026-11-23, `tools\disc-image.ps1`). What it took to
  find: the class is `IMAPI2FS.MsftFileSystemImage` (asked for as
  `MSFileSystemImage`, it is "class not registered"); its size
  limit is a CD's until `FreeMediaBlocks` is set to 0; PowerShell cannot
  read the stream it hands back, so a few lines of C# write it to a file;
  and the files put in stay open until the COM objects are released and
  collected. File systems 7 (ISO 9660, Joliet and UDF) keeps long names:
  Windows reads the UDF. `Mount-DiskImage` takes an image only by its whole
  path, and with `-NoDriveLetter` nothing shows in File Explorer: the disc
  is then read at the volume's own path (`\\?\Volume{...}\`), which
  `Get-Volume` gives. A disc's name is kept whole up to 32 characters and
  cut without a word beyond. Everything on a mounted disc is read-only. It
  all works from an emulated x64 shell too.
- `tools\build.ps1` now compiles the C# afresh every time
  (`--no-incremental`), because of this machine's clock (see above). A bare
  `dotnet build` or `dotnet test` still trusts the clock.
- **A part of a Windows file version holds no more than 65535.** The build
  number is the last part (`0.3.0.52`), and the scripts read it back from
  `Heracles.dll` with `FileVersionInfo`. The update rehearsal first used the
  Mac's made-up numbers, 99901 and 99902: the app, which reads the number
  from its own attribute as text, said 99901, and the file said 34365. The
  rehearsal uses 9901 and 9902. The commit count is the real number, so
  this bites at commit 65536.
- **A folder with a running program in it can be renamed**, on this
  Windows 11 at least: a spike renamed the folder a copied `ping.exe` was
  running from. What stops a rename is an open *handle* to a file inside
  (a test holds one, and the rename is refused). So "the app's folder
  cannot be moved while the app runs" is not something to lean on, and the
  installer does not: it waits for the app's process to be gone.
- **A point that is not on the curve is not a `CryptographicException`.**
  `ECDsa.Create` with such a public key throws
  `PlatformNotSupportedException` ("the specified curve or its parameters
  are not valid for this platform"), with Windows' "the parameter is
  incorrect" inside it. A test with a nonsense key found it; the check
  that every update rests on would otherwise have thrown where it should
  say no.
- **The pipe a second copy hands over through was made a moment after the
  name was taken**, on a borrowed thread (`Task.Run`). One run of the whole
  suite, cold, failed `ASecondLaunchHandsItsArgumentsToTheHolder`, and the
  next three passed. Asked of Windows directly (the pipes there are, listed
  under `\\.\pipe\`), the gap was there every time: no timing in the
  question, so no luck in the answer. The pipe is now made before `Claim`
  returns. Whether that gap was the cause of the one failure is not
  proven: it has not been seen since.
- **Two backslashes in a command given to the shell tool arrive as one**,
  even inside a here-document whose end marker is quoted; a single
  backslash before an ordinary character arrives as it was. A Perl script
  written that way looked for "tools`" where the README says "tools\`",
  found nothing, and stopped (it was written to stop). Files with doubled
  backslashes are written with the editing tools.
- **A line number stops being an address the moment anything above it is
  edited.** A `sed` replacement by line number, composed before three
  edits higher in the same file, replaced a closing brace 13 lines away
  from where it was meant. Read back at once, as these notes say to. Edit
  by the text, not by the number.
- In a raw string with `$$`, two closing braces in a row end an
  interpolation, so JSON that ends `}}}` after one does not compile. Put
  spaces between them, or use `$$$`.
- **A script that cannot be run yet can still be parsed.**
  `check-published-update.ps1` is for after a release, so it was committed
  unrun, with `"...at $Package: this needs..."` in it: PowerShell reads
  `$Package:` as a variable on a drive, and the whole file is a parse
  error. It was found by its first run, after the release it was for.
  Before committing a script: `[System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)`
  for every `.ps1` under `tools\`, and write `${name}:` where a colon
  follows a variable in a string.
- **`ui.ps1` and `capture.ps1` used to drive, and photograph, every process
  named `Heracles`**, whatever folder it ran from: with the owner's own
  copy open, a check would have read, clicked and moved things to the
  trash in his library. It went unnoticed while nobody ran the app for
  real. Both now look only at copies started from under this checkout's
  `build\` or from a disc (`Test-OwnCopy`). A check that starts the app
  from anywhere else will find "Heracles is not running": put the copy
  under `build\`.
- **An installer for an app that replaces its own folder** (2026-11-25,
  `tools\installer\Heracles.iss`, Inno Setup 7). What must stay true:
  - The uninstaller's files are not in the app's folder
    (`UninstallFilesDir`): the first update would rename them away with
    the old folder, and "Installed apps" would point at nothing. Never
    move them back, and never change `UninstallFilesDir` in a later
    version (Setup would not find the old uninstall log).
  - `AppId` in the script and `InstalledCopy.AppId` are one value (a test
    compares them) and never change: Windows and the next installer know
    an installed copy by it.
  - The installer stays per user. Where the user cannot write, the app
    cannot update itself (`UpdateInstaller.WhyNotHere`).
  - The app's folder is the app's alone, for the installer as for the
    updater: `PrepareToInstall` refuses a folder with other things in it,
    `[InstallDelete]` empties one that holds Heracles, `[UninstallDelete]`
    removes it whole. Inno Setup's help warns against that last one, for
    the case of a wrongly chosen folder; the refusal is what makes it
    safe here.
  - The uninstaller removes the link scheme and the reminder identity
    only where they point at the copy it removes.
  - Inno Setup details that cost time (found in the Toolbox port the same
    day): `--define=Name=value` on ISCC's command line; `{{#AppId}`
    writes a brace before a GUID; `PrepareToInstall` returning text ends
    a silent install with exit code 7; `InitializeUninstall` returning
    False gives a non-zero exit; a running program is found by trying to
    open its file for writing (`TFileStream` with `fmShareExclusive`);
    `InternalCompressLevel=none` leaves Setup's tables readable, which is
    how `Find-PrivatePaths` can look through the installer (it stores no
    source paths); the Setup program is 32-bit x86 for both packages and
    installs in 64-bit mode, so the ARM64 installer also runs where there
    is no x64 emulation. The uninstaller hands its last steps to a copy
    of itself in TEMP: wait for the registry key to go, not for the
    process.
  - The folder a disc image is made from cannot always be removed the
    moment the image is written, now that a 60 MB program is in it: the
    disc writer lets go of its files only when .NET collects it, and a
    scanner reads a freshly copied program. `Remove-BusyFolder` in
    `build.ps1` collects and tries again for up to a minute.

## For the Mac repository

Core changes made here that the Mac repository can take. Each is portable:
the core's own tests pass with it on Windows, and nothing is behind a
Windows-only switch except where a comment says so.

As of 2026-11-25 the core here is the Mac's at `91076bf` (its changes
since `5dd0252` were taken as a patch: "the original app" in comments and
messages, and the backup named `heracles-original-<time>.sqlite`) plus the
three changes below, which the Mac has still to take. Nothing else
differs: `core/src/ffi.rs`, `model.rs`, `store/tasks.rs`, `transfer.rs`
and `tests/import_export.rs` are the only files that are not the Mac's
byte for byte.

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

The checklist. Ticked means built and checked through the tests or the
harness; "Not verified" below says what the checks could not reach.

- [x] Sidebar: notebooks, nested notes, favorites, archive, trash, search
- [x] Panes and tabs: splits within splits, draggable dividers, tabs that
      move between panes, Ctrl-click to open beside, layout kept per window
- [x] Multiple windows on one shared model, kept in step by the change feed
- [x] The native block editor: paragraph, headings, bullet, numbered and
      task lists, quote, callout, toggle, code block, divider, image, columns
- [x] Editor behaviour: Markdown as you type, slash menu, block handle,
      drag to move and to make columns, whole-block selection, the editor's
      own undo, marks and links, paste through the core's converter
      (built, and tested with real text boxes in no window; typing, Enter,
      Backspace, undo, the arrows, Markdown and the slash menu were also
      done with real keys, in `check-input.ps1`; **the block handle, its
      drags, and selecting with the mouse have never been tried by hand**:
      see "Not verified")
- [x] Pictures in the image store; remote pictures fetched on open (pasted,
      dropped and imported pictures are stored and shown; a picture on the
      web is fetched when its block is shown, tested against a web server
      of the test's own on this machine; a stored picture and a missing one
      were looked at in the window, in the seeded note)
- [x] Kanban boards: columns added, renamed, moved and deleted, cards added,
      ticked, opened and deleted, and dragged onto a card and onto a
      column (by a real mouse, in `check-input.ps1`)
- [x] Task detail: notes, priority, dates, location, labels, reminders,
      nested subtasks, comments
- [x] Reminders delivered by Windows, whether or not the app is running
      (built and tested up to Windows' own list: a reminder handed over
      under a test identity was listed there and taken back. **The app
      itself has never registered or scheduled anything**, because it has
      only ever run on fixtures; no reminder of its own has been seen to
      arrive, and none has been clicked: see "Not verified")
- [x] Import of Markdown and HTML files; export as Markdown (File > Import
      and File > Export as Markdown, checked through the system's own
      dialogs)
- [x] The Electron library imported on first start, with a summary (checked
      in the window with a small made-up library; **no real Electron
      library has ever been imported**: see "Not verified")
- [x] Quick Open, the icon picker, the menu bar (with Edit and Format)
- [x] `heracles://note/<id>` links, with `#block=<id>` (parsed, followed and
      handed over between copies, and the editor scrolls to the block and
      marks it; the scheme is registered with Windows at start, which is
      tested against a registry key of the test's own and **has never run
      against the real one**: see "Not verified")
- [x] Nothing lost on quit: closing a tab, a pane or a window, and
      quitting, save every editor first
- [x] Packages for ARM64 and x64, and a release script (both packages
      built, started and checked, and released as 0.2.0 on 2026-11-23; a
      package is a disc image from 0.2.1)
- [x] An app icon: the Mac's, drawn for Windows from the same layers (on
      the program, its windows and the taskbar; seen in the title bars of
      the window and of the update window. **On reminders it has never
      been seen**: see "Not verified")
- [x] Updates over the air: a daily look and Help ▸ Check for Updates…,
      the release notes shown, installed when the user agrees, and only if
      the owner signed it (built with tests for every rule, and rehearsed
      whole by `check-update.ps1`: an old copy replaced itself with a new
      one and came back as it; an altered archive and an altered feed were
      each refused. Once 0.3.0 was out, a copy with the real key took the
      published update from GitHub and became it. **Never yet from one
      published version to the next**, which cannot be until a second
      release with a feed exists: see "Not verified")
- [x] "The original app" for the Electron app, in the app, the core and
      the README, and the licence, as on the Mac

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

Run 2026-11-22: a scripted program driving a real `RichTextBox` with
`TextCompositionManager.StartComposition` and `EditingCommands`. The block
was one paragraph: plain `ab`, bold `cd`, a line break, italic `e😀f`, a
code run, a link run.

**(a) The mapping works.** A model offset counts UTF-16 units, as on the
Mac. In the paragraph:

- Each UTF-16 unit of a `Run`'s text is one. A `LineBreak` is one (the
  model's `\n`). The edges of elements (`Run`, `Span`, the paragraph) are
  none.
- `PointerAt(offset)` walks the paragraph counting those; at a boundary
  between two runs it returns the end of the earlier run. An offset between
  the two halves of a character outside the basic plane snaps to the
  character's start.
- `OffsetOf(pointer)` is the same walk the other way.
- Checked: every offset round-trips; the caret set at any offset reports
  that offset back; offsets on either side of the line break land on the
  right side of it.

**(b) Decision: diff at the caret.** Three ways of learning what was typed
were run against eleven edits and the Mac editor's rule for the style of
typed text (`RichText.style(before:)`):

| Way | Right | Wrong |
|---|---|---|
| Read the marks back from the runs (a model style carried on each `Run` in an attached property) | 6 | 5 |
| Diff the text by common prefix and suffix | 10 | 1 |
| Diff the text anchored at the caret (what follows the caret is unchanged) | 11 | 0 |

Why reading back fails, each seen in the run:

- Typing at the end of a link extends the link. The model says text typed
  after a link is not linked.
- Typing at the boundary between two runs goes into the **later** run, even
  when the caret was put at the end of the earlier one. The model says the
  earlier style.
- A style the user has just switched on, with nothing selected, exists only
  as the editor's intention. WPF has no public typing attributes to hold it.
- Typing into an empty paragraph makes a `Run` with no attached property.

The plain diff fails where the same letter stands on both sides of the
caret (`cc|cc`): it cannot tell where the new one went. The caret can.

So the editor does what the Mac does with typing attributes, but holds the
typing style itself: on each change it finds the changed range by the diff
anchored at the caret, applies it to the model with the typing style (the
pending style if there is one, else the model's rule), and then, **if the
box shows the text otherwise than the model says, redraws the paragraph's
inlines from the model and puts the caret back.** Run as a handler of
`TextChanged`, two keys at a time, in seven places (the four above, an
empty block, the middle of a run, after a line break): model, box text, box
formatting and caret were right every time, with no exception from changing
the document inside the handler. A redraw was needed for 4 of 11 single
edits; at a run boundary it is needed on every key.

Also seen: an attached property on a `Run` survives typing inside the run
and deleting across runs, so it is usable for other purposes.

Not done here, for the editor-views plan to mind: nothing may be redrawn
while an input method has text in composition (the Mac checks
`hasMarkedText()`); and the caret-anchored diff assumes the caret ends
where the edit ended, which holds for typing, deleting and pasting, and
must be checked for drag-and-drop of text inside a box.

### Colour emoji

Run 2026-11-22. The test: draw `💡 📓 ✅ 🧭` on white, save the picture,
count pixels whose red, green and blue differ by more than 40.

**WPF on .NET 10 draws emoji in one colour.** `TextBlock` and
`RichTextBox`, with Segoe UI Emoji named or found by fallback, at 14, 20
and 32 px: outline glyphs, 0 coloured pixels.

**Finding: colour with a library.** `Emoji.Wpf` 0.3.4 (Sam Hocevar,
https://github.com/samhocevar/emoji.wpf, licence WTFPL) draws the same
text with 5538 coloured pixels, on this ARM64 machine, from .NET 10.

- It restores from nuget.org and builds for `win-arm64` with no warnings.
  The package targets .NET Core 3.1 and .NET Framework 4.0, not current
  .NET; it loads and works all the same. It adds four assemblies, 1.7 MB:
  `Emoji.Wpf.dll`, `Typography.OpenFont.dll`, `Typography.GlyphLayout.dll`,
  `Stfu.dll`. The licences of the two Typography assemblies were not read.
- Drawn in colour, each seen in the saved picture: its own `TextBlock` at
  all three sizes; a standard `Image` given an emoji through the attached
  property `Emoji.Wpf.Image.Source`; a **subclass of the standard
  `RichTextBox`** with an `EmojiInline` placed among ordinary runs, and the
  same after `FlowDocumentExtensions.SubstituteGlyphs`. So the editor's own
  text box class can stay; the library's `RichTextBox` is not needed.
- Where it fits with no cost: sidebar icons, the callout's emoji, the icon
  picker, tab titles, and blocks drawn as static text. All of those are
  pictures or read-only text.
- Where it has a cost: emoji inside a text box being edited. An
  `EmojiInline` is an embedded element, not text: `TextRange.Text` gives a
  space for it, it spans 3 pointer positions, and its real text (1 or 2
  UTF-16 units, more for joined sequences) is in its `Text` property. The
  offset mapping and the text walk would have to count it by that text, and
  the caret, selection and copy around embedded elements were not tried.
  Emoji typed into a box arrive as ordinary text and stay one colour until
  the block is redrawn from the model.

**Decided: colour emoji, with the library** (owner, 2026-11-22). How it is
staged is Claude's suggestion, not contested: the library for icons,
callouts and static text from the start; emoji inside a text box being
edited stay one colour until the editor works, and the inline is tried
there then.

A caution about the spike itself: its per-row pixel counts were taken
before the library had drawn (they read 0 for rows the picture shows in
colour). The figures above are counts over the saved pictures
(`build\spikes\emoji\native.png`, `build\spikes\emoji\lib\library.png`).

### Scheduled toasts

Run 2026-11-22, with a throwaway program under its own identity
(`com.heracles.notes.spike`) so nothing of the real app's was touched.

**Reminders can be handed to Windows by an app with no installer.** A
toast scheduled for 15 s ahead was on screen at its due time with no
process of the program running (seen in a picture of the screen corner:
the banner, under the name "Heracles (spike)", with "Book ferry" and
"Reminder"). Windows then kept it in the notification centre.

What it took:

- One registry key, written by the app at start, no administrator needed:
  `HKCU\Software\Classes\AppUserModelId\<identity>` with `DisplayName`.
  That alone is enough for the banner, for the name shown on it, and for
  the notification centre to keep it. No Start Menu shortcut, no COM
  activator, no package. (An `IconUri` value would give it the app's icon
  in place of the generic one; not tried.)
- `SetCurrentProcessExplicitAppUserModelID(<identity>)` in the process.
- `ToastNotificationManager.CreateToastNotifier(<identity>)`, then
  `AddToSchedule(new ScheduledToastNotification(xml, when))`. The WinRT
  types come with the `net10.0-windows10.0.22621.0` target the app already
  has. `GetScheduledToastNotifications()` lists what is pending, which is
  what `ReminderPlan` needs to compare against; a `Tag` and `Group` on each
  identify it.
- The link scheme: `HKCU\Software\Classes\<scheme>` with `URL Protocol` and
  `shell\open\command` = `"<exe>" "%1"`. Opening a link of that scheme
  started the program with the link as its argument.
- The toast's XML: `<toast activationType="protocol" launch="<link>">`, so
  a click opens the link and needs no code of the app's running.

Things to know:

- Before the identity's first toast, `notifier.Setting` throws "Element not
  found" and the history is empty. After the first one Windows knows the
  app (`Setting` reads `Enabled`) and keeps its toasts. Do not read
  `Setting` to decide whether to schedule.
- Windows makes two keys of its own for the identity
  (`…\Notifications\Settings\<identity>` and
  `…\PushNotifications\Backup\<identity>`). The spike's were deleted
  afterwards along with its own two.
- A banner stays about six seconds, then lives in the notification centre.
  `scenario="reminder"` keeps a banner up until dismissed, but needs a
  button on it; not tried.
- Whether Do Not Disturb was on could not be read. It was evidently off.

How the check was made, since it nearly went wrong twice: a picture of the
screen corner before and during. Take it with the thread made DPI-aware
(`SetThreadDpiAwarenessContext(-4)`) and real pixel coordinates; without
that the picture is of the middle of the screen and shows nothing. UI
Automation did not find the banner at all, by window or by its text.

### Views with no window

Run 2026-11-23, before the editor's views were written: a throwaway
program with real `RichTextBox`es and `TextBlock`s (.NET 10, Segoe UI
15 px, a line height of 24).

**A window off the screen is not a window nobody notices.** Shown at
-20000 with `ShowActivated` off, it does not come forward. But the first
`Focus()` on anything in it makes it the foreground window: the owner's
keystrokes would go to a test. So no test shows a window.

**Views work with no window at all.** A tree of elements measured and
arranged by hand (`Measure`, `Arrange`, `UpdateLayout` on its root) lays
out its text boxes fully:

- `Focus()` returns false and nothing moves, so the editor keeps its own
  record of which text box has the caret (the Mac editor does the same when
  it has no window).
- Text sent with `TextCompositionManager.StartComposition` is typed into the
  box, **after the dispatcher has run its Background work**: a
  `RichTextBox` queues typing. A hundred keys, each followed by that pump,
  took 95 ms (2 s in a shown window, which renders between keys).
- `EditingCommands` (Backspace, the caret moves, select by word) work.
- `TextPointer.GetLineStartPosition(-1, out n)` gives `n == 0` on the first
  line and `GetLineStartPosition(1, out n)` gives `n == 0` on the last;
  `GetCharacterRect` and `GetPositionFromPoint` answer.

**A text box and static text measure alike when their padding matches.**

- A `RichTextBox`'s document keeps a `PagePadding` of 5 at each side.
  Setting it to nothing is undone (set on the document before or after it is
  given to the box, or in `Loaded`); only setting it in `OnApplyTemplate`
  holds. Left at 5, with a `TextBlock` given `Padding` 5 at each side, the
  same paragraph was the same height in both, in 45 of 45 cases: five
  widths from 180 to 720, text that wraps, marks, line breaks, emoji,
  Chinese and Japanese, and one long word. With the paddings unequal, 5 of
  45 differed by a line.
- What that needs: a template for the box that is only its content host
  (a `Border` named `PART_ContentHost`), no border, no padding, no minimum
  height; the paragraph's margin nothing; and the same `LineHeight` with
  `LineStackingStrategy.BlockLineHeight` on both.

**What things cost** (200 of each, made and laid out, no window): a text
box 2.5 ms; static text 0.34 ms; static text with two colour emoji 1.1 ms
(an `EmojiInline` is about 0.4 ms). **The library's own `TextBlock` is
4.5 ms each even with no emoji in it**, slower than a text box. So static
text is a plain `TextBlock` whose runs are split by `EmojiData.MatchOne`,
with an `EmojiInline` only where there is an emoji (`EmojiData.MatchStart`
says cheaply whether a string can hold one).

**Decided from this (Claude's call): one text box, where the caret is.**
The first spike's "text boxes near the viewport" is narrowed to the block
being typed in; every other block is static text. Static text costs a
seventh, shows emoji in colour, and needs no rule about spelling. The cost
is that the first click in a block lands on static text, so the editor
swaps in the text box and puts the caret where the click was.

## Not verified

- The x64 build has only run under emulation, on the ARM64 dev machine
  (where the x64 package passes all five automation checks). It has never
  run on x64 hardware.
- Windows 10. Everything so far ran on Windows 11.
- A PC with nothing installed. The packages are self-contained and have
  only been started on the machine that built them.
- **A download.** The files of a release are byte for byte the packages
  that were checked (GitHub's digests match `SHA256SUMS`), but nobody has
  downloaded one from the release page and started the app from it. What
  Windows says to this app when it comes from the web (the release notes
  say "More info", then "Run anyway") has not been seen; nor what it says
  to an `.iso` from the web when it is double-clicked (the notes say it may
  ask whether to open the file, which is from memory of other downloads,
  not from this one).
- **A hand opening the disc.** The image was mounted by a script, as a
  drive, and the app was started from it (`check-disc.ps1`). Nobody has
  double-clicked the `.iso`, dragged the `Heracles` folder out in File
  Explorer and ejected the disc. Not known: whether the copy File Explorer
  makes keeps the files read-only, as they are on a disc. The app writes
  nothing beside itself, so that would only matter to someone deleting the
  folder.
- **Clicking a reminder.** The toast spike proved the two halves apart: the
  banner appears, and opening a link of the app's scheme starts the app
  with that link. A click on the banner itself was not made: UI Automation
  could not find the banner, and a real click on the owner's desktop was
  not taken. The owner should click one by hand when reminders are built.
- **The app making itself known to Windows.** Reminders are built
  (2026-11-23), and every part is tested: the plan, the scheduler against
  a made-up list, the toast's XML, the registry keys under a key of the
  test's own, and `ToastCenter` against Windows' real list under a test
  identity (opt-in: `HERACLES_TEST_TOASTS=1`, run once, passed). What has
  never run is the app doing it for itself, since that happens only on the
  real library and the app has only ever been started on fixtures: the
  dozen lines of `App.RegisterWithWindows`, the scheme key under the real
  `HKCU\Software\Classes`, a toast under the identity
  `com.heracles.notes.native`, one arriving, and one being clicked. **The
  first start on the real library is the first time any of it runs.** A
  failure there is caught and logged (`windows: … could not be
  registered`), and the app goes on without reminders. To see it work:
  start the app, give a task a reminder two minutes ahead, close the app,
  wait, and click the banner.
- Reminders with Do Not Disturb on, and on Windows 10.
- **A person using it.** On 2026-11-23 the app was used for a minute with
  a real mouse and keyboard, by `tools\e2e\check-input.ps1` (23 checks, on
  a fixture): a click on a sidebar row; a click into a paragraph and
  typing; Enter for a new block; `# ` making a heading as it is typed; the
  slash menu, by typing `/task` and Enter; Enter on an empty task leaving
  the list; Backspace in an empty block; Ctrl+Z; the Up arrow into the
  block above; Ctrl+K, a name and Enter in Quick Open; a click on a card's
  box; a card dragged onto a card and onto a column; a click on a card to
  open its task; a title typed over and kept on Enter; two subtasks typed
  one after the other; Esc closing the task window; a divider dragged; a
  tab dragged to another pane. That run found one real fault (a dragged
  card or tab was carried as plain text, and a text box it was dropped on
  typed that text into itself), which is fixed. **It is a script, and it
  did only those things.** What follows is still untried.
- **Under a real hand, never yet tried**: the context menus (sidebar rows,
  tabs, a card, a column's header, a comment, a block, selected text);
  Ctrl-click to open beside; the block handle appearing under the pointer,
  its grip dragging a block, and a column's grip; a drag selecting text
  within a block; whole blocks selected by dragging; the link panel; the
  calendars in a task (a date was only ever set by "Set a date", which
  chooses today); editing a comment (its prompt); tabbing through the task
  window; the keyboard shortcuts other than Ctrl+K and Ctrl+Z; scrolling a
  long note; spell-checking.
- **The system clipboard** (Ctrl+C, Ctrl+X, Ctrl+V). No check touches it,
  on purpose: it is the owner's. Copy and paste are tested through the
  data object the clipboard would carry.
- **Files dropped from Explorer** onto a note. The method the drop calls is
  tested; the drop is not.
- Typing through a real input method (composition, candidate windows). The
  real keys sent were plain ones.
- Deleting a note for good while it is open in two windows, through the
  window. The model and workspace tests cover it; the only way to it in the
  window is a context menu.
- Light mode was looked at once (`look-editor.ps1`, 2026-11-23, with
  `--theme-light`): the window and a note of every kind of block read
  properly. Windows itself was in dark mode; the app following a change of
  Windows' own setting while it runs has not been seen.
- A window saved on a second display that is then unplugged.
- A picture from the real web. The fetching is tested against a server on
  this machine (`EditorCoordinatorTests`, over plain HTTP on the loopback
  address): a picture, a page that is not one, one that says it is too
  big, one sent with "not found", and a place that does not answer. HTTPS,
  redirects and a slow server were not tried.
- **A real Electron library.** The import has only ever been given the
  small library in `app/Heracles.Tests/Fixtures` and the ones the core's
  own tests make. The owner's own (`%APPDATA%\Heracles\heracles.sqlite`, if
  the Electron app was used on this PC) is imported the first time the
  native app starts on the real library, which has not happened yet. The
  core copies the old file to `backups\` first and only ever reads it, and
  both the core's tests and `check-legacy.ps1` check that it is byte for
  byte as it was afterwards. The design also asks that the folder be
  confirmed against a machine that has run the Electron build (Electron
  names it `heracles`; Windows file names ignore case): not done.
- **An update from one published version to the next.** It cannot be tried
  until there are two releases with a feed, and 0.3.0 is the first. Two
  things stand in for it. The rehearsal (`check-update.ps1`, 31 checks,
  passed on both packages): an old copy replaced by a new one through the
  app's own windows and its own Exit, and the two refusals, with nothing
  real in it. And, once 0.3.0 was out, `check-published-update.ps1` (11
  checks, passed on both): a copy with the real key in it read the
  published feed, took the published archive from GitHub over HTTPS and
  became 0.3.0, file for file. **In that one only the old copy's version
  was made up** (its own assembly compiled again as 0.0.9; the rest of it
  was the 0.3.0 package). So what is left unproved is narrow: a copy that
  really is an older release, with whatever else then differs between
  the two. **That has now been done, once** (2026-11-25, 0.3.1):
  `check-published-update.ps1 -From 0.3.0`, on both processors, with the
  released 0.3.0 package as the old copy, unchanged. It was offered 0.3.1
  with its notes, fetched the archive from GitHub, installed it through
  its own window and its own Exit, and came back as 0.3.1, file for file
  (11 checks each). What that still is not: a copy on somebody's PC, on
  its real library, with the daily look making the offer. The copy ran on
  a test library, on this machine, and was told which feed to read (the
  real one).
- **Updates under a hand.** Nobody has clicked Check for Updates, read the
  window, or pressed Install and Restart. The rehearsal does all three by
  automation. Skip This Version, Remind Me Later and Cancel during a
  download are covered by tests of what they decide, and were not pressed
  in a window at all.
- **Text typed in the moment before an update installs.** The install
  quits the app through its ordinary Exit, which saves every editor first,
  and the rehearsal takes that path (unlike the Mac's, which stops its
  copies with a signal). But nothing was being typed when it did.
- **A copy that cannot replace itself**: run from the disc, or from a
  folder Windows keeps it from changing. It is meant to say so in the
  update window and offer nothing to install (`UpdateInstaller.WhyNotHere`,
  tested for a folder at the top of a drive and for one that is not
  there). Not seen in a window, and not tried from a real disc.
- **What Windows says about the new version when it arrives by itself.**
  The first copy of the app comes from the web and Windows asks before
  running it. An update is fetched by the app, not by a browser, so the
  files should carry no mark of the web and nothing should be asked. On
  this machine nothing was (the rehearsal's copies came back by
  themselves), but those archives came from this machine. Defender, or a
  company's rules, may think otherwise of a program that replaces itself.
- Updates on Windows 10, on x64 hardware (the x64 rehearsal ran under
  emulation), behind a proxy, and on a slow or broken connection. A
  download that stalls is given up after a minute with nothing kept,
  which a test covers only for a download that never starts.
- **The icon on a reminder.** The identity now names the picture
  (`IconUri`), tested against a registry key of the test's own. No
  reminder of the app's has been seen, with or without it.
- **The icon as Windows shows it**: in File Explorer, on the taskbar, in
  Alt-Tab, at each of its ten sizes. It was looked at as the script draws
  it (every size, on white and on black) and in the title bars of the
  app's windows.
- **The way back from a lost update key.** There is none in the app, and
  none was tried. See "Decisions".
- **The installer under a hand** (0.3.1). Every run of it was silent, by
  `check-installer.ps1`: its pages were never looked at, the folder page
  and the desktop-shortcut box were never used, and "Launch Heracles" at
  the end was never ticked. It has run on this PC only (the x64 one under
  emulation), never on Windows 10, and never as a file from the web: what
  SmartScreen says to an unsigned installer has not been seen.
- **An installed copy taking a real update.** The check replaces the
  installed folder itself, by the two renames the updater makes, and then
  sees that Windows still knows the copy and can remove it. The updater
  doing that to an installed copy has not been watched; its swap is the
  same code whatever put the folder there.
- **An install over an older installed version.** There is no older
  installer yet. The script empties a folder that holds Heracles before
  installing; that line has only run on a folder it had just filled.
- **Uninstalling a copy that had made itself known to Windows.** A run
  on a test library registers no link scheme and no reminder identity,
  so the check plants both, pointing at the installed copy, and sees
  them removed (and does that only where the account has none of its
  own; where it has, it sees them left alone). **On this PC the account
  has both, from a real copy, so only "left alone" was ever seen: the
  script lines that remove them (`RemoveRegistrations` in
  `Heracles.iss`) have never run.** They would run on a PC whose only
  copy is an installed one, at its uninstall.

## Known faults

Found and not yet fixed. Each is small; none loses data.

- The sidebar has no keyboard navigation of its own beyond the list's
  (no left and right to fold and unfold).
- A `file://` picture URL written with two slashes before a drive
  (`file://C:/x.png`) is read as naming another machine and the picture is
  left as written, not imported.
- `tools\e2e\capture.ps1` still looks for Windows Security dialog windows,
  which Toolbox needed and Heracles does not. Harmless.
- **Emoji in the block being typed in are drawn in one colour**, as
  outlines, and in colour again when the caret leaves. This was the plan
  (a text box counts characters, and a colour emoji is not one); the
  library's inline has still to be tried inside the text box.
- Text copied from an app that offers only RTF is pasted as plain text.
  The Mac converts RTF first.
- Text cannot be dragged out of or into a block's text. (Picture files can
  be dropped on the page.)
- The block handle's grip cannot be reached by the keyboard or by UI
  Automation, so the block menu is the mouse's only. Its commands are also
  in the Format menu, except Duplicate, Delete and Copy Link to Block.
- A numbered list begun by typing `+1. ` is not a list here and is on the
  Mac (see "Current state", stage 3).
- Emoji in a card's title, a label or a comment are drawn in one colour,
  as they are in the sidebar's titles and in tabs.
- What is typed in a box for something new (a card, a label, a comment)
  and not handed over is dropped when the board or the task window goes.
  The Mac does the same.
- A task window is not reopened when the app starts again.
- The summary of an import names a note that had to be adjusted by its id,
  not by its title (the core's words; the Mac shows the same).
- A reminder can be set for a time already past. It is kept on the task and
  never scheduled. (The Mac allows the same.)
- The title and icon stand over the editor's column by fixed margins, so
  they sit a few pixels off it when the scroll bar shows.
- An update is the whole app each time (61 MB for ARM64, 66 for x64), and
  while it installs there are three copies of the app on the disc: some
  550 MB, for a few seconds. (No delta updates, as on the Mac.)
- A daily look that fails says nothing in the app: the reason is in the
  log (`updates: stopped: ...`). One asked for from the menu says why.
- When an install cannot go through once the app has quit (something has
  one of its files open, the folder cannot be changed), the app as it was
  is started again and says nothing. The log has the reason, and the next
  daily look offers the update again.
- Release notes in the update window show a link as its words, and it
  cannot be followed. (On purpose: nothing in a feed is more than words.)
- The offer made by the daily look opens without taking the keyboard, so
  that typing in a note is not interrupted. It may therefore open behind
  another program's window, and wait there to be seen.
- Reminders already handed to Windows stay with Windows when the app is
  uninstalled: they still appear at their times, and clicking one then
  opens nothing. (The uninstaller cannot take them back; only the app
  can, and it is gone.)
- The installer does not offer to remove the library, and the
  uninstaller leaves it (`%APPDATA%\Heracles`) and the log
  (`%LOCALAPPDATA%\Heracles`) where they are. On purpose, and said
  nowhere in the uninstaller's own window.

## Current state (update in every commit)

**Fetch before anything else, on every machine: `git fetch origin`, then
`git status`.** This repository is worked on from more than one machine.
On 2026-11-25 a session on the Windows PC built a whole release on a
checkout that was one commit behind GitHub, where the licence commit
below, made on the Mac two days before, was waiting. It found out when its
push was refused, rebased its two commits onto that one, and had to
package and check everything again, since the build number is the commit
count. The status a session is shown when it starts says nothing of what
is on GitHub.

**0.3.1 (2026-11-25, later the same day as 0.3.0): the same app, with an
installer.** On the owner's word, given in a Toolbox session for both
apps (quoted under Decisions): every release is to carry an installer
for each processor beside the disc image, and one was to be released at
once. It is a new version and not files added to 0.3.0's release: the
disc image changes, and the app gains one thing that installed copies
need. (The session believed this repository's releases to be immutable,
as Toolbox's are. GitHub says they are not: `immutable` is false on every
one. The script's care about drafts costs nothing either way.) **The
latest release is 0.3.1**:
https://github.com/tacticalbadger/Heracles-Windows/releases/tag/v0.3.1,
the tag `v0.3.1` at commit `26a618d`, build 52, with an installer, a disc
image and an update archive for each processor, `update-feed.json` and
`SHA256SUMS`. All of `main` is pushed, this commit included. What changed:

- `tools\installer\Heracles.iss` and the installer step in `tools\build.ps1
  package` (Inno Setup 7.1.1, installed on the PC that day, per user).
  The package step now makes three things for a processor:
  `Heracles-<version>-win-<arch>-setup.exe`, the `.iso`, which holds that
  installer beside the `Heracles` folder and a README that names it
  first, and the `.zip` as before.
- `Support/InstalledCopy.cs`, called once at start: the one change to the
  app. Eight tests.
- `tools\publish.ps1` requires, checks and sends the installers, and its
  install text is rewritten around them.
- `tools\e2e\check-installer.ps1` (new), `check-disc.ps1` (the disc's new
  contents), and `check-published-update.ps1 -From <version>`, which
  takes an earlier release's package as it was handed out for the old
  copy, where it otherwise compiles the current one under an old number.
- The README, release notes for 0.3.1, and these notes (Decisions,
  "Found on Windows", "Not verified", "Known faults").

How it was checked, and how the release went, is at the very end of
these notes ("0.3.1: the installer"). **What is below, from "As of
2026-11-25", was written for 0.3.0 and is still true of the app**, which
0.3.1 does not change.

**License, 2026-11-23, on the owner's word and pushed** (commit `4946a75`,
made on the Mac from a fresh clone, so the tests were not run for it):
`LICENSE` is proprietary (see Conventions) and the README's License
section says so. Up to `6132977` the repository carried the MIT license.
No code changed in that commit.

As of 2026-11-25, on `main`. `main` on GitHub was replaced on 2026-11-22
(commit `9b56acf`, a force-push on the owner's word): the Electron app and
its history are no longer there. Any other clone of this repository must
`git fetch origin` and `git reset --hard origin/main` before it is used,
and must never push its old history. 0.2.0 and 0.2.1 were released on
2026-11-23. **The latest release is 0.3.0** (2026-11-25, on the owner's
word: "this version needs feature parity and testing, with a new release
pushed to git hub".
https://github.com/tacticalbadger/Heracles-Windows/releases/tag/v0.3.0,
the tag `v0.3.0` at commit `01edb61`, build 50, with a disc image and an
update archive for each processor, `update-feed.json` and `SHA256SUMS`).
How the release went is under "Since stage 7" at the end. **All of `main`
is pushed, this commit included.** What is committed after that stays
local until he says push: `git log origin/main..HEAD` lists it.

**What 0.3.0 is: the Mac app's last sixteen commits, brought across.** The
Mac repository was read from `5dd0252`, which this app was built against,
to `91076bf`, its 0.4.0. It had gained four things, and this app now has
them:

- **An icon.** The Mac's own, a notebook page with a heading, a checklist
  and lines of text, drawn for Windows from the same layers: on the
  program, its windows, the taskbar and its reminders.
- **Updates over the air.** The app looks for a new version once a day and
  from Help ▸ Check for Updates…, shows what changed, and installs it when
  the user agrees, after every editor has saved. It takes an update only
  if the feed that announces it was signed with the update key, and the
  archive is the one that feed describes. The Mac does this with Sparkle;
  here it is the app's own code, for the reasons under "Decisions". **From
  0.3.0 on nobody has to download a disc image again; 0.2.1 and earlier
  have no updater and cannot be told.**
- **"The original app"** where the app, the core and the README said
  "Electron", and the copy an import makes named
  `heracles-original-<time>.sqlite`.
- **The licence**: proprietary, as the Mac's. The repository has carried
  it since 2026-11-23 (above); 0.3.0 is the first release under it.

And three things found on the way, each fixed with a test that fails
without the fix: the pipe a second copy hands over through was made a
moment after the name was taken (see "Found on Windows"); the harness
would have driven the owner's own copy of the app, had one been open; and
the copy that installs an update took its command line at its word.

**Where 0.3.0 stands.** From a clean build: no warnings, 203 Rust tests,
763 .NET tests and one skipped on purpose (155 of them new: 148 of
updates, 6 of the icon on reminders, 1 of the hand-over), 116 scripted
checks of the real window by automation (`check-shell.ps1` 32,
`check-editor.ps1` 15, `check-boards.ps1` 39, `check-transfer.ps1` 10,
`check-legacy.ps1` 20), 19 of a packaged disc (`check-disc.ps1`), 31 of a
whole update (`check-update.ps1`), 23 by a real mouse and keyboard
(`check-input.ps1`), and, once it was out, 11 of the published update
taken from GitHub (`check-published-update.ps1`). The checks were run
against the packages that were released: the disc, the update and the
published update on both, the window checks and real input on the ARM64
one, and `check-shell.ps1` on the x64 one too (under emulation, as
everything of x64 is here). Every rule an update rests on was broken on
purpose
once (ten of them: the feed's signature, the archive's checksum, files
landing outside the folder, a feed from the command line, an older
build offered, somebody's folder removed, the three guards on the copy
that installs, and a look that failed counted as the day's), and each
time a test failed.

**What that does not say** is under "Not verified". The entries that
matter most are the ones 0.2 already had (nobody has used the app; it has
never run on the real library) and one that is new: **no copy has yet
updated itself from one published version to the next**, because 0.3.0 is
the first that can. The rehearsal stands in for it, and so does a copy
that took the published 0.3.0 from GitHub with only its own version made
up. The first real update is the one to whatever is released after
0.3.0.

**What is left, and it is the owner's**:

1. **Keep a copy of the update key, before anything else.** It is in this
   account's Windows credentials on this PC and nowhere else. Run
   `tools\update-key.ps1 -Export <a file outside the repository>`, put
   what the file holds into a password manager, and delete the file. If
   the key is lost (the account goes, Windows is put in again), no copy
   of 0.3.0 or later can ever be updated: everyone downloads by hand
   again, as before. Nobody but the owner can do this: a session must
   never write the private key anywhere, and has not.
2. *Use it.* Put 0.3.0 on this PC from its disc image, into
   `%LOCALAPPDATA%\Programs`, and start it on the real library. That first
   start imports a library of the original app if one is there, shows the
   summary, and registers the app's links and reminders with Windows. Ten
   minutes of typing, dragging blocks by their handles, right-clicking
   things, copying and pasting. Give a task a reminder two minutes ahead,
   close the app, and click the banner when it comes. Choose Help ▸ Check
   for Updates…: it should say that Heracles is up to date. What goes
   wrong goes in "Known faults", or gets fixed, and what is fixed is the
   next release, **which that copy should then offer by itself**.
3. *The next release*, when he says so. A version that has a release is
   taken (the script refuses it): change `VERSION`, write
   `docs\release-notes\<version>.md` (it is what the update window shows),
   commit, and push on his word. Then, from the commit that was pushed:
   `tools\build.ps1 core package -Arch x64`, then `tools\build.ps1 core
   package` (this machine's last, so the bindings match its DLL), then
   `tools\build.ps1 feed` (it needs both packages and the update key, and
   refuses a build number that is not higher than the published one).
   Check them: `tools\e2e\check-disc.ps1`, `tools\e2e\check-installer.ps1`
   and `tools\e2e\check-update.ps1`
   for each processor (`-Arch x64` for the other). Then
   `tools\publish.ps1 -DryRun`, and when that reads right,
   `tools\publish.ps1`, which ends by reading the feed back from where
   installed copies read it. Then `tools\e2e\check-published-update.ps1`,
   for each processor: a copy with the real key takes what was just
   published (and with `-From <the version before>`, while that
   version's folder is still in `build\release`, the copy is that
   release itself). The packages must be built from the very
   commit that is pushed (the build number is the commit count, and the
   script refuses any other), so a commit made after packaging means
   packaging again, and the feed again.

Nothing else is planned. The design's "out of scope" list, and "Known
faults" above, are what a next session would choose from.

What follows this paragraph, down to "What follows is the record", is the
head of these notes as it stood at 0.2.1, kept for what it says of the
disc image and of the app as a whole.

**Before 0.3.0, the latest release was 0.2.1, handed out as disc images** (2026-11-23, on
the owner's word: "the next release as an iso".
https://github.com/tacticalbadger/Heracles-Windows/releases/tag/v0.2.1,
the tag `v0.2.1` at commit `2444eeb`, build 46, with
`Heracles-0.2.1-win-arm64.iso`, `Heracles-0.2.1-win-x64.iso` and
`SHA256SUMS`). The app is 0.2.0's, unchanged: no file
under `app/` or `core/` differs but the version. What changed is the
package. `tools\build.ps1 package` makes
`Heracles-<version>-win-<arch>.iso` where it made a zip: the app in a
folder named `Heracles` and a `README.txt`, on a disc named
`Heracles <version>`. The step reads the image back, by mounting it, and
refuses it if any file differs from what went in (seen to say so for a
file changed, missing, added, or spelled in another case), and it now
searches the whole image, not three files of it, for this machine's
profile folder. `tools\e2e\check-disc.ps1` opens the image as a drive and
starts the app from the disc (16 checks; given the ARM64 disc under the
x64 name, three of them fail, as they should). `tools\publish.ps1` sends
the `.iso` files, and its install text is rewritten for a disc. How the
release went is under "Stage 7" at the end.

**Where things stood at 0.2.1: the app built and released, all seven
stages of the design done.** 0.2.0 came first (2026-11-23:
https://github.com/tacticalbadger/Heracles-Windows/releases/tag/v0.2.0,
the tag `v0.2.0` at commit `4fd1a3d`, build 44, with zips for ARM64 and
x64), and is still there. The app
does what the Mac app does: one process per library, any number of
windows, a sidebar, panes and tabs, Quick Open; a native block editor with
the Mac editor's behaviour (every kind of block, Markdown as it is typed,
the slash menu, marks and links, the editor's own undo, copy and paste,
pictures, and saving that loses nothing on close or quit); boards with
columns and cards, a window for each task with everything the Mac's task
has, and reminders handed to Windows; files imported and a note exported
from the File menu; and a library left by the Electron app brought across
at the first start, with a summary. Every line of the parity list is
ticked. From a clean build at the end: no warnings, 203 Rust tests, 608
.NET tests and one skipped on purpose (it talks to Windows' notification
platform: `HERACLES_TEST_TOASTS=1` runs it), 114 scripted checks of the
real window by automation (`check-shell.ps1` 30, `check-editor.ps1` 15,
`check-boards.ps1` 39, `check-transfer.ps1` 10, `check-legacy.ps1` 20), 16
of a packaged disc (`check-disc.ps1`) and 23 by a real mouse and keyboard
(`check-input.ps1`). Both packages were built, each was started and
checked, and they are the files of the release: see "Stage 7" at the end.

**What that did not say**, and still does not, is under "Not verified",
and three entries matter. The releases change none of them: each was
made, on the owner's word, before any of the three was tried. No person
has used the app yet. A script used it for a minute with
a real mouse and keyboard (`check-input.ps1`: typing, keys, clicks and
drags, and it found a fault no test could have), but it did only what it
was written to do: the block handle, the context menus, selecting with the
mouse and the clipboard are among what is still untried. The app has never
yet made itself known to Windows (its link scheme, its reminders), because
it does that only on the real library and has only ever been started on
fixtures: the first start on the real library is the first time that runs.
And that same first start is when a real Electron library, if there is
one, is imported for the first time: only made-up ones have been.

What follows is the record, stage by stage, as each was finished. Read it
for how a thing was done; some of it describes the app as it was then.

**Stage 2 (model and shell) is built: the app can be browsed.** One process
per library; any number of windows on one model; a sidebar (favorites,
notebooks with their trees, archive, trash, search); panes with tabs,
splits within splits and draggable dividers; Quick Open; the icon picker
with colour emoji; the note actions and menus; the layout of every window
remembered. A note's content is shown as plain text that cannot be changed:
the editor is stage 3 and 4. A todo list says its board is still to come.
Verified from a clean build by 139 .NET tests, 203 Rust tests, and the 30
checks of `tools\e2e\check-shell.ps1`, which were also run against both
packaged builds (`win-arm64`, and `win-x64` under emulation). What follows
below is the record of stages 0 and 1.

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
- A bundle of the old history is beside this checkout on the dev machine
  (`..\Heracles-Windows-old-history.bundle`), for the owner to keep or
  delete. The old history must never be pushed again. GitHub keeps the old
  commits reachable by hash for some time; GitHub Support removes them on
  request.
- **Nothing has been released.** There is no GitHub Release and no
  `publish.ps1`: the app only opens a library and counts its notes. The
  first release is 0.2.0, when the app is usable (stage 7 of the design).

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

- **Spike 2 of 4 is done**: model offsets map onto a `RichTextBox` both
  ways, and the editor learns what was typed by a diff anchored at the
  caret, holding the typing style itself and redrawing a block's text where
  WPF chose another style.

- **Spike 3 of 4 is done**: WPF draws emoji in one colour; the `Emoji.Wpf`
  library draws them in colour, including inside a subclass of the standard
  `RichTextBox`. Whether to take the library is the owner's decision.

- **Spike 4 of 4 is done**: Windows delivers a scheduled reminder for an
  app with no installer, with the app closed, given one registry key the
  app writes itself. Clicking the banner is not verified.

Decided by the owner on 2026-11-22: the earlier account name stays in the
one link in `docs/review-and-native-rewrite.md`, as in the Mac repository.
Do not change it. That decision also kept the name in `LICENSE`; the
owner's decision of 2026-11-23 on the license (see Conventions) replaced
that half, in both repositories.

**Stage 3 (the editor's document model) is built and verified** (2026-11-23):
from a clean build, no warnings, 203 Rust tests and 302 .NET tests pass,
`tools\e2e\check-shell.ps1` passes its 30 checks, and nothing under
`app/Heracles/Editor/` names a `System.Windows` type. Plan:
`docs/superpowers/plans/2026-11-22-windows-stage-3-editor-model.md`.
Reviewed by the session that wrote it; no one else has read it.

- Done, each translated from the Mac with its tests: `JsonValue` and
  `RichText` (Task 1); `Block`, `EditorDocument` read from and written to
  the stored JSON, `BlockIds`, `EditorSelection` (Task 2); the tree and
  every edit that changes a note's shape (Task 3): split, the two deletes,
  indent and outdent, turning a block into another kind, remove, duplicate,
  move (with columns), the Markdown beginnings, paste, selecting whole
  blocks, blocks as plain text, and on `RichText` the inline Markdown marks
  and linking a typed web address.
- 302 .NET tests pass (203 Rust). Of the 117 for Task 3, 97 are the Mac's
  `EditorDocumentTests.swift` test for test (the plan said 98; there are
  97), and 20 are new: finding blocks, and what a C# translation can get
  wrong that Swift cannot (a stale id or offset from a view, a note
  emptied, an id given twice, a copy that shares something with its
  original).
- Where the Windows model differs from the Mac's on purpose: a numbered
  list begins from digits only (the Mac also reads a sign, so `+1. ` makes
  a list there); `Paste` brings a stale range back into the text before
  using it; `Move` takes an id given twice as one block.
- (At the end of stage 3 nothing in the app used the model. Stage 4 put
  the editor on it.)

**Stage 4 (the editor's views) is built and verified** (2026-11-23): from a
clean build, no warnings, 203 Rust tests and 437 .NET tests passed; then,
with the sidebar change below, 438, and both scripted checks of the window
(30 and 15). Reviewed by the session that wrote it, by breaking the code on
purpose where a test had passed at its first run (fourteen breaks over
the stage, thirteen caught; the one that was not is a guard kept against an
order of events WPF was seen to give both ways) and by looking at the
window in dark and in light; no one else has read it. Plan:
`docs/superpowers/plans/2026-11-23-windows-stage-4-editor-views.md`, six
tasks: the theme and the text box; block views on a page; typing, keys,
undo, marks and the slash menu; block actions, the clipboard, pictures and
the mouse; the editor in the app; verify. A fifth spike was run for it
("Views with no window", above), and what it decided is in the plan: one
text box, where the caret is; tests that never show a window, take the
keyboard or touch the clipboard.

- Done (Task 1): `EditorTheme` and `BlockTextBox`, with 37 tests on one
  STA thread and no window (`app/Heracles.Tests/Editor/ViewTestSupport.cs`:
  `Sta.Run`, `Views.Layout`, `Views.Pump`, `Views.Type`). A text box and
  static text of the same block are the same height for fifteen kinds of
  text at five widths. 339 .NET tests pass. Nothing in the app uses either
  yet.
- Done (Task 2): a note is shown on a page. `BlockViews.cs` (a view for
  each kind of block, laid out by hand as the Mac's are: `HandLaidView`
  places things in `MeasureOverride` and arranges them in
  `ArrangeOverride`), `BlockEditorView.cs` (the page: views kept by block
  id from one edit to the next, the block under a point, where a drop would
  land, the selection drawn over whole blocks), and of
  `BlockEditorController.cs` what loads a note and holds the selection.
  **Only the block with the caret has a text box, and giving a block the
  text box moves nothing on the page** (tested for twelve blocks at two
  widths). 600 blocks open in well under the test's three seconds. 354
  .NET tests pass. Tests: `EditorHarness.cs` (the Mac's harness, with no
  window) and `EditorLayoutTests.cs`.
- Done (Task 3): typing, keys, undo, marks, links and the slash menu, in
  `BlockEditorController.Typing.cs` (the Mac's one controller file is two
  here, in the same order). The Mac's `EditorTypingTests`,
  `EditorShortcutTests` and `EditorNavigationTests` pass with real text
  boxes (`EditorTypingTests.cs`, 43 tests with the Windows ones). 397
  .NET tests pass. How it works, for whoever changes it:
  - What was typed is found by `Diff(old, now, caret)` and put into the
    document with `typingStyle`, the marks the editor holds for what is
    typed next. Then, if the box shows the text otherwise than the document
    says, `Restyle` redraws it (`BlockTextBox.Show`) and puts the caret
    back. `TypeInto` types by way of the document, for a line break (which
    a text box would make a paragraph of) and for code's indent.
  - **A text box reports its caret after the text has changed, and for a
    move that moved nothing.** Marks chosen for what is typed next
    (`chosenStyle`: Ctrl+I with nothing selected, or the end of `**bold**`)
    therefore hold until the caret is somewhere else.
  - An edit changes the document where it is; `Edit` clones it first, to
    compare with and to go back to.
  - In a test a key is `harness.Press(Key, ModifierKeys)`: it goes to
    `BlockTextBox.OfferKey` as a key press does, and what the editor leaves
    alone is carried out by the `EditingCommands` command that key is bound
    to.
- Done (Task 4): block actions, the clipboard, pictures and the mouse, in
  `BlockEditorController.Actions.cs` and `BlockEditorView.Mouse.cs`. The
  Mac's `EditorBlockActionTests` and `EditorPasteboardTests` pass
  (`EditorActionTests.cs`, 28 tests with the Windows ones). 425 .NET tests
  pass. For whoever changes it:
  - **Everything the mouse can do is a method that takes a point**
    (`ActivateAt`, `ClickAt`, `ShowHandle`, `BeginGripDrag` /
    `ContinueGripDrag` / `EndGripDrag`, `BeginTextDrag` / `ContinueTextDrag`
    / `EndTextDrag`, `FilesDraggedOver` / `FilesDropped`, `Resize`). The
    tests call those. The mouse handlers only turn events into those calls,
    and **the handlers themselves have never run**: no test moves the mouse.
  - Copy and paste go through `IDataObject` (`CopySelection`,
    `AddCopiedText`, `CutSelection`, `Paste`). Only `PasteFromClipboard`
    and `CopyToClipboard` touch the system's clipboard, and no test calls
    them. Pasting is the editor's wherever the caret is (Ctrl+V, Shift+Insert,
    and the text box's own Paste command are all taken), because a text box
    would only take text. A page copied from a browser arrives wrapped;
    `HtmlFragment` takes the wrapping off before the core converts it.
  - A text copied from a block only as rich text (RTF, no HTML) is pasted as
    plain text. The Mac converts RTF to HTML first; this does not.
- Done (Task 5): **the editor is in the app, and a note can be written.**
  `Notes/EditorCoordinator.cs` (loads a note, saves 300 ms after a change
  with the editor's id as the origin so it never reloads from its own save,
  follows a save made elsewhere unless it holds unsaved typing, keeps
  pictures in the image store and reads them back, opens links by
  `NavigationPolicy`), `NoteDetailView` with the editor in place of the
  read-only text, Edit and Format menus that act on the focused pane's
  editor, and saving before a tab, a pane or a window closes and before the
  app ends (`AppModel.RegisterSaver`, `SaveEverything`). 11 tests against a
  real core (`EditorCoordinatorTests.cs`); 437 .NET tests pass.
  `tools\e2e\check-editor.ps1` passes its 15 checks in the real window and
  `check-shell.ps1` still passes its 30. Pictures in dark and in light were
  looked at, and two faults found by looking were fixed (see "Found on
  Windows": the emoji's colour, the text box's colour).
- Done (Task 6): the stage verified from a clean build, as said above.
  And one fault of stage 2 fixed, because the editor made it constant: a
  save changes a note's date, the core reports the note changed, and the
  sidebar was drawn again from the top at every pause in typing. The
  sidebar now draws its rows again only when they would come out different
  (`SidebarRows.ShowTheSame`), and stays scrolled to where it was when it
  does.

**Stage 5 (boards, task details and reminders) is built and verified**
(2026-11-23): from a clean build, no warnings, 203 Rust tests, 558 .NET
tests and one skipped on purpose, and the three scripted checks of the
window (30, 15 and 39). Reviewed by the session that wrote it, by breaking
rules on purpose where a test had passed at its first run (six, all
caught) and by a read-through that found three things, each then given a
test that failed first: a task deleted or sent to the trash under an open
task window was still written to; a program that only hosts the app would
have been given its links; and the process took an identity it did not
need. No one else has read it. Plan:
`docs/superpowers/plans/2026-11-23-windows-stage-5-boards-and-reminders.md`.

- Done (Task 1): what a board and a task window decide, in classes with no
  view in them, against a real core. `TaskDates` and `ReminderPlan` are the
  Mac's with its seven tests; `Board`, `TaskDetail`, `ReminderScheduler`
  (behind `IReminderCenter`), and the registry keys (written under a key
  that is passed in) are new here. Six rules were broken on purpose, one at
  a time, and each was caught.
- Done (Task 2): the views, and Windows. `BoardView` and `TaskDetailWindow`
  draw `Board` and `TaskDetail` and call them; views are kept by column, by
  card and by subtask from one reload to the next, so a box being typed in
  keeps the keyboard when the board changes under it. `ToastCenter` is
  Windows' own list of scheduled notifications; `App.RegisterWithWindows`
  hands the reminders over at start, on the real library only.
  `check-boards.ps1` passes its 39 checks, and the pictures were looked at
  in dark and in light.
- For whoever changes it:
  - **A board redraws only when the change feed said it changed**
    (`AppModel.BoardVersion`), and a task window the same. Each edit in
    `TaskDetail` also reads the task again itself, so the tests need no
    view.
  - Notes in a task are held by `TaskDetail` while they are typed
    (`TypeNotes`), saved 600 ms after typing pauses and before the window
    moves to another task or closes. `Notes` is what is being typed, or
    else what is stored: drawing it never replaces typing.
  - A subtask made a card becomes the task the window starts from: there is
    no way back up to a task it is no longer under. (The Mac leaves the old
    breadcrumb.)
  - A reminder's identifier is the Mac's
    (`heracles.reminder.<taskId>.<remindAt>`), which is longer than the tag
    Windows knows a toast by. It rides in the toast's own link
    (`ReminderToast`), and is read back out of the toast's content. So is
    the title, which is how a renamed task's reminder is found and made
    again.
  - The scheduler asks Windows nothing while the reminders stand as they
    did when it last handed them all over. The library changes at every
    pause in typing; a reminder almost never does.
  - Tests write their log lines to `%TEMP%\heracles-tests-log`, not into
    the log of the app the owner runs (a module initialiser in
    `TestLibrary.cs`). Before this stage they wrote into the real one.
  - **Every write `TaskDetail` makes asks first whether the task is still
    there and its list out of the trash** (`CanChange`): another window
    may have changed either since this one last looked.
  - The app does not give its process an identity
    (`SetCurrentProcessExplicitAppUserModelID`), though the toast spike
    did. Handing a reminder to Windows does not need it (the opt-in test
    does it from a process that never said who it was), and it parts a
    running app from a taskbar pin made from its program file.
  - Links are registered to the app's own program only: started by a
    host, the path would be the host's.

**Stage 6 (import, export and the Electron library) is built and verified**
(2026-11-23): from a clean build, no warnings, 203 Rust tests, 600 .NET
tests and one skipped on purpose, and the five scripted checks of the
window (114 in all). Reviewed by the session that wrote it, by breaking
rules on purpose where a test had passed at its first run (six, five
caught; the one that was not is a guard the core makes redundant, kept as
on the Mac); no one else has read it. Plan:
`docs/superpowers/plans/2026-11-23-windows-stage-6-import-export.md`.

- Done (Task 1): File > Import (Ctrl+Shift+I) and File > Export as
  Markdown (Ctrl+Shift+E). `Model/Transfer.cs` says which files the dialog
  offers and what an exported note is called; the reading and writing are
  the core's. `check-transfer.ps1` goes through the system's own dialogs.
- Done (Task 2): a library the Electron app left (`heracles.sqlite` in the
  library folder) is brought across before the first window opens, and
  `ImportSummaryWindow` says what came. `check-legacy.ps1` checks the first
  start, the second, and an old library that is not a database.
- For whoever changes it:
  - The import is asked for at every start, and is cheap when there is
    nothing to do: no old file, or a library that already holds notes.
    The core refuses a second import by a mark it keeps in the new
    library, so a library emptied by hand does not fill up again.
  - The core copies the old database through SQLite's backup interface.
    The copy is the same library, not the same bytes.
  - The system's file dialogs are driven by window messages (see "Found on
    Windows"). The Open dialog gives the files back in an order of its own.
  - A note's name in the automation tree (`NoteText:<leaf>`) holds line
    breaks when the note has several blocks, and `ui.ps1 find` then prints
    several lines, of which the helpers read the first. Ask for a block
    by its words (`Get-UiId -Name '…' -Prefix 'Block:'`).

**Since stage 6** (2026-11-23), before packaging:

- Pictures, the last feature on the parity list not ticked. Fetching one
  from the web had been "written and never run"; it is now run, against a
  web server of the test's own on this machine, and two rules broken on
  purpose (the limit on size, and "not found") were each caught once the
  test was made sharp enough to tell. The seeded note "Every kind of block"
  holds a real picture, made in code, beside one that is missing, and
  `look-editor.ps1` shows both.
- The view tests' shared thread takes one test at a time (see "Found on
  Windows"). This was a fault in the tests since stage 4, found when a new
  slow test made it show. 604 .NET tests pass, three runs in a row.
- **A minute of real input**, the first the app has had
  (`tools\e2e\check-input.ps1`, 23 checks). The design asks that each
  mouse path be driven at least once through the harness, and until now
  none had been. At its first run 20 of 23 passed: typing, Enter, Markdown,
  Backspace, undo, the arrows, Quick Open by its key, clicking and ticking
  a card, dragging a card onto a card, the task window, Esc, a divider and
  a tab dragged. Of the three that failed, one was the check asking the
  wrong thing (text inside a quote is a paragraph), one was the harness
  clicking where a box scrolled out of view would have been (it now
  refuses that, and refuses any click or drag that would not be over the
  app), and one was a real fault: what was dragged was carried as plain
  text, so a text box under the drop typed it into itself, and a card let
  go over "Add a card" never reached its column. Both are fixed, with
  tests for how a card and a tab are carried, and all 23 pass. 608 .NET
  tests pass.

**Stage 7 (package and release) is done: 0.2.0 was released on
2026-11-23, and 0.2.1 the same day.**
Plan: `docs/superpowers/plans/2026-11-23-windows-stage-7-package-and-release.md`.

- Done (Task 1): `tools\publish.ps1`, `docs\release-notes\0.2.0.md`, and
  `README.md` rewritten for the app as it is, against the Mac's README
  feature for feature.
- Done (Task 2): from a clean build, no warnings, 203 Rust tests, 608 .NET
  tests and one skipped on purpose. Both packages built
  (`Heracles-0.2.0-win-arm64.zip`, 61 MB; `Heracles-0.2.0-win-x64.zip`,
  66 MB; `SHA256SUMS`), and neither names the build machine's profile
  folder (the package step checks). **Each package was started on the
  fixture and passed all five automation checks** (114 each; the x64 one
  under emulation), and the ARM64 package passed `check-input.ps1` too (23,
  by a real mouse and keyboard).
- `tools\publish.ps1 -DryRun` was read first: it checks the checksums and
  that both packages are the build of the current commit, and shows the
  title, the files and the notes.
- **The release** (2026-11-23, on the owner's word). `main` was pushed,
  after 203 Rust and 608 .NET tests had passed once more and every commit
  going out had been searched for the owner's names, for paths under a
  profile folder and for anything shaped like a key (none). Then
  `tools\publish.ps1`. Its first real run stopped before it had sent
  anything, at the list of releases (see "Found on Windows": a JSON list
  in PowerShell 5.1). That line was put right, and `Invoke-Git` was made
  to outlive anything git writes to stderr; each fault was seen to fail
  first in a throwaway script. The second run made the tag `v0.2.0` at
  `4fd1a3d`, a draft, attached the three files and published it.
- **Read back from GitHub with no credential**, as anyone sees it: not a
  draft, not a pre-release, the latest release, three files, and the
  digests GitHub gives for both zips are the ones in `SHA256SUMS`. The
  files out there are the packages that were checked.
- The tag points at `4fd1a3d`, the commit the packages were built from.
  The script that published them is the one in the commit after it: at the
  tag, `tools\publish.ps1` still has the fault.
- Still untried in the script: `-Replace`, `-Prerelease`, and carrying on
  with a draft that a failed run left behind. GitHub says the 0.2.0
  release is not immutable, so `-Replace` is open to it.
- **0.2.1, the same app as disc images** (2026-11-23, on the owner's word).
  203 Rust and 608 .NET tests passed on what was then committed as
  `2444eeb` and pushed, and both packages were built from it (build 46:
  `Heracles-0.2.1-win-arm64.iso`, 176 MB; `Heracles-0.2.1-win-x64.iso`,
  165 MB), each read back by the package step. Against those packages:
  `check-disc.ps1` on both (16 each: the image opened as a drive, and the
  app started from the disc, the x64 one under emulation); all five
  automation checks on the ARM64 package (114) and `check-shell.ps1` on
  the x64 one (30); and `check-input.ps1` on the ARM64 package (23, by a
  real mouse and keyboard, with the owner told first). The dry run read
  right, and `tools\publish.ps1` published at its first run this time,
  with a release already in the list. Read back from GitHub with no
  credential: `v0.2.1` at `2444eeb`, not a draft, the latest release,
  and the digests GitHub gives for both images are the ones in
  `SHA256SUMS`.
- Packages are not kept: they are under `build\release`, which is not in
  git, and they are only good for the commit they were built from. Those
  of 0.2.0 and 0.2.1 went from this machine with a clean build on
  2026-11-25 (`tools\build.ps1 clean` removes all of `build\`). GitHub
  has them.

**Since stage 7: parity with the Mac's 0.4.0** (2026-11-25, on the owner's
word: "review the mac OS app for changes made since the last commit. this
version needs feature parity and testing, with a new release pushed to git
hub"). No plan document was written for it: the Mac's own design for
updates (`docs/superpowers/specs/2026-11-24-ota-updates-design.md` in its
repository) says what must hold, and "Decisions" above says how Windows
does it. Reviewed by the session that wrote it; no one else has read it.

- The review. `git fetch origin` in `..\Heracles` brought sixteen commits
  after `5dd0252`: a notarized disk image (0.3.1), the icon (0.3.2,
  0.3.3), the licence, Sparkle and everything round it (0.4.0), and the
  original app removed. Each was read as a diff. Nothing else in the
  Swift sources changed: `TaskSupport.swift` and `LegacyImport.swift`
  only in their words, `Commands.swift` and `HeraclesApp.swift` only for
  updates, `SelfTest.swift` only for the icon.
- The core: the Mac's diff of `core/` applied as a patch, clean. 203
  tests, the same number: one of them now also asks what the backup is
  called.
- The icon: `tools\make-icon.ps1`, and a look at every size on white and
  on black, and at the title bars.
- Updates, test first, the parts with no view in them before the parts
  with one: the policy (the Mac's nine tests, less its two flags that
  Windows has not, plus what Windows adds), the feed and its signature,
  what is remembered, looking and fetching against a web server of the
  test's own, the swap of the folder, the release notes read for showing.
  Then the window, the menu and the timer, which only the rehearsal
  reaches.
- For whoever changes it:
  - **A look**: `UpdateCenter.Look` asks `Updater.CheckAsync`, which
    fetches the feed, has `UpdateFeed.Read` check its signature before
    reading a word of it, and compares build numbers. Only a look asked
    for from the menu says anything when there is nothing, or when it
    could not be done.
  - **A fetch** is by `Updater.FetchAsync`: the archive into
    `<updates>\download.part` with its SHA-256 worked out as it arrives,
    refused unless length and checksum are the feed's, unpacked into
    `<updates>\staged`, nothing kept of one that was refused.
  - **The install** is the new version's own doing, on the old one's quit
    (`UpdateCenter.InstallOnExit`, then `App.InstallUpdate` in the copy
    that was started). It leaves `<name>.update-old` beside the app; the
    new app removes that, and `staged`, when it starts, before any look.
  - **The daily look is not timed across days.** A timer comes round
    every hour at most and asks whether a look is due, so a PC that slept
    or a clock that was put right does not throw it out; a last look in
    the future counts as due.
  - **An offer the daily look makes does not take the keyboard**
    (`ShowActivated` off), so that someone typing in a note does not
    press "Install Update" with the Enter key.
  - The signature made by Windows PowerShell and the one checked by .NET
    are the same thing by a test that holds one of each
    (`ASignatureMadeByTheReleaseScriptsHolds`): if the scripts' signing
    is ever changed, make a new one for that test.
  - The rehearsal starts each copy on a library that "last looked two
    days ago" (it writes that into `updates\preferences.json`), or
    nothing would be looked for after the first start.
  - `check-shell.ps1` and `check-disc.ps1` now also say that a run on a
    test library does not look for updates. If either ever fails there,
    a test is about to offer itself the published app.
- What reading the code back found, before any of it was out (each then
  given a test, or put right where no test reaches): the copy that
  installs took its command line at its word (see "Decisions"); a copy
  that could not open its library would, on "Install and Restart", have
  written an empty list over the saved windows (it quits another way
  now); the offer took the keyboard from whoever was typing; and **a look
  that could not be made counted as the day's look**, so a PC that always
  starts before its network is up would never have looked at all. Only a
  look that read the feed is remembered now, and one that failed is tried
  again in an hour (`UpdatePreferences.After`, `RetryAfter`). That last
  one was found after the first commit of 0.3.0 and before any package of
  it went anywhere: the packages are built from the commit that has it.
- The first rehearsal passed 27 of 31, and the four were the check's own
  (build numbers too big for a file version: see "Found on Windows"). The
  update itself went through at the first try: found, fetched, checked,
  handed over on quit, installed in a second, and the app back as the new
  version.
- **The push that was refused.** The work was committed as `bfc11f0` and
  `486d89d` on top of `6132977`, and both packages were built from it
  (build 49) and passed everything, real input included. Then `git push`
  was refused: GitHub had `4946a75`, the licence, made on the Mac two days
  earlier. Nothing was forced. The two commits were rebased onto it
  (`b75f31b`, `01edb61`): `LICENSE` and the README's licence line taken as
  that commit has them, the notes of both kept. No code differed from what
  had been checked, but the commit count was now 50, so the packages of
  build 49 were thrown away, never having gone anywhere, and everything
  below was done again on build 50.
- **The release** (2026-11-25, on the owner's word). From `01edb61`,
  pushed first: 203 Rust tests and 763 .NET tests, no warnings. Both
  packages (`Heracles-0.3.0-win-arm64.iso`, 176 MB, and `.zip`, 61 MB;
  `Heracles-0.3.0-win-x64.iso`, 165 MB, and `.zip`, 66 MB), each image
  and each archive read back by the package step, and the feed, signed
  with the update key ("no feed is published yet: build 50 will be the
  first with one"). Against those packages: `check-disc.ps1` on both (19
  each); all five automation checks on the ARM64 package (116) and
  `check-shell.ps1` on the x64 one (32); `check-update.ps1` on both (31
  each, the x64 one under emulation, the ARM64 one in light with
  pictures, which were looked at, as the dark ones had been); and
  `check-input.ps1` on the ARM64 package (23, by a real mouse and
  keyboard, with the owner told first, as it had been on build 49). The
  dry run read right, and `tools\publish.ps1` published at its first run:
  the tag `v0.3.0`, a draft, six files, published, and the feed read back
  from `releases/latest/download/update-feed.json`, the same bytes as the
  one that was built.
- **Read back from GitHub with no credential**: `v0.3.0` at `01edb61`, not
  a draft, not a pre-release, the latest release, and the digest GitHub
  gives for each of the six files is the local file's.
- **Then a copy took the published update** (`check-published-update.ps1`,
  11 checks, on both packages; the x64 one under emulation). The package
  with its own assembly compiled again as 0.0.9, build 1, and the real
  feed address and the real update key in it as every copy has them: it
  read the published feed, believed it, was offered 0.3.0 with the release
  notes, fetched the archive from GitHub, found it to be the one the feed
  describes, quit through its own Exit, and came back as 0.3.0, build 50,
  file for file what was packaged. Asked again, it was offered nothing.
  **Only the old copy's version was made up.** That script's own first run
  was a parse error (see "Found on Windows"): it had been committed
  unrun, since it could not run before there was a release.
- Three of the publish script's new refusals were then seen, in dry runs
  once the release was out: as things stood ("build 50 is not higher than
  the published build 50"), with the feed moved away ("there is no
  update-feed.json"), and with a feed signed by a key made for the
  purpose ("is not signed with the update key", once for each package).
  The feed was put back, the same bytes. Not seen: a package for one
  processor only, a feed describing other archives, and the wrong
  addresses.
- Still untried in `tools\publish.ps1`: `-Replace`, `-Prerelease`, and
  carrying on with a draft that a failed run left behind.
- The packages on this machine are the released ones of 0.3.0, build 50,
  under `build\release`. `tools\e2e\check-published-update.ps1` needs
  them, and they go with the next clean build.

### 0.3.1: the installer (2026-11-25)

Asked for by the owner in a Toolbox session, for both apps at once (see
Decisions). The work was done there first and brought here; the two
`build.ps1`, `publish.ps1` and `disc-image.ps1` are twins in this respect,
and a fix to one belongs in the other.

- **Why not an MSI, and why per user.** The app replaces its own folder.
  Windows Installer keeps a record of every file it installed and repairs
  what differs; and an install for all users would put the app where its
  user cannot write, so it could not update itself at all
  (`UpdateInstaller.WhyNotHere` would say so on every look).
- **What the swap of folders means for an installer.** The first plan
  kept Inno Setup's defaults, with the uninstaller in the app's folder.
  Reading `UpdateInstaller.Install` before writing a line of it showed
  that the first update would rename the uninstaller away with the old
  folder. Hence `UninstallFilesDir`, and hence the app's folder being
  removed whole at uninstall (after an update nothing in it is a file
  the installer wrote).
- **What the first run of the check found.** `check-installer.ps1`
  refused to start: a real copy of Heracles had been run on this PC that
  afternoon, and keeps what its user chose about updates
  (`preferences.json`) in `%LOCALAPPDATA%\Heracles\updates`, which the
  uninstaller, as first written, removed whole. The check was written to
  refuse in order to protect a real copy from the check; what it caught
  was the installer. The uninstaller now removes only a fetched update
  (`updates\staged`, `download.part`), and the check sees that the
  preferences are byte for byte as they were.
- **Two things the check itself needed**, both from the machine being
  busy (a release of Toolbox was uploading): the app's folder cannot
  always be renamed in the instant after the app quits (the x64 copy,
  under emulation: "access is denied"; the updater has always tried
  again, and the check now does), and File > Exit by automation is asked
  again if the app is still there.
- **Checked on trial packages before the commit** (from the working
  tree, build 51): all Rust tests and 771 .NET tests with one skipped on
  purpose (8 new, of `InstalledCopy`); `package` for both processors
  (each image read back and compared, the installer and the image
  searched for this machine's paths); `check-disc.ps1` on both (21
  checks each: the three things at the top of the disc, the README
  naming the installer, the installer on the disc being the one beside
  it, and the app started from the disc); `check-installer.ps1` on both
  (34 checks each, the x64 one under emulation). This account has a link
  scheme and a reminder identity of its own, so the check planted
  neither and saw both left alone; the removal of ones that point at the
  removed copy has therefore not run (see "Not verified").
- **Checked on the packages that were released** (build 52, from
  `26a618d`): `check-disc.ps1` 21 and `check-installer.ps1` 34 on both;
  `check-shell.ps1` 32 on each packaged build (and on the build under
  `build\app`); `check-update.ps1` 31 on both. The x64 rehearsal failed
  once first, at "altered archive: the update is found and offered": the
  copy had started and said which feed it would read, and never asked
  the local server for it within the 40 seconds the check waits. In that
  same minute a release of Toolbox lost its upload connection, for the
  third time that afternoon. Run again, alone, it passed. Not explained
  further than that: the network was coming and going, and a copy's
  first request waits on Windows working out its proxy. **Not run:
  `check-input.ps1`**, the one with a real mouse and keyboard. Nothing in
  how keys or the mouse are handled changed since it was run for 0.3.0
  the same day, and the owner was at the PC (a real copy of the app had
  been started that afternoon).
- **Published** with `tools\publish.ps1`, after a dry run that would
  refuse nothing: a draft, eight files (the two disc images are some
  230 MB each now), published under `v0.3.1`, and the feed read back from
  `releases/latest/download/update-feed.json`, the same bytes as the one
  that was built. No upload broke off. The script had been changed an
  hour before, after Toolbox's release went wrong with its twin: there, a
  first run's upload broke off, a second run carried on with the draft,
  changed its notes without naming the tag, and GitHub published it as
  `untagged-<hex>`, where no installed copy could fetch what it was
  offered. So this script now tries a broken upload again, leaves alone a
  file the draft already holds whole, names the tag in every change to a
  draft, reads the draft back before publishing (its tag, and every file
  whole), publishes with the tag and `make_latest`, and stops if GitHub
  answers with another tag. Of those, only the last reading-back ran
  here; **carrying on with a draft is still untried in this repository**,
  and was what failed in the other.
- **Read back from GitHub**: `v0.3.1` at `26a618d`, not a draft, not a
  pre-release, the latest release; the SHA-256 GitHub gives for each of
  the six packages is the one in `SHA256SUMS`; an installer, an image and
  both archives answer at their `v0.3.1` addresses.
- **Then copies took the published update** (`check-published-update.ps1`,
  11 checks each time): with `-From 0.3.0` on both processors, which is
  the first update from one release to the next with nothing made up
  (see "Not verified" for what it still is not), and once as before, on
  ARM64, with the 0.3.1 package compiled again as 0.0.9.
- The packages on this machine are the released ones of 0.3.1, build 52,
  and those of 0.3.0, under `build\release`. `check-published-update.ps1
  -From` needs the older ones; they go with the next clean build.
- **For the owner, from this release**: Inno Setup's authors ask
  commercial users to buy a licence (Decisions has the address); whether
  that is you is yours to say. To move this PC's own copy to an
  installed one: close Heracles, run the installer, delete the old
  folder; the library is not in it. After that, `check-installer.ps1`
  refuses to run in this account, since it will not install over and
  then remove a real copy. And the update key still has only the one
  copy, in this account: `tools\update-key.ps1 -Export <file>`.
