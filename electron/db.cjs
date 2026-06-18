const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const Database = require('better-sqlite3');
const { drizzle } = require('drizzle-orm/better-sqlite3');
const { migrate } = require('drizzle-orm/better-sqlite3/migrator');
const { desc, eq } = require('drizzle-orm');
const { sqliteTable, text, integer } = require('drizzle-orm/sqlite-core');

const notes = sqliteTable('notes', {
  id: text('id').primaryKey(),
  title: text('title').notNull(),
  content: text('content').notNull(),
  icon: text('icon'),
  kind: text('kind').notNull().default('page'),
  parentId: text('parent_id'),
  position: integer('position').notNull().default(0),
  favorited: integer('favorited', { mode: 'boolean' }).notNull().default(false),
  createdAt: text('created_at').notNull(),
  updatedAt: text('updated_at').notNull(),
  archived: integer('archived', { mode: 'boolean' }).notNull().default(false),
  trashed: integer('trashed', { mode: 'boolean' }).notNull().default(false)
});

let db;
let orm;

const initDatabase = (userDataPath) => {
  if (db) {
    return;
  }

  const dbPath = path.join(userDataPath, 'heracles.sqlite');
  db = new Database(dbPath);
  orm = drizzle(db);
  const migrationsFolder = path.join(__dirname, 'migrations');
  const journalPath = path.join(migrationsFolder, 'meta', '_journal.json');
  if (fs.existsSync(journalPath)) {
    migrate(orm, { migrationsFolder });
  }

  db.exec(`
    CREATE TABLE IF NOT EXISTS notes (
      id TEXT PRIMARY KEY,
      title TEXT NOT NULL,
      content TEXT NOT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      archived INTEGER NOT NULL DEFAULT 0,
      trashed INTEGER NOT NULL DEFAULT 0
    );
  `);

  const columns = db.prepare('PRAGMA table_info(notes)').all().map((col) => col.name);
  if (!columns.includes('archived')) {
    db.exec('ALTER TABLE notes ADD COLUMN archived INTEGER NOT NULL DEFAULT 0;');
  }
  if (!columns.includes('trashed')) {
    db.exec('ALTER TABLE notes ADD COLUMN trashed INTEGER NOT NULL DEFAULT 0;');
  }
  if (!columns.includes('icon')) {
    db.exec('ALTER TABLE notes ADD COLUMN icon TEXT;');
  }
  if (!columns.includes('parent_id')) {
    db.exec('ALTER TABLE notes ADD COLUMN parent_id TEXT;');
  }
  if (!columns.includes('position')) {
    db.exec('ALTER TABLE notes ADD COLUMN position INTEGER NOT NULL DEFAULT 0;');
  }
  if (!columns.includes('favorited')) {
    db.exec('ALTER TABLE notes ADD COLUMN favorited INTEGER NOT NULL DEFAULT 0;');
  }
  if (!columns.includes('kind')) {
    db.exec("ALTER TABLE notes ADD COLUMN kind TEXT NOT NULL DEFAULT 'page';");
  }

  // Todo-list / kanban tables -------------------------------------------------
  db.exec(`
    CREATE TABLE IF NOT EXISTS task_columns (
      id TEXT PRIMARY KEY,
      todo_list_id TEXT NOT NULL,
      name TEXT NOT NULL,
      position INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );
  `);
  db.exec(`
    CREATE TABLE IF NOT EXISTS tasks (
      id TEXT PRIMARY KEY,
      todo_list_id TEXT NOT NULL,
      parent_task_id TEXT,
      column_id TEXT,
      title TEXT NOT NULL,
      description TEXT NOT NULL DEFAULT '',
      completed INTEGER NOT NULL DEFAULT 0,
      position INTEGER NOT NULL DEFAULT 0,
      due_date TEXT,
      deadline TEXT,
      priority INTEGER NOT NULL DEFAULT 4,
      labels TEXT NOT NULL DEFAULT '[]',
      reminders TEXT NOT NULL DEFAULT '[]',
      location TEXT,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );
  `);
  db.exec(`
    CREATE TABLE IF NOT EXISTS task_comments (
      id TEXT PRIMARY KEY,
      task_id TEXT NOT NULL,
      body TEXT NOT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );
  `);
  db.exec('CREATE INDEX IF NOT EXISTS idx_tasks_todo_list_id ON tasks(todo_list_id);');
  db.exec('CREATE INDEX IF NOT EXISTS idx_tasks_parent_task_id ON tasks(parent_task_id);');
  db.exec('CREATE INDEX IF NOT EXISTS idx_tasks_column_id ON tasks(column_id);');
  db.exec('CREATE INDEX IF NOT EXISTS idx_task_columns_todo_list_id ON task_columns(todo_list_id);');
  db.exec('CREATE INDEX IF NOT EXISTS idx_task_comments_task_id ON task_comments(task_id);');
};

