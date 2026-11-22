//! The change feed: what each write announces, and when.

use heracles_core::{Change, ChangeObserver, CoreError, NewNote, NewTask, Note, NoteKind, Store};
use std::sync::{Arc, Mutex, Weak};
use tempfile::TempDir;

#[derive(Default)]
struct Recorder {
    batches: Mutex<Vec<Vec<Change>>>,
}

impl ChangeObserver for Recorder {
    fn on_changes(&self, changes: Vec<Change>) {
        self.batches.lock().unwrap().push(changes);
    }
}

impl Recorder {
    /// Everything received since the last call.
    fn take(&self) -> Vec<Vec<Change>> {
        std::mem::take(&mut self.batches.lock().unwrap())
    }
}

fn open() -> (TempDir, Arc<Store>, Arc<Recorder>) {
    let dir = tempfile::tempdir().unwrap();
    let store = Arc::new(Store::open(dir.path()).unwrap());
    let recorder = Arc::new(Recorder::default());
    store.subscribe(recorder.clone());
    (dir, store, recorder)
}

fn note(store: &Store, kind: NoteKind, parent: Option<&Note>) -> Note {
    store
        .create_note(NewNote {
            kind: Some(kind),
            parent_id: parent.map(|parent| parent.id.clone()),
            ..NewNote::default()
        })
        .unwrap()
}

fn doc(text: &str) -> String {
    format!(r#"{{"type":"doc","content":[{{"type":"paragraph","content":[{{"type":"text","text":"{text}"}}]}}]}}"#)
}

fn sorted(mut ids: Vec<String>) -> Vec<String> {
    ids.sort();
    ids
}

#[test]
fn creating_a_note_announces_it() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    assert_eq!(recorder.take(), vec![vec![Change::NotesChanged { ids: vec![page.id] }]]);
}

#[test]
fn saving_a_document_announces_the_revision_and_who_saved_it() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    recorder.take();

    store.save_document(&page.id, &doc("one"), Some("editor-7")).unwrap();
    store.save_document(&page.id, &doc("two"), None).unwrap();

    assert_eq!(
        recorder.take(),
        vec![
            vec![
                Change::NotesChanged { ids: vec![page.id.clone()] },
                Change::DocumentChanged { note_id: page.id.clone(), revision: 1, origin: Some("editor-7".into()) },
            ],
            vec![
                Change::NotesChanged { ids: vec![page.id.clone()] },
                Change::DocumentChanged { note_id: page.id.clone(), revision: 2, origin: None },
            ],
        ]
    );
}

#[test]
fn saving_unchanged_content_announces_nothing() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    store.save_document(&page.id, &doc("same"), Some("editor-7")).unwrap();
    recorder.take();

    store.save_document(&page.id, &doc("same"), Some("editor-8")).unwrap();
    assert!(recorder.take().is_empty());
}

#[test]
fn trashing_announces_every_note_in_the_subtree() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    let child = note(&store, NoteKind::Page, Some(&page));
    recorder.take();

    store.trash_note(&page.id).unwrap();

    let batches = recorder.take();
    assert_eq!(batches.len(), 1);
    let [Change::NotesChanged { ids }] = batches[0].as_slice() else { panic!("unexpected {batches:?}") };
    assert_eq!(sorted(ids.clone()), sorted(vec![page.id, child.id]));
}

#[test]
fn deleting_announces_every_id_that_went() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    let list = note(&store, NoteKind::TodoList, Some(&page));
    recorder.take();

    store.delete_note(&page.id).unwrap();

    let batches = recorder.take();
    assert_eq!(batches.len(), 1);
    let [Change::NotesDeleted { ids }] = batches[0].as_slice() else { panic!("unexpected {batches:?}") };
    assert_eq!(sorted(ids.clone()), sorted(vec![page.id, list.id]));
}

#[test]
fn deleting_a_notebook_announces_the_notes_that_moved_and_the_notebook_that_went() {
    let (_dir, store, recorder) = open();
    let doomed = note(&store, NoteKind::Notebook, None);
    let page = note(&store, NoteKind::Page, Some(&doomed));
    recorder.take();

    store.delete_note(&doomed.id).unwrap();

    assert_eq!(
        recorder.take(),
        vec![vec![
            Change::NotesChanged { ids: vec![page.id] },
            Change::NotesDeleted { ids: vec![doomed.id] },
        ]]
    );
}

#[test]
fn moving_announces_the_note_and_the_siblings_that_shifted() {
    let (_dir, store, recorder) = open();
    let a = note(&store, NoteKind::Page, None);
    let b = note(&store, NoteKind::Page, None);
    let c = note(&store, NoteKind::Page, None);
    recorder.take();

    store.move_note(&c.id, a.parent_id.as_deref(), Some(&a.id)).unwrap();

    let batches = recorder.take();
    let [Change::NotesChanged { ids }] = batches[0].as_slice() else { panic!("unexpected {batches:?}") };
    assert_eq!(sorted(ids.clone()), sorted(vec![a.id, b.id, c.id]));
}

