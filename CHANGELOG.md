# Changelog

All notable changes to Heracles are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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

### Technical

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
[0.1.0]: https://github.com/
