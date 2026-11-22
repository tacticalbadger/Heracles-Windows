//! Todo lists: board columns, tasks with nested subtasks, and comments.

use super::notes::require_note;
use super::{Store, new_id, now, query_ids, query_optional};
use crate::error::{CoreError, Result};
use crate::model::{NewTask, Note, NoteKind, Reminder, Task, TaskColumn, TaskComment, TaskPatch};
use rusqlite::types::Value as SqlValue;
use rusqlite::{Connection, Row, params, params_from_iter};
use std::collections::{HashMap, HashSet};

const DEFAULT_COLUMNS: [&str; 3] = ["Up Next", "Active", "Done"];

/// The column a board falls back to when its last column is deleted.
const FALLBACK_COLUMN: &str = "Up Next";

const COLUMN_COLUMNS: &str = "id, todo_list_id, name, position, created_at, updated_at";

const TASK_COLUMNS: &str = "id, todo_list_id, parent_task_id, column_id, title, description, completed, \
     position, due_date, deadline, priority, labels, reminders, location, created_at, updated_at";

const NEEDS_A_HOME: &str = "a task needs a column or a parent task";

fn column_from_row(row: &Row<'_>) -> rusqlite::Result<TaskColumn> {
    Ok(TaskColumn {
        id: row.get(0)?,
        todo_list_id: row.get(1)?,
        name: row.get(2)?,
        position: row.get(3)?,
        created_at: row.get(4)?,
        updated_at: row.get(5)?,
    })
}

fn task_from_row(row: &Row<'_>) -> rusqlite::Result<Task> {
    Ok(Task {
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
        labels: string_list(&row.get::<_, String>(11)?),
        reminders: string_list(&row.get::<_, String>(12)?),
        location: row.get(13)?,
        created_at: row.get(14)?,
        updated_at: row.get(15)?,
    })
}

/// Read a stored JSON list of strings, skipping anything that is not one.
pub(crate) fn string_list(json: &str) -> Vec<String> {
    let values: Vec<serde_json::Value> = serde_json::from_str(json).unwrap_or_default();
    values.into_iter().filter_map(|value| value.as_str().map(String::from)).collect()
}

fn to_json(list: &[String]) -> String {
    serde_json::to_string(list).expect("a list of strings always serializes")
}

fn require_todo_list(connection: &Connection, id: &str) -> Result<Note> {
    let note = require_note(connection, id)?;
    if note.kind != NoteKind::TodoList {
        return Err(CoreError::invalid(format!("note `{id}` is not a todo list")));
    }
    Ok(note)
}

fn require_column(connection: &Connection, id: &str) -> Result<TaskColumn> {
    let sql = format!("SELECT {COLUMN_COLUMNS} FROM task_columns WHERE id = ?1");
    query_optional(connection, &sql, [id], column_from_row)?.ok_or_else(|| CoreError::not_found("column", id))
}

fn find_task(connection: &Connection, id: &str) -> Result<Option<Task>> {
    let sql = format!("SELECT {TASK_COLUMNS} FROM tasks WHERE id = ?1");
    query_optional(connection, &sql, [id], task_from_row)
}

fn require_task(connection: &Connection, id: &str) -> Result<Task> {
    find_task(connection, id)?.ok_or_else(|| CoreError::not_found("task", id))
}

/// A comment and the todo list its task belongs to.
fn require_comment(connection: &Connection, id: &str) -> Result<(TaskComment, String)> {
    let sql = "SELECT c.id, c.task_id, c.body, c.created_at, c.updated_at, t.todo_list_id
               FROM task_comments c JOIN tasks t ON t.id = c.task_id WHERE c.id = ?1";
    let found = query_optional(connection, sql, [id], |row| {
        let comment = TaskComment {
            id: row.get(0)?,
            task_id: row.get(1)?,
            body: row.get(2)?,
            created_at: row.get(3)?,
            updated_at: row.get(4)?,
        };
        Ok((comment, row.get(5)?))
    })?;
    found.ok_or_else(|| CoreError::not_found("comment", id))
}

fn check_priority(priority: u8) -> Result<u8> {
    if (1..=4).contains(&priority) {
        Ok(priority)
    } else {
        Err(CoreError::invalid(format!("priority must be between 1 and 4, not {priority}")))
    }
}

fn same_board(what: &str, theirs: &str, ours: &str) -> Result<()> {
    if theirs == ours {
        Ok(())
    } else {
        Err(CoreError::invalid(format!("the {what} belongs to another todo list")))
    }
}