#[test]
fn board_writes_announce_their_todo_list() {
    let (_dir, store, recorder) = open();
    let list = note(&store, NoteKind::TodoList, None);
    let columns = store.list_columns(&list.id).unwrap();
    recorder.take();
    let board_changed = || vec![vec![Change::BoardChanged { todo_list_id: list.id.clone() }]];

    let task = store
        .create_task(NewTask {
            todo_list_id: list.id.clone(),
            column_id: Some(columns[0].id.clone()),
            ..NewTask::default()
        })
        .unwrap();
    assert_eq!(recorder.take(), board_changed());

    store.move_task(&task.id, Some(&columns[1].id), None, None).unwrap();
    assert_eq!(recorder.take(), board_changed());

    let comment = store.create_comment(&task.id, "hi").unwrap();
    assert_eq!(recorder.take(), board_changed());

    store.delete_comment(&comment.id).unwrap();
    assert_eq!(recorder.take(), board_changed());

    store.delete_column(&columns[1].id).unwrap();
    assert_eq!(recorder.take(), board_changed());

    store.delete_task(&task.id).unwrap();
    assert_eq!(recorder.take(), board_changed());
}

#[test]
fn a_write_that_fails_announces_nothing() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    recorder.take();

    assert!(store.save_document(&page.id, "not a document", None).is_err());
    assert!(matches!(store.rename_note("missing", "x"), Err(CoreError::NotFound { .. })));
    assert!(recorder.take().is_empty());
}

#[test]
fn reads_and_writes_that_change_nothing_announce_nothing() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    recorder.take();

    store.list_notes().unwrap();
    store.get_document(&page.id).unwrap();
    store.rename_note(&page.id, "   ").unwrap();
    store.move_note(&page.id, Some(&page.id), None).unwrap();
    assert!(recorder.take().is_empty());
}

#[test]
fn unsubscribing_stops_delivery() {
    let dir = tempfile::tempdir().unwrap();
    let store = Store::open(dir.path()).unwrap();
    let kept = Arc::new(Recorder::default());
    let dropped = Arc::new(Recorder::default());
    store.subscribe(kept.clone());
    let subscription = store.subscribe(dropped.clone());

    note(&store, NoteKind::Page, None);
    store.unsubscribe(subscription);
    note(&store, NoteKind::Page, None);

    assert_eq!(kept.take().len(), 2);
    assert_eq!(dropped.take().len(), 1);
}

/// Renames every newly created note from inside the callback.
struct Renamer {
    store: Weak<Store>,
    seen: Mutex<Vec<Vec<Change>>>,
}

impl ChangeObserver for Renamer {
    fn on_changes(&self, changes: Vec<Change>) {
        let first_sighting = {
            let mut seen = self.seen.lock().unwrap();
            seen.push(changes.clone());
            seen.len() == 1
        };
        if let (true, Some(store), Some(Change::NotesChanged { ids })) =
            (first_sighting, self.store.upgrade(), changes.first())
        {
            store.rename_note(&ids[0], "renamed by observer").unwrap();
        }
    }
}

#[test]
fn an_observer_may_write_from_inside_its_callback() {
    let dir = tempfile::tempdir().unwrap();
    let store = Arc::new(Store::open(dir.path()).unwrap());
    let renamer = Arc::new(Renamer { store: Arc::downgrade(&store), seen: Mutex::default() });
    store.subscribe(renamer.clone());

    let page = note(&store, NoteKind::Page, None);

    assert_eq!(store.get_note(&page.id).unwrap().unwrap().title, "renamed by observer");
    let seen = renamer.seen.lock().unwrap();
    let expected = vec![Change::NotesChanged { ids: vec![page.id.clone()] }];
    assert_eq!(*seen, vec![expected.clone(), expected], "the create, then the observer's own rename");
}

#[test]
fn writes_from_many_threads_are_announced_in_commit_order() {
    let (_dir, store, recorder) = open();
    let page = note(&store, NoteKind::Page, None);
    recorder.take();

    let writers: Vec<_> = (0..4)
        .map(|writer| {
            let store = Arc::clone(&store);
            let id = page.id.clone();
            std::thread::spawn(move || {
                for save in 0..25 {
                    // Each save differs from the one before it, whichever
                    // writer made that one, so each is a real change.
                    store.save_document(&id, &doc(&format!("writer {writer} save {save}")), None).unwrap();
                }
            })
        })
        .collect();
    for writer in writers {
        writer.join().unwrap();
    }

    let revisions: Vec<i64> = recorder
        .take()
        .into_iter()
        .flatten()
        .filter_map(|change| match change {
            Change::DocumentChanged { revision, .. } => Some(revision),
            _ => None,
        })
        .collect();
    assert_eq!(revisions, (1..=100).collect::<Vec<i64>>());
}
