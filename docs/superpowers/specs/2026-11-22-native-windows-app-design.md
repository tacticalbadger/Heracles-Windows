# Heracles for Windows: a native app over the Rust core

Design, 2026-11-22. Status: the owner chose the editor approach and the
commit author and raised no objection to the rest. The implementation plan
is written from this document.

## Intent

Heracles on the Mac was rebuilt as a native app: a Swift interface over a
Rust core (`heracles-core`), released as 0.3.0. The Windows repository still
holds the Electron app that the Mac left behind.

The owner wants the Windows app to do what the Mac app does, on the same
Rust core, with Electron gone. The Mac rewrite's own reasons carry over: one
core owns the data, so windows stay in step, the last edit is not lost on
quit, and nothing in the app runs a web page.

## Goals

- A native Windows app with feature parity with the Mac app at 0.3.0 (Mac
  commit `5dd0252`). The Mac app is the behavioural reference: when this
  document is silent, do what the Mac app does.
- The same `heracles-core` crate, with Windows fixes written so the Mac
  repository can take them unchanged.
- The same library format, so a library is readable by either app.
- Electron, the React renderer and the Node toolchain leave the repository.
- The GitHub repository is rebuilt around the new app (see
  [Rebuilding the repository](#rebuilding-the-repository)).

## Non-goals

What the Mac app has not built yet stays out of this work:

- Updating itself. A new version is installed by replacing the old one.
- Syntax colouring in code blocks.
- Find within a note.
- Selecting text across blocks, and dragging a rectangle to select blocks.
- Resizing an image.
- Per-section sort modes, and dragging notes in the sidebar to reorder.
- Moving a subtask under a different task.
- HTML export.

Also out: code signing and an installer (unsigned zips, as Toolbox ships
today), sync, and anything for Windows on ARM32 or Windows 8. The Mac
repository has no app icon, so this app has none either until the owner
supplies a drawing.

## Anti-goals

- No web view. The app loads no browser engine and runs no JavaScript.
- No second copy of the data rules in C#. Storage, search, import, export,
  HTML and Markdown conversion and the image store stay in the core.
- Nothing of the owner's own setup in git: no real names, account emails,
  hostnames or employer, in files or in commit metadata.
- No behaviour invented for Windows where the Mac app already made a choice,
  apart from the platform conventions listed under
  [Windows conventions](#windows-conventions).

## Constraints

- Dev machine: Windows 11 on ARM64, not an administrator, 200 % scaling.
  Rust 1.100 (ARM64 and x64 targets), Visual Studio 2022 with C++ tools,
  .NET SDK 10 and 11, `uniffi-bindgen-cs` v0.12.0 (for UniFFI 0.32, which
  the core already uses). Node is not installed and is not needed.
- The harness shell is Windows PowerShell 5.1. Scripts are ASCII `.ps1`.
- The Mac repository is cloned beside this one (`..\Heracles`). It is read
  for reference and never added as a remote here.
- The owner may be at the PC while tests run. Prefer UI Automation patterns
  to real mouse and keyboard input, and leave the clipboard as it was found.
- Commits are made when the owner asks, on `main`. Nothing is pushed until
  the owner says so.

## Approaches

### The shell: settled by the Toolbox port

The owner pointed at the Toolbox Windows port and said its decisions stand.
So: **WPF on .NET 10 in C#**, a thin shell over the core, bindings from
`uniffi-bindgen-cs`. Toolbox rejected WinUI 3 and Tauri partly because they
could not host the RDP control, which does not matter here. The reason to
keep WPF anyway is that the build scripts, the release flow, the test
harness and a long list of hard-won WPF notes come across as they are.

Checked on 2026-11-22: the core builds for `aarch64-pc-windows-msvc` and
194 of its 195 tests pass. `uniffi-bindgen-cs` generates C# for the whole
interface, including the change feed's callback, but **the generated file
does not compile as the core stands**: `TaskPatch` has three fields of type
`Option<Option<String>>`, which come out as `string??`, and C# has no such
type. With those flattened in a scratch copy, the file compiles with no
errors and no warnings, so this is the only obstacle. See
[The core](#the-core) for the fix.

### The editor: three ways considered

The block editor is about 60 % of the Mac app's code.

1. **Native, one rich-text box per block (chosen by the owner).** The Mac
   editor's design, translated: a document model in plain C#, one WPF
   `RichTextBox` per block, and a page that stacks them and draws what
   floats over them. Behaves as the Mac editor does and runs no JavaScript.
   It is the largest piece of work, and WPF's text box has costs to manage
   (see [Risks](#risks-to-settle-first)).
2. **The existing TipTap editor in WebView2.** Usable sooner, as Toolbox
   does for its terminal. Dropped: it keeps a JavaScript build, behaves
   differently from the Mac editor (text selection across blocks, a
   floating toolbar), puts a native child window in every pane, and is the
   approach the Mac abandoned at 0.3.0.
3. **One rich-text document for the whole note.** Dropped: a single
   `FlowDocument` cannot give each block a handle, columns or a drop
   target without rebuilding most of option 1 on top of it.

## Design

### Repository layout

```
core/                     heracles-core, at the same path as in the Mac repo
  uniffi.toml             C# namespace Heracles.Core, class CoreApi
Cargo.toml, Cargo.lock    workspace with the one member
app/
  Heracles/               the WPF project (net10.0-windows10.0.22621.0)
  Heracles.Tests/         xunit tests
  Generated/, runtimes/   produced by the core build, not in git
  nuget.config            pins nuget.org
tools/
  build.ps1               core, app, test, run, package, clean
  publish.ps1             GitHub Release from the packages
  e2e/                    fixture, launch, capture, ui (UI Automation)
docs/                     release notes, this design, the Mac's rewrite notes
VERSION                   marketing version; build number = commit count
CLAUDE.md                 handoff notes, kept current in every commit
README.md
```

The core stays at `core/`, not `core/heracles-core/`, so that a diff against
the Mac repository's core is path for path. `tools/bindgen` (the Swift
generator) is not carried over.

### Swift to C#

| Mac (`macos/Sources/HeraclesApp/`) | Windows (`app/Heracles/`) |
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

Types and methods keep the Mac's names where C# allows, so the two trees can
be read side by side and later Mac changes port file by file.

### The core

Changes, each written to be portable so the Mac repository can take it:

- `local_image_file` in `transfer.rs` turns a `file://` URL into a path by
  trimming slashes, which gives `/C:/...` on Windows. Fix it, and make the
  one failing test build its URL from forward slashes.
- Add `core/uniffi.toml` for the C# bindings.
- **One change to the interface the core exports.** `TaskPatch` says
  "clear this field" with a nested optional (`Some(None)`), which C# cannot
  express. The exported record becomes flat: each of the three clearable
  fields (`due_date`, `deadline`, `location`) is a plain optional value
  plus a `clear_…` flag, and the flag wins. The Rust type and the Rust
  tests stay as they are; only what crosses to the interface changes. When
  the Mac repository takes this, its Swift changes in the few places that
  build a `TaskPatch`: `.some(x)` becomes `x`, and `.some(nil)` becomes
  `clearDueDate: true`. (Claude's call, found on 2026-11-22.)
- Whatever else the Windows tests turn up.

The app calls the core on the UI thread, as the Mac app does. The core
calls the change observer on whichever thread made the write: if that is
the UI thread, apply at once, so views are current when the call returns;
otherwise post to the dispatcher.

### One process, many windows

The change feed lives inside the process. Two processes on one library
would not see each other's changes, so **the app is single-instance**: a
second launch hands its arguments to the first and exits. The first opens
a new window or follows the link it was given.

- Any number of windows share one `AppModel`. Each window has its own
  `Workspace` and pane tree.
- Each window's size, place and pane tree are saved to
  `%APPDATA%\Heracles\windows.json` and restored at the next start. (The
  Mac keeps these in scene storage.)
- Closing a window and quitting both wait for every editor to hand over
  what it is holding. Windows signing the user out does the same.

### Data

- Library folder: `%APPDATA%\Heracles`, which is the folder the Electron
  app uses on Windows, as on the Mac. In it: `heracles.db`, `images\`,
  `backups\`, `windows.json`, `settings.json`. (Electron names its folder
  after the package, `heracles`. Windows file names ignore case, so it is
  the same folder. Confirm against a machine that has run the Electron
  build before relying on it.)
- Log: `%LOCALAPPDATA%\Heracles\heracles.log`.
- `HERACLES_DATA_DIR` overrides the library folder, for tests and for
  trying the importer on a copy.
- **First start imports an Electron library** found as `heracles.sqlite`
  in that folder, when the new library is empty. The core backs the old
  file up first and never changes it. A summary window lists what came
  across and what had to be repaired, as on the Mac.

### Panes, tabs and the sidebar

- `PaneTree` is ported as it is: a value type with the Mac's operations
  (open, split, close, move tab, set ratios, remove notes that are gone)
  and the Mac's tests.
- A pane shows a tab row and, under it, a note editor, a board, or an
  empty state. Tabs drag between panes. Ctrl-click in the sidebar opens a
  note beside the current one.
- Everything in a pane is WPF content, so the airspace problems in the
  Toolbox notes do not arise: splits can be ordinary grids with splitters.
- Sidebar: notebooks with nested notes, favorites, archive, trash, a
  search box over the core's full-text search, context menus for rename,
  icon, favorite, archive, trash, restore, delete, duplicate and "Move to
  Notebook".
- Quick Open (Ctrl+K): recent notes until something is typed, then search.

### The editor

Three layers, as on the Mac.

**The document model** (`EditorBlock.cs`, `EditorDocument.*.cs`,
`RichText.cs`, `JsonValue.cs`, `EditorSelection.cs`). Plain C#, no WPF
types. A note is a tree of blocks; a list item is a block with its text on
it and what is nested in it as its children, converted to and from the
stored JSON's lists-of-items shape. Every edit that changes a note's shape
is a function on this model: split, join backward and forward, indent,
outdent, turn into another kind, remove, duplicate, move, paste, the
Markdown shortcuts and autolinking. Unknown blocks, attributes and marks
survive a round trip.

Text offsets are UTF-16 units on the Mac. C# strings count the same way,
so offsets and the tests' expected values carry over without conversion.

This layer is ported test first: the Mac's `EditorDocumentTests`,
`EditorBlockTests` and `RichTextTests` (133 tests) are translated before
the code they test.

**One text box per block** (`BlockTextBox.cs`, a `RichTextBox` subclass).
Typing inside a block is the text box's own business, which is where
native behaviour comes from: input methods, spelling, selection. Its own
undo is off. After each change the block's text is read back from the
text box into `RichText`. What crosses blocks (Enter, Backspace at the
start, Delete at the end, arrows at an edge, Tab) goes to the controller,
which changes the model and then makes the views match. The text box
supplies what the controller needs to move the caret between blocks:
whether the caret is on its first or last line, how far from the left
edge it is, and the offset nearest a given x on the first or last line.

**The page** (`BlockEditorView.cs`, `BlockViews.cs`,
`BlockEditorController.cs`). Stacks the block views and draws what floats
over them: the block handle, the selection of whole blocks, the drop line
and the slash menu.

Behaviour, all taken from the Mac editor:

- Blocks: paragraph, headings, bullet, numbered and task lists, quote,
  callout, toggle, code block with a language menu, divider, image,
  columns. Unknown blocks are shown as a placeholder and stored untouched.
- A selection is either text inside one block or whole blocks. A drag from
  one block into another selects whole blocks. Shift-click extends.
- Undo is the editor's own, one history per note, kept as snapshots. A run
  of typing in one block is one step.
- Markdown as you type, a slash menu of blocks, and autolinking.
- The block handle: drag to move, drag to a side to make columns,
  duplicate, turn into, delete, copy link to block. Column dividers drag.
- Marks: bold, italic, strikethrough, code, link. Code stands alone.
- A click on a link opens it. Only `http`, `https`, `mailto` and
  `heracles` links can be made or followed.
- Paste: the editor's own format first, then HTML through the core's
  converter (allow-listed), then plain text. Copy adds the editor's own
  format beside what the text box offers.
- Pictures: pasted, or dropped as files onto the page, go into the image
  store. Remote pictures are fetched when a note that refers to one is
  opened, with the Mac's 32 MB limit.
- No floating toolbar. The Format menu, its shortcuts and the context menu
  do that work.
- Saving: 300 ms after typing pauses, with the editor's id as the origin
  so it does not reload from its own save. A save made elsewhere replaces
  the working copy unless this editor has unsaved typing.

### Boards and tasks

- A todo list opens as a kanban board: columns you name, reorder and
  delete; cards that drag within and between columns.
- A task detail window: title, notes, completed, priority, due date,
  deadline, labels, location, reminders, nested subtasks to any depth
  (a subtask can be promoted to a card), comments.
- Dates are stored as the Mac stores them: UTC, ISO 8601 with
  milliseconds. A due date is 09:00 local on the chosen day, a deadline
  17:00.
- A board reloads when the change feed says its todo list changed.

### Reminders

The Mac schedules each reminder with the system, so it arrives whether or
not the app is running. The Windows counterpart is a scheduled toast
notification. `ReminderPlan` (what to add and remove so the system's list
matches the library's) is ported with its tests; only the system calls
differ. Clicking a reminder opens its todo list through a `heracles://`
link. See the risk about unpackaged apps below.

### Links

- `heracles://note/<id>` opens a note, and `#block=<id>` scrolls to a
  block. `AppLink` and `NavigationPolicy` are ported with their tests.
- The app registers the `heracles` scheme for the current user at start
  (under `HKCU`, no administrator needed), pointing at the running copy.

### Import and export

File ▸ Import takes Markdown and HTML files (the Mac's extension list) and
makes pages of them, pictures included. File ▸ Export as Markdown writes
the open note with its pictures in a folder beside it. Both are core
calls; the app supplies the file dialogs.

### Windows conventions

Where Windows differs from the Mac, by convention and not by invention:

- Shortcuts are the Mac's with Ctrl for Command and Alt for Option:
  Ctrl+N page, Ctrl+Shift+N section, Ctrl+Shift+T todo list, Ctrl+K quick
  open (link when text is selected), Ctrl+W close tab, Ctrl+B, Ctrl+I,
  Ctrl+Shift+X strikethrough, Ctrl+E code, Ctrl+Alt+0 to 3 text and
  headings, Ctrl+Shift+8, 7, 9 lists, Ctrl+\ split right, Ctrl+Shift+\
  split down, Ctrl+D duplicate, Ctrl+Shift+F favorite, Ctrl+Shift+C copy
  link, Ctrl+Shift+I import, Ctrl+Shift+E export, Ctrl+Z and Ctrl+Y undo
  and redo.
- The menu bar is in each window: File, Edit, Note, Format, View, Window,
  Help. Window ▸ New Window opens another window. Help ▸ About shows the
  version and build.
- Light and dark follow Windows, through the Fluent theme.
- Icons come from Segoe Fluent Icons where the Mac uses SF Symbols.

### Testing

- **Core:** `cargo test`, all 195 passing on Windows.
- **App logic** (xunit, no window): the translated Mac suites for the
  document model, rich text, pane tree, library, navigation, task dates
  and reminder plan, workspace and app model, and a bindings test against
  a real core in a temporary folder.
- **Editor with real text boxes** (xunit on an STA thread): the Mac's
  `EditorControllerTests` (60 tests), which drive the real views with keys
  and the clipboard. Clipboard tests save and restore what was there.
- **End to end:** `tools\e2e`, adapted from Toolbox: a fixture library
  under `build\e2e` reached through `HERACLES_DATA_DIR`, launch, capture
  to PNG, and UI Automation. Controls get automation ids as they are
  built. This replaces the Mac's `--self-test` and `--snapshot`.
- `tools\build.ps1 test` runs the Rust tests and then the app tests.

The Mac notes say nobody has used that app by hand and that its mouse
paths (dragging blocks, tabs, cards and dividers) were never exercised.
Here they can be, through the harness, and each is driven at least once.

### Build, package and release

Taken from Toolbox and renamed:

- `build.ps1 core` builds the release DLL, generates the bindings, and
  keeps the build machine's paths out of the binaries. `package` refuses a
  build that still names the profile folder.
- Packages are self-contained zips for `win-arm64` and `win-x64` with
  `SHA256SUMS`. The x64 core is cross-compiled from ARM64.
- `publish.ps1` attaches the files to a draft release and publishes last.
- Windows versions are numbered on their own. The first native release is
  **0.2.0**, above the Electron app's 0.1.0.

### Rebuilding the repository

The owner asked for the GitHub repository to be rewritten for the new app.
The Toolbox port is the pattern:

1. Start a new history with no parent. First commit: the Mac repository's
   tree at `5dd0252`, imported as the reference for the port.
2. Second commit: turn the tree into the Windows repository.
   - Remove `macos/`, `electron/`, `renderer/`, `tools/bindgen`,
     `scripts/`, `build/`, the Node, Vite and TypeScript files, and the
     documents that describe the Electron or Mac builds (`BUILDING.md`,
     `CHANGELOG.md`, `ROADMAP.md`, `windowsInstructions.md`,
     `docs/architecture.md`, `docs/electron.md`).
   - Keep `core/`, the Cargo workspace (without the bindgen member),
     `LICENSE`, `docs/review-and-native-rewrite.md` as the record of why
     the rewrite happened, and the owner's tooling files (`.claude/`,
     `skills-lock.json`, `.vscode/`, `.githooks/`).
   - Add `CLAUDE.md`, a README that describes the Windows app, a
     `.gitignore` for Rust and .NET output, and this design.
   - From here on, `docs/release-notes/<version>.md` takes the place of a
     changelog, as in Toolbox.
3. The app is built on top, a commit per stage.
4. `main` on GitHub is replaced with a force-push **when the owner gives
   the word**, not before.

What this does to the old history: it is no longer reachable. Every file
in it also exists in the Mac repository except Electron packaging details
that have no further use. The old commits carry an earlier author
identity, which is the other reason to replace them. Before the
force-push, a `git bundle` of the old history is written outside the
repository as a safety copy for the owner to keep or delete. GitHub keeps
unreachable commits for some time; GitHub Support removes them on request.

**Author of the new commits: `tacticalbadger`** (owner, 2026-11-22), with
the same GitHub no-reply address as the Mac repository's recent commits.
This machine's git is set to a different name, so the name and address
are set in this repository's own config before the first commit, and every
commit in the new history carries them.

## Order of work

Each stage ends with something that runs and with its tests passing.

0. **Repository and skeleton.** The new history, the core fix, the build
   script, bindings, an empty window that opens the library, tests wired.
1. **Risk spikes** (below). Throwaway code; findings go into `CLAUDE.md`.
2. **Model and shell.** `AppModel`, `Library`, `PaneTree`, `Workspace`,
   navigation, single instance, windows, the sidebar, tabs and panes,
   Quick Open, note actions. Notes open as read-only text for now.
3. **Editor model.** The C# document model, test first.
4. **Editor views.** Text boxes, the page, keys, marks, the slash menu,
   handles, drag, columns, pictures, clipboard, undo, links, saving.
5. **Boards, task details and reminders.**
6. **Import, export, the Electron importer, links from outside, menus.**
7. **Package and release.** Both architectures, release notes, a parity
   pass against the Mac README, then 0.2.0 when the owner asks.

## Risks to settle first

Each gets a short spike in stage 1, before anything is built on it.

1. **Many rich-text boxes on one page.** A `RichTextBox` is heavy. Measure
   a note of 500 blocks: time to open, memory, scrolling. If it is too
   slow, only blocks near the viewport get a text box and the rest are
   drawn as static text of the same height.
2. **Offsets and marks through a `RichTextBox`.** The model counts UTF-16
   units; WPF counts text pointers. Prove a two-way mapping, and prove
   that marks can be read back after typing through an input method.
3. **Colour emoji.** WPF has drawn emoji in one colour. Note icons and
   callouts are emoji. Check on .NET 10; if still so, choose between a
   library that draws them in colour and living with one colour. The
   owner decides if it comes to that.
4. **Scheduled toasts from a zip.** An app with no installer must register
   its own identity before Windows will show its notifications. Prove a
   toast scheduled for a minute ahead arrives with the app closed, and
   that clicking it starts the app with the link.
5. **x64 core from ARM64.** Cross-compile and run the x64 package under
   emulation.
6. **Pictures WPF cannot decode** (SVG, AVIF, HEIC, sometimes WebP). Show
   a placeholder with the file's name; do not fail the note.

## Calls made here, open to change

These are Claude's, not the owner's. Each is cheap to change now.

- Import an Electron library on first start (Toolbox started fresh; the
  Mac imports, and the importer already exists).
- The library lives in `%APPDATA%\Heracles`, shared with the Electron
  app's folder.
- The core stays at `core/`, and its crate types are left as they are.
- Single instance, with a second launch handing over to the first.
- No `--self-test` or `--snapshot`; the `tools\e2e` harness instead.
- A click opens a link, as on the Mac, though Windows editors usually
  want Ctrl-click.
- Shortcuts map Command to Ctrl one for one.
- First native version is 0.2.0.
- A bundle of the old history is kept outside the repository.
- Supported from Windows 10 version 2004, as Toolbox is.
- The application identity is `com.heracles.notes.native`, matching the
  Mac's bundle id, so it does not collide with the Electron app's.

## Left to the builder

- Class layout inside each folder, XAML against code-built views, and
  whether view models are used or views read the model directly.
- How the single-instance handover is carried (a named pipe is expected).
- The shape of `windows.json` and `settings.json`.
- How drags are implemented, and what the drop highlight looks like.
- The slash menu's and icon picker's look, taken from the Mac screenshots
  that `macos/Sources/HeraclesApp/Snapshot.swift` describes.
- Automation ids, following the Toolbox pattern.
