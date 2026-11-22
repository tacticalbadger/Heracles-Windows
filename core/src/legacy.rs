//! The one-time import of the Electron app's library.
//!
//! The old database had no foreign keys, so it can hold things the new
//! schema refuses: a note whose parent is gone, a card in no column. The
//! import repairs each of those so that nothing the user wrote is dropped
//! from view, and reports every repair it made.

use crate::doc::html::{HtmlOptions, html_to_doc};
use crate::error::{CoreError, Result};
use crate::model::{ImportReport, NoteKind};
use crate::store::{Changes, Store, new_id, now, query_ids, query_optional, string_list};
use rusqlite::backup::Backup;
use rusqlite::{Connection, OpenFlags, Row, params};
use std::collections::{HashMap, HashSet, VecDeque};
use std::path::{Path, PathBuf};
use std::time::Duration;

/// The setting that records a completed import, so it never runs twice.
const IMPORT_MARKER: &str = "legacy_import";

const BACKUPS_DIR: &str = "backups";

/// The column a board gets when it has tasks to show and no columns.
const RESCUE_COLUMN: &str = "Up Next";

struct OldNote {
    id: String,
    title: String,
    content: String,
    icon: Option<String>,
    kind: String,
    parent_id: Option<String>,
    position: i64,
    favorited: bool,
    archived: bool,
    trashed: bool,
    created_at: String,
    updated_at: String,
}

struct OldColumn {
    id: String,
    todo_list_id: String,
    name: String,
    position: i64,
    created_at: String,
    updated_at: String,
}

struct OldTask {
    id: String,
    todo_list_id: String,
    parent_task_id: Option<String>,
    column_id: Option<String>,
    title: String,
    description: String,
    completed: bool,
    position: i64,
    due_date: Option<String>,
    deadline: Option<String>,
    priority: i64,
    labels: String,
    reminders: String,
    location: Option<String>,
    created_at: String,
    updated_at: String,
}

struct OldComment {
    id: String,
    task_id: String,
    body: String,
    created_at: String,
    updated_at: String,
}

struct OldLibrary {
    notes: Vec<OldNote>,
    columns: Vec<OldColumn>,
    tasks: Vec<OldTask>,
    comments: Vec<OldComment>,
}

/// The names of a table's columns. Empty if the table does not exist.
fn table_columns(connection: &Connection, table: &str) -> Result<HashSet<String>> {
    let mut statement = connection.prepare(&format!("PRAGMA table_info({table})"))?;
    let names = statement.query_map([], |row| row.get(1))?.collect::<rusqlite::Result<_>>()?;
    Ok(names)
}

