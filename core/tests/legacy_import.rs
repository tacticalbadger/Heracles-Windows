//! Importing the original app's library: what carries across, what gets
//! repaired on the way, and what protects the user's data while it happens.

use heracles_core::{CoreError, ImportReport, NewNote, Note, NoteKind, Store, Task};
use rusqlite::{Connection, params};
use serde_json::{Value, json};
use std::path::{Path, PathBuf};
use tempfile::TempDir;

const PNG_BASE64: &str =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

const CREATED: &str = "2025-03-01T10:00:00.000Z";
const UPDATED: &str = "2025-04-02T11:30:00.000Z";

/// The schema the original app's database ended up with after all of its
/// migrations.
const LEGACY_SCHEMA: &str = "
    CREATE TABLE notes (
        id TEXT PRIMARY KEY, title TEXT NOT NULL, content TEXT NOT NULL,
        created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
        archived INTEGER NOT NULL DEFAULT 0, trashed INTEGER NOT NULL DEFAULT 0,
        icon TEXT, parent_id TEXT, position INTEGER NOT NULL DEFAULT 0,
        favorited INTEGER NOT NULL DEFAULT 0, kind TEXT NOT NULL DEFAULT 'page'
    );
    CREATE TABLE task_columns (
        id TEXT PRIMARY KEY, todo_list_id TEXT NOT NULL, name TEXT NOT NULL,
        position INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
    );
    CREATE TABLE tasks (
        id TEXT PRIMARY KEY, todo_list_id TEXT NOT NULL, parent_task_id TEXT, column_id TEXT,
        title TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', completed INTEGER NOT NULL DEFAULT 0,
        position INTEGER NOT NULL DEFAULT 0, due_date TEXT, deadline TEXT,
        priority INTEGER NOT NULL DEFAULT 4, labels TEXT NOT NULL DEFAULT '[]',
        reminders TEXT NOT NULL DEFAULT '[]', location TEXT,
        created_at TEXT NOT NULL, updated_at TEXT NOT NULL
    );
    CREATE TABLE task_comments (
        id TEXT PRIMARY KEY, task_id TEXT NOT NULL, body TEXT NOT NULL,
        created_at TEXT NOT NULL, updated_at TEXT NOT NULL
    );
";

struct Legacy {
    connection: Connection,
    path: PathBuf,
}

impl Legacy {
    fn create(dir: &Path) -> Legacy {
        let path = dir.join("heracles.sqlite");
        let connection = Connection::open(&path).unwrap();
        connection.execute_batch(LEGACY_SCHEMA).unwrap();
        Legacy { connection, path }
    }

    fn note(&self, id: &str, kind: &str, parent: Option<&str>, position: i64, content: &str) -> &Legacy {
        self.connection
            .execute(
                "INSERT INTO notes (id, title, content, created_at, updated_at, parent_id, position, kind)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)",
                params![id, format!("Title {id}"), content, CREATED, UPDATED, parent, position, kind],
            )
            .unwrap();
        self
    }

    fn column(&self, id: &str, list: &str, name: &str, position: i64) -> &Legacy {
        self.connection
            .execute(
                "INSERT INTO task_columns (id, todo_list_id, name, position, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?5)",
                params![id, list, name, position, CREATED],
            )
            .unwrap();
        self
    }

    fn task(&self, id: &str, list: &str, parent: Option<&str>, column: Option<&str>, position: i64) -> &Legacy {
        self.connection
            .execute(
                "INSERT INTO tasks (id, todo_list_id, parent_task_id, column_id, title, position, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?7)",
                params![id, list, parent, column, format!("Task {id}"), position, CREATED],
            )
            .unwrap();
        self
    }

    fn comment(&self, id: &str, task: &str, body: &str) -> &Legacy {
        self.connection
            .execute(
                "INSERT INTO task_comments (id, task_id, body, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?4)",
                params![id, task, body, CREATED],
            )
            .unwrap();
        self
    }

    fn sql(&self, sql: &str) -> &Legacy {
        self.connection.execute_batch(sql).unwrap();
        self
    }

    /// Close the database and hand back where it is.
    fn finish(self) -> String {
        let Legacy { connection, path } = self;
        connection.close().unwrap();
        path.to_string_lossy().into_owned()
    }
}

struct Setup {
    _dir: TempDir,
    data_dir: PathBuf,
    legacy_dir: PathBuf,
}

