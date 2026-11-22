//! The parts of the interface that need a shape of their own.
//!
//! Most of the store is exported as it is. What lives here is the
//! constructor, the few methods whose Rust signatures borrow optional
//! strings (exported under the same names with owned arguments), the task
//! patch in a shape every binding can express, and constants the interface
//! would otherwise have to copy.

use crate::error::Result;
use crate::model::{IMAGE_URL_SCHEME, Note, StoredDocument, Task, TaskPatch};
use crate::store::Store;
use std::sync::Arc;

/// The fields to change on a task, as they cross to the interface.
///
/// In Rust a patch says "clear this field" with a nested optional. That
/// shape does not survive every binding: C# has no optional of an optional
/// string. So each field that can be emptied crosses as a value and a flag.
/// A value sets the field, the flag clears it, and with neither the field
/// is left alone. The flag wins over a value.
#[derive(Debug, Clone, Default, uniffi::Record)]
#[uniffi(name = "TaskPatch")]
pub struct TaskPatchRecord {
    #[uniffi(default = None)]
    pub title: Option<String>,
    #[uniffi(default = None)]
    pub description: Option<String>,
    #[uniffi(default = None)]
    pub completed: Option<bool>,
    #[uniffi(default = None)]
    pub due_date: Option<String>,
    #[uniffi(default = false)]
    pub clear_due_date: bool,
    #[uniffi(default = None)]
    pub deadline: Option<String>,
    #[uniffi(default = false)]
    pub clear_deadline: bool,
    #[uniffi(default = None)]
    pub priority: Option<u8>,
    #[uniffi(default = None)]
    pub labels: Option<Vec<String>>,
    #[uniffi(default = None)]
    pub reminders: Option<Vec<String>>,
    #[uniffi(default = None)]
    pub location: Option<String>,
    #[uniffi(default = false)]
    pub clear_location: bool,
}

impl From<TaskPatchRecord> for TaskPatch {
    fn from(record: TaskPatchRecord) -> TaskPatch {
        let clearable = |value: Option<String>, clear: bool| if clear { Some(None) } else { value.map(Some) };
        TaskPatch {
            title: record.title,
            description: record.description,
            completed: record.completed,
            due_date: clearable(record.due_date, record.clear_due_date),
            deadline: clearable(record.deadline, record.clear_deadline),
            priority: record.priority,
            labels: record.labels,
            reminders: record.reminders,
            location: clearable(record.location, record.clear_location),
        }
    }
}

#[uniffi::export]
impl Store {
    /// Open the library in `data_dir`, creating it on first use.
    #[uniffi::constructor]
    pub fn new(data_dir: String) -> Result<Arc<Store>> {
        Ok(Arc::new(Store::open(data_dir)?))
    }

    /// Set a note's icon, or clear it with `nil`.
    #[uniffi::method(name = "set_note_icon")]
    pub fn ffi_set_note_icon(&self, id: &str, icon: Option<String>) -> Result<Note> {
        self.set_note_icon(id, icon.as_deref())
    }

    /// Put a note under `parent_id`, before the sibling `before_id` or at the
    /// end. Moving a note into itself or its own subtree changes nothing.
    #[uniffi::method(name = "move_note")]
    pub fn ffi_move_note(&self, id: &str, parent_id: Option<String>, before_id: Option<String>) -> Result<Note> {
        self.move_note(id, parent_id.as_deref(), before_id.as_deref())
    }

    /// Save a note's content. Returns once the write is committed. `origin`
    /// comes back in the change feed, so an editor can tell its own save
    /// from someone else's.
    #[uniffi::method(name = "save_document")]
    pub fn ffi_save_document(&self, note_id: &str, content: &str, origin: Option<String>) -> Result<StoredDocument> {
        self.save_document(note_id, content, origin.as_deref())
    }

    /// Create a page from a Markdown or HTML file. The page is named for the
    /// file, and images the file refers to beside it are brought into the
    /// image store. With no parent it goes in the first notebook.
    #[uniffi::method(name = "import_file")]
    pub fn ffi_import_file(&self, path: &str, parent_id: Option<String>) -> Result<Note> {
        self.import_file(path, parent_id.as_deref())
    }

    /// Write a note's content to `destination` as Markdown. Its images are
    /// copied into a folder beside the file and linked from there.
    #[uniffi::method(name = "export_markdown")]
    pub fn ffi_export_markdown(&self, note_id: &str, destination: &str) -> Result<()> {
        self.export_markdown(note_id, destination)
    }

    /// Every kind of move: reorder within a column or parent, change column,
    /// promote a subtask to a card, or put a task under another. Give a
    /// parent for a subtask, otherwise a column. `before_id` is the sibling
    /// to land in front of, or `nil` for the end.
    #[uniffi::method(name = "move_task")]
    pub fn ffi_move_task(
        &self,
        id: &str,
        column_id: Option<String>,
        parent_task_id: Option<String>,
        before_id: Option<String>,
    ) -> Result<Task> {
        self.move_task(id, column_id.as_deref(), parent_task_id.as_deref(), before_id.as_deref())
    }

    /// Change the fields a patch names, and leave the rest alone.
    #[uniffi::method(name = "update_task")]
    pub fn ffi_update_task(&self, id: &str, patch: TaskPatchRecord) -> Result<Task> {
        self.update_task(id, patch.into())
    }
}

/// The URL scheme of images in the image store, for the handler that serves
/// them to the editor.
#[uniffi::export]
pub fn image_url_scheme() -> String {
    IMAGE_URL_SCHEME.to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::TaskPatch;

    fn patch(record: TaskPatchRecord) -> TaskPatch {
        record.into()
    }

    #[test]
    fn a_value_sets_the_field() {
        let p = patch(TaskPatchRecord { due_date: Some("2026-12-01".into()), ..Default::default() });
        assert_eq!(p.due_date, Some(Some("2026-12-01".to_string())));
    }

    #[test]
    fn a_clear_flag_clears_the_field() {
        let p = patch(TaskPatchRecord { clear_deadline: true, ..Default::default() });
        assert_eq!(p.deadline, Some(None));
    }

    #[test]
    fn neither_leaves_the_field_alone() {
        let p = patch(TaskPatchRecord::default());
        assert_eq!((p.due_date, p.deadline, p.location), (None, None, None));
    }

    #[test]
    fn clearing_wins_over_a_value() {
        let p = patch(TaskPatchRecord {
            location: Some("Harbour".into()),
            clear_location: true,
            ..Default::default()
        });
        assert_eq!(p.location, Some(None));
    }

    #[test]
    fn the_plain_fields_pass_through() {
        let p = patch(TaskPatchRecord {
            title: Some("Book ferry".into()),
            completed: Some(true),
            priority: Some(2),
            labels: Some(vec!["travel".into()]),
            ..Default::default()
        });
        assert_eq!(
            (p.title.as_deref(), p.completed, p.priority, p.labels),
            (Some("Book ferry"), Some(true), Some(2), Some(vec!["travel".to_string()]))
        );
    }
}