const listNotes = () => {
  return orm.select().from(notes).orderBy(desc(notes.updatedAt)).all();
};

const getNote = (id) => {
  return orm.select().from(notes).where(eq(notes.id, id)).get();
};

const createNote = ({ title, content, icon, parentId, kind }) => {
  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  const nextKind =
    kind === 'section' ? 'section' : kind === 'todolist' ? 'todolist' : 'page';
  const nextTitle =
    title?.trim() ||
    (nextKind === 'section'
      ? 'New section'
      : nextKind === 'todolist'
        ? 'New todo list'
        : 'Untitled');
  const nextContent = content ?? (nextKind === 'section' || nextKind === 'todolist' ? '' : '<p></p>');
  const nextIcon = icon ?? null;
  const nextParentId = parentId ?? null;

  // Place new note at the END of its parent's siblings.
  const siblings = orm.select().from(notes).all().filter((n) => (n.parentId ?? null) === nextParentId);
  const nextPosition = siblings.length > 0 ? Math.max(...siblings.map((n) => n.position)) + 1 : 0;

  orm
    .insert(notes)
    .values({
      id,
      title: nextTitle,
      content: nextContent,
      icon: nextIcon,
      kind: nextKind,
      parentId: nextParentId,
      position: nextPosition,
      favorited: false,
      createdAt: now,
      updatedAt: now,
      archived: false,
      trashed: false
    })
    .run();

  return getNote(id);
};

const setNoteStatus = ({ id, archived, trashed }) => {
  const existing = getNote(id);
  if (!existing) {
    return null;
  }

  const now = new Date().toISOString();
  orm
    .update(notes)
    .set({
      archived: archived ?? existing.archived,
      trashed: trashed ?? existing.trashed,
      updatedAt: now
    })
    .where(eq(notes.id, id))
    .run();

  return getNote(id);
};

const archiveNote = ({ id, archived }) => setNoteStatus({ id, archived, trashed: false });

const trashNote = ({ id }) => setNoteStatus({ id, trashed: true, archived: false });

const restoreNote = ({ id }) => setNoteStatus({ id, trashed: false, archived: false });

const deleteNote = ({ id }) => {
  orm.delete(notes).where(eq(notes.id, id)).run();
  return { success: true };
};

const emptyTrash = () => {
  orm.delete(notes).where(eq(notes.trashed, true)).run();
  return { success: true };
};

const updateNote = ({ id, title, content, icon }) => {
  const existing = getNote(id);
  if (!existing) {
    return null;
  }

  const now = new Date().toISOString();
  const nextTitle = title?.trim() || existing.title;
  const nextContent = content ?? existing.content;
  const nextIcon = icon === undefined ? existing.icon : icon;

  orm
    .update(notes)
    .set({
      title: nextTitle,
      content: nextContent,
      icon: nextIcon,
      updatedAt: now
    })
    .where(eq(notes.id, id))
    .run();

  return getNote(id);
};