fn setup() -> Setup {
    let dir = tempfile::tempdir().unwrap();
    let data_dir = dir.path().join("library");
    let legacy_dir = dir.path().join("original");
    std::fs::create_dir_all(&legacy_dir).unwrap();
    Setup { _dir: dir, data_dir, legacy_dir }
}

fn note(store: &Store, id: &str) -> Note {
    store.get_note(id).unwrap().unwrap_or_else(|| panic!("note {id} was not imported"))
}

fn task(store: &Store, id: &str) -> Task {
    store.get_task(id).unwrap().unwrap_or_else(|| panic!("task {id} was not imported"))
}

fn document(store: &Store, id: &str) -> Value {
    serde_json::from_str(&store.get_document(id).unwrap().unwrap().content).unwrap()
}

/// A healthy library: two notebooks, a section, pages, and a board.
fn healthy(setup: &Setup) -> String {
    let page_html = format!(
        concat!(
            r#"<h1 data-block-id="h-1">Trip plan</h1>"#,
            r#"<p data-block-id="p-1">Pack the <strong>lantern</strong></p>"#,
            r#"<ul data-type="taskList"><li data-checked="true" data-type="taskItem"><label>"#,
            r#"<input type="checkbox" checked="checked"><span></span></label><div><p>Book ferry</p></div></li></ul>"#,
            r#"<pre language="rust" code="fn main() {{}}" data-code-mirror="true"></pre>"#,
            r#"<img src="data:image/png;base64,{png}" alt="Map" class="editor-image">"#
        ),
        png = PNG_BASE64
    );
    let legacy = Legacy::create(&setup.legacy_dir);
    legacy
        .note("nb-1", "notebook", None, 0, "")
        .note("nb-2", "notebook", None, 1, "")
        .note("section", "section", Some("nb-1"), 0, "")
        .note("page", "page", Some("section"), 0, &page_html)
        .note("child", "page", Some("page"), 0, "<p>child text</p>")
        .note("blank", "page", Some("nb-2"), 3, "")
        .note("list", "todolist", Some("page"), 1, "")
        .column("col-next", "list", "Up Next", 0)
        .column("col-done", "list", "Done", 1)
        .task("task-1", "list", None, Some("col-next"), 0)
        .task("task-2", "list", None, Some("col-done"), 0)
        .task("sub-1", "list", Some("task-1"), None, 0)
        .task("sub-2", "list", Some("sub-1"), None, 0)
        .comment("comment-1", "task-1", "first")
        .sql(
            "UPDATE notes SET icon = '🧭', favorited = 1 WHERE id = 'page';
             UPDATE notes SET archived = 1 WHERE id = 'child';
             UPDATE notes SET trashed = 1 WHERE id = 'blank';
             UPDATE tasks SET description = 'details', completed = 1, due_date = '2025-06-01',
                    deadline = '2025-06-10', priority = 2, labels = '[\"travel\",\"urgent\"]',
                    reminders = '[\"2025-05-30T09:00:00.000Z\"]', location = 'Harbour'
             WHERE id = 'task-1';",
        );
    legacy.finish()
}

fn import(setup: &Setup, legacy: &str) -> (Store, ImportReport) {
    let store = Store::open(&setup.data_dir).unwrap();
    let report = store.import_legacy(legacy).unwrap();
    (store, report)
}

// ─── What carries across ────────────────────────────────────────────────────

#[test]
fn notes_keep_their_ids_place_flags_and_dates() {
    let setup = setup();
    let (store, report) = import(&setup, &healthy(&setup));

    let all = store.list_notes().unwrap();
    assert_eq!(all.len(), 7, "the seven old notes, and the new library's placeholder notebook is gone");
    assert_eq!(report.notes, 7);

    let page = note(&store, "page");
    assert_eq!(page.kind, NoteKind::Page);
    assert_eq!(page.title, "Title page");
    assert_eq!(page.parent_id.as_deref(), Some("section"));
    assert_eq!(page.icon.as_deref(), Some("🧭"));
    assert!(page.favorited);
    assert_eq!((page.created_at.as_str(), page.updated_at.as_str()), (CREATED, UPDATED));

    assert_eq!(note(&store, "nb-1").kind, NoteKind::Notebook);
    assert_eq!((note(&store, "nb-1").position, note(&store, "nb-2").position), (0, 1));
    assert_eq!(note(&store, "section").kind, NoteKind::Section);
    assert_eq!(note(&store, "list").kind, NoteKind::TodoList);
    assert_eq!(note(&store, "list").position, 1);
    assert!(note(&store, "child").archived);
    assert!(note(&store, "blank").trashed);
    assert_eq!(note(&store, "blank").position, 3);
    assert!(report.repairs.is_empty(), "a healthy library needs no repairs: {:?}", report.repairs);
}

