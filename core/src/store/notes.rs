//! Notes and their documents.

use super::images::referenced_images;
use super::tasks::{copy_board, seed_default_columns};
use super::{Changes, Store, new_id, now, query_ids, query_optional};
use crate::doc::Document;
use crate::error::{CoreError, Result};
use crate::model::{NewNote, Note, NoteKind, StoredDocument};
use rusqlite::{Connection, Row, params};

const NOTE_COLUMNS: &str =
    "id, kind, parent_id, title, icon, position, favorited, archived, trashed, created_at, updated_at";

/// A note and everything under it, the note itself first.
const SUBTREE: &str = "WITH RECURSIVE subtree(id) AS (
        SELECT id FROM notes WHERE id = ?1
        UNION
        SELECT child.id FROM notes child JOIN subtree ON child.parent_id = subtree.id
    )";

/// Siblings in manual order. Title and key break ties left by older data.
const SIBLING_ORDER: &str = "ORDER BY position, title COLLATE NOCASE, key";

pub(crate) fn note_from_row(row: &Row<'_>) -> rusqlite::Result<Note> {
    Ok(Note {
        id: row.get(0)?,
        kind: NoteKind::from_stored(&row.get::<_, String>(1)?),
        parent_id: row.get(2)?,
        title: row.get(3)?,
        icon: row.get(4)?,
        position: row.get(5)?,
        favorited: row.get(6)?,
        archived: row.get(7)?,
        trashed: row.get(8)?,
        created_at: row.get(9)?,
        updated_at: row.get(10)?,
    })
}

pub(crate) fn find_note(connection: &Connection, id: &str) -> Result<Option<Note>> {
    let sql = format!("SELECT {NOTE_COLUMNS} FROM notes WHERE id = ?1");
    query_optional(connection, &sql, [id], note_from_row)
}

pub(crate) fn require_note(connection: &Connection, id: &str) -> Result<Note> {
    find_note(connection, id)?.ok_or_else(|| CoreError::not_found("note", id))
}

/// The position after the last child of `parent_id`.
fn next_position(connection: &Connection, parent_id: Option<&str>) -> Result<i64> {
    let sql = "SELECT COALESCE(MAX(position) + 1, 0) FROM notes WHERE parent_id IS ?1";
    Ok(connection.query_row(sql, [parent_id], |row| row.get(0))?)
}

/// The default home for content with no parent given: the first notebook in
/// manual order, preferring one that is not in the trash.
fn first_notebook(connection: &Connection) -> Result<Option<String>> {
    let sql = "SELECT id FROM notes WHERE kind = 'notebook' ORDER BY trashed, position, key LIMIT 1";
    query_optional(connection, sql, [], |row| row.get(0))
}

fn subtree_ids(connection: &Connection, id: &str) -> Result<Vec<String>> {
    query_ids(connection, &format!("{SUBTREE} SELECT id FROM subtree"), [id])
}

struct NoteRow<'a> {
    id: &'a str,
    kind: NoteKind,
    parent_id: Option<&'a str>,
    title: &'a str,
    icon: Option<&'a str>,
    position: i64,
    now: &'a str,
}

fn insert_note(connection: &Connection, row: NoteRow<'_>) -> Result<()> {
    connection.execute(
        "INSERT INTO notes (id, kind, parent_id, title, icon, position, created_at, updated_at)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?7)",
        params![row.id, row.kind.as_str(), row.parent_id, row.title, row.icon, row.position, row.now],
    )?;
    Ok(())
}

/// Content must live in a notebook, so there is always at least one.
pub(crate) fn ensure_notebook(connection: &Connection, changes: &mut Changes) -> Result<()> {
    if first_notebook(connection)?.is_some() {
        return Ok(());
    }
    let id = new_id();
    let position = next_position(connection, None)?;
    insert_note(
        connection,
        NoteRow {
            id: &id,
            kind: NoteKind::Notebook,
            parent_id: None,
            title: "Notebook",
            icon: None,
            position,
            now: &now(),
        },
    )?;
    changes.note(&id);
    Ok(())
}