const setNoteParent = ({ id, parentId }) => {
  const existing = getNote(id);
  if (!existing) return null;
  const now = new Date().toISOString();
  orm
    .update(notes)
    .set({ parentId: parentId ?? null, updatedAt: now })
    .where(eq(notes.id, id))
    .run();
  return getNote(id);
};

const setNoteFavorited = ({ id, favorited }) => {
  const existing = getNote(id);
  if (!existing) return null;
  const now = new Date().toISOString();
  orm
    .update(notes)
    .set({ favorited: !!favorited, updatedAt: now })
    .where(eq(notes.id, id))
    .run();
  return getNote(id);
};

// Detect if `candidateId` is the same as `nodeId` or one of its descendants;
// used to prevent moving a node into its own subtree.
const isSelfOrDescendant = (candidateId, nodeId) => {
  if (!candidateId) return false;
  if (candidateId === nodeId) return true;
  const all = orm.select().from(notes).all();
  const blocked = new Set([nodeId]);
  let added = true;
  while (added) {
    added = false;
    for (const n of all) {
      if (n.parentId && blocked.has(n.parentId) && !blocked.has(n.id)) {
        blocked.add(n.id);
        added = true;
      }
    }
  }
  return blocked.has(candidateId);
};

/**
 * Move a note to a new parent and position. `beforeId` is the sibling
 * the note should be inserted before (or null = append). All siblings under
 * the new parent are renumbered with consecutive integer positions.
 */
const reorderNote = ({ id, parentId, beforeId }) => {
  const existing = getNote(id);
  if (!existing) return null;

  const targetParentId = parentId ?? null;
  if (isSelfOrDescendant(targetParentId, id)) {
    return existing;
  }

  const now = new Date().toISOString();
  orm
    .update(notes)
    .set({ parentId: targetParentId, updatedAt: now })
    .where(eq(notes.id, id))
    .run();

  const all = orm.select().from(notes).all();
  const siblings = all
    .filter((n) => (n.parentId ?? null) === targetParentId && n.id !== id)
    .sort((a, b) => a.position - b.position || a.title.localeCompare(b.title));

  let insertIndex = siblings.length;
  if (beforeId) {
    const idx = siblings.findIndex((n) => n.id === beforeId);
    if (idx >= 0) insertIndex = idx;
  }

  const moving = getNote(id);
  const ordered = [
    ...siblings.slice(0, insertIndex),
    moving,
    ...siblings.slice(insertIndex)
  ];

  ordered.forEach((n, index) => {
    if (n.position !== index) {
      orm.update(notes).set({ position: index }).where(eq(notes.id, n.id)).run();
    }
  });

  return getNote(id);
};

// ─── Todo lists ────────────────────────────────────────────────────────────
// Helpers: rows in SQLite use snake_case, while the JS API uses camelCase.