fn read_rows<T>(connection: &Connection, sql: &str, map: impl Fn(&Row<'_>) -> rusqlite::Result<T>) -> Result<Vec<T>> {
    let mut statement = connection.prepare(sql)?;
    let rows = statement.query_map([], map)?.collect::<rusqlite::Result<_>>()?;
    Ok(rows)
}

/// An old timestamp, or the present if the old row had none.
fn stamp(row: &Row<'_>, index: usize) -> rusqlite::Result<String> {
    let value: Option<String> = row.get(index)?;
    Ok(value.filter(|value| !value.is_empty()).unwrap_or_else(now))
}

/// Read everything out of the old database. Every value is cast in SQL, so
/// a column holding an unexpected type cannot stop the import.
fn read_library(connection: &Connection) -> Result<OldLibrary> {
    let note_columns = table_columns(connection, "notes")?;
    if !["id", "title", "content"].iter().all(|column| note_columns.contains(*column)) {
        return Err(CoreError::invalid("that file is not a Heracles library: it has no notes table"));
    }
    // Columns the Electron app added over time. A library last opened by an
    // early version lacks some of them.
    let or = |column: &str, fallback: &str| {
        if note_columns.contains(column) { column.to_string() } else { fallback.to_string() }
    };
    let notes = read_rows(
        connection,
        &format!(
            "SELECT CAST(id AS TEXT), COALESCE(CAST(title AS TEXT), ''), COALESCE(CAST(content AS TEXT), ''),
                    CAST({icon} AS TEXT), COALESCE(CAST({kind} AS TEXT), 'page'), CAST({parent} AS TEXT),
                    COALESCE(CAST({position} AS INTEGER), 0), COALESCE({favorited}, 0) <> 0,
                    COALESCE({archived}, 0) <> 0, COALESCE({trashed}, 0) <> 0,
                    CAST({created} AS TEXT), CAST({updated} AS TEXT)
             FROM notes WHERE id IS NOT NULL ORDER BY rowid",
            icon = or("icon", "NULL"),
            kind = or("kind", "'page'"),
            parent = or("parent_id", "NULL"),
            position = or("position", "0"),
            favorited = or("favorited", "0"),
            archived = or("archived", "0"),
            trashed = or("trashed", "0"),
            created = or("created_at", "NULL"),
            updated = or("updated_at", "NULL"),
        ),
        |row| {
            Ok(OldNote {
                id: row.get(0)?,
                title: row.get(1)?,
                content: row.get(2)?,
                icon: row.get(3)?,
                kind: row.get(4)?,
                parent_id: row.get(5)?,
                position: row.get(6)?,
                favorited: row.get(7)?,
                archived: row.get(8)?,
                trashed: row.get(9)?,
                created_at: stamp(row, 10)?,
                updated_at: stamp(row, 11)?,
            })
        },
    )?;

    // The board tables were created together, so each is whole or absent.
    let has_table = |table: &str| table_columns(connection, table).map(|columns| !columns.is_empty());
    let columns = if has_table("task_columns")? {
        read_rows(
            connection,
            "SELECT CAST(id AS TEXT), CAST(todo_list_id AS TEXT), COALESCE(CAST(name AS TEXT), ''),
                    COALESCE(CAST(position AS INTEGER), 0), CAST(created_at AS TEXT), CAST(updated_at AS TEXT)
             FROM task_columns WHERE id IS NOT NULL AND todo_list_id IS NOT NULL ORDER BY rowid",
            |row| {
                Ok(OldColumn {
                    id: row.get(0)?,
                    todo_list_id: row.get(1)?,
                    name: row.get(2)?,
                    position: row.get(3)?,
                    created_at: stamp(row, 4)?,
                    updated_at: stamp(row, 5)?,
                })
            },
        )?
    } else {
        Vec::new()
    };
    let tasks = if has_table("tasks")? {
        read_rows(
            connection,
            "SELECT CAST(id AS TEXT), CAST(todo_list_id AS TEXT), CAST(parent_task_id AS TEXT),
                    CAST(column_id AS TEXT), COALESCE(CAST(title AS TEXT), ''),
                    COALESCE(CAST(description AS TEXT), ''), COALESCE(completed, 0) <> 0,
                    COALESCE(CAST(position AS INTEGER), 0), CAST(due_date AS TEXT), CAST(deadline AS TEXT),
                    COALESCE(CAST(priority AS INTEGER), 4), COALESCE(CAST(labels AS TEXT), '[]'),
                    COALESCE(CAST(reminders AS TEXT), '[]'), CAST(location AS TEXT),
                    CAST(created_at AS TEXT), CAST(updated_at AS TEXT)
             FROM tasks WHERE id IS NOT NULL AND todo_list_id IS NOT NULL ORDER BY rowid",
            |row| {
                Ok(OldTask {
                    id: row.get(0)?,
                    todo_list_id: row.get(1)?,
                    parent_task_id: row.get(2)?,
                    column_id: row.get(3)?,
                    title: row.get(4)?,
                    description: row.get(5)?,
                    completed: row.get(6)?,
                    position: row.get(7)?,
                    due_date: row.get(8)?,
                    deadline: row.get(9)?,
                    priority: row.get(10)?,
                    labels: row.get(11)?,
                    reminders: row.get(12)?,
                    location: row.get(13)?,
                    created_at: stamp(row, 14)?,
                    updated_at: stamp(row, 15)?,
                })
            },
        )?
    } else {
        Vec::new()
    };
    let comments = if has_table("task_comments")? {
        read_rows(
            connection,
            "SELECT CAST(id AS TEXT), CAST(task_id AS TEXT), COALESCE(CAST(body AS TEXT), ''),
                    CAST(created_at AS TEXT), CAST(updated_at AS TEXT)
             FROM task_comments WHERE id IS NOT NULL AND task_id IS NOT NULL ORDER BY rowid",
            |row| {
                Ok(OldComment {
                    id: row.get(0)?,
                    task_id: row.get(1)?,
                    body: row.get(2)?,
                    created_at: stamp(row, 3)?,
                    updated_at: stamp(row, 4)?,
                })
            },
        )?
    } else {
        Vec::new()
    };
    Ok(OldLibrary { notes, columns, tasks, comments })
}

/// Follow each item's parent upward and cut any loop it finds, by clearing
/// the parent of the item where the loop closes. `parents[i]` is the index
/// of item `i`'s parent. Returns the indexes that were cut.
fn cut_loops(parents: &mut [Option<usize>]) -> Vec<usize> {
    let mut settled = vec![false; parents.len()];
    let mut cut = Vec::new();
    for start in 0..parents.len() {
        let mut path: Vec<usize> = Vec::new();
        let mut current = Some(start);
        while let Some(index) = current.filter(|index| !settled[*index]) {
            if path.contains(&index) {
                parents[index] = None;
                cut.push(index);
                break;
            }
            path.push(index);
            current = parents[index];
        }
        for index in path {
            settled[index] = true;
        }
    }
    cut
}

/// The order to insert items in so that every parent goes in before its
/// children. `parents` must be free of loops.
fn parents_first(parents: &[Option<usize>]) -> Vec<usize> {
    let mut children: HashMap<usize, Vec<usize>> = HashMap::new();
    let mut queue: VecDeque<usize> = VecDeque::new();
    for (index, parent) in parents.iter().enumerate() {
        match parent {
            Some(parent) => children.entry(*parent).or_default().push(index),
            None => queue.push_back(index),
        }
    }
    let mut order = Vec::with_capacity(parents.len());
    while let Some(index) = queue.pop_front() {
        order.push(index);
        queue.extend(children.remove(&index).unwrap_or_default());
    }
    order
}

/// Check that this library may still receive an import.
fn check_importable(connection: &Connection) -> Result<()> {
    let done: Option<String> =
        query_optional(connection, "SELECT value FROM settings WHERE key = ?1", [IMPORT_MARKER], |row| row.get(0))?;
    if done.is_some() {
        return Err(CoreError::invalid("the Electron library has already been imported"));
    }
    let content: i64 = connection.query_row(
        "SELECT (SELECT COUNT(*) FROM notes WHERE kind <> 'notebook')
              + MAX((SELECT COUNT(*) FROM notes WHERE kind = 'notebook') - 1, 0)",
        [],
        |row| row.get(0),
    )?;
    if content > 0 {
        return Err(CoreError::invalid("this library already has content, so the Electron library cannot be imported into it"));
    }
    Ok(())
}

struct Counts {
    notes: u32,
    documents: u32,
    columns: u32,
    tasks: u32,
    comments: u32,
}

#[uniffi::export]
impl Store {
    /// Bring the Electron app's `heracles.sqlite` into this library.
    ///
    /// The old database is copied to a backup first and is only ever read.
    /// The import is one write: if any part of it fails, this library is left
    /// exactly as it was. It runs once, into a library with no content yet.
    pub fn import_legacy(&self, legacy_database: &str) -> Result<ImportReport> {
        self.read(check_importable)?;
        let backup = self.back_up(Path::new(legacy_database))?;
        let imported = self.import_from(&backup);
        if imported.is_err() {
            // A backup of something that could not be imported helps no one.
            let _ = std::fs::remove_file(&backup);
        }
        imported
    }

}

impl Store {
    /// Copy the old database into this library's backup folder. SQLite's
    /// backup interface takes a consistent copy even while the Electron app
    /// has the database open.
    fn back_up(&self, source_path: &Path) -> Result<PathBuf> {
        // Read-only, so a wrong path is an error and not a new empty file.
        let source = Connection::open_with_flags(source_path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;
        if table_columns(&source, "notes")?.is_empty() {
            return Err(CoreError::invalid("that file is not a Heracles library: it has no notes table"));
        }
        let folder = self.data_dir().join(BACKUPS_DIR);
        std::fs::create_dir_all(&folder)?;
        let moment: String = now().chars().filter(char::is_ascii_alphanumeric).collect();
        let target = folder.join(format!("heracles-electron-{moment}.sqlite"));

        let copied = (|| -> Result<()> {
            let mut destination = Connection::open(&target)?;
            Backup::new(&source, &mut destination)?.run_to_completion(256, Duration::from_millis(5), None)?;
            Ok(())
        })();
        if let Err(error) = copied {
            let _ = std::fs::remove_file(&target);
            return Err(error);
        }
        Ok(target)
    }

    /// Import from the backup copy, so the copy kept is exactly what was
    /// imported.
    fn import_from(&self, backup: &Path) -> Result<ImportReport> {
        let old = read_library(&Connection::open_with_flags(backup, OpenFlags::SQLITE_OPEN_READ_ONLY)?)?;
        self.write(|tx, changes| {
            check_importable(tx)?;
            let images_before: u32 = tx.query_row("SELECT COUNT(*) FROM images", [], |row| row.get(0))?;
            let mut repairs = Vec::new();
            let mut counts = Counts { notes: 0, documents: 0, columns: 0, tasks: 0, comments: 0 };

            let todo_lists = self.import_notes(tx, &old.notes, changes, &mut repairs, &mut counts)?;
            import_boards(tx, &old, &todo_lists, &mut repairs, &mut counts)?;

            let marker = serde_json::json!({"imported_at": now(), "backup": backup.to_string_lossy()});
            tx.execute("INSERT INTO settings (key, value) VALUES (?1, ?2)", params![IMPORT_MARKER, marker.to_string()])?;
            let images_after: u32 = tx.query_row("SELECT COUNT(*) FROM images", [], |row| row.get(0))?;
            Ok(ImportReport {
                notes: counts.notes,
                documents: counts.documents,
                images: images_after - images_before,
                columns: counts.columns,
                tasks: counts.tasks,
                comments: counts.comments,
                backup_path: backup.to_string_lossy().into_owned(),
                repairs,
            })
        })
    }

    /// Import the notes and their content. Returns the ids of the todo lists.
    fn import_notes(
        &self,
        tx: &Connection,
        notes: &[OldNote],
        changes: &mut Changes,
        repairs: &mut Vec<String>,
        counts: &mut Counts,
    ) -> Result<HashSet<String>> {
        let placeholder: String =
            tx.query_row("SELECT id FROM notes WHERE kind = 'notebook' ORDER BY position, key LIMIT 1", [], |row| {
                row.get(0)
            })?;
        let index_of: HashMap<&str, usize> =
            notes.iter().enumerate().map(|(index, note)| (note.id.as_str(), index)).collect();

        let kinds: Vec<NoteKind> = notes
            .iter()
            .map(|note| {
                let kind = NoteKind::from_stored(&note.kind);
                if kind.as_str() != note.kind {
                    repairs.push(format!("note `{}` had the unknown kind `{}` and was imported as a page", note.id, note.kind));
                }
                kind
            })
            .collect();

        // Old notebooks replace the new library's placeholder. A library
        // from before notebooks existed has none, and moves into it.
        let home: Option<usize> = (0..notes.len())
            .filter(|index| kinds[*index] == NoteKind::Notebook)
            .min_by_key(|index| (notes[*index].position, *index));

        // `None` is the top level for a notebook, and "in the home notebook"
        // for anything else.
        let mut parents: Vec<Option<usize>> = Vec::with_capacity(notes.len());
        for (index, note) in notes.iter().enumerate() {
            let named = note.parent_id.as_deref();
            let parent = named.and_then(|parent| index_of.get(parent).copied()).filter(|parent| *parent != index);
            if kinds[index] == NoteKind::Notebook {
                if named.is_some() {
                    repairs.push(format!("notebook `{}` was inside another note and was moved to the top level", note.id));
                }
                parents.push(None);
            } else {
                if parent.is_none() {
                    let why = match named {
                        None => "had no parent",
                        Some(parent) if parent == note.id => "was its own parent",
                        Some(_) => "had a parent that no longer exists",
                    };
                    repairs.push(format!("note `{}` {why} and was placed in the first notebook", note.id));
                }
                parents.push(parent);
            }
        }
        for index in cut_loops(&mut parents) {
            repairs.push(format!(
                "note `{}` was part of a loop of parents and was placed in the first notebook",
                notes[index].id
            ));
        }

        if home.is_some() {
            tx.execute("DELETE FROM notes WHERE id = ?1", [&placeholder])?;
            changes.deleted([&placeholder]);
        }
        let home_id = home.map_or(placeholder.as_str(), |index| notes[index].id.as_str());

        let mut todo_lists = HashSet::new();
        for index in parents_first(&parents) {
            let (note, kind) = (&notes[index], kinds[index]);
            let parent_id = match (kind, parents[index]) {
                (NoteKind::Notebook, _) => None,
                (_, Some(parent)) => Some(notes[parent].id.as_str()),
                (_, None) => Some(home_id),
            };
            tx.execute(
                "INSERT INTO notes (id, kind, parent_id, title, icon, position, favorited, archived, trashed,
                                    created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11)",
                params![
                    note.id,
                    kind.as_str(),
                    parent_id,
                    note.title,
                    note.icon,
                    note.position,
                    note.favorited,
                    note.archived,
                    note.trashed,
                    note.created_at,
                    note.updated_at
                ],
            )?;
            if !note.content.trim().is_empty() {
                let mut document = html_to_doc(&note.content, HtmlOptions::default());
                document.ensure_block_ids();
                self.write_document(tx, &note.id, document)?;
                counts.documents += 1;
            }
            if kind == NoteKind::TodoList {
                todo_lists.insert(note.id.clone());
            }
            changes.note(&note.id);
            counts.notes += 1;
        }
        Ok(todo_lists)
    }
}

/// Import columns, tasks, and comments for the todo lists that came across.
fn import_boards(
    tx: &Connection,
    old: &OldLibrary,
    todo_lists: &HashSet<String>,
    repairs: &mut Vec<String>,
    counts: &mut Counts,
) -> Result<()> {
    let mut column_list: HashMap<&str, &str> = HashMap::new();
    for column in &old.columns {
        if !todo_lists.contains(&column.todo_list_id) {
            repairs.push(format!("column `{}` belonged to a todo list that no longer exists and was left out", column.id));
            continue;
        }
        tx.execute(
            "INSERT INTO task_columns (id, todo_list_id, name, position, created_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
            params![column.id, column.todo_list_id, column.name, column.position, column.created_at, column.updated_at],
        )?;
        column_list.insert(&column.id, &column.todo_list_id);
        counts.columns += 1;
    }

    let kept: Vec<&OldTask> = old
        .tasks
        .iter()
        .filter(|task| {
            let has_list = todo_lists.contains(&task.todo_list_id);
            if !has_list {
                repairs.push(format!("task `{}` belonged to a todo list that no longer exists and was left out", task.id));
            }
            has_list
        })
        .collect();
    let index_of: HashMap<&str, usize> = kept.iter().enumerate().map(|(index, task)| (task.id.as_str(), index)).collect();

    let mut parents: Vec<Option<usize>> = Vec::with_capacity(kept.len());
    for (index, task) in kept.iter().enumerate() {
        let named = task.parent_task_id.as_deref();
        let parent = named
            .and_then(|parent| index_of.get(parent).copied())
            .filter(|parent| *parent != index && kept[*parent].todo_list_id == task.todo_list_id);
        if named.is_some() && parent.is_none() {
            repairs.push(format!("task `{}` was a subtask of a task that no longer exists and became a card", task.id));
        }
        parents.push(parent);
    }
    for index in cut_loops(&mut parents) {
        repairs.push(format!("task `{}` was part of a loop of subtasks and became a card", kept[index].id));
    }

    // The column a card falls back to: its board's first, made if need be.
    let mut first_columns: HashMap<String, String> = HashMap::new();
    let mut first_column = |list: &str, counts: &mut Counts| -> Result<String> {
        if let Some(column) = first_columns.get(list) {
            return Ok(column.clone());
        }
        let existing = query_ids(
            tx,
            "SELECT id FROM task_columns WHERE todo_list_id = ?1 ORDER BY position, created_at LIMIT 1",
            [list],
        )?;
        let column = match existing.into_iter().next() {
            Some(column) => column,
            None => {
                let column = new_id();
                tx.execute(
                    "INSERT INTO task_columns (id, todo_list_id, name, position, created_at, updated_at)
                     VALUES (?1, ?2, ?3, 0, ?4, ?4)",
                    params![column, list, RESCUE_COLUMN, now()],
                )?;
                counts.columns += 1;
                column
            }
        };
        first_columns.insert(list.to_string(), column.clone());
        Ok(column)
    };

    for index in parents_first(&parents) {
        let task = kept[index];
        let parent_id = parents[index].map(|parent| kept[parent].id.as_str());
        let column_id = match (parent_id, task.column_id.as_deref()) {
            // A subtask has no column of its own.
            (Some(_), _) => None,
            (None, Some(column)) if column_list.get(column) == Some(&task.todo_list_id.as_str()) => {
                Some(column.to_string())
            }
            (None, _) => {
                let column = first_column(&task.todo_list_id, counts)?;
                repairs.push(format!(
                    "task `{}` was in no column its board could show and was placed in the board's first column",
                    task.id
                ));
                Some(column)
            }
        };
        let priority = if (1..=4).contains(&task.priority) { task.priority } else { 4 };
        let list_json = |json: &str| serde_json::to_string(&string_list(json)).expect("a list of strings serializes");
        tx.execute(
            "INSERT INTO tasks (id, todo_list_id, parent_task_id, column_id, title, description, completed, position,
                                due_date, deadline, priority, labels, reminders, location, created_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16)",
            params![
                task.id,
                task.todo_list_id,
                parent_id,
                column_id,
                task.title,
                task.description,
                task.completed,
                task.position,
                task.due_date,
                task.deadline,
                priority,
                list_json(&task.labels),
                list_json(&task.reminders),
                task.location,
                task.created_at,
                task.updated_at
            ],
        )?;
        counts.tasks += 1;
    }

    for comment in &old.comments {
        if !index_of.contains_key(comment.task_id.as_str()) {
            repairs.push(format!("comment `{}` was on a task that no longer exists and was left out", comment.id));
            continue;
        }
        tx.execute(
            "INSERT INTO task_comments (id, task_id, body, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?5)",
            params![comment.id, comment.task_id, comment.body, comment.created_at, comment.updated_at],
        )?;
        counts.comments += 1;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{cut_loops, parents_first};

    #[test]
    fn a_loop_is_cut_once_where_it_closes() {
        // 0 → 1 → 2 → 0, and 3 hangs off the loop.
        let mut parents = vec![Some(1), Some(2), Some(0), Some(0)];
        assert_eq!(cut_loops(&mut parents), vec![0]);
        assert_eq!(parents, vec![None, Some(2), Some(0), Some(0)]);
    }

    #[test]
    fn a_tree_is_left_alone() {
        let mut parents = vec![None, Some(0), Some(1), Some(0)];
        assert!(cut_loops(&mut parents).is_empty());
        assert_eq!(parents, vec![None, Some(0), Some(1), Some(0)]);
    }

    #[test]
    fn parents_come_before_their_children() {
        let parents = vec![Some(2), Some(0), None, None, Some(1)];
        let order = parents_first(&parents);
        assert_eq!(order.len(), parents.len());
        let place = |index: usize| order.iter().position(|item| *item == index).unwrap();
        for (child, parent) in parents.iter().enumerate() {
            if let Some(parent) = parent {
                assert!(place(*parent) < place(child));
            }
        }
    }
}