impl Store {
    /// Store a document for a note: move its embedded images into the image
    /// store, record which images it uses, and index its text for search.
    ///
    /// Returns what is now stored, and whether that differs from what was
    /// there. Content equal to the stored content is not written again, so
    /// an editor that sends a document back unchanged leaves no trace.
    pub(crate) fn write_document(
        &self,
        connection: &Connection,
        note_id: &str,
        mut document: Document,
    ) -> Result<(StoredDocument, bool)> {
        self.externalize_images(connection, &mut document.root)?;
        let content = document.to_json();

        let stored: Option<(String, i64)> = query_optional(
            connection,
            "SELECT content, revision FROM documents WHERE note_id = ?1",
            [note_id],
            |row| Ok((row.get(0)?, row.get(1)?)),
        )?;
        // A note with nothing stored yet reads as an empty document.
        let (current, revision) = stored.unwrap_or_else(|| (Document::empty().to_json(), 0));
        if content == current {
            return Ok((StoredDocument { note_id: note_id.to_string(), content, revision }, false));
        }

        let mut images = Vec::new();
        referenced_images(&document.root, &mut images);
        connection.execute("DELETE FROM document_images WHERE note_id = ?1", [note_id])?;
        for hash in &images {
            connection.execute("INSERT INTO document_images (note_id, hash) VALUES (?1, ?2)", params![note_id, hash])?;
        }
        let revision = revision + 1;
        connection.execute(
            "INSERT INTO documents (note_id, content, revision) VALUES (?1, ?2, ?3)
             ON CONFLICT(note_id) DO UPDATE SET content = excluded.content, revision = excluded.revision",
            params![note_id, content, revision],
        )?;
        connection.execute(
            "UPDATE notes_fts SET body = ?2 WHERE rowid = (SELECT key FROM notes WHERE id = ?1)",
            params![note_id, document.plain_text()],
        )?;
        Ok((StoredDocument { note_id: note_id.to_string(), content, revision }, true))
    }
}

/// Set one column on one note and stamp it as changed.
fn update_note(store: &Store, id: &str, assignment: &str, value: &dyn rusqlite::ToSql) -> Result<Note> {
    store.write(|tx, changes| {
        require_note(tx, id)?;
        let sql = format!("UPDATE notes SET {assignment} = ?2, updated_at = ?3 WHERE id = ?1");
        tx.execute(&sql, params![id, value, now()])?;
        changes.note(id);
        require_note(tx, id)
    })
}

#[uniffi::export]
impl Store {
    /// Every note, most recently changed first, without content.
    pub fn list_notes(&self) -> Result<Vec<Note>> {
        self.read(|connection| {
            let sql = format!("SELECT {NOTE_COLUMNS} FROM notes ORDER BY updated_at DESC, key DESC");
            let mut statement = connection.prepare(&sql)?;
            let notes = statement.query_map([], note_from_row)?.collect::<rusqlite::Result<Vec<Note>>>()?;
            Ok(notes)
        })
    }

    pub fn get_note(&self, id: &str) -> Result<Option<Note>> {
        self.read(|connection| find_note(connection, id))
    }

    /// Create a note at the end of its parent's children. A todo list gets
    /// its default columns in the same write.
    pub fn create_note(&self, new: NewNote) -> Result<Note> {
        self.write(|tx, changes| {
            let kind = new.kind.unwrap_or(NoteKind::Page);
            let title = new.title.as_deref().map(str::trim).filter(|title| !title.is_empty());
            let parent_id = match (kind, new.parent_id) {
                (NoteKind::Notebook, _) => None,
                (_, Some(parent_id)) => Some(require_note(tx, &parent_id)?.id),
                (_, None) => first_notebook(tx)?,
            };
            let document = match (&new.content, kind) {
                (Some(content), _) => Some(Document::from_json(content)?),
                (None, NoteKind::Page) => Some(Document::empty()),
                (None, _) => None,
            };

            let id = new_id();
            insert_note(
                tx,
                NoteRow {
                    id: &id,
                    kind,
                    parent_id: parent_id.as_deref(),
                    title: title.unwrap_or(kind.default_title()),
                    icon: new.icon.as_deref(),
                    position: next_position(tx, parent_id.as_deref())?,
                    now: &now(),
                },
            )?;
            if let Some(document) = document {
                self.write_document(tx, &id, document)?;
            }
            if kind == NoteKind::TodoList {
                seed_default_columns(tx, &id)?;
            }
            changes.note(&id);
            require_note(tx, &id)
        })
    }

    /// Rename a note. A blank title keeps the current one.
    pub fn rename_note(&self, id: &str, title: &str) -> Result<Note> {
        let title = title.trim();
        if title.is_empty() {
            return self.read(|connection| require_note(connection, id));
        }
        update_note(self, id, "title", &title)
    }

    pub fn set_note_favorited(&self, id: &str, favorited: bool) -> Result<Note> {
        update_note(self, id, "favorited", &favorited)
    }

    pub fn set_note_archived(&self, id: &str, archived: bool) -> Result<Note> {
        update_note(self, id, "archived", &archived)
    }

