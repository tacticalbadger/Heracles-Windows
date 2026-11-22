//! The records the core hands to the interface.

/// What a note is. Notebooks sit at the top level and hold everything else.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, uniffi::Enum)]
pub enum NoteKind {
    Notebook,
    Section,
    Page,
    TodoList,
}

impl NoteKind {
    /// The name stored in the database.
    pub fn as_str(self) -> &'static str {
        match self {
            NoteKind::Notebook => "notebook",
            NoteKind::Section => "section",
            NoteKind::Page => "page",
            NoteKind::TodoList => "todolist",
        }
    }

    /// Read a stored name. Anything unknown is a page, as the Electron app
    /// treated it.
    pub fn from_stored(name: &str) -> NoteKind {
        match name {
            "notebook" => NoteKind::Notebook,
            "section" => NoteKind::Section,
            "todolist" => NoteKind::TodoList,
            _ => NoteKind::Page,
        }
    }

    pub(crate) fn default_title(self) -> &'static str {
        match self {
            NoteKind::Notebook => "New notebook",
            NoteKind::Section => "New section",
            NoteKind::Page => "Untitled",
            NoteKind::TodoList => "New todo list",
        }
    }
}

/// A note without its content. Listing notes never loads documents, so the
/// sidebar stays cheap however large the pages are.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct Note {
    pub id: String,
    pub kind: NoteKind,
    /// `None` for notebooks only.
    pub parent_id: Option<String>,
    pub title: String,
    pub icon: Option<String>,
    /// Manual order among the notes that share a parent.
    pub position: i64,
    pub favorited: bool,
    pub archived: bool,
    pub trashed: bool,
    /// UTC, ISO 8601 with milliseconds, as JavaScript's `toISOString` writes.
    pub created_at: String,
    pub updated_at: String,
}

/// What to create. Everything is optional: the default is an untitled page
/// in the first notebook.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct NewNote {
    #[uniffi(default = None)]
    pub kind: Option<NoteKind>,
    /// Where it goes. Ignored for notebooks. `None` means the first notebook.
    #[uniffi(default = None)]
    pub parent_id: Option<String>,
    #[uniffi(default = None)]
    pub title: Option<String>,
    #[uniffi(default = None)]
    pub icon: Option<String>,
    /// Document JSON for the new note. `None` gives a page one empty paragraph.
    #[uniffi(default = None)]
    pub content: Option<String>,
}

/// A note's content as stored.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct StoredDocument {
    pub note_id: String,
    /// Document JSON. See [`crate::doc`].
    pub content: String,
    /// Counts saves. A window holding an older revision is out of date.
    pub revision: i64,
}

/// A column of a todo list's board.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct TaskColumn {
    pub id: String,
    pub todo_list_id: String,
    pub name: String,
    pub position: i64,
    pub created_at: String,
    pub updated_at: String,
}

/// A card on a board, or a subtask of one. A card has a column and no
/// parent. A subtask has a parent and no column.
// Swift's own `Task` is its unit of concurrency, so this crosses as `TodoTask`.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
#[uniffi(name = "TodoTask")]
pub struct Task {
    pub id: String,
    pub todo_list_id: String,
    pub parent_task_id: Option<String>,
    pub column_id: Option<String>,
    pub title: String,
    pub description: String,
    pub completed: bool,
    /// Order within the column, or within the parent's subtasks.
    pub position: i64,
    pub due_date: Option<String>,
    pub deadline: Option<String>,
    /// 1 is the most urgent, 4 the default.
    pub priority: u8,
    pub labels: Vec<String>,
    /// Times to remind at, as ISO 8601 strings.
    pub reminders: Vec<String>,
    pub location: Option<String>,
    pub created_at: String,
    pub updated_at: String,
}

/// What to create. Give a column for a card or a parent for a subtask.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct NewTask {
    pub todo_list_id: String,
    #[uniffi(default = None)]
    pub column_id: Option<String>,
    #[uniffi(default = None)]
    pub parent_task_id: Option<String>,
    #[uniffi(default = None)]
    pub title: Option<String>,
    #[uniffi(default = None)]
    pub description: Option<String>,
    #[uniffi(default = None)]
    pub priority: Option<u8>,
}

/// Fields to change on a task. `None` leaves a field alone. For the fields
/// that can be empty, `Some(None)` clears the value.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct TaskPatch {
    #[uniffi(default = None)]
    pub title: Option<String>,
    #[uniffi(default = None)]
    pub description: Option<String>,
    #[uniffi(default = None)]
    pub completed: Option<bool>,
    #[uniffi(default = None)]
    pub due_date: Option<Option<String>>,
    #[uniffi(default = None)]
    pub deadline: Option<Option<String>>,
    #[uniffi(default = None)]
    pub priority: Option<u8>,
    #[uniffi(default = None)]
    pub labels: Option<Vec<String>>,
    #[uniffi(default = None)]
    pub reminders: Option<Vec<String>>,
    #[uniffi(default = None)]
    pub location: Option<Option<String>>,
}

#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct TaskComment {
    pub id: String,
    pub task_id: String,
    pub body: String,
    pub created_at: String,
    pub updated_at: String,
}

/// A reminder that has not been dealt with: its task is not completed and
/// its todo list is not in the trash.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct Reminder {
    pub task_id: String,
    pub todo_list_id: String,
    pub task_title: String,
    pub remind_at: String,
}

/// One search result.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct SearchHit {
    pub note: Note,
    /// A stretch of the note's text around the match, cut into the parts
    /// that matched and the parts between them.
    pub snippet: Vec<SnippetPart>,
}

/// A piece of a search snippet.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct SnippetPart {
    pub text: String,
    /// True if this piece is one of the words searched for.
    pub matched: bool,
}

/// An image in the image store.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct StoredImage {
    /// SHA-256 of the bytes, in lowercase hex. This is the image's name.
    pub hash: String,
    pub mime: String,
    pub byte_count: i64,
    /// What a document's image block holds as its `src`.
    pub url: String,
}

/// The URL scheme of images held in the image store.
pub const IMAGE_URL_SCHEME: &str = "heracles-image";

/// What importing the Electron app's library did.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct ImportReport {
    pub notes: u32,
    /// Notes whose HTML content became a document.
    pub documents: u32,
    /// Images moved out of note content into the image store.
    pub images: u32,
    pub columns: u32,
    pub tasks: u32,
    pub comments: u32,
    /// The copy of the old database that was made before anything was read.
    pub backup_path: String,
    /// One line for each thing that had to be changed or left out to make
    /// the old data fit: orphaned notes, tasks with no column, and so on.
    pub repairs: Vec<String>,
}