fn list_columns(connection: &Connection, todo_list_id: &str) -> Result<Vec<TaskColumn>> {
    let sql =
        format!("SELECT {COLUMN_COLUMNS} FROM task_columns WHERE todo_list_id = ?1 ORDER BY position, created_at");
    let mut statement = connection.prepare(&sql)?;
    let columns = statement.query_map([todo_list_id], column_from_row)?.collect::<rusqlite::Result<_>>()?;
    Ok(columns)
}

/// Add a column at the right-hand end and return its id.
fn insert_column(connection: &Connection, todo_list_id: &str, name: &str) -> Result<String> {
    let id = new_id();
    connection.execute(
        "INSERT INTO task_columns (id, todo_list_id, name, position, created_at, updated_at)
         VALUES (?1, ?2, ?3,
                 (SELECT COALESCE(MAX(position) + 1, 0) FROM task_columns WHERE todo_list_id = ?2),
                 ?4, ?4)",
        params![id, todo_list_id, name, now()],
    )?;
    Ok(id)
}

/// Give a new todo list its starting columns.
pub(crate) fn seed_default_columns(connection: &Connection, todo_list_id: &str) -> Result<()> {
    for name in DEFAULT_COLUMNS {
        insert_column(connection, todo_list_id, name)?;
    }
    Ok(())
}

/// Copy one todo list's columns and tasks onto another, which must be empty.
/// Comments stay behind: they read as a dated conversation about the
/// original.
pub(crate) fn copy_board(connection: &Connection, from: &str, to: &str) -> Result<()> {
    let now = now();

    let mut new_columns: HashMap<String, String> = HashMap::new();
    for column in query_ids(connection, "SELECT id FROM task_columns WHERE todo_list_id = ?1", [from])? {
        let copy = new_id();
        connection.execute(
            "INSERT INTO task_columns (id, todo_list_id, name, position, created_at, updated_at)
             SELECT ?2, ?3, name, position, ?4, ?4 FROM task_columns WHERE id = ?1",
            params![column, copy, to, now],
        )?;
        new_columns.insert(column, copy);
    }

    type Link = (String, Option<String>, Option<String>);
    let mut statement =
        connection.prepare("SELECT id, parent_task_id, column_id FROM tasks WHERE todo_list_id = ?1")?;
    let mut waiting: Vec<Link> = statement
        .query_map([from], |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)))?
        .collect::<rusqlite::Result<_>>()?;
    let new_tasks: HashMap<String, String> = waiting.iter().map(|(id, _, _)| (id.clone(), new_id())).collect();

    // A subtask's row refers to its parent's, so parents are written first.
    let mut written: HashSet<String> = HashSet::new();
    while !waiting.is_empty() {
        let (ready, rest): (Vec<Link>, Vec<Link>) = waiting
            .into_iter()
            .partition(|(_, parent, _)| parent.as_ref().is_none_or(|parent| written.contains(parent)));
        if ready.is_empty() {
            break;
        }
        for (id, parent, column) in ready {
            connection.execute(
                "INSERT INTO tasks (id, todo_list_id, parent_task_id, column_id, title, description, completed,
                                    position, due_date, deadline, priority, labels, reminders, location,
                                    created_at, updated_at)
                 SELECT ?2, ?3, ?4, ?5, title, description, completed, position, due_date, deadline, priority,
                        labels, reminders, location, ?6, ?6
                 FROM tasks WHERE id = ?1",
                params![
                    id,
                    new_tasks[&id],
                    to,
                    parent.as_ref().map(|parent| &new_tasks[parent]),
                    column.as_ref().map(|column| &new_columns[column]),
                    now
                ],
            )?;
            written.insert(id);
        }
        waiting = rest;
    }
    Ok(())
}

/// Number a group of siblings 0, 1, 2, … in the given order.
fn renumber(connection: &Connection, table: &str, order: &[String]) -> Result<()> {
    let sql = format!("UPDATE {table} SET position = ?2 WHERE id = ?1");
    let mut statement = connection.prepare(&sql)?;
    for (position, id) in order.iter().enumerate() {
        statement.execute(params![id, position as i64])?;
    }
    Ok(())
}

#[uniffi::export]
impl Store {
    /// A todo list's columns, left to right.
    pub fn list_columns(&self, todo_list_id: &str) -> Result<Vec<TaskColumn>> {
        self.read(|connection| list_columns(connection, todo_list_id))
    }

    /// Add a column at the right-hand end. A blank name becomes "Untitled".
    pub fn create_column(&self, todo_list_id: &str, name: &str) -> Result<TaskColumn> {
        self.write(|tx, changes| {
            require_todo_list(tx, todo_list_id)?;
            let name = Some(name.trim()).filter(|name| !name.is_empty()).unwrap_or("Untitled");
            let id = insert_column(tx, todo_list_id, name)?;
            changes.board(todo_list_id);
            require_column(tx, &id)
        })
    }