#[test]
fn html_content_becomes_a_block_document() {
    let setup = setup();
    let (store, report) = import(&setup, &healthy(&setup));
    let doc = document(&store, "page");
    let blocks = doc["content"].as_array().unwrap();

    assert_eq!(blocks[0]["type"], json!("heading"));
    assert_eq!(blocks[0]["attrs"], json!({"level": 1, "blockId": "h-1"}));
    assert_eq!(blocks[1]["attrs"]["blockId"], json!("p-1"), "block links must keep working");
    assert_eq!(blocks[1]["content"][1], json!({"type": "text", "text": "lantern", "marks": [{"type": "bold"}]}));
    assert_eq!(blocks[2]["type"], json!("taskList"));
    assert_eq!(blocks[2]["content"][0]["attrs"]["checked"], json!(true));
    assert_eq!(blocks[3]["attrs"]["code"], json!("fn main() {}"));
    assert_eq!(blocks[4]["type"], json!("image"));

    let ids: Vec<&str> = [&blocks[2]["content"][0], &blocks[3], &blocks[4]]
        .iter()
        .map(|block| block["attrs"]["blockId"].as_str().expect("blocks without an id get one"))
        .collect();
    assert!(ids.iter().all(|id| !id.is_empty()));

    // A page that was empty reads as an empty document. It is not missing.
    assert_eq!(document(&store, "blank"), json!({"type": "doc", "content": [{"type": "paragraph"}]}));
    assert_eq!(report.documents, 2, "only notes that had content count");
}

#[test]
fn embedded_images_move_to_the_image_store() {
    let setup = setup();
    let (store, report) = import(&setup, &healthy(&setup));

    let content = store.get_document("page").unwrap().unwrap().content;
    assert!(!content.contains("base64"));
    let doc: Value = serde_json::from_str(&content).unwrap();
    let image = &doc["content"][4]["attrs"];
    let src = image["src"].as_str().unwrap();
    assert!(src.starts_with("heracles-image://local/"));
    assert_eq!(image["alt"], json!("Map"));
    assert!(Path::new(&store.image_path(src).unwrap().unwrap()).exists());
    assert_eq!(report.images, 1);
    assert!(store.collect_unused_images(0).unwrap().is_empty(), "an imported image is in use");
}

#[test]
fn imported_text_is_searchable() {
    let setup = setup();
    let (store, _) = import(&setup, &healthy(&setup));
    let titles = |query: &str| -> Vec<String> {
        store.search_notes(query, 10).unwrap().into_iter().map(|hit| hit.note.title).collect()
    };
    assert_eq!(titles("lantern"), vec!["Title page"]);
    assert_eq!(titles("ferry"), vec!["Title page"]);
    assert_eq!(titles("title child"), vec!["Title child"]);
}

#[test]
fn boards_carry_across_with_every_field() {
    let setup = setup();
    let (store, report) = import(&setup, &healthy(&setup));

    let columns = store.list_columns("list").unwrap();
    assert_eq!(
        columns.iter().map(|column| (column.id.as_str(), column.name.as_str())).collect::<Vec<_>>(),
        vec![("col-next", "Up Next"), ("col-done", "Done")]
    );
    let first = task(&store, "task-1");
    assert_eq!(first.column_id.as_deref(), Some("col-next"));
    assert_eq!(first.title, "Task task-1");
    assert_eq!(first.description, "details");
    assert!(first.completed);
    assert_eq!(first.due_date.as_deref(), Some("2025-06-01"));
    assert_eq!(first.deadline.as_deref(), Some("2025-06-10"));
    assert_eq!(first.priority, 2);
    assert_eq!(first.labels, vec!["travel", "urgent"]);
    assert_eq!(first.reminders, vec!["2025-05-30T09:00:00.000Z"]);
    assert_eq!(first.location.as_deref(), Some("Harbour"));
    assert_eq!(first.created_at, CREATED);

    assert_eq!(task(&store, "sub-1").parent_task_id.as_deref(), Some("task-1"));
    assert_eq!(task(&store, "sub-1").column_id, None);
    assert_eq!(task(&store, "sub-2").parent_task_id.as_deref(), Some("sub-1"));
    let comments = store.list_comments("task-1").unwrap();
    assert_eq!(comments.len(), 1);
    assert_eq!((comments[0].id.as_str(), comments[0].body.as_str()), ("comment-1", "first"));
    assert_eq!((report.columns, report.tasks, report.comments), (2, 4, 1));
}

