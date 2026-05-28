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
  const nextKind = kind === 'section' ? 'section' : 'page';
  const nextTitle = title?.trim() || (nextKind === 'section' ? 'New section' : 'Untitled');
  const nextContent = content ?? (nextKind === 'section' ? '' : '<p></p>');
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
  reorderNote
};
