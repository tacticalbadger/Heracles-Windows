//! The parts of the Swift interface that need a shape of their own.
//!
//! Most of the store is exported as it is. What lives here is the
//! constructor, the few methods whose Rust signatures borrow optional
//! strings (exported under the same names with owned arguments), and
//! constants Swift would otherwise have to copy.

use crate::error::Result;
use crate::model::{IMAGE_URL_SCHEME, Note, StoredDocument, Task};
use crate::store::Store;
use std::sync::Arc;

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
}

/// The URL scheme of images in the image store, for the handler that serves
/// them to the editor.
#[uniffi::export]
pub fn image_url_scheme() -> String {
    IMAGE_URL_SCHEME.to_string()
}