// ─── Repairs ────────────────────────────────────────────────────────────────

#[test]
fn notes_with_no_place_in_the_tree_land_in_the_first_notebook() {
    let setup = setup();
    let legacy = Legacy::create(&setup.legacy_dir);
    legacy
        .note("nb-late", "notebook", None, 5, "")
        .note("nb-first", "notebook", None, 2, "")
        .note("rootless", "page", None, 0, "<p>a</p>")
        .note("orphan", "page", Some("ghost"), 0, "<p>b</p>")
        .note("orphan-child", "page", Some("orphan"), 0, "")
        .note("selfish", "page", Some("selfish"), 0, "")
        .note("loop-a", "page", Some("loop-b"), 0, "")
        .note("loop-b", "page", Some("loop-a"), 0, "")
        .note("nested-notebook", "notebook", Some("nb-late"), 9, "")
        .note("weird", "mystery", Some("nb-late"), 0, "<p>c</p>");
    let (store, report) = import(&setup, &legacy.finish());

    for id in ["rootless", "orphan", "selfish"] {
        assert_eq!(note(&store, id).parent_id.as_deref(), Some("nb-first"), "{id}");
    }
    assert_eq!(note(&store, "orphan-child").parent_id.as_deref(), Some("orphan"), "its own parent was fine");
    assert_eq!(note(&store, "nested-notebook").parent_id, None, "a notebook is always top level");
    assert_eq!(note(&store, "weird").kind, NoteKind::Page);
    assert_eq!(note(&store, "weird").parent_id.as_deref(), Some("nb-late"));

    // A loop is cut in one place. Both notes stay, one under the other.
    let (a, b) = (note(&store, "loop-a"), note(&store, "loop-b"));
    let parents = [a.parent_id.as_deref(), b.parent_id.as_deref()];
    assert!(
        parents == [Some("nb-first"), Some("loop-a")] || parents == [Some("loop-b"), Some("nb-first")],
        "unexpected parents {parents:?}"
    );

    assert_eq!(store.list_notes().unwrap().len(), 10, "nothing is dropped");
    for id in ["rootless", "orphan", "selfish", "nested-notebook", "weird"] {
        assert!(report.repairs.iter().any(|line| line.contains(id)), "no repair line for {id}: {:?}", report.repairs);
    }
    assert!(report.repairs.iter().any(|line| line.contains("loop-a") || line.contains("loop-b")));
}

#[test]
fn a_library_from_before_notebooks_goes_into_the_new_librarys_notebook() {
    let setup = setup();
    let legacy_path = setup.legacy_dir.join("heracles.sqlite");
    let connection = Connection::open(&legacy_path).unwrap();
    // The very first schema: no kind, no parent, no board tables.
    connection
        .execute_batch(
            "CREATE TABLE notes (id TEXT PRIMARY KEY, title TEXT NOT NULL, content TEXT NOT NULL,
                 created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                 archived INTEGER NOT NULL DEFAULT 0, trashed INTEGER NOT NULL DEFAULT 0);
             INSERT INTO notes VALUES ('old-1', 'First', '<p>one</p>', '2024-01-01T00:00:00.000Z',
                 '2024-01-02T00:00:00.000Z', 0, 0);
             INSERT INTO notes VALUES ('old-2', 'Second', '<p>two</p>', '2024-01-01T00:00:00.000Z',
                 '2024-01-03T00:00:00.000Z', 1, 0);",
        )
        .unwrap();
    connection.close().unwrap();

    let store = Store::open(&setup.data_dir).unwrap();
    let placeholder = store.list_notes().unwrap().remove(0);
    let report = store.import_legacy(&legacy_path.to_string_lossy()).unwrap();

    assert_eq!(report.notes, 2);
    assert_eq!(store.list_notes().unwrap().len(), 3);
    for id in ["old-1", "old-2"] {
        assert_eq!(note(&store, id).kind, NoteKind::Page);
        assert_eq!(note(&store, id).parent_id, Some(placeholder.id.clone()));
    }
    assert!(note(&store, "old-2").archived);
    assert_eq!(document(&store, "old-1")["content"][0]["content"][0]["text"], json!("one"));
}

