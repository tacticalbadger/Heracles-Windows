# Changelog

All notable changes to Heracles are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`npm run release:upload`** — uploads a built release (installer, zip,
  blockmaps, and the `latest-*.yml` update feed) to the Cloudflare R2 bucket
  that serves auto-updates, replacing the manual dashboard/AWS-CLI step. Signs
  R2's S3 API directly with Node built-ins (no CLI or SDK required); reads an
  R2 API token from `R2_ACCESS_KEY_ID`/`R2_SECRET_ACCESS_KEY` or a git-ignored
  `.env.r2`. Uploads feeds last so clients never see a half-published release,
  marks feeds `no-cache`, skips artifacts that don't match the current
  `package.json` version, and supports `--dry-run`.

- **Automatic updates** — the app checks a release feed on launch (and hourly),
  downloads new versions in the background, and shows a **"Restart to update"**
  banner; clicking it quits and installs. Built on `electron-updater`, published
  to a Cloudflare R2 public bucket via the `generic` provider. New IPC surface
  `window.heracles.updates` (`onDownloaded`, `restart`, `check`).
- **Signed & notarized macOS builds** — releases are signed with a Developer ID
  Application certificate, run under the hardened runtime, and are notarized +
  stapled (both the `.app`/`.zip` and the `.dmg`). This is what lets Gatekeeper
  open the app cleanly and lets the updater refuse any build not signed by us.
