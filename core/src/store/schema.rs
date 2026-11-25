//! The database schema and how a connection is set up.
//!
//! The rules the original app enforced in its own code live in the schema
//! here: foreign keys cascade deletes, and `CHECK` constraints hold the
//! invariants that a bug could otherwise break.

use crate::error::{CoreError, Result};
use rusqlite::Connection;

/// Each entry moves the schema one version forward. `PRAGMA user_version`
/// records how many have run. Never edit an entry that has shipped: add one.
const MIGRATIONS: &[&str] = &[V1];

const V1: &str = r#"
CREATE TABLE notes (
    -- An integer key so the search index has a stable rowid to share.
    key        INTEGER PRIMARY KEY,
    id         TEXT NOT NULL UNIQUE,
    kind       TEXT NOT NULL CHECK (kind IN ('notebook', 'section', 'page', 'todolist')),
    parent_id  TEXT REFERENCES notes(id) ON DELETE CASCADE,
    title      TEXT NOT NULL,
    icon       TEXT,
    position   INTEGER NOT NULL DEFAULT 0,
    favorited  INTEGER NOT NULL DEFAULT 0 CHECK (favorited IN (0, 1)),
    archived   INTEGER NOT NULL DEFAULT 0 CHECK (archived IN (0, 1)),
    trashed    INTEGER NOT NULL DEFAULT 0 CHECK (trashed IN (0, 1)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    -- Notebooks sit at the top level, and nothing else does.
    CHECK ((kind = 'notebook') = (parent_id IS NULL)),
    CHECK (parent_id IS NULL OR parent_id <> id)
);
CREATE INDEX notes_by_parent ON notes(parent_id, position);

-- Content lives apart from the note row, so listing notes never reads it.
CREATE TABLE documents (
    note_id  TEXT PRIMARY KEY REFERENCES notes(id) ON DELETE CASCADE,
    content  TEXT NOT NULL,
    revision INTEGER NOT NULL
) WITHOUT ROWID;

-- Search index. Its rowid is the note's key. Triggers keep titles in step,
-- and saving a document writes the body.
CREATE VIRTUAL TABLE notes_fts USING fts5(
    title, body,
    tokenize = 'unicode61 remove_diacritics 2',
    prefix = '2 3 4'
);
CREATE TRIGGER notes_fts_insert AFTER INSERT ON notes BEGIN
    INSERT INTO notes_fts(rowid, title, body) VALUES (new.key, new.title, '');
END;
CREATE TRIGGER notes_fts_title AFTER UPDATE OF title ON notes BEGIN
    UPDATE notes_fts SET title = new.title WHERE rowid = new.key;
END;
CREATE TRIGGER notes_fts_delete AFTER DELETE ON notes BEGIN
    DELETE FROM notes_fts WHERE rowid = old.key;
END;

CREATE TABLE task_columns (
    id           TEXT PRIMARY KEY,
    todo_list_id TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    name         TEXT NOT NULL,
    position     INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL
);
CREATE INDEX task_columns_by_list ON task_columns(todo_list_id, position);

CREATE TABLE tasks (
    id             TEXT PRIMARY KEY,
    todo_list_id   TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    parent_task_id TEXT REFERENCES tasks(id) ON DELETE CASCADE,
    column_id      TEXT REFERENCES task_columns(id) ON DELETE CASCADE,
    title          TEXT NOT NULL DEFAULT '',
    description    TEXT NOT NULL DEFAULT '',
    completed      INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0, 1)),
    position       INTEGER NOT NULL DEFAULT 0,
    due_date       TEXT,
    deadline       TEXT,
    priority       INTEGER NOT NULL DEFAULT 4 CHECK (priority BETWEEN 1 AND 4),
    labels         TEXT NOT NULL DEFAULT '[]' CHECK (json_valid(labels)),
    reminders      TEXT NOT NULL DEFAULT '[]' CHECK (json_valid(reminders)),
    location       TEXT,
    created_at     TEXT NOT NULL,
    updated_at     TEXT NOT NULL,
    -- A card sits in a column. A subtask sits under its parent. Never both,
    -- never neither, so no task can be stored where the board cannot show it.
    CHECK ((parent_task_id IS NULL) <> (column_id IS NULL))
);
CREATE INDEX tasks_by_list ON tasks(todo_list_id);
CREATE INDEX tasks_by_parent ON tasks(parent_task_id, position);
CREATE INDEX tasks_by_column ON tasks(column_id, position);

CREATE TABLE task_comments (
    id         TEXT PRIMARY KEY,
    task_id    TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    body       TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX task_comments_by_task ON task_comments(task_id, created_at);

-- Images are files named by the SHA-256 of their bytes. This table records
-- what each one is, and `document_images` which notes still use it.
CREATE TABLE images (
    hash       TEXT PRIMARY KEY,
    mime       TEXT NOT NULL,
    byte_count INTEGER NOT NULL,
    created_at TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE document_images (
    note_id TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    hash    TEXT NOT NULL,
    PRIMARY KEY (note_id, hash)
) WITHOUT ROWID;
CREATE INDEX document_images_by_hash ON document_images(hash);

CREATE TABLE settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) WITHOUT ROWID;
"#;

/// Configure a fresh connection and bring the schema up to date.
pub(crate) fn prepare(connection: &mut Connection) -> Result<()> {
    // WAL lets a crash lose at most the write in flight, never the file.
    connection.pragma_update(None, "journal_mode", "WAL")?;
    connection.pragma_update(None, "synchronous", "NORMAL")?;
    connection.pragma_update(None, "foreign_keys", true)?;
    connection.busy_timeout(std::time::Duration::from_secs(5))?;

    let version: u32 = connection.pragma_query_value(None, "user_version", |row| row.get(0))?;
    let version = version as usize;
    if version > MIGRATIONS.len() {
        return Err(CoreError::invalid(format!(
            "this library was written by a newer version of Heracles (schema {version}, this build knows {})",
            MIGRATIONS.len()
        )));
    }
    for (index, migration) in MIGRATIONS.iter().enumerate().skip(version) {
        let tx = connection.transaction()?;
        tx.execute_batch(migration)?;
        tx.pragma_update(None, "user_version", (index + 1) as u32)?;
        tx.commit()?;
    }
    Ok(())
}
