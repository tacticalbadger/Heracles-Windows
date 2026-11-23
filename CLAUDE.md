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
    SingleInstance.cs     one process per library; hand-over by named pipe
    MainWindow.xaml(.cs)  one window: menus, shortcuts, saved layout
    Model/                AppModel (the change feed), Library, Navigation
                          (AppLink, NavigationPolicy)
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
    Support/              AppPaths, Log, WindowStates (SavedWindow), Ui
                          (shared view helpers, LandmarkPeer), Prompts,
                          E2eSeed
  Heracles.Tests/         xunit tests: the crossing into the core
                          (BindingTests) and every class with logic
  Generated/, runtimes/   produced by the core build, not in git
  Directory.Build.props   version from VERSION, build number, PathMap
  nuget.config            pins nuget.org
tools/build.ps1           targets core, app, test, run, package, clean
tools/e2e/                make-fixture, launch, capture, ui (UI Automation
                          and synthetic input), adapted from Toolbox;
                          ui-check (helpers), check-shell and check-editor
                          (the scripted checks of the window and of writing
                          in a note), look-editor (pictures, light and dark)
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
- **The clock steps backwards now and then** (two minutes, seen
  2026-11-22; seven, in the Toolbox notes). A file edited just after such a
  step is older than the output built just before it, so `dotnet build`
  decides nothing changed and **runs the old program** with no warning. If
  a change seems to have had no effect, compare the times of the source and
  the output, and rebuild with `--no-incremental` (or `tools\build.ps1
  clean`). Do not read much into gaps between log timestamps either.

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
tools\e2e\launch.ps1 --e2e-seed       # ...with a few notes of every kind in it
tools\e2e\launch.ps1 -Again --new-window   # a second copy while one runs: it hands over
tools\e2e\capture.ps1                 # PNG of every Heracles window -> build\e2e\shots
tools\e2e\ui.ps1 tree                 # also: find, invoke, expand, select, setvalue, focus, click, clickat, drag, keys, text
tools\e2e\check-shell.ps1             # the scripted check of the window: 30 checks, exit code = failures
tools\e2e\check-editor.ps1            # the scripted check of writing in a note: 15 checks
tools\e2e\look-editor.ps1             # pictures of a note of every kind of block, dark and light -> build\e2e\shots\editor-*.png
tools\e2e\launch.ps1 --e2e-seed --theme-light   # the app in light (or --theme-dark) whatever Windows is set to
```

**Run `tools\e2e\check-shell.ps1` after any change to the window, and
`tools\e2e\check-editor.ps1` after any change to the editor or to how a
note is shown or saved.** The second opens a note, writes in a block by
automation ("set value"), uses the Format and Edit menus, ticks a task,
watches a second pane follow, restarts the app to see the words kept, and
tries to change a note in the trash.

**Then look.** `tools\e2e\look-editor.ps1` takes six pictures. Twice in one
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
library is empty; among its notes is "Every kind of block"), and
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
  `instance: second launch: <args>`; `error shown: <text>`; `exit`.
- Automation ids. Sidebar: `Sidebar` (the list), rows `note:<id>` (in the
  tree), `favorite:<id>`, `archived:<id>`, `trashed:<id>`, headers
  `notebook:<id>`, `section:favorites|archive|trash`, `toggle:<id>`,
  `SidebarSearch`, results `hit:<id>`, `NewMenu`. Panes: `Pane:<leafId>`,
  `Tab:<leafId>:<noteId>`, `TabClose:<leafId>:<noteId>`,
  `EmptyPane:<leafId>`, `Divider:<splitId>:<n>`. A note: `NoteTitle:<leafId>`,
  `NoteIcon:<leafId>`, `NoteText:<leafId>` (the editor's page, a Document
  whose name is the note's words; for a todo list, the line that says its
  board is to come). In a note: `Block:<blockId>` for each block with text
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
  `ErrorOk`. `StartupError` shows only when the library did not open. Menu
  items go by name (`ui.ps1 expand -Name View`, then `invoke -Name 'Split
  Right'`).
- A hidden element is still in the tree, marked `[offscreen]`; so is a row
  scrolled out of view. `Find-Ui` asks whether something exists,
  `Find-UiShown` whether it can be seen.
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
- **A value set through UI Automation does not move the keyboard**, so a
  title set that way never lost focus and was never committed. The title
  box commits at once when it is changed without having the keyboard.
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
      (built, and tested with real text boxes in no window; **never yet
      used with a real mouse or keyboard**: see "Not verified")
- [ ] Pictures in the image store; remote pictures fetched on open (pasted
      and dropped pictures are stored and shown, tested; a picture on the
      web is fetched when its block is shown, written and never run; no
      picture has been looked at in the window)