    /// Rename a column. A blank name keeps the current one.
    pub fn rename_column(&self, id: &str, name: &str) -> Result<TaskColumn> {
        self.write(|tx, changes| {
            let column = require_column(tx, id)?;
            let name = name.trim();
            if name.is_empty() {
                return Ok(column);
            }
            tx.execute("UPDATE task_columns SET name = ?2, updated_at = ?3 WHERE id = ?1", params![id, name, now()])?;
            changes.board(&column.todo_list_id);
            require_column(tx, id)
        })
    }

    /// Delete a column and return the id of the column that took its cards.
    /// The last column of a board is replaced by a new "Up Next" column.
    pub fn delete_column(&self, id: &str) -> Result<String> {
        self.write(|tx, changes| {
            let column = require_column(tx, id)?;
            let list = &column.todo_list_id;
            let other = "SELECT id FROM task_columns WHERE todo_list_id = ?1 AND id <> ?2
                         ORDER BY position, created_at LIMIT 1";
            let fallback = match query_optional(tx, other, params![list, id], |row| row.get(0))? {
                Some(fallback) => fallback,
                None => insert_column(tx, list, FALLBACK_COLUMN)?,
            };

            // The cards go after the fallback column's own, in their order.
            let first_free: i64 = tx.query_row(
                "SELECT COALESCE(MAX(position) + 1, 0) FROM tasks WHERE column_id = ?1",
                [&fallback],
                |row| row.get(0),
            )?;
            let cards = query_ids(tx, "SELECT id FROM tasks WHERE column_id = ?1 ORDER BY position, created_at", [id])?;
            let now = now();
            for (offset, card) in cards.iter().enumerate() {
                tx.execute(
                    "UPDATE tasks SET column_id = ?2, position = ?3, updated_at = ?4 WHERE id = ?1",
                    params![card, fallback, first_free + offset as i64, now],
                )?;
            }
            tx.execute("DELETE FROM task_columns WHERE id = ?1", [id])?;

            let remaining = query_ids(
                tx,
                "SELECT id FROM task_columns WHERE todo_list_id = ?1 ORDER BY position, created_at",
                [list],
            )?;
            renumber(tx, "task_columns", &remaining)?;
            changes.board(list);
            Ok(fallback)
        })
    }

    /// Put a board's columns in the given order. Columns left out of
    /// `ordered_ids` keep their relative order after the listed ones.
    pub fn reorder_columns(&self, todo_list_id: &str, ordered_ids: &[String]) -> Result<Vec<TaskColumn>> {
        self.write(|tx, changes| {
            let current = query_ids(
                tx,
                "SELECT id FROM task_columns WHERE todo_list_id = ?1 ORDER BY position, created_at",
                [todo_list_id],
            )?;
            let mut order: Vec<String> = Vec::with_capacity(current.len());
            for id in ordered_ids.iter().chain(&current) {
                if current.contains(id) && !order.contains(id) {
                    order.push(id.clone());
                }
            }
            renumber(tx, "task_columns", &order)?;
            tx.execute(
                "UPDATE task_columns SET updated_at = ?2 WHERE todo_list_id = ?1",
                params![todo_list_id, now()],
            )?;
            if !order.is_empty() {
                changes.board(todo_list_id);
            }
            list_columns(tx, todo_list_id)
        })
    }

    /// Every task of a todo list, subtasks included, in manual order.
    pub fn list_tasks(&self, todo_list_id: &str) -> Result<Vec<Task>> {
        self.read(|connection| {
            let sql = format!("SELECT {TASK_COLUMNS} FROM tasks WHERE todo_list_id = ?1 ORDER BY position, created_at");
            let mut statement = connection.prepare(&sql)?;
            let tasks = statement.query_map([todo_list_id], task_from_row)?.collect::<rusqlite::Result<_>>()?;
            Ok(tasks)
        })
    }

    pub fn get_task(&self, id: &str) -> Result<Option<Task>> {
        self.read(|connection| find_task(connection, id))
    }