    /// Move a note and everything under it to the trash.
    pub fn trash_note(&self, id: &str) -> Result<Note> {
        self.write(|tx, changes| {
            require_note(tx, id)?;
            changes.notes(&subtree_ids(tx, id)?);
            let sql = format!(
                "{SUBTREE} UPDATE notes SET trashed = 1, archived = 0, updated_at = ?2
                 WHERE id IN (SELECT id FROM subtree)"
            );
            tx.execute(&sql, params![id, now()])?;
            require_note(tx, id)
        })
    }

    /// Bring a note and everything under it back. If its parent is still in
    /// the trash, it moves to the nearest ancestor that is not.
    pub fn restore_note(&self, id: &str) -> Result<Note> {
        self.write(|tx, changes| {
            let note = require_note(tx, id)?;
            changes.notes(&subtree_ids(tx, id)?);
            let sql = format!(
                "{SUBTREE} UPDATE notes SET trashed = 0, archived = 0, updated_at = ?2
                 WHERE id IN (SELECT id FROM subtree)"
            );
            tx.execute(&sql, params![id, now()])?;

            // Restored under a parent that is still trashed, it would come
            // back invisible.
            let mut home = note.parent_id.clone();
            while let Some(ancestor_id) = &home {
                let ancestor = require_note(tx, ancestor_id)?;
                if !ancestor.trashed {
                    break;
                }
                home = ancestor.parent_id;
            }
            if home.is_none() && note.kind != NoteKind::Notebook {
                let sql = "SELECT id FROM notes WHERE kind = 'notebook' AND trashed = 0
                           ORDER BY position, key LIMIT 1";
                home = query_optional(tx, sql, [], |row| row.get(0))?;
            }
            if let Some(home) = home.filter(|home| Some(home) != note.parent_id.as_ref()) {
                tx.execute(
                    "UPDATE notes SET parent_id = ?2, position = ?3 WHERE id = ?1",
                    params![id, home, next_position(tx, Some(&home))?],
                )?;
            }
            require_note(tx, id)
        })
    }

    /// Delete a note for good and return every id that went with it. A
    /// notebook hands its contents to another notebook first, and the last
    /// notebook cannot be deleted.
    pub fn delete_note(&self, id: &str) -> Result<Vec<String>> {
        self.write(|tx, changes| {
            let note = require_note(tx, id)?;
            let deleted = if note.kind == NoteKind::Notebook {
                let sql = "SELECT id FROM notes WHERE kind = 'notebook' AND id <> ?1
                           ORDER BY position, key LIMIT 1";
                let heir: String =
                    query_optional(tx, sql, [id], |row| row.get(0))?.ok_or(CoreError::LastNotebook)?;

                // The adopted notes go after the heir's own, in their old
                // order, each with a position of its own.
                let first_free = next_position(tx, Some(&heir))?;
                let adopted =
                    query_ids(tx, &format!("SELECT id FROM notes WHERE parent_id = ?1 {SIBLING_ORDER}"), [id])?;
                let now = now();
                for (offset, child) in adopted.iter().enumerate() {
                    tx.execute(
                        "UPDATE notes SET parent_id = ?2, position = ?3, updated_at = ?4 WHERE id = ?1",
                        params![child, heir, first_free + offset as i64, now],
                    )?;
                }
                changes.notes(&adopted);
                vec![note.id]
            } else {
                subtree_ids(tx, id)?
            };
            // Foreign keys remove the subtree, its documents, and its board.
            tx.execute("DELETE FROM notes WHERE id = ?1", [id])?;
            changes.deleted(&deleted);
            Ok(deleted)
        })
    }

