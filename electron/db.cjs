const path = require('path');
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
  migrate(orm, { migrationsFolder });

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
};

const listNotes = () => {
  return orm.select().from(notes).orderBy(desc(notes.updatedAt)).all();
};

const getNote = (id) => {
  return orm.select().from(notes).where(eq(notes.id, id)).get();
};

const createNote = ({ title, content }) => {
  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  const nextTitle = title?.trim() || 'Untitled';
  const nextContent = content ?? '<p></p>';

  orm
    .insert(notes)
    .values({
      id,
      title: nextTitle,
      content: nextContent,
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

const updateNote = ({ id, title, content }) => {
  const existing = getNote(id);
  if (!existing) {
    return null;
  }

  const now = new Date().toISOString();
  const nextTitle = title?.trim() || existing.title;
  const nextContent = content ?? existing.content;

  orm
    .update(notes)
    .set({
      title: nextTitle,
      content: nextContent,
      updatedAt: now
    })
    .where(eq(notes.id, id))
    .run();

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
  emptyTrash
};
