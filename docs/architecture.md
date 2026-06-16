# Notebook App Architecture Notes

This document captures the current product goals and the chosen technical stack for the
Notion-like, multi-pane notebook application.

## Goals
- Block-based editor similar to Notion.
- Multi-pane layout (side-by-side and vertical splits) like Obsidian.
- Clean, modern UI with consistent UX across OS.
- Slash commands for formatting and block creation.
- Strong code block experience.
- Offline-first with local storage now, cloud sync later.
- Built-in features: toast notifications, to-do list, calendar integration for Office 365,
  and future Slack notifications.

## Selected Stack
- Desktop shell: Electron
- UI: React + TypeScript
- Editor engine: TipTap (ProseMirror)
- Code blocks: CodeMirror 6
- Local data: SQLite with Drizzle ORM

## High-Level Architecture
- Electron main process
  - App lifecycle, window management, OS integrations.
  - Database access via a single IPC service to keep the renderer clean.
- Renderer process (React)
  - Editor UI, multi-pane layout, command palette, notifications.
  - Uses IPC to query and mutate data.

## Data Model (Block-Based)
A document is composed of ordered blocks. Each block has a type and properties.

Suggested tables:
- documents
  - id, title, created_at, updated_at
- blocks
  - id, document_id, type, order, props_json, text

Example block types:
- paragraph, heading, list, todo, code, quote, callout, divider

## Editor Design
- TipTap schema defines each block node and its attributes.
- Slash commands map to block creation or transformation.
- Code blocks embed CodeMirror 6 for syntax highlighting and editing.

## Multi-Pane Layout
- Pane tree (`renderer/src/workspace/paneTreeOps.ts`) stored as a recursive
  union of `LeafPane` and `SplitPane`. Each split has direction, children, and
  ratios.
- Each leaf has its own ordered tab list of note IDs and an active tab.
- Splits are unbounded — any leaf may split horizontally or vertically.
- Closing a leaf collapses parent splits with only one remaining child.
- Tab interactions:
  - Click tab → activate
  - Right-click tab → split right / split down / close
  - ⌘/Ctrl+click sidebar item → open in new split
- Resize handle between siblings updates ratios in place (Obsidian-style).
- The component layer is split into `paneTreeOps.ts` (pure tree state),
  `PaneTree.tsx` (recursive renderer), `EditorPane.tsx` (per-leaf editor +
  tab strip), and `Sidebar.tsx` (Notion-style page list).

## Tasks / Kanban (implemented)
- A page can be rendered as a Kanban board (`renderer/src/workspace/KanbanBoard.tsx`).
- Tables: `task_columns`, `tasks` (self-referential via `parent_task_id` for
  unlimited subtask nesting), and `task_comments`.
- Cards (`TaskCard.tsx`) show priority, due date, labels, location, and a
  completed/total subtask roll-up.
- `TaskDetailModal.tsx` edits a task's fields, comments, and subtasks, and lets
  you drill into any subtask.
- A single IPC call, `tasks.move({ id, columnId, parentTaskId, beforeId })`,
  covers every move: reorder within a column/parent, move between columns,
  promote a subtask to a top-level card, or reparent it under another task.
- Drag-and-drop MIME types: `application/x-heracles-task` (cards on the board)
  and `application/x-heracles-subtask` (subtasks within the detail modal).
- Because the detail modal overlays the board, subtasks are relocated through a
  `⤴` "move to…" menu rather than by dragging onto a column. Candidate targets
  exclude the subtask itself, its descendants, and its current parent to prevent
  cycles.

## Offline-First Approach
- All data stored in local SQLite.
- Sync layer added later (separate module) to avoid early complexity.

## Phased Roadmap (Guidance)
Phase 1 (MVP)
- Core editor + block system
- Slash commands
- Code blocks
- Multi-pane layout
- Local SQLite storage
- Basic to-do list + toast notifications

Phase 2
- Calendar view + Office 365 integration
- Slack notifications
- Search and backlinks

Phase 3
- Cloud sync and multi-device support
- Conflict resolution strategy (CRDT or merge-based)

## Open Questions
- Sync model (CRDT vs merge-based) and conflict resolution.
- Attachment storage strategy (local files vs DB blobs).
- Encryption requirements for local data.