const mapColumnRow = (row) => {
  if (!row) return null;
  return {
    id: row.id,
    todoListId: row.todo_list_id,
    name: row.name,
    position: row.position,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
};

const mapTaskRow = (row) => {
  if (!row) return null;
  let labels = [];
  let reminders = [];
  try {
    labels = JSON.parse(row.labels ?? '[]');
    if (!Array.isArray(labels)) labels = [];
  } catch {
    labels = [];
  }
  try {
    reminders = JSON.parse(row.reminders ?? '[]');
    if (!Array.isArray(reminders)) reminders = [];
  } catch {
    reminders = [];
  }
  return {
    id: row.id,
    todoListId: row.todo_list_id,
    parentTaskId: row.parent_task_id ?? null,
    columnId: row.column_id ?? null,
    title: row.title,
    description: row.description ?? '',
    completed: !!row.completed,
    position: row.position,
    dueDate: row.due_date ?? null,
    deadline: row.deadline ?? null,
    priority: row.priority ?? 4,
    labels,
    reminders,
    location: row.location ?? null,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
};

const mapCommentRow = (row) => {
  if (!row) return null;
  return {
    id: row.id,
    taskId: row.task_id,
    body: row.body,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
};

const listColumns = (todoListId) => {
  const rows = db
    .prepare('SELECT * FROM task_columns WHERE todo_list_id = ? ORDER BY position ASC, created_at ASC')
    .all(todoListId);
  return rows.map(mapColumnRow);
};

const createColumn = ({ todoListId, name }) => {
  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  const existing = db
    .prepare('SELECT MAX(position) AS max FROM task_columns WHERE todo_list_id = ?')
    .get(todoListId);
  const nextPosition = existing && existing.max != null ? existing.max + 1 : 0;
  db.prepare(
    'INSERT INTO task_columns (id, todo_list_id, name, position, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
  ).run(id, todoListId, (name && name.trim()) || 'Untitled', nextPosition, now, now);
  return mapColumnRow(db.prepare('SELECT * FROM task_columns WHERE id = ?').get(id));
};

const updateColumn = ({ id, name }) => {
  const existing = db.prepare('SELECT * FROM task_columns WHERE id = ?').get(id);
  if (!existing) return null;
  const now = new Date().toISOString();
  const nextName = name == null ? existing.name : name.trim() || existing.name;
  db.prepare('UPDATE task_columns SET name = ?, updated_at = ? WHERE id = ?').run(nextName, now, id);
  return mapColumnRow(db.prepare('SELECT * FROM task_columns WHERE id = ?').get(id));
};

const deleteColumn = ({ id }) => {
  const existing = db.prepare('SELECT * FROM task_columns WHERE id = ?').get(id);
  if (!existing) return { success: false };
  // Find or create a fallback column in the same list to receive orphaned tasks.
  const remaining = db
    .prepare('SELECT * FROM task_columns WHERE todo_list_id = ? AND id != ? ORDER BY position ASC LIMIT 1')
    .get(existing.todo_list_id, id);
  let fallbackId = remaining ? remaining.id : null;
  if (!fallbackId) {
    const fallback = createColumn({ todoListId: existing.todo_list_id, name: 'Up Next' });
    fallbackId = fallback.id;
  }
  const now = new Date().toISOString();
  db.prepare('UPDATE tasks SET column_id = ?, updated_at = ? WHERE column_id = ?').run(
    fallbackId,
    now,
    id
  );
  db.prepare('DELETE FROM task_columns WHERE id = ?').run(id);
  // Re-sequence remaining columns
  const cols = db
    .prepare('SELECT id FROM task_columns WHERE todo_list_id = ? ORDER BY position ASC, created_at ASC')
    .all(existing.todo_list_id);
  cols.forEach((col, index) => {
    db.prepare('UPDATE task_columns SET position = ? WHERE id = ?').run(index, col.id);
  });
  return { success: true, fallbackId };
};

const reorderColumns = ({ todoListId, orderedIds }) => {
  if (!Array.isArray(orderedIds)) return null;
  const now = new Date().toISOString();
  const stmt = db.prepare('UPDATE task_columns SET position = ?, updated_at = ? WHERE id = ? AND todo_list_id = ?');
  orderedIds.forEach((columnId, index) => {
    stmt.run(index, now, columnId, todoListId);
  });
  return listColumns(todoListId);
};

const listTasks = (todoListId) => {
  const rows = db
    .prepare('SELECT * FROM tasks WHERE todo_list_id = ? ORDER BY position ASC, created_at ASC')
    .all(todoListId);
  return rows.map(mapTaskRow);
};

const getTask = (id) => {
  const row = db.prepare('SELECT * FROM tasks WHERE id = ?').get(id);
  return row ? mapTaskRow(row) : null;
};

const createTask = ({
  todoListId,
  columnId = null,
  parentTaskId = null,
  title = '',
  description = '',
  priority = 4
}) => {
  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  // New task placed at the END of its sibling group.
  const sibling = db
    .prepare(
      'SELECT MAX(position) AS max FROM tasks WHERE todo_list_id = ? AND ' +
        (parentTaskId == null
          ? 'parent_task_id IS NULL AND column_id IS ?'
          : 'parent_task_id = ?')
    )
    .get(todoListId, parentTaskId == null ? columnId : parentTaskId);
  const nextPosition = sibling && sibling.max != null ? sibling.max + 1 : 0;
  db.prepare(
    `INSERT INTO tasks (
      id, todo_list_id, parent_task_id, column_id, title, description,
      completed, position, due_date, deadline, priority, labels, reminders, location,
      created_at, updated_at
    ) VALUES (?, ?, ?, ?, ?, ?, 0, ?, NULL, NULL, ?, '[]', '[]', NULL, ?, ?)`
  ).run(
    id,
    todoListId,
    parentTaskId,
    parentTaskId ? null : columnId,
    title || '',
    description || '',
    nextPosition,
    priority,
    now,
    now
  );
  return getTask(id);
};

const updateTask = ({ id, ...patch }) => {
  const existing = getTask(id);
  if (!existing) return null;
  const now = new Date().toISOString();
  const fields = [];
  const values = [];
  const setField = (column, value) => {
    fields.push(`${column} = ?`);
    values.push(value);
  };
  if ('title' in patch) setField('title', patch.title || '');
  if ('description' in patch) setField('description', patch.description ?? '');
  if ('completed' in patch) setField('completed', patch.completed ? 1 : 0);
  if ('dueDate' in patch) setField('due_date', patch.dueDate ?? null);
  if ('deadline' in patch) setField('deadline', patch.deadline ?? null);
  if ('priority' in patch) setField('priority', patch.priority ?? 4);
  if ('labels' in patch) setField('labels', JSON.stringify(Array.isArray(patch.labels) ? patch.labels : []));
  if ('reminders' in patch)
    setField('reminders', JSON.stringify(Array.isArray(patch.reminders) ? patch.reminders : []));
  if ('location' in patch) setField('location', patch.location ?? null);
  if (fields.length === 0) return existing;
  fields.push('updated_at = ?');
  values.push(now);
  values.push(id);
  db.prepare(`UPDATE tasks SET ${fields.join(', ')} WHERE id = ?`).run(...values);
  return getTask(id);
};

const toggleTaskComplete = ({ id, completed }) => {
  return updateTask({ id, completed: !!completed });
};

// Returns true if `candidateAncestorId` is the same as `taskId` or one of its descendants.
const isTaskSelfOrDescendant = (candidateAncestorId, taskId) => {
  if (!candidateAncestorId) return false;
  if (candidateAncestorId === taskId) return true;
  const all = db.prepare('SELECT id, parent_task_id FROM tasks').all();
  const blocked = new Set([taskId]);
  let added = true;
  while (added) {
    added = false;
    for (const t of all) {
      if (t.parent_task_id && blocked.has(t.parent_task_id) && !blocked.has(t.id)) {
        blocked.add(t.id);
        added = true;
      }
    }
  }
  return blocked.has(candidateAncestorId);
};

const moveTask = ({ id, columnId = null, parentTaskId = null, beforeId = null }) => {
  const existing = getTask(id);
  if (!existing) return null;
  if (parentTaskId && isTaskSelfOrDescendant(parentTaskId, id)) {
    return existing; // can't reparent under self/descendant
  }
  const now = new Date().toISOString();
  // When a task has a parent, column is irrelevant (it inherits from root via UI).
  const nextColumnId = parentTaskId ? null : columnId;
  db.prepare(
    'UPDATE tasks SET parent_task_id = ?, column_id = ?, updated_at = ? WHERE id = ?'
  ).run(parentTaskId ?? null, nextColumnId ?? null, now, id);

  // Re-sequence siblings.
  const siblingsQuery = parentTaskId
    ? db
        .prepare('SELECT id FROM tasks WHERE parent_task_id = ? AND id != ? ORDER BY position ASC, created_at ASC')
        .all(parentTaskId, id)
    : db
        .prepare(
          'SELECT id FROM tasks WHERE todo_list_id = ? AND parent_task_id IS NULL AND column_id IS ? AND id != ? ORDER BY position ASC, created_at ASC'
        )
        .all(existing.todoListId, nextColumnId, id);

  let insertIndex = siblingsQuery.length;
  if (beforeId) {
    const idx = siblingsQuery.findIndex((s) => s.id === beforeId);
    if (idx >= 0) insertIndex = idx;
  }
  const ordered = [
    ...siblingsQuery.slice(0, insertIndex).map((s) => s.id),
    id,
    ...siblingsQuery.slice(insertIndex).map((s) => s.id)
  ];
  ordered.forEach((taskId, index) => {
    db.prepare('UPDATE tasks SET position = ? WHERE id = ?').run(index, taskId);
  });
  return getTask(id);
};

const deleteTask = ({ id }) => {
  // Cascade-delete subtasks.
  const all = db.prepare('SELECT id, parent_task_id FROM tasks').all();
  const toDelete = new Set([id]);
  let added = true;
  while (added) {
    added = false;
    for (const t of all) {
      if (t.parent_task_id && toDelete.has(t.parent_task_id) && !toDelete.has(t.id)) {
        toDelete.add(t.id);
        added = true;
      }
    }
  }
  const placeholders = Array.from(toDelete).map(() => '?').join(',');
  if (placeholders.length > 0) {
    db.prepare(`DELETE FROM task_comments WHERE task_id IN (${placeholders})`).run(
      ...Array.from(toDelete)
    );
    db.prepare(`DELETE FROM tasks WHERE id IN (${placeholders})`).run(...Array.from(toDelete));
  }
  return { success: true, deletedIds: Array.from(toDelete) };
};

const listComments = (taskId) => {
  const rows = db
    .prepare('SELECT * FROM task_comments WHERE task_id = ? ORDER BY created_at ASC')
    .all(taskId);
  return rows.map(mapCommentRow);
};

const createComment = ({ taskId, body }) => {
  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  db.prepare(
    'INSERT INTO task_comments (id, task_id, body, created_at, updated_at) VALUES (?, ?, ?, ?, ?)'
  ).run(id, taskId, body || '', now, now);
  return mapCommentRow(db.prepare('SELECT * FROM task_comments WHERE id = ?').get(id));
};

const updateComment = ({ id, body }) => {
  const existing = db.prepare('SELECT * FROM task_comments WHERE id = ?').get(id);
  if (!existing) return null;
  const now = new Date().toISOString();
  db.prepare('UPDATE task_comments SET body = ?, updated_at = ? WHERE id = ?').run(
    body || '',
    now,
    id
  );
  return mapCommentRow(db.prepare('SELECT * FROM task_comments WHERE id = ?').get(id));
};

const deleteComment = ({ id }) => {
  db.prepare('DELETE FROM task_comments WHERE id = ?').run(id);
  return { success: true };
};

// Convenience: when a todo list is created we seed it with default columns.
const seedDefaultColumns = (todoListId) => {
  const defaults = ['Up Next', 'Active', 'Done'];
  return defaults.map((name) => createColumn({ todoListId, name }));
};

module.exports = {
  initDatabase,
  listNotes,
  getNote,
  createNote,
  updateNote,
  archiveNote,
  trashNote,
  restoreNote,
  deleteNote,
  emptyTrash,
  setNoteParent,
  setNoteFavorited,
  reorderNote,
  // Tasks
  listColumns,
  createColumn,
  updateColumn,
  deleteColumn,
  reorderColumns,
  listTasks,
  getTask,
  createTask,
  updateTask,
  toggleTaskComplete,
  moveTask,
  deleteTask,
  listComments,
  createComment,
  updateComment,
  deleteComment,
  seedDefaultColumns
};
