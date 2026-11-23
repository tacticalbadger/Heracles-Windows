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

**One branch: `main`.** Commit when the owner asks. Push when the owner
says.

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
    Support/              AppPaths, Log, WindowStates (SavedWindow), Ui
                          (shared view helpers, LandmarkPeer), Prompts,
                          E2eSeed, LinkRegistration (the link scheme, the
                          app's identity, WindowsIntegration: what the app
                          writes in the registry for itself at start)
  Heracles.Tests/         xunit tests: the crossing into the core
                          (BindingTests) and every class with logic.
                          Fixtures/legacy.sqlite is a small library as the
                          Electron app kept it (its README says what is in
                          it and how it was made)
  Generated/, runtimes/   produced by the core build, not in git
  Directory.Build.props   version from VERSION, build number, PathMap
  nuget.config            pins nuget.org
tools/build.ps1           targets core, app, test, run, package, clean
tools/e2e/                make-fixture, launch, capture, ui (UI Automation
                          and synthetic input), adapted from Toolbox;
                          ui-check (helpers), check-shell, check-editor,
                          check-boards and check-transfer (the scripted
                          checks of the window, of writing in a note, of a
                          board and a task, and of files in and out),
                          check-legacy (the first start beside an Electron
                          library), check-input (a minute of REAL mouse and
                          keyboard), look-editor and look-boards (pictures,
                          light and dark)
tools/publish.ps1         a GitHub release from the packages: checks, a
                          draft, the files, then published. -DryRun sends
                          nothing. Run by the owner, when he says
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
tools\build.ps1 core package    # self-contained zip for this machine -> build\release
tools\build.ps1 core package -Arch x64   # and for the other processor (then "core" again, for this one's bindings)
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
tools\e2e\launch.ps1 -Exe build\release\Heracles-0.2.0-win-x64\Heracles.exe   # a packaged build instead
tools\e2e\launch.ps1 -DataDir <path>  # another library folder (to see what the app does with a bad one)
tools\e2e\launch.ps1 --e2e-seed       # ...with a few notes of every kind in it
tools\e2e\launch.ps1 -Again --new-window   # a second copy while one runs: it hands over
tools\e2e\capture.ps1                 # PNG of every Heracles window -> build\e2e\shots
tools\e2e\ui.ps1 tree                 # also: find, invoke, expand, select, setvalue, focus, scroll, click, clickat, drag, keys, text
tools\e2e\check-shell.ps1             # the scripted check of the window: 30 checks, exit code = failures
tools\e2e\check-editor.ps1            # the scripted check of writing in a note: 15 checks
tools\e2e\check-boards.ps1            # the scripted check of a board and a task: 39 checks
tools\e2e\check-transfer.ps1          # the scripted check of File > Import and Export, through the system's dialogs: 10 checks
tools\e2e\make-fixture.ps1 -Legacy    # a library folder holding only the Electron app's library, to be imported
tools\e2e\check-legacy.ps1            # the scripted check of the first start on an Electron library: 20 checks
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
list "Packing", a board with cards of every sort), and
`--theme-light` or `--theme-dark` (a testing aid: the theme, whatever
Windows is set to).

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
- `tools\build.ps1` now compiles the C# afresh every time
  (`--no-incremental`), because of this machine's clock (see above). A bare
  `dotnet build` or `dotnet test` still trusts the clock.

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
      built, started and checked; the script has only ever run dry: the
      release is the owner's to make)

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

## Current state (update in every commit)

As of 2026-11-23, on `main`. `main` on GitHub was replaced on 2026-11-22
(commit `9b56acf`, a force-push on the owner's word): the Electron app and
its history are no longer there. Any other clone of this repository must
`git fetch origin` and `git reset --hard origin/main` before it is used,
and must never push its old history. **Stage 2 and the first two tasks of
stage 3 were pushed on 2026-11-23, on the owner's word.** What is committed
after that stays local until he says push: `git log origin/main..HEAD`
lists it.

**Where things stand: the app is built. All seven stages of the design are
done but for the release itself, which is the owner's to make.** The app
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
`check-boards.ps1` 39, `check-transfer.ps1` 10, `check-legacy.ps1` 20) and
23 by a real mouse and keyboard (`check-input.ps1`). Both packages were
built, and each was started and checked: see "Stage 7" at the end.

**What that does not say** is under "Not verified", and three entries
matter. No person has used the app yet. A script used it for a minute with
a real mouse and keyboard (`check-input.ps1`: typing, keys, clicks and
drags, and it found a fault no test could have), but it did only what it
was written to do: the block handle, the context menus, selecting with the
mouse and the clipboard are among what is still untried. The app has never
yet made itself known to Windows (its link scheme, its reminders), because
it does that only on the real library and has only ever been started on
fixtures: the first start on the real library is the first time that runs.
And that same first start is when a real Electron library, if there is
one, is imported for the first time: only made-up ones have been.

**What is left, and it is the owner's**:

1. *Use it.* Start a build on the real library (`tools\build.ps1 run`, or
   a package). That first start imports an Electron library if one is
   there, shows the summary, and registers the app's links and reminders
   with Windows. Ten minutes of typing, dragging blocks by their handles,
   right-clicking things, copying and pasting. Give a task a reminder two
   minutes ahead, close the app, and click the banner when it comes. What
   goes wrong goes in "Known faults", or gets fixed.
2. *Push.* Everything since stage 3's second task is local:
   `git log origin/main..HEAD` lists it.
3. *Release*, when he says so. From the commit that was pushed:
   `tools\build.ps1 core package -Arch x64`, then
   `tools\build.ps1 core package` (this machine's last, so the bindings
   match its DLL), then `tools\publish.ps1 -DryRun`, and when that reads
   right, `tools\publish.ps1`. The packages must be built from the very
   commit that is pushed (the build number is the commit count, and the
   script refuses any other), so a commit made after packaging means
   packaging again.

Nothing else is planned. The design's "out of scope" list, and "Known
faults" above, are what a next session would choose from.

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

Decided by the owner on 2026-11-22: the earlier account name stays in
`LICENSE` and in the one link in `docs/review-and-native-rewrite.md`, as in
the Mac repository. Do not change either.

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

**Stage 7 (package and release) is done but for the release** (2026-11-23).
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
- `tools\publish.ps1 -DryRun` was read: it checks the checksums and that
  both packages are the build of the current commit, shows the title, the
  files and the notes, and names the one thing that would stop a real run
  (the commit is not on `origin/main`). **A real run has never been made**:
  the draft, the upload and the publishing are the Toolbox port's code,
  renamed, and are untried here.
- Packages are not kept: they are under `build\release`, which is not in
  git, and they are only good for the commit they were built from. The
  ones on this machine were built from the commit this note is in.