#[test]
fn tasks_the_board_could_not_show_are_given_a_column() {
    let setup = setup();
    let legacy = Legacy::create(&setup.legacy_dir);
    legacy
        .note("nb", "notebook", None, 0, "")
        .note("list", "todolist", Some("nb"), 0, "")
        .note("other-list", "todolist", Some("nb"), 1, "")
        .note("bare-list", "todolist", Some("nb"), 2, "")
        .column("col-b", "list", "Second", 1)
        .column("col-a", "list", "First", 0)
        .column("col-other", "other-list", "Elsewhere", 0)
        .column("col-lost", "missing-list", "Lost", 0)
        .task("placed", "list", None, Some("col-a"), 0)
        .task("no-column", "list", None, None, 0)
        .task("gone-column", "list", None, Some("col-deleted"), 1)
        .task("foreign-column", "list", None, Some("col-other"), 2)
        .task("lost-parent", "list", Some("ghost"), None, 3)
        .task("both", "list", Some("placed"), Some("col-b"), 0)
        .task("loop-1", "list", Some("loop-2"), None, 0)
        .task("loop-2", "list", Some("loop-1"), None, 0)
        .task("bare", "bare-list", None, None, 0)
        .task("listless", "missing-list", None, None, 0)
        .comment("kept", "placed", "kept")
        .comment("dropped", "missing-task", "dropped")
        .sql(
            "UPDATE tasks SET priority = 9, labels = 'not json', reminders = '{\"a\":1}' WHERE id = 'placed';
             UPDATE tasks SET priority = 0 WHERE id = 'no-column';",
        );
    let (store, report) = import(&setup, &legacy.finish());

    for id in ["no-column", "gone-column", "foreign-column", "lost-parent"] {
        let repaired = task(&store, id);
        assert_eq!(repaired.column_id.as_deref(), Some("col-a"), "{id} goes to the board's first column");
        assert_eq!(repaired.parent_task_id, None, "{id}");
    }
    let both = task(&store, "both");
    assert_eq!((both.parent_task_id.as_deref(), both.column_id), (Some("placed"), None));

    let (one, two) = (task(&store, "loop-1"), task(&store, "loop-2"));
    let on_the_board = [&one, &two].iter().filter(|task| task.column_id.as_deref() == Some("col-a")).count();
    assert_eq!(on_the_board, 1, "a loop of subtasks is cut in one place");
    assert!(one.parent_task_id.is_some() != two.parent_task_id.is_some());

    let bare_columns = store.list_columns("bare-list").unwrap();
    assert_eq!(bare_columns.len(), 1, "a board with tasks and no columns gets one");
    assert_eq!(bare_columns[0].name, "Up Next");
    assert_eq!(task(&store, "bare").column_id, Some(bare_columns[0].id.clone()));

    let placed = task(&store, "placed");
    assert_eq!(placed.priority, 4, "an out-of-range priority becomes the default");
    assert_eq!(task(&store, "no-column").priority, 4);
    assert!(placed.labels.is_empty() && placed.reminders.is_empty());

    assert_eq!(store.get_task("listless").unwrap(), None, "its todo list does not exist");
    assert!(store.list_columns("missing-list").unwrap().is_empty());
    assert_eq!(store.list_comments("placed").unwrap().len(), 1);
    assert_eq!((report.columns, report.tasks, report.comments), (4, 9, 1));
    for id in ["no-column", "gone-column", "foreign-column", "lost-parent", "bare", "listless", "col-lost", "dropped"] {
        assert!(report.repairs.iter().any(|line| line.contains(id)), "no repair line for {id}: {:?}", report.repairs);
    }

    // Everything imported is reachable through the normal board calls.
    let on_board: Vec<Task> = store.list_tasks("list").unwrap();
    assert_eq!(on_board.len(), 8);
    assert!(on_board.iter().all(|task| task.column_id.is_some() != task.parent_task_id.is_some()));
}

// ─── Protecting the data ────────────────────────────────────────────────────