- [ ] Kanban boards with drag between columns
- [ ] Task detail: notes, priority, dates, location, labels, reminders,
      nested subtasks, comments
- [ ] Reminders delivered by Windows, whether or not the app is running
- [ ] Import of Markdown and HTML files; export as Markdown
- [ ] The Electron library imported on first start, with a summary
- [x] Quick Open, the icon picker, the menu bar (with Edit and Format)
- [ ] `heracles://note/<id>` links, with `#block=<id>` (parsed, followed and
      handed over between copies, and the editor scrolls to the block and
      marks it; not yet registered with Windows)
- [x] Nothing lost on quit: closing a tab, a pane or a window, and
      quitting, save every editor first
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

- The x64 build has only run under emulation, on the ARM64 dev machine.
  It has never run on x64 hardware.
- Windows 10. Everything so far ran on Windows 11.
- A PC with nothing installed. The packages are self-contained and have
  only been started on the machine that built them.
- **Clicking a reminder.** The toast spike proved the two halves apart: the
  banner appears, and opening a link of the app's scheme starts the app
  with that link. A click on the banner itself was not made: UI Automation
  could not find the banner, and a real click on the owner's desktop was
  not taken. The owner should click one by hand when reminders are built.
- Reminders with Do Not Disturb on, and on Windows 10.
- Typing through a real input method (composition, candidate windows), and
  real key presses. The text-input spike sent finished text to the box
  through `TextCompositionManager`, which a script can do; an input method
  it cannot.
- **Everything in the window that needs a real mouse or keyboard**: the
  context menus (sidebar rows, tabs), dragging a tab within and between
  panes, dragging a divider, Ctrl-click to open beside, the arrow keys and
  Enter in Quick Open, and every keyboard shortcut. The scripted check
  reaches the same code through the menus and automation where it can
  (Split Right, Close Tab, Quick Open, Move to Trash); the rest is written
  and has never been used.
- Deleting a note for good while it is open in two windows, through the
  window. The model and workspace tests cover it; the only way to it in the
  window is a context menu.
- **The editor under a real hand.** Nobody has typed in it, clicked in it
  or dragged in it. What the tests do: send finished characters to real
  text boxes in no window, offer keys to the same method a key press
  reaches, and call the methods the mouse handlers call. What has never
  run: the handlers themselves (`OnPreviewKeyDown`, the mouse overrides in
  `BlockEditorView.Mouse.cs`, `GripView`, `ColumnGrip`, the static text's
  mouse-down), keyboard focus moving from block to block in a real window,
  the caret being seen, the first click in static text landing where it
  was aimed, a drag selecting text, the block handle appearing under the
  pointer, the block menu and the text menu, the link panel, the system
  clipboard (Ctrl+C, Ctrl+V), files dropped from Explorer, spell-checking,
  and scrolling a long note. **The owner should spend ten minutes typing in
  a note before anything else is built on this.**
- Light mode was looked at once (`look-editor.ps1`, 2026-11-23, with
  `--theme-light`): the window and a note of every kind of block read
  properly. Windows itself was in dark mode; the app following a change of
  Windows' own setting while it runs has not been seen.
- A window saved on a second display that is then unplugged.

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

**Where things stand: stages 0 to 4 of the design's seven are done. The
app can be browsed, and a note can be written in it.** One process per
library, any number of windows, a sidebar, panes and tabs, Quick Open, and
a native block editor with the Mac editor's behaviour: every kind of block,
Markdown as it is typed, the slash menu, marks and links, the editor's own
undo, copy and paste, pictures, and saving that loses nothing on close or
quit. From a clean build: no warnings, 203 Rust tests, 438 .NET tests, and
45 scripted checks of the real window (`check-shell.ps1` 30,
`check-editor.ps1` 15).

**What that does not say** is under "Not verified", and the first entry
matters: nobody has yet typed, clicked or dragged in the editor with a real
keyboard and mouse. The tests reach the code behind every key and every
mouse gesture, with real text boxes, but not the last inch between Windows'
input and that code.

**Next: stage 5**, boards, task details and reminders (the design's
"Boards and tasks" and "Reminders"; the Mac's `BoardView.swift`,
`TaskDetailView.swift`, `TaskSupport.swift` and their tests; the fourth
spike's findings on scheduled toasts). It has no plan yet: write one in
`docs/superpowers/plans/` first. A todo list still says "Boards arrive in a
later version." Then stage 6 (import and export of files, the Electron
library imported on first start, the link scheme registered with Windows),
and stage 7 (packages, a release script, and 0.2.0 when the owner asks).

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
