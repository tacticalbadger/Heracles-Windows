# Heracles

A local-first, hierarchical notebook for macOS. Electron shell, React renderer, TipTap rich-text editor, SQLite storage.

## Features

- **Hierarchical pages** — nest pages under pages or sections; drag-reorder/reparent in the sidebar
- **Multi-pane workspace** — split panes horizontally or vertically, drag tabs/pages between panes, drop on a pane edge to split
- **Tabbed panes** — each pane has its own tab strip; close a tab without closing the pane
- **Rich-text editor** — TipTap with floating bubble menu (bold/italic/strike, headings), block handles (`+` / `⋮⋮`), task lists, code blocks (CodeMirror)
- **Page states** — favorite, archive, trash (with undo), permanent delete
- **Auto-collapse panes** — deleting/trashing a page closes its tabs everywhere and collapses any pane that becomes empty, redistributing space to siblings
- **Icon picker** per page
- **Multiple windows** — open any page in its own window
- **Error boundary** — render-error recovery without losing in-memory state

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
| `npm run preview` | Preview the built renderer in a browser (no Electron) |

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
├── workspace/
│   ├── Sidebar.tsx        Hierarchical tree, drag-drop reorder/reparent
│   ├── PaneTree.tsx       Recursive split/leaf renderer
│   ├── EditorPane.tsx     Tab strip + TipTap editor + bubble/block menus
│   ├── paneTreeOps.ts     Pure functions over the pane tree (split/move/close)
│   ├── PageMenu.tsx       Right-click context menu (active page)
│   ├── SidebarItemMenu.tsx Right-click context menu (sidebar item)
│   ├── SectionMenu.tsx    Section header context menu
│   ├── MovePagePicker.tsx "Move to..." dialog
│   └── IconPicker.tsx     Emoji icon picker
└── styles.css             All renderer styles
```

## Data storage

A single SQLite database file: `heracles.sqlite` inside Electron's `userData` directory.

| Mode | Path (macOS) |
|---|---|
| Dev | `~/Library/Application Support/heracles/heracles.sqlite` |
| Packaged | `~/Library/Application Support/Heracles/heracles.sqlite` |

Only the **notes** table is persisted. Pane layout, open tabs, and sidebar collapse state live in renderer state and reset on app restart.

To inspect:

```bash
sqlite3 ~/Library/Application\ Support/heracles/heracles.sqlite "SELECT id, title, parent_id FROM notes;"
```

## Architecture notes

- **Pane tree** is an immutable `PaneNode` tree (`leaf` or `split`) manipulated by the pure functions in `renderer/src/workspace/paneTreeOps.ts`. Splits auto-collapse when reduced to one child; removing the last note in a leaf removes the leaf.
- **IPC** flows through `window.heracles.notes.*` declared in `electron/preload.cjs` and handled in `electron/main.cjs`. The renderer never touches SQLite directly.
- **Drag-and-drop** uses two custom MIME types: `application/x-heracles-tab` (tab moves between panes) and `application/x-heracles-page` (sidebar page onto pane / between panes).
- **BubbleMenu host** — TipTap's bubble-menu plugin detaches its DOM via `element.remove()`, which clashes with React reconciliation if it has dynamic siblings. The menu is wrapped in a dedicated empty `.bubble-menu-host` div so the detachment never disturbs sibling reconciliation.

## License

See `LICENSE`.