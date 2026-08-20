# Heracles

A local-first, hierarchical notebook for macOS. Electron shell, React renderer, TipTap rich-text editor, SQLite storage.

## Features

- **Notebooks (top-level groups)** — the sidebar is a filing cabinet: create any number of notebooks, each holding sections and pages/todos; rename, collapse, drag-reorder, and delete them
- **Hierarchical pages** — nest pages under pages or sections inside a notebook; drag-reorder/reparent in the sidebar
- **Create under active context** — new pages (blank or imported) are created as children of whatever page/section is currently active, in its notebook
- **Multi-pane workspace** — split panes horizontally or vertically, drag tabs/pages between panes, drop on a pane edge to split
- **Tabbed panes** — each pane has its own tab strip; close a tab without closing the pane
- **Rich-text editor** — TipTap with floating bubble menu (bold/italic/strike, headings), block handles (`⠿` / `+` / `⋮⋮`), task lists, code blocks (CodeMirror), and links
- **Images** — drag an image file onto a note (or paste a screenshot) to insert an image block; embedded inline as data URLs, so notes stay local-first
- **Column layouts** — drag a block by its `⠿` grip to another block's right edge to make columns; add more columns by dropping at the edge, and drag the boundary to resize
- **Import Markdown** — "New page from Markdown" turns one or more `.md` files into pages, converting headings, lists, task lists, code blocks, and links to editor content
- **Import HTML** — "New page from HTML" imports `.html`/`.htm` files the same way, preserving common formatting
- **Paste HTML** — pasting rich HTML (e.g. from a web page) is converted to clean editor content instead of raw markup
- **Page states** — favorite, archive, trash (with undo), permanent delete; trash, restore, and permanent delete apply to a page's whole subtree (deleting a todo list also removes its board data)
- **Auto-collapse panes** — deleting/trashing a page closes its tabs everywhere and collapses any pane that becomes empty, redistributing space to siblings
- **Icon picker** per page
- **Multiple windows** — open any page in its own window
- **Automatic updates** — checks for new versions on launch, downloads in the background, and shows a "Restart to update" banner; signed + notarized builds published to a Cloudflare R2 bucket
- **Error boundary** — render-error recovery without losing in-memory state
- **Tasks / Kanban board** — turn a page into a board with custom columns; cards carry priority, due dates, labels, location, reminders, and comments
- **Reorder columns** — drag a column left/right to rearrange the board; order is persisted
- **Subtasks** — unlimited nesting; drill into any subtask in the detail modal, with completion roll-up counts on cards
- **Subtask drag-reorder** — drag the `⋮⋮` handle to reorder subtasks within their parent
- **Move a subtask anywhere** — the `⤴` menu on each subtask can pop it out into its own card in any column, or re-parent it under any other task (self/descendants excluded)

## Tech stack

| Layer | Choice |
|---|---|
| Shell | Electron 41 |
| Renderer | React 18 + TypeScript 5.6 |
| Editor | TipTap 2 + CodeMirror 6 |
| Bundler | Vite 8 |
| Storage | SQLite via `better-sqlite3` + `drizzle-orm` |
| Packaging | `electron-builder` |

## Getting started

```bash
npm install
npm run dev
```

Starts Vite on `http://localhost:5173` and launches the Electron shell with DevTools detached.

## Scripts

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server + Electron with HMR |
| `npm run build` | Production renderer bundle into `dist/renderer/` |
| `npm run package:mac` | Build + package an unsigned macOS `.app` into `app/macOS/` |
| `npm run package:mac:dmg` | Build a `.dmg` installer into `app/macOS/` |
| `npm run package:win` | Build a Windows NSIS installer into `app/windows/` (run on Windows) |
| `npm run package:win:dir` | Build an unpacked Windows folder into `app/windows/` |
| `npm run release:mac` | Build a signed + notarized `zip` + `dmg` for distribution (needs `APPLE_API_*` env vars) |
| `npm run release:win` | Build a Windows NSIS installer for distribution (run on Windows) |
| `npm run release:upload` | Upload the built release + update feed to the R2 bucket |
| `npm run preview` | Preview the built renderer in a browser (no Electron) |
| `npm test` | Functional test of the SQLite layer (`scripts/db-cascade-test.cjs`) |

For full build/packaging/distribution instructions on both macOS and Windows
(including code signing notes and cross-compilation via Docker), see
[`BUILDING.md`](./BUILDING.md).

## Releases & auto-update