    /// Delete everything in the trash and return the ids that were removed.
    pub fn empty_trash(&self) -> Result<Vec<String>> {
        self.write(|tx, changes| {
            // Anything under a trashed note goes with it, whatever its own flag.
            let deleted = query_ids(
                tx,
                "WITH RECURSIVE doomed(id) AS (
                     SELECT id FROM notes WHERE trashed = 1
                     UNION
                     SELECT child.id FROM notes child JOIN doomed ON child.parent_id = doomed.id
                 )
                 SELECT id FROM doomed",
                [],
            )?;
            tx.execute("DELETE FROM notes WHERE trashed = 1", [])?;
            ensure_notebook(tx, changes)?;
            changes.deleted(&deleted);
            Ok(deleted)
        })
    }

    /// Copy a note beside the original. A todo list's columns and tasks are
    /// copied with it. Comments and child notes are not.
    pub fn duplicate_note(&self, id: &str) -> Result<Note> {
        self.write(|tx, changes| {
            let note = require_note(tx, id)?;
            let copy_id = new_id();
            let title = match note.title.as_str() {
                "" => "Untitled".to_string(),
                title => format!("{title} (copy)"),
            };
            insert_note(
                tx,
                NoteRow {
                    id: &copy_id,
                    kind: note.kind,
                    parent_id: note.parent_id.as_deref(),
                    title: &title,
                    icon: note.icon.as_deref(),
                    position: next_position(tx, note.parent_id.as_deref())?,
                    now: &now(),
                },
            )?;
            tx.execute(
                "INSERT INTO documents (note_id, content, revision)
                 SELECT ?2, content, 0 FROM documents WHERE note_id = ?1",
                params![id, copy_id],
            )?;
            tx.execute(
                "UPDATE notes_fts SET body = (
                     SELECT body FROM notes_fts WHERE rowid = (SELECT key FROM notes WHERE id = ?1)
                 )
                 WHERE rowid = (SELECT key FROM notes WHERE id = ?2)",
                params![id, copy_id],
            )?;
            tx.execute(
                "INSERT INTO document_images (note_id, hash)
                 SELECT ?2, hash FROM document_images WHERE note_id = ?1",
                params![id, copy_id],
            )?;
            if note.kind == NoteKind::TodoList {
                copy_board(tx, id, &copy_id)?;
            }
            changes.note(&copy_id);
            require_note(tx, &copy_id)
        })
    }

    /// A note's content. `None` if the note does not exist. A note that has
    /// never been written to reads as an empty document at revision 0.
    pub fn get_document(&self, note_id: &str) -> Result<Option<StoredDocument>> {
        self.read(|connection| {
            if find_note(connection, note_id)?.is_none() {
                return Ok(None);
            }
            let stored = query_optional(
                connection,
                "SELECT content, revision FROM documents WHERE note_id = ?1",
                [note_id],
                |row| Ok((row.get(0)?, row.get(1)?)),
            )?;
            let (content, revision) = stored.unwrap_or_else(|| (Document::empty().to_json(), 0));
            Ok(Some(StoredDocument { note_id: note_id.to_string(), content, revision }))
        })
    }
}

// These take borrowed optional strings, which cannot cross to Swift as they
// are. `ffi.rs` exports each under the same name with owned arguments.
impl Store {
    pub fn set_note_icon(&self, id: &str, icon: Option<&str>) -> Result<Note> {
        update_note(self, id, "icon", &icon)
    }

    /// Put a note under `parent_id`, before the sibling `before_id` or at the
    /// end. Moving a note into itself or its own subtree changes nothing.
    pub fn move_note(&self, id: &str, parent_id: Option<&str>, before_id: Option<&str>) -> Result<Note> {
        self.write(|tx, changes| {
            let note = require_note(tx, id)?;
            let parent_id = match (note.kind, parent_id) {
                (NoteKind::Notebook, _) => None,
                (_, Some(parent_id)) => Some(require_note(tx, parent_id)?.id),
                (_, None) => first_notebook(tx)?,
            };
            // A note moved into its own subtree would drop out of the tree.
            if let Some(parent_id) = &parent_id
                && subtree_ids(tx, id)?.contains(parent_id)
            {
                return Ok(note);
            }

            let mut order = query_ids(
                tx,
                &format!("SELECT id FROM notes WHERE parent_id IS ?1 AND id <> ?2 {SIBLING_ORDER}"),
                params![parent_id, id],
            )?;
            let index = before_id
                .and_then(|before| order.iter().position(|sibling| sibling == before))
                .unwrap_or(order.len());
            order.insert(index, note.id.clone());

            tx.execute(
                "UPDATE notes SET parent_id = ?2, updated_at = ?3 WHERE id = ?1",
                params![id, parent_id, now()],
            )?;
            changes.note(id);
            for (position, sibling) in order.iter().enumerate() {
                let moved = tx.execute(
                    "UPDATE notes SET position = ?2 WHERE id = ?1 AND position <> ?2",
                    params![sibling, position as i64],
                )?;
                if moved > 0 {
                    changes.note(sibling);
                }
            }
            require_note(tx, id)
        })
    }

    /// Save a note's content. Returns once the write is committed. Saving
    /// content equal to what is stored changes nothing and announces nothing.
    pub fn save_document(&self, note_id: &str, content: &str, origin: Option<&str>) -> Result<StoredDocument> {
        self.write(|tx, changes| {
            require_note(tx, note_id)?;
            let document = Document::from_json(content)?;
            let (stored, changed) = self.write_document(tx, note_id, document)?;
            if changed {
                tx.execute("UPDATE notes SET updated_at = ?2 WHERE id = ?1", params![note_id, now()])?;
                changes.document(note_id, stored.revision, origin);
                changes.note(note_id);
            }
            Ok(stored)
        })
    }
}