#[test]
fn the_old_database_is_backed_up_first_and_never_changed() {
    let setup = setup();
    let legacy = healthy(&setup);
    let before = std::fs::read(&legacy).unwrap();

    let (_store, report) = import(&setup, &legacy);

    assert_eq!(std::fs::read(&legacy).unwrap(), before, "the old database must only be read");
    let backup = Path::new(&report.backup_path);
    assert!(backup.starts_with(&setup.data_dir), "the backup lives with the new library");
    let name = backup.file_name().unwrap().to_string_lossy();
    assert!(
        name.starts_with("heracles-original-") && name.ends_with(".sqlite"),
        "the backup is named for what it is a copy of, not {name}"
    );
    let copy = Connection::open(backup).unwrap();
    let notes: i64 = copy.query_row("SELECT COUNT(*) FROM notes", [], |row| row.get(0)).unwrap();
    let html: String = copy.query_row("SELECT content FROM notes WHERE id = 'page'", [], |row| row.get(0)).unwrap();
    assert_eq!(notes, 7);
    assert!(html.contains(PNG_BASE64), "the backup holds the original content, images and all");
}

#[test]
fn import_runs_once() {
    let setup = setup();
    let legacy = healthy(&setup);
    let (store, _) = import(&setup, &legacy);

    assert!(matches!(store.import_legacy(&legacy), Err(CoreError::Invalid(_))));
    assert_eq!(store.list_notes().unwrap().len(), 7);

    // Still refused after a restart, and after the library is emptied.
    drop(store);
    let store = Store::open(&setup.data_dir).unwrap();
    for id in ["section", "blank"] {
        store.delete_note(id).unwrap();
    }
    assert!(matches!(store.import_legacy(&legacy), Err(CoreError::Invalid(_))));
}

#[test]
fn import_is_refused_once_the_library_has_content() {
    let setup = setup();
    let legacy = healthy(&setup);
    let store = Store::open(&setup.data_dir).unwrap();
    let mine = store.create_note(NewNote { title: Some("Mine".into()), ..NewNote::default() }).unwrap();

    assert!(matches!(store.import_legacy(&legacy), Err(CoreError::Invalid(_))));
    assert_eq!(store.list_notes().unwrap().len(), 2);
    assert!(store.get_note(&mine.id).unwrap().is_some());
}

#[test]
fn a_failed_import_leaves_the_library_as_it_was() {
    let setup = setup();
    let store = Store::open(&setup.data_dir).unwrap();
    let before = store.list_notes().unwrap();

    let missing = setup.legacy_dir.join("nowhere.sqlite");
    assert!(store.import_legacy(&missing.to_string_lossy()).is_err());
    assert!(!missing.exists(), "a missing database must not be created");

    let garbage = setup.legacy_dir.join("garbage.sqlite");
    std::fs::write(&garbage, b"this is not a database").unwrap();
    assert!(store.import_legacy(&garbage.to_string_lossy()).is_err());

    let empty = setup.legacy_dir.join("empty.sqlite");
    Connection::open(&empty).unwrap().execute_batch("CREATE TABLE unrelated (x)").unwrap();
    assert!(store.import_legacy(&empty.to_string_lossy()).is_err(), "no notes table");

    assert_eq!(store.list_notes().unwrap(), before);

    // And a later import of a real library still works.
    let report = store.import_legacy(&healthy(&setup)).unwrap();
    assert_eq!(report.notes, 7);
}

#[test]
fn every_imported_note_is_announced_once_the_import_commits() {
    use heracles_core::{Change, ChangeObserver};
    use std::sync::{Arc, Mutex};

    #[derive(Default)]
    struct Recorder(Mutex<Vec<Vec<Change>>>);
    impl ChangeObserver for Recorder {
        fn on_changes(&self, changes: Vec<Change>) {
            self.0.lock().unwrap().push(changes);
        }
    }

    let setup = setup();
    let legacy = healthy(&setup);
    let store = Store::open(&setup.data_dir).unwrap();
    let placeholder = store.list_notes().unwrap().remove(0);
    let recorder = Arc::new(Recorder::default());
    store.subscribe(recorder.clone());

    store.import_legacy(&legacy).unwrap();

    let batches = recorder.0.lock().unwrap();
    assert_eq!(batches.len(), 1, "one import is one write");
    let [Change::NotesChanged { ids }, Change::NotesDeleted { ids: deleted }] = batches[0].as_slice() else {
        panic!("unexpected changes {:?}", batches[0]);
    };
    assert_eq!(ids.len(), 7);
    assert_eq!(deleted, &vec![placeholder.id]);
}
