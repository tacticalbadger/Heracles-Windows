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

This starts Vite on `http://localhost:5173` and launches the Electron shell.

## Build renderer bundle

```bash
npm run build
```