    /// Create a card at the end of a column, or a subtask at the end of its
    /// parent's subtasks.
    pub fn create_task(&self, new: NewTask) -> Result<Task> {
        self.write(|tx, changes| {
            let list = require_todo_list(tx, &new.todo_list_id)?.id;
            let priority = check_priority(new.priority.unwrap_or(4))?;
            let (parent_id, column_id) = match (new.parent_task_id, new.column_id) {
                (Some(parent_id), _) => {
                    same_board("parent task", &require_task(tx, &parent_id)?.todo_list_id, &list)?;
                    (Some(parent_id), None)
                }
                (None, Some(column_id)) => {
                    same_board("column", &require_column(tx, &column_id)?.todo_list_id, &list)?;
                    (None, Some(column_id))
                }
                (None, None) => return Err(CoreError::invalid(NEEDS_A_HOME)),
            };

            let id = new_id();
            tx.execute(
                "INSERT INTO tasks (id, todo_list_id, parent_task_id, column_id, title, description, priority,
                                    position, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7,
                         (SELECT COALESCE(MAX(position) + 1, 0) FROM tasks
                          WHERE parent_task_id IS ?3 AND column_id IS ?4),
                         ?8, ?8)",
                params![
                    id,
                    list,
                    parent_id,
                    column_id,
                    new.title.unwrap_or_default(),
                    new.description.unwrap_or_default(),
                    priority,
                    now()
                ],
            )?;
            changes.board(&list);
            require_task(tx, &id)
        })
    }

    /// Delete a task with its subtasks and comments. Returns every task id
    /// that was removed.
    pub fn delete_task(&self, id: &str) -> Result<Vec<String>> {
        self.write(|tx, changes| {
            let task = require_task(tx, id)?;
            let deleted = query_ids(
                tx,
                "WITH RECURSIVE subtree(id) AS (
                     SELECT id FROM tasks WHERE id = ?1
                     UNION
                     SELECT child.id FROM tasks child JOIN subtree ON child.parent_task_id = subtree.id
                 )
                 SELECT id FROM subtree",
                [id],
            )?;
            // Foreign keys remove the subtasks and every comment.
            tx.execute("DELETE FROM tasks WHERE id = ?1", [id])?;
            changes.board(&task.todo_list_id);
            Ok(deleted)
        })
    }

    /// A task's comments, oldest first.
    pub fn list_comments(&self, task_id: &str) -> Result<Vec<TaskComment>> {
        self.read(|connection| {
            let mut statement = connection.prepare(
                "SELECT id, task_id, body, created_at, updated_at FROM task_comments
                 WHERE task_id = ?1 ORDER BY created_at, rowid",
            )?;
            let comments = statement
                .query_map([task_id], |row| {
                    Ok(TaskComment {
                        id: row.get(0)?,
                        task_id: row.get(1)?,
                        body: row.get(2)?,
                        created_at: row.get(3)?,
                        updated_at: row.get(4)?,
                    })
                })?
                .collect::<rusqlite::Result<_>>()?;
            Ok(comments)
        })
    }

    pub fn create_comment(&self, task_id: &str, body: &str) -> Result<TaskComment> {
        self.write(|tx, changes| {
            let task = require_task(tx, task_id)?;
            let id = new_id();
            tx.execute(
                "INSERT INTO task_comments (id, task_id, body, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?4)",
                params![id, task_id, body, now()],
            )?;
            changes.board(&task.todo_list_id);
            Ok(require_comment(tx, &id)?.0)
        })
    }

    pub fn update_comment(&self, id: &str, body: &str) -> Result<TaskComment> {
        self.write(|tx, changes| {
            let (_, list) = require_comment(tx, id)?;
            tx.execute("UPDATE task_comments SET body = ?2, updated_at = ?3 WHERE id = ?1", params![id, body, now()])?;
            changes.board(&list);
            Ok(require_comment(tx, id)?.0)
        })
    }

    pub fn delete_comment(&self, id: &str) -> Result<()> {
        self.write(|tx, changes| {
            let (_, list) = require_comment(tx, id)?;
            tx.execute("DELETE FROM task_comments WHERE id = ?1", [id])?;
            changes.board(&list);
            Ok(())
        })
    }

    /// Every outstanding reminder, soonest first, for scheduling
    /// notifications.
    pub fn list_reminders(&self) -> Result<Vec<Reminder>> {
        self.read(|connection| {
            let mut statement = connection.prepare(
                "SELECT task.id, task.todo_list_id, task.title, reminder.value
                 FROM tasks task
                 JOIN notes list ON list.id = task.todo_list_id
                 JOIN json_each(task.reminders) reminder
                 WHERE task.completed = 0 AND list.trashed = 0 AND reminder.type = 'text'
                 ORDER BY reminder.value, task.created_at",
            )?;
            let reminders = statement
                .query_map([], |row| {
                    Ok(Reminder {
                        task_id: row.get(0)?,
                        todo_list_id: row.get(1)?,
                        task_title: row.get(2)?,
                        remind_at: row.get(3)?,
                    })
                })?
                .collect::<rusqlite::Result<_>>()?;
            Ok(reminders)
        })
    }
}