Updates are delivered with [`electron-updater`](https://www.electron.build/auto-update):
the app polls a release feed (`latest-mac.yml` / `latest.yml`) on a **Cloudflare R2
public bucket**, downloads new versions in the background, and prompts the user to
restart. electron-builder's `publish` config (`generic` provider) bakes that bucket
URL into each build's `app-update.yml`.

Releasing a new version:

1. **Bump `version`** in `package.json` (must be greater than the installed one).
2. Build the signed artifacts:
   - macOS: `APPLE_API_KEY=… APPLE_API_KEY_ID=… APPLE_API_ISSUER=… npm run release:mac`
     (signs with Developer ID, notarizes + staples the `.app`/`.zip`/`.dmg`).
   - Windows: `npm run release:win` on a Windows machine — see
     [`windowsInstructions.md`](./windowsInstructions.md) for signing.
3. **Upload** with `npm run release:upload` — it pushes the update feed
   (`latest-mac.yml` / `latest.yml`), the `*.zip` (+ `.blockmap`), and the
   installer (`.dmg` / `.exe`) to the **bucket root** so they are served at the
   public URL. It signs requests against R2's S3 API directly (no CLI needed);
   put an R2 API token in `R2_ACCESS_KEY_ID` / `R2_SECRET_ACCESS_KEY` env vars
   or a git-ignored `.env.r2` file. Pass `-- --dry-run` to preview.

For auto-update to *apply* (not just download), builds must be code-signed: macOS
needs a notarized Developer ID build (already wired); Windows needs an Authenticode
signature so the updater can verify the publisher — instructions in
[`windowsInstructions.md`](./windowsInstructions.md).

## Documentation

- [`CHANGELOG.md`](./CHANGELOG.md) — notable changes per release
- [`ROADMAP.md`](./ROADMAP.md) — known gaps and planned work
- [`docs/architecture.md`](./docs/architecture.md) — goals, stack, and design notes
- [`BUILDING.md`](./BUILDING.md) — build, package, and distribution guide
- [`windowsInstructions.md`](./windowsInstructions.md) — sign & ship the official Windows build

## Sibling repositories

Heracles ships from two platform repositories that share this codebase:

- [Heracles](https://github.com/mattdobbs/Heracles) — macOS (signed + notarized builds, R2 auto-update feed)
- [Heracles-Windows](https://github.com/mattdobbs/Heracles-Windows) — Windows (NSIS installer; signing per `windowsInstructions.md`)

Everything outside platform packaging config is shared code. Apply changes —
and their documentation updates — to both repositories together: commit to one,
then merge or cherry-pick into the other (the repos share git history, so
`git merge` works cleanly). Platform-specific bits (electron-builder
`mac`/`win`/`nsis` config, signing setup, the README intro) stay in their
own repo.

## Project structure

```
electron/                  Electron main + preload + SQLite layer
├── main.cjs               BrowserWindow lifecycle, IPC handlers
├── preload.cjs            window.heracles bridge (contextBridge)
├── db.cjs                 better-sqlite3 + drizzle schema & queries
└── migrations/            drizzle migrations
renderer/src/
├── App.tsx                Top-level state, IPC orchestration
├── ErrorBoundary.tsx      Global render-error recovery
├── editor/                TipTap extensions, slash menu, CodeMirror block
│   ├── ImageBlock.ts      Block image node (drag/paste → inline data URL)
│   ├── ColumnBlock.ts     Columns container node (flex row)
│   ├── Column.ts          Single resizable column node
│   ├── ColumnNodeView.tsx Column render + drag-to-resize handle
│   └── columnCommands.ts  Block-drop geometry, restructuring tx, normalizer
├── lib/
│   ├── markdownImport.ts  Markdown → TipTap-compatible HTML converter
│   └── htmlImport.ts      HTML sanitizer/normalizer for import & paste
├── workspace/
│   ├── Sidebar.tsx        Hierarchical tree, drag-drop reorder/reparent
│   ├── PaneTree.tsx       Recursive split/leaf renderer
│   ├── EditorPane.tsx     Tab strip + TipTap editor + bubble/block menus
│   ├── paneTreeOps.ts     Pure functions over the pane tree (split/move/close)
│   ├── PageMenu.tsx       Right-click context menu (active page)
│   ├── SidebarItemMenu.tsx Right-click context menu (sidebar item)
│   ├── SectionMenu.tsx    Section header context menu
│   ├── MovePagePicker.tsx "Move to..." dialog
│   ├── KanbanBoard.tsx    Task board: columns, cards, drag-drop, composer
│   ├── TaskCard.tsx       Single card (priority flag, due date, subtask count)
│   ├── TaskDetailModal.tsx Task detail: fields, subtasks, comments, move menu
│   └── IconPicker.tsx     Emoji icon picker
└── styles.css             All renderer styles
```

## Data storage

A single SQLite database file: `heracles.sqlite` inside Electron's `userData` directory.

| Mode | Path (macOS) |
|---|---|
| Dev | `~/Library/Application Support/heracles/heracles.sqlite` |
| Packaged | `~/Library/Application Support/Heracles/heracles.sqlite` |

Persisted tables: **notes**, plus **task_columns**, **tasks**, and **task_comments** for the Kanban feature. The `notes.kind` column is one of `page`, `section`, `todolist`, or `notebook`; notebooks are the top-level containers (`parent_id IS NULL`) and everything else lives inside one. On first launch after upgrading, a one-time migration creates a default notebook, adopts existing top-level notes into it, and writes a `heracles.pre-notebooks-<timestamp>.sqlite` backup alongside the database. Pane layout and open tabs live in renderer state; sidebar collapse state (including collapsed notebooks) is persisted in `localStorage`.

To inspect:

```bash
sqlite3 ~/Library/Application\ Support/heracles/heracles.sqlite "SELECT id, title, parent_id FROM notes;"
```

## Architecture notes

- **Pane tree** is an immutable `PaneNode` tree (`leaf` or `split`) manipulated by the pure functions in `renderer/src/workspace/paneTreeOps.ts`. Splits auto-collapse when reduced to one child; removing the last note in a leaf removes the leaf.
- **IPC** flows through `window.heracles.notes.*` and `window.heracles.tasks.*` declared in `electron/preload.cjs` and handled in `electron/main.cjs`. The renderer never touches SQLite directly. `tasks.move({ id, columnId, parentTaskId, beforeId })` handles both reordering and reparenting/promoting in one call.
- **Drag-and-drop** uses six custom MIME types: `application/x-heracles-tab` (tab moves between panes), `application/x-heracles-page` (sidebar page onto pane / between panes / into a notebook), `application/x-heracles-task` (card reorder/move between columns), `application/x-heracles-subtask` (subtask reorder within a parent), `application/x-heracles-column` (column reorder on a board), and `application/x-heracles-block` (editor block reorder / make a column).
- **Notebooks** — modeled as a `kind: 'notebook'` note pinned to the root, reusing the existing note tree (create/rename/reorder/icons/IPC). `electron/db.cjs` enforces the invariant via `resolveParentId` (notebooks stay at the root; non-notebooks can't sit at the root) and a one-time migration; deleting a notebook reassigns its children to the first remaining one. The sidebar renders notebooks as collapsible groups in place of the old "Private" section.
- **Columns** — `ColumnBlock` (a `<div data-type="columns">` flex row) holds `Column` nodes (`data-type="column"`, with a `data-width` flex weight), so layouts round-trip through the stored note HTML with no DB change. Dragging a block emits `application/x-heracles-block`; `columnCommands.ts` computes the drop target, restructures via one mapped transaction, and a `ColumnsNormalizer` appendTransaction plugin keeps rows valid (drops empty columns, unwraps rows with <2 columns).
- **Import & paste** — `electron/main.cjs` exposes `notes.importMarkdown()` and `notes.importHtml()` (native file dialogs that read file contents); the renderer converts them with `renderer/src/lib/markdownImport.ts` / `htmlImport.ts`. The same `htmlToEditorHtml` converter powers the editor's `handlePaste`, so pasted HTML is sanitized to supported nodes (`<img>` becomes an image block, `<pre>` becomes a CodeMirror block, checkbox lists become task lists); internal copies (marked with `data-pm-slice`) paste natively so column layouts survive. Markdown import still converts images to links, since relative image paths can't resolve after import.
- **Subtask moves** — because the task detail modal covers the board, subtasks are moved via the `⤴` menu (not by dragging onto a column). The menu offers every column (promote to a standalone card) and a depth-indented tree of every other task (reparent), filtering out the subtask itself, its descendants, and its current parent.
- **BubbleMenu host** — TipTap's bubble-menu plugin detaches its DOM via `element.remove()`, which clashes with React reconciliation if it has dynamic siblings. The menu is wrapped in a dedicated empty `.bubble-menu-host` div so the detachment never disturbs sibling reconciliation.

## License

See `LICENSE`.