- **Notebooks (top-level groups)** — the sidebar is now a filing cabinet: create
  any number of top-level **notebooks** ("New notebook"), each holding sections
  and pages/todos. Notebooks are renamable (double-click or the `⋯` menu),
  collapsible, drag-to-reorder, and deletable (contents move to the first
  remaining notebook; the last notebook can't be deleted). A new note `kind`,
  `notebook`, models them.
- **Images in notes** — drag an image file from Finder onto a note, or paste a
  screenshot, to insert it as an image block. Images embed as base64 data URLs so
  notes stay fully local-first (no asset files to manage).
- **Column layouts** — drag any block by its gutter grip (`⠿`) to the right edge
  of another block to place it beside that block as a column; drop at the edge of
  an existing row to add more columns, or between blocks to reorder. Column
  widths are **resizable** by dragging the boundary, and rows auto-collapse back
  to normal flow when reduced below two columns.
- **Import HTML** — "New page from HTML" in the sidebar imports one or more
  `.html`/`.htm` files as pages, converting common formatting to editor content.
- **Paste HTML** — pasting rich HTML into the editor (e.g. from a web page) is
  now sanitized and converted to supported nodes instead of pasted as raw markup.
- **Import Markdown** — "New page from Markdown" imports one or more `.md` files
  as pages, converting headings, lists, task lists, code blocks, and links.
- **Create under active context** — new pages (blank, Markdown, or HTML) are now
  created as children of the currently active page/section.
- **Reorder Kanban columns** — drag a column left/right to rearrange the board;
  the new order is persisted via `tasks.reorderColumns`.
- **Subtask drag-reorder** — drag the `⋮⋮` handle to reorder subtasks within a
  parent task.
- **Move a subtask anywhere** — a `⤴` menu promotes a subtask to a standalone
  card in any column or re-parents it under any other task (self and descendants
  excluded to prevent cycles).

### Changed

- The single hard-coded "Private" sidebar section is replaced by user-created
  notebooks. Every page/section/todo now lives inside a notebook; a one-time
  migration creates a default **"Notebook"**, adopts all existing top-level
  items into it, and writes a `heracles.pre-notebooks-<timestamp>.sqlite` backup
  first so nothing can be lost.
- Pasting HTML that contains `<img>` now keeps it as an image block (previously
  images were downgraded to links).
- The editor now registers TipTap's `Link` extension so links survive import,
  paste, and editing.
- Added a sixth drag-and-drop MIME type, `application/x-heracles-block`, for
  moving editor blocks (reorder / make a column). The fifth,
  `application/x-heracles-column`, handles column reordering on a board.
- macOS target now builds `zip` + `dmg` (the `zip` is what the updater applies);
  `dir` is still used by `npm run package:mac` for quick local builds.

### Fixed

- **Duplicating a block changed the original block's id**, which broke any
  link to that block. The copy was made by clearing the id on attributes it
  shared with the original. Fixed in the Electron editor and the native one.
- **Typing is no longer reverted by metadata updates.** Toggling favorite,
  archiving, changing a page's icon, or any sidebar refresh while an autosave
  was still pending (the 600 ms debounce) replaced the in-memory note with the
  older database copy, resetting the editor and silently dropping the last
  moments of typing. Pending edits are now overlaid on every server response
  until the save lands, and a rename during the debounce window updates the
  pending payload instead of being clobbered by it.
- **Dragging a kanban card no longer also drags its column.** The card's
  dragstart bubbled into the draggable column, stamping the column-reorder
  payload onto the same drag — so moving a card across columns could also
  reorder the source column (and mis-style it). Card drags now stop
  propagation, and card drops take priority over column drops.
- **"Move to…" can no longer create a parent cycle.** `setNoteParent` now
  refuses to move a note under itself or one of its descendants (the same
  guard `reorderNote` already had); a cycle would detach the subtree from the
  sidebar permanently. Covered by new `npm test` cases.
- **Windows can only display the app itself.** A navigation/window-open
  policy (`electron/navigation.cjs`) allows only the app's own document and
  denies everything else: a window can no longer be navigated to another local
  file (e.g. one dropped onto it) or to a remote page, either of which would
  have loaded with the preload database bridge attached. Any `http(s)`
  navigation is handed to the system browser instead. Clicking a link while
  editing still does nothing (the editor sets `openOnClick: false`).
- **Deleting a page no longer orphans its children.** Permanent delete and
  "Empty trash" now remove the whole subtree, plus any kanban data (columns,
  tasks, comments) owned by deleted todo lists. Previously children stayed in
  the database invisibly, and a deleted todo list leaked its board rows forever.
- **Trash and restore now cascade.** Trashing a page trashes its subtree (so
  children don't silently vanish from the sidebar), restoring brings the
  subtree back, and restoring a child whose parent is still in the trash hoists
  it to its nearest visible ancestor instead of leaving it unreachable.
- **Pasted HTML is actually converted now.** The paste handler computed the
  sanitized conversion and then discarded it, inserting a spurious empty
  paragraph and pasting the raw clipboard HTML instead. Pasted HTML now becomes
  supported nodes (task lists, CodeMirror code blocks, image blocks), and
  internal editor copies still paste natively so column layouts survive.
- **HTML import/paste keeps images.** `htmlToEditorHtml` still downgraded
  `<img>` to links even though the editor has an image block; images are now
  preserved (with `src`/`alt`/`title` sanitized) as promised in this changelog.
- **Kanban column drag-reorder off-by-one.** Dropping a column on the left half
  of a target could land it one slot too far left (or make the move a no-op).
- **Duplicate preserves the note's kind.** Duplicating a todo list previously
  produced an empty *page*, silently dropping the board; it now duplicates the
  board itself — columns, cards, subtasks, and their fields (comments are not
  copied). Duplicating a section stays a section.
- **Sidebar filter matches text, not markup.** Filtering pages no longer
  searches raw note HTML, which matched base64 image data and got slower as
  images were added; both the filter and ⌘K search now match rendered text only.
- **Unsaved edits flush on window close.** The 600 ms autosave debounce could
  drop the last edit if the window closed quickly; pending saves now flush on
  `beforeunload`.
- **Import dialogs skip unreadable files** instead of failing the whole import.
- electron-builder no longer packs the build-output directory back into
  `app.asar`. A missing `files` allowlist (combined with a case-mismatched
  output path) caused each build to bundle the previous one, snowballing the app
  to ~1.9 GB. With the allowlist the app is back to ~300 MB.

### Technical

- Auto-update: `electron-updater` wired in `electron/main.cjs`
  (`setupAutoUpdates`, packaged-only) + `electron/preload.cjs`
  (`window.heracles.updates`) + a "Restart to update" banner in `App.tsx`.
  `build.publish` uses the `generic` provider against the R2 public URL.
- Release pipeline: `build.mac` sets `hardenedRuntime`, `entitlements`
  (`build/entitlements.mac.plist`), Developer ID `identity`, and `notarize`
  (App Store Connect API key via `APPLE_API_*` env vars). An
  `afterAllArtifactBuild` hook (`build/notarize-dmg.cjs`) notarizes + staples the
  DMG and patches `latest-mac.yml`. New scripts: `release:mac`, `release:win`.
- New TipTap nodes: `ImageBlock` (block image), and `ColumnBlock` / `Column`
  with a `ColumnNodeView` (resize handle) and `columnCommands.ts`
  (`resolveOuterBlock`, `getDropTarget`, `applyBlockDrop`, and a
  `ColumnsNormalizer` appendTransaction plugin that drops empty columns and
  unwraps under-filled rows). Image drops/pastes are handled in the editor's
  `handleDrop` / `handlePaste` / `handleDOMEvents`.
- `notes.create` accepts `kind: 'notebook'`. `electron/db.cjs` adds the
  one-time notebook migration plus a `resolveParentId` invariant that keeps
  notebooks at the root and every other note inside a notebook; deleting a
  notebook reassigns its children to the first remaining notebook.
- New conversion utilities: `renderer/src/lib/markdownImport.ts`
  (`markdownToEditorHtml`, backed by `marked`) and
  `renderer/src/lib/htmlImport.ts` (`htmlToEditorHtml`, also used by the editor's
  paste handler).
- New IPC: `notes.importMarkdown()` and `notes.importHtml()` open native
  multi-select file dialogs and return file names + contents.

## [0.3.0] - 2026-11-22

The native Mac app's second preview. The editor is now native, and the
download is notarized, so it opens without a detour through System Settings.
It is `Heracles-0.3.0-mac.zip` on the
[release page](https://github.com/tacticalbadger/Heracles/releases/tag/v0.3.0):
a universal app for macOS 14 or later. It does not update itself.

As with 0.2.1, this version number belongs to the native app only. The
Electron app's changes are under Unreleased above.

### Added

- **The editor is native.** The web editor in a web view is gone, and with it
  the last web technology in the native app. Each block's text is a system
  text view, so typing, selection, spelling, text replacement, dictation and
  input methods behave as they do in every other Mac app. The app no longer
  loads WebKit or bundles any JavaScript.
- **Blocks.** Paragraphs, headings, bullet, numbered and task lists nested to
  any depth, quotes, callouts, toggles, code blocks with a language menu,
  dividers, images and column layouts with dividers to drag. Notes written
  with the web editor open unchanged: the stored format is the same.
- **Keys.** Return splits a block and ends a list on an empty item. Backspace
  at the start of a block joins it to the one above. Tab and Shift-Tab indent
  and outdent list items. The arrow keys cross from block to block and keep
  their column. Shift with an arrow at the edge of a block, a drag from one
  block into another, or a second Select All selects whole blocks, which can
  be copied, cut, deleted, moved or indented together.
- **Markdown as you type.** `# `, `- `, `1. `, `[] `, `> `, ` ``` ` and `---`
  at the start of a line make the block. `**bold**`, `*italic*`, `` `code` ``
  and `~~struck~~` make the mark. A web address becomes a link when the space
  after it is typed.
- **Slash menu.** `/` where a word could start lists the kinds of block, and
  narrows as you type.
- **Block handle.** The handle beside the block under the pointer adds a block
  below, drags the block to a new place or to the right-hand end of another to
  make columns, and opens a menu to turn the block into another kind,
  duplicate it, copy a link to it or delete it.
- **Format menu**, with shortcuts for bold, italic, strikethrough, code,
  headings and lists. ⌘K links the selected text, and is still Quick Open when
  nothing is selected.
- **Undo and redo for the whole note**, not one block at a time.
- **Copy and paste.** Blocks copy to other apps as Markdown, and back into
  Heracles as blocks. Rich text from other apps still goes through the core's
  allow-list. A web address pasted over selected text links it. Pictures can
  be pasted, or dropped in as files.

### Changed

- The download is notarized by Apple and has its ticket stapled.
- A click on a link opens it. The web editor wanted ⌘-click.
- The native build no longer needs Node.js. `macos/release.sh` also accepts an
  Apple ID with an app-specific password, and checks the credentials with
  Apple before it builds.

### Removed

- The web editor in a web view, and WebKit with it.
- The floating toolbar over selected text. The Format menu, its shortcuts and
  the context menu do its job.

## [0.2.1] - 2026-11-22

The first preview of the native Mac app. This version number belongs to the
native app only: the Electron app is still 0.1.0, and its changes since then
are under Unreleased above.

The download is `Heracles-0.2.1-mac.zip` on the
[release page](https://github.com/tacticalbadger/Heracles/releases/tag/v0.2.1):
a universal app for macOS 14 or later. It is signed with the Developer ID
certificate and **not notarized**, so macOS refuses to open it the first time;
allow it under System Settings → Privacy & Security → Open Anyway. It does not
update itself.

### Added

- **Native Mac app, first stage** — the start of the Swift + Rust rewrite
  planned in [`docs/review-and-native-rewrite.md`](./docs/review-and-native-rewrite.md).
  It lives beside the Electron app, which is unchanged.
  - `core/` is **heracles-core**, a Rust library that owns the data: a SQLite
    schema with foreign keys and WAL, one write transaction per user action,
    documents stored as block JSON in ProseMirror's shape, images in a
    content-addressed store outside note content, FTS5 search, and a change
    feed that tells every window what each write touched.
  - A one-time importer brings the Electron library (`heracles.sqlite`)
    across: it backs the old database up first, only reads it, converts each
    note's HTML to block JSON, moves base64 images into the image store, and
    repairs what the old schema allowed and the new one does not (orphaned
    notes, cards in no column), reporting every repair.
  - `macos/` is the app: a SwiftUI shell (sidebar, search, kanban board,
    multiple windows) over the core through UniFFI bindings, with the existing
    TipTap editor hosted in a web view (`renderer/editor-host/`) until a
    native block editor replaces it.
  - Build with `macos/build-app.sh`. See
    [`BUILDING.md`](./BUILDING.md#building-the-native-mac-app-in-progress).
- **Native Mac app, second stage** — the pieces the first stage left out:
  - **Panes and tabs.** Split a window right or down, as deep as you like,
    drag the dividers, drag tabs between panes, and ⌘-click a note in the
    sidebar to open it beside the current one. Each window remembers its
    layout.
  - **Block handle in the editor.** Hover a block for its handle: drag to
    reorder or to make columns, + to add a block, ⋮⋮ for a menu with copy
    link, duplicate, delete and "turn into". Selecting text shows the
    formatting toolbar.
  - **Task details.** Click a card for its notes, priority, due date,
    deadline, location, labels, reminders, nested subtasks and comments.
  - **Reminders** are scheduled as system notifications, so they arrive
    whether or not the app is open. Clicking one opens its todo list.
  - **Import and export.** Import Markdown and HTML files as pages, with the
    pictures beside them brought into the image store. Markdown images are
    now image blocks, not links. Export a note as Markdown, with its pictures
    copied into a folder beside the file.
  - **Quick Open (⌘K)**, an icon picker, and a menu bar with shortcuts.
  - **Links to blocks.** "Copy link to block" gives a `heracles://` link that
    opens the note and scrolls to the block.
  - **Paste goes through the core.** Rich text pasted into the editor is
    converted by the same allow-list as an import.
  - **`macos/release.sh`** builds a universal app, signs it with the
    Developer ID certificate under the hardened runtime with no exceptions,
    notarizes it and staples the ticket.

## [0.2.0] - 2026-11-22

Published on GitHub without its download, by mistake. The repository's
releases are immutable, so the file could not be added afterwards. 0.2.1 is
the same app with the download attached.

## [0.1.0]

### Added

- Local-first, hierarchical notebook: nest pages under pages or sections with
  drag-reorder/reparent in the sidebar.
- Multi-pane workspace with horizontal/vertical splits, tabbed panes, and
  drag-to-split; panes auto-collapse when emptied.
- TipTap rich-text editor with bubble menu, block handles, slash commands, task
  lists, and CodeMirror code blocks.
- Page states: favorite, archive, trash (with undo), and permanent delete.
- Per-page icon picker and the ability to open any page in its own window.
- Tasks / Kanban board: custom columns and cards with priority, due dates,
  labels, location, reminders, and comments; unlimited subtask nesting.
- SQLite storage via `better-sqlite3` + `drizzle-orm`; global render-error
  boundary.

[Unreleased]: https://github.com/
[0.3.0]: https://github.com/tacticalbadger/Heracles/releases/tag/v0.3.0
[0.2.1]: https://github.com/tacticalbadger/Heracles/releases/tag/v0.2.1
[0.2.0]: https://github.com/tacticalbadger/Heracles/releases/tag/v0.2.0
[0.1.0]: https://github.com/