// Exported from `ffi.rs` with owned arguments.
impl Store {
    /// Change the fields a patch names, and leave the rest alone. Exported
    /// from `ffi.rs` as a flat record: see `TaskPatchRecord`.
    pub fn update_task(&self, id: &str, patch: TaskPatch) -> Result<Task> {
        self.write(|tx, changes| {
            let task = require_task(tx, id)?;
            let optional = |value: Option<String>| value.map_or(SqlValue::Null, SqlValue::Text);

            let mut fields: Vec<(&str, SqlValue)> = Vec::new();
            if let Some(title) = patch.title {
                fields.push(("title", SqlValue::Text(title)));
            }
            if let Some(description) = patch.description {
                fields.push(("description", SqlValue::Text(description)));
            }
            if let Some(completed) = patch.completed {
                fields.push(("completed", SqlValue::Integer(completed.into())));
            }
            if let Some(due_date) = patch.due_date {
                fields.push(("due_date", optional(due_date)));
            }
            if let Some(deadline) = patch.deadline {
                fields.push(("deadline", optional(deadline)));
            }
            if let Some(priority) = patch.priority {
                fields.push(("priority", SqlValue::Integer(check_priority(priority)?.into())));
            }
            if let Some(labels) = patch.labels {
                fields.push(("labels", SqlValue::Text(to_json(&labels))));
            }
            if let Some(reminders) = patch.reminders {
                fields.push(("reminders", SqlValue::Text(to_json(&reminders))));
            }
            if let Some(location) = patch.location {
                fields.push(("location", optional(location)));
            }
            if fields.is_empty() {
                return Ok(task);
            }
            fields.push(("updated_at", SqlValue::Text(now())));

            let assignments: Vec<String> =
                fields.iter().enumerate().map(|(index, (name, _))| format!("{name} = ?{}", index + 2)).collect();
            let sql = format!("UPDATE tasks SET {} WHERE id = ?1", assignments.join(", "));
            let values = std::iter::once(SqlValue::Text(id.to_string())).chain(fields.into_iter().map(|(_, v)| v));
            tx.execute(&sql, params_from_iter(values))?;
            changes.board(&task.todo_list_id);
            require_task(tx, id)
        })
    }

    /// Every kind of move: reorder within a column or parent, change column,
    /// promote a subtask to a card, or put a task under another. Give a
    /// parent for a subtask, otherwise a column. `before_id` is the sibling
    /// to land in front of, or `None` for the end. Moving a task under
    /// itself or one of its own subtasks changes nothing.
    pub fn move_task(
        &self,
        id: &str,
        column_id: Option<&str>,
        parent_task_id: Option<&str>,
        before_id: Option<&str>,
    ) -> Result<Task> {
        self.write(|tx, changes| {
            let task = require_task(tx, id)?;
            let list = &task.todo_list_id;
            let (parent_id, column_id) = match (parent_task_id, column_id) {
                (Some(parent_id), _) => {
                    same_board("parent task", &require_task(tx, parent_id)?.todo_list_id, list)?;
                    let own_subtree = query_ids(
                        tx,
                        "WITH RECURSIVE subtree(id) AS (
                             SELECT id FROM tasks WHERE id = ?1
                             UNION
                             SELECT child.id FROM tasks child JOIN subtree ON child.parent_task_id = subtree.id
                         )
                         SELECT id FROM subtree",
                        [id],
                    )?;
                    if own_subtree.iter().any(|member| member == parent_id) {
                        return Ok(task);
                    }
                    (Some(parent_id), None)
                }
                (None, Some(column_id)) => {
                    same_board("column", &require_column(tx, column_id)?.todo_list_id, list)?;
                    (None, Some(column_id))
                }
                (None, None) => return Err(CoreError::invalid(NEEDS_A_HOME)),
            };

            let mut order = query_ids(
                tx,
                "SELECT id FROM tasks WHERE parent_task_id IS ?1 AND column_id IS ?2 AND id <> ?3
                 ORDER BY position, created_at",
                params![parent_id, column_id, id],
            )?;
            let index = before_id
                .and_then(|before| order.iter().position(|sibling| sibling == before))
                .unwrap_or(order.len());
            order.insert(index, task.id.clone());

            tx.execute(
                "UPDATE tasks SET parent_task_id = ?2, column_id = ?3, updated_at = ?4 WHERE id = ?1",
                params![id, parent_id, column_id, now()],
            )?;
            renumber(tx, "tasks", &order)?;
            changes.board(list);
            require_task(tx, id)
        })
    }
}
