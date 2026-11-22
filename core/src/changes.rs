//! The change feed. Every committed write tells every subscriber what it
//! touched, so no window has to guess whether its copy is current.

/// One thing a committed write changed.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Enum)]
pub enum Change {
    /// Notes were created, renamed, moved, or had a flag change. Their
    /// content did not.
    NotesChanged { ids: Vec<String> },
    /// Notes are gone, along with everything they owned.
    NotesDeleted { ids: Vec<String> },
    /// A note's content was saved. `origin` is whatever the saver passed, so
    /// the editor that made the change can tell its own echo apart.
    DocumentChanged { note_id: String, revision: i64, origin: Option<String> },
    /// A todo list's columns, tasks, or comments changed.
    BoardChanged { todo_list_id: String },
}

/// Receives each committed write's changes, in commit order, one write at a
/// time. Called on the thread that made the write, with no locks held, so an
/// observer may call back into the store.
#[uniffi::export(foreign)]
pub trait ChangeObserver: Send + Sync {
    fn on_changes(&self, changes: Vec<Change>);
}
