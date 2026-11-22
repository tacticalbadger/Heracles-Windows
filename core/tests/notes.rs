//! Notes and documents: the filing-cabinet rules, the trash, and moving.

use heracles_core::{CoreError, NewNote, Note, NoteKind, Store};
use serde_json::{Value, json};
use tempfile::TempDir;

fn open() -> (TempDir, Store) {
    let dir = tempfile::tempdir().unwrap();
    let store = Store::open(dir.path()).unwrap();
    (dir, store)
}

fn notebook(store: &Store) -> Note {
    let mut notebooks: Vec<Note> = store
        .list_notes()
        .unwrap()
        .into_iter()
        .filter(|note| note.kind == NoteKind::Notebook)
        .collect();
    notebooks.sort_by_key(|note| note.position);
    notebooks.remove(0)
}

fn add(store: &Store, kind: NoteKind, title: &str, parent: &Note) -> Note {
    store
        .create_note(NewNote {
            kind: Some(kind),
            title: Some(title.to_string()),
            parent_id: Some(parent.id.clone()),
            ..NewNote::default()
        })
        .unwrap()
}

fn get(store: &Store, note: &Note) -> Note {
    store.get_note(&note.id).unwrap().expect("note should exist")
}

/// Titles of a parent's children in manual order.
fn children(store: &Store, parent: &Note) -> Vec<String> {
    let mut notes: Vec<Note> = store
        .list_notes()
        .unwrap()
        .into_iter()
        .filter(|note| note.parent_id.as_deref() == Some(parent.id.as_str()))
        .collect();
    notes.sort_by_key(|note| note.position);
    notes.into_iter().map(|note| note.title).collect()
}

fn positions(store: &Store, parent: &Note) -> Vec<i64> {
    let mut positions: Vec<i64> = store
        .list_notes()
        .unwrap()
        .into_iter()
        .filter(|note| note.parent_id.as_deref() == Some(parent.id.as_str()))
        .map(|note| note.position)
        .collect();
    positions.sort();
    positions
}

fn doc_with(text: &str) -> String {
    json!({"type": "doc", "content": [
        {"type": "paragraph", "content": [{"type": "text", "text": text}]}
    ]})
    .to_string()
}

// ─── Opening ────────────────────────────────────────────────────────────────

#[test]
fn a_new_library_has_one_default_notebook() {
    let (_dir, store) = open();
    let notes = store.list_notes().unwrap();
    assert_eq!(notes.len(), 1);
    assert_eq!(notes[0].kind, NoteKind::Notebook);
    assert_eq!(notes[0].title, "Notebook");
    assert_eq!(notes[0].parent_id, None);
}

#[test]
fn reopening_keeps_the_data_and_adds_no_second_notebook() {
    let dir = tempfile::tempdir().unwrap();
    let page = {
        let store = Store::open(dir.path()).unwrap();
        let page = add(&store, NoteKind::Page, "Kept", &notebook(&store));
        store.save_document(&page.id, &doc_with("still here"), None).unwrap();
        page
    };
    let store = Store::open(dir.path()).unwrap();
    let notes = store.list_notes().unwrap();
    assert_eq!(notes.iter().filter(|note| note.kind == NoteKind::Notebook).count(), 1);
    assert_eq!(get(&store, &page).title, "Kept");
    assert_eq!(store.get_document(&page.id).unwrap().unwrap().content, doc_with("still here"));
}

// ─── Creating ───────────────────────────────────────────────────────────────

#[test]
fn a_new_page_defaults_to_untitled_in_the_first_notebook_with_one_empty_paragraph() {
    let (_dir, store) = open();
    let page = store.create_note(NewNote::default()).unwrap();
    assert_eq!(page.kind, NoteKind::Page);
    assert_eq!(page.title, "Untitled");
    assert_eq!(page.parent_id, Some(notebook(&store).id));
    assert!(!page.favorited && !page.archived && !page.trashed);
    assert_eq!(page.created_at, page.updated_at);
    assert_eq!(page.created_at.len(), "2026-01-02T03:04:05.678Z".len());
    assert!(page.created_at.ends_with('Z'));

    let document = store.get_document(&page.id).unwrap().unwrap();
    let content: Value = serde_json::from_str(&document.content).unwrap();
    assert_eq!(content, json!({"type": "doc", "content": [{"type": "paragraph"}]}));
    assert_eq!(document.revision, 0);
}

#[test]
fn each_kind_has_its_own_default_title_and_blank_titles_fall_back_to_it() {
    let (_dir, store) = open();
    let create = |kind, title: &str| {
        store
            .create_note(NewNote { kind: Some(kind), title: Some(title.to_string()), ..NewNote::default() })
            .unwrap()
            .title
    };
    assert_eq!(create(NoteKind::Section, "  "), "New section");
    assert_eq!(create(NoteKind::TodoList, ""), "New todo list");
    assert_eq!(create(NoteKind::Notebook, " "), "New notebook");
    assert_eq!(create(NoteKind::Page, "  Trimmed  "), "Trimmed");
}

#[test]
fn new_notes_go_to_the_end_of_their_parent() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let a = add(&store, NoteKind::Page, "a", &root);
    let b = add(&store, NoteKind::Page, "b", &root);
    let inner = add(&store, NoteKind::Page, "inner", &a);
    assert_eq!((a.position, b.position, inner.position), (0, 1, 0));
}

#[test]
fn a_notebook_is_always_top_level_even_when_given_a_parent() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let second = add(&store, NoteKind::Notebook, "Second", &root);
    assert_eq!(second.parent_id, None);
    assert_eq!(second.position, 1);
}

#[test]
fn creating_under_a_parent_that_does_not_exist_fails_and_stores_nothing() {
    let (_dir, store) = open();
    let result = store.create_note(NewNote { parent_id: Some("missing".into()), ..NewNote::default() });
    assert!(matches!(result, Err(CoreError::NotFound { .. })));
    assert_eq!(store.list_notes().unwrap().len(), 1);
}

#[test]
fn creating_with_content_stores_it_and_rejects_content_that_is_not_a_document() {
    let (_dir, store) = open();
    let page = store
        .create_note(NewNote { content: Some(doc_with("seeded")), ..NewNote::default() })
        .unwrap();
    assert_eq!(store.get_document(&page.id).unwrap().unwrap().content, doc_with("seeded"));

    let bad = store.create_note(NewNote { content: Some("<p>html</p>".into()), ..NewNote::default() });
    assert!(matches!(bad, Err(CoreError::Document(_))));
    assert_eq!(store.list_notes().unwrap().len(), 2, "the failed create must leave no note behind");
}

// ─── Editing ────────────────────────────────────────────────────────────────

#[test]
fn rename_trims_and_a_blank_title_keeps_the_old_one() {
    let (_dir, store) = open();
    let page = add(&store, NoteKind::Page, "Old", &notebook(&store));
    assert_eq!(store.rename_note(&page.id, "  New name ").unwrap().title, "New name");
    assert_eq!(store.rename_note(&page.id, "   ").unwrap().title, "New name");
}

#[test]
fn icon_can_be_set_and_cleared() {
    let (_dir, store) = open();
    let page = add(&store, NoteKind::Page, "P", &notebook(&store));
    assert_eq!(store.set_note_icon(&page.id, Some("🦁")).unwrap().icon.as_deref(), Some("🦁"));
    assert_eq!(store.set_note_icon(&page.id, None).unwrap().icon, None);
}

#[test]
fn favorite_and_archive_flags() {
    let (_dir, store) = open();
    let page = add(&store, NoteKind::Page, "P", &notebook(&store));
    assert!(store.set_note_favorited(&page.id, true).unwrap().favorited);
    assert!(!store.set_note_favorited(&page.id, false).unwrap().favorited);
    assert!(store.set_note_archived(&page.id, true).unwrap().archived);
    assert!(!store.set_note_archived(&page.id, false).unwrap().archived);
}

#[test]
fn changing_a_note_that_does_not_exist_is_a_not_found_error() {
    let (_dir, store) = open();
    assert!(matches!(store.rename_note("nope", "x"), Err(CoreError::NotFound { .. })));
    assert!(matches!(store.trash_note("nope"), Err(CoreError::NotFound { .. })));
    assert!(matches!(store.move_note("nope", None, None), Err(CoreError::NotFound { .. })));
    assert!(matches!(store.save_document("nope", &doc_with("x"), None), Err(CoreError::NotFound { .. })));
    assert!(matches!(store.delete_note("nope"), Err(CoreError::NotFound { .. })));
    assert_eq!(store.get_note("nope").unwrap(), None);
    assert_eq!(store.get_document("nope").unwrap(), None);
}

// ─── Documents ──────────────────────────────────────────────────────────────

#[test]
fn saving_a_document_counts_revisions_and_touches_the_note() {
    let (_dir, store) = open();
    let page = add(&store, NoteKind::Page, "P", &notebook(&store));
    std::thread::sleep(std::time::Duration::from_millis(3));

    let first = store.save_document(&page.id, &doc_with("one"), None).unwrap();
    let second = store.save_document(&page.id, &doc_with("two"), None).unwrap();
    assert_eq!((first.revision, second.revision), (1, 2));
    assert_eq!(store.get_document(&page.id).unwrap().unwrap(), second);
    assert_eq!(second.content, doc_with("two"));
    assert!(get(&store, &page).updated_at > page.updated_at);
}

#[test]
fn saving_content_that_has_not_changed_is_not_an_edit() {
    let (_dir, store) = open();
    let page = add(&store, NoteKind::Page, "P", &notebook(&store));
    let first = store.save_document(&page.id, &doc_with("same"), None).unwrap();
    let touched = get(&store, &page).updated_at;
    std::thread::sleep(std::time::Duration::from_millis(3));

    // An editor may send a document back just for having opened it. The same
    // content with different spacing is still the same content.
    let spaced = doc_with("same").replace(":", " : ").replace(",", " , ");
    for content in [doc_with("same"), spaced] {
        let again = store.save_document(&page.id, &content, None).unwrap();
        assert_eq!(again, first, "nothing new was stored");
    }
    assert_eq!(get(&store, &page).updated_at, touched, "the note must not look edited");

    // A page that has only ever held its first, empty document is the same.
    let fresh = add(&store, NoteKind::Page, "Fresh", &notebook(&store));
    let empty = store.get_document(&fresh.id).unwrap().unwrap();
    assert_eq!(store.save_document(&fresh.id, &empty.content, None).unwrap().revision, 0);
    assert_eq!(get(&store, &fresh).updated_at, fresh.updated_at);
}

#[test]
fn saving_something_that_is_not_a_document_fails_and_keeps_the_old_content() {
    let (_dir, store) = open();
    let page = add(&store, NoteKind::Page, "P", &notebook(&store));
    store.save_document(&page.id, &doc_with("good"), None).unwrap();

    assert!(matches!(store.save_document(&page.id, "<p>html</p>", None), Err(CoreError::Document(_))));
    assert!(matches!(
        store.save_document(&page.id, r#"{"type":"paragraph"}"#, None),
        Err(CoreError::Document(_))
    ));
    let stored = store.get_document(&page.id).unwrap().unwrap();
    assert_eq!((stored.content, stored.revision), (doc_with("good"), 1));
}

#[test]
fn a_note_that_was_never_written_reads_as_an_empty_document() {
    let (_dir, store) = open();
    let section = add(&store, NoteKind::Section, "S", &notebook(&store));
    let document = store.get_document(&section.id).unwrap().unwrap();
    let content: Value = serde_json::from_str(&document.content).unwrap();
    assert_eq!(content, json!({"type": "doc", "content": [{"type": "paragraph"}]}));
    assert_eq!(document.revision, 0);
}

#[test]
fn list_notes_is_ordered_by_most_recent_change() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let a = add(&store, NoteKind::Page, "a", &root);
    std::thread::sleep(std::time::Duration::from_millis(3));
    let b = add(&store, NoteKind::Page, "b", &root);
    std::thread::sleep(std::time::Duration::from_millis(3));
    store.save_document(&a.id, &doc_with("edited"), None).unwrap();

    let order: Vec<String> = store.list_notes().unwrap().into_iter().map(|note| note.id).collect();
    assert_eq!(order, vec![a.id, b.id, root.id]);
}

// ─── Trash ──────────────────────────────────────────────────────────────────

/// notebook > section > page > (child, todo)
fn tree(store: &Store) -> (Note, Note, Note, Note, Note) {
    let root = notebook(store);
    let section = add(store, NoteKind::Section, "S", &root);
    let page = add(store, NoteKind::Page, "P", &section);
    let child = add(store, NoteKind::Page, "C", &page);
    let todo = add(store, NoteKind::TodoList, "T", &page);
    (root, section, page, child, todo)
}

#[test]
fn trash_cascades_down_and_leaves_the_parent_alone() {
    let (_dir, store) = open();
    let (_root, section, page, child, todo) = tree(&store);
    store.set_note_archived(&child.id, true).unwrap();

    assert!(store.trash_note(&page.id).unwrap().trashed);
    assert!(get(&store, &child).trashed);
    assert!(!get(&store, &child).archived, "trashing clears the archived flag");
    assert!(get(&store, &todo).trashed);
    assert!(!get(&store, &section).trashed);
}

#[test]
fn restore_cascades_down() {
    let (_dir, store) = open();
    let (_root, _section, page, child, todo) = tree(&store);
    store.trash_note(&page.id).unwrap();

    assert!(!store.restore_note(&page.id).unwrap().trashed);
    assert!(!get(&store, &child).trashed);
    assert!(!get(&store, &todo).trashed);
    assert_eq!(get(&store, &child).parent_id, Some(page.id));
}

#[test]
fn restoring_a_child_alone_moves_it_to_the_nearest_ancestor_not_in_the_trash() {
    let (_dir, store) = open();
    let (_root, section, page, child, _todo) = tree(&store);
    let neighbour = add(&store, NoteKind::Page, "N", &section);
    store.trash_note(&page.id).unwrap();

    let restored = store.restore_note(&child.id).unwrap();
    assert!(!restored.trashed);
    assert_eq!(restored.parent_id, Some(section.id.clone()));
    assert!(restored.position > neighbour.position, "it lands after the notes already there");
    assert!(get(&store, &page).trashed);
}

#[test]
fn empty_trash_removes_trashed_subtrees_and_reports_every_id() {
    let (_dir, store) = open();
    let (_root, section, page, child, todo) = tree(&store);
    store.save_document(&child.id, &doc_with("survivor"), None).unwrap();
    store.trash_note(&page.id).unwrap();
    store.restore_note(&child.id).unwrap();

    let mut deleted = store.empty_trash().unwrap();
    deleted.sort();
    let mut expected = vec![page.id.clone(), todo.id.clone()];
    expected.sort();
    assert_eq!(deleted, expected);
    assert_eq!(store.get_note(&page.id).unwrap(), None);
    assert_eq!(store.get_note(&todo.id).unwrap(), None);
    assert_eq!(store.get_document(&page.id).unwrap(), None);
    assert_eq!(get(&store, &child).parent_id, Some(section.id));
    assert_eq!(store.get_document(&child.id).unwrap().unwrap().content, doc_with("survivor"));
}

#[test]
fn emptying_a_trash_that_holds_every_notebook_leaves_a_fresh_one() {
    let (_dir, store) = open();
    let root = notebook(&store);
    add(&store, NoteKind::Page, "P", &root);
    store.trash_note(&root.id).unwrap();

    assert_eq!(store.empty_trash().unwrap().len(), 2);
    let notes = store.list_notes().unwrap();
    assert_eq!(notes.len(), 1);
    assert_eq!(notes[0].kind, NoteKind::Notebook);
    assert_ne!(notes[0].id, root.id);
}

// ─── Deleting ───────────────────────────────────────────────────────────────

#[test]
fn deleting_a_note_removes_its_whole_subtree_and_reports_the_ids() {
    let (_dir, store) = open();
    let (_root, section, page, child, todo) = tree(&store);

    let mut deleted = store.delete_note(&page.id).unwrap();
    deleted.sort();
    let mut expected = vec![page.id.clone(), child.id.clone(), todo.id.clone()];
    expected.sort();
    assert_eq!(deleted, expected);
    for note in [&page, &child, &todo] {
        assert_eq!(store.get_note(&note.id).unwrap(), None);
        assert_eq!(store.get_document(&note.id).unwrap(), None);
    }
    assert!(store.get_note(&section.id).unwrap().is_some());
}

#[test]
fn the_last_notebook_cannot_be_deleted() {
    let (_dir, store) = open();
    let root = notebook(&store);
    assert!(matches!(store.delete_note(&root.id), Err(CoreError::LastNotebook)));
    assert!(store.get_note(&root.id).unwrap().is_some());
}

#[test]
fn deleting_a_notebook_hands_its_contents_to_the_first_other_notebook_in_order() {
    let (_dir, store) = open();
    let first = notebook(&store);
    let kept_a = add(&store, NoteKind::Page, "kept a", &first);
    let kept_b = add(&store, NoteKind::Page, "kept b", &first);
    let doomed = add(&store, NoteKind::Notebook, "Doomed", &first);
    let moved_a = add(&store, NoteKind::Page, "moved a", &doomed);
    let moved_b = add(&store, NoteKind::Section, "moved b", &doomed);
    let nested = add(&store, NoteKind::Page, "nested", &moved_b);
    let _third = add(&store, NoteKind::Notebook, "Third", &first);

    assert_eq!(store.delete_note(&doomed.id).unwrap(), vec![doomed.id.clone()]);

    assert_eq!(store.get_note(&doomed.id).unwrap(), None);
    assert_eq!(children(&store, &first), vec!["kept a", "kept b", "moved a", "moved b"]);
    assert_eq!(positions(&store, &first), vec![0, 1, 2, 3], "adopted notes must not share positions");
    assert_eq!(get(&store, &nested).parent_id, Some(moved_b.id.clone()));
    for note in [&kept_a, &kept_b, &moved_a, &moved_b] {
        assert_eq!(get(&store, note).parent_id, Some(first.id.clone()));
    }
}

// ─── Moving ─────────────────────────────────────────────────────────────────

#[test]
fn moving_a_note_into_itself_or_its_own_subtree_changes_nothing() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let a = add(&store, NoteKind::Page, "A", &root);
    let b = add(&store, NoteKind::Page, "B", &a);
    let c = add(&store, NoteKind::Page, "C", &b);

    assert_eq!(store.move_note(&a.id, Some(&a.id), None).unwrap().parent_id, Some(root.id.clone()));
    assert_eq!(store.move_note(&a.id, Some(&b.id), None).unwrap().parent_id, Some(root.id.clone()));
    assert_eq!(store.move_note(&a.id, Some(&c.id), None).unwrap().parent_id, Some(root.id.clone()));
    assert_eq!(get(&store, &b).parent_id, Some(a.id.clone()));
}

#[test]
fn moving_to_a_new_parent_appends_by_default() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let section = add(&store, NoteKind::Section, "S", &root);
    add(&store, NoteKind::Page, "x", &section);
    add(&store, NoteKind::Page, "y", &section);
    let mover = add(&store, NoteKind::Page, "mover", &root);

    let moved = store.move_note(&mover.id, Some(&section.id), None).unwrap();
    assert_eq!(moved.parent_id, Some(section.id.clone()));
    assert_eq!(children(&store, &section), vec!["x", "y", "mover"]);
    assert_eq!(positions(&store, &section), vec![0, 1, 2]);
}

#[test]
fn moving_before_a_sibling_renumbers_the_whole_group() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let a = add(&store, NoteKind::Page, "a", &root);
    let _b = add(&store, NoteKind::Page, "b", &root);
    let c = add(&store, NoteKind::Page, "c", &root);

    store.move_note(&c.id, Some(&root.id), Some(&a.id)).unwrap();
    assert_eq!(children(&store, &root), vec!["c", "a", "b"]);
    assert_eq!(positions(&store, &root), vec![0, 1, 2]);

    // An unknown `before` appends.
    store.move_note(&c.id, Some(&root.id), Some("missing")).unwrap();
    assert_eq!(children(&store, &root), vec!["a", "b", "c"]);
}

#[test]
fn moving_with_no_parent_puts_a_note_in_the_first_notebook_and_reorders_notebooks() {
    let (_dir, store) = open();
    let first = notebook(&store);
    let second = add(&store, NoteKind::Notebook, "Second", &first);
    let page = add(&store, NoteKind::Page, "P", &second);

    assert_eq!(store.move_note(&page.id, None, None).unwrap().parent_id, Some(first.id.clone()));

    let moved = store.move_note(&second.id, Some(&page.id), Some(&first.id)).unwrap();
    assert_eq!(moved.parent_id, None, "a notebook never gets a parent");
    assert_eq!((get(&store, &second).position, get(&store, &first).position), (0, 1));
}

#[test]
fn moving_under_a_parent_that_does_not_exist_fails_and_leaves_the_note_in_place() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let page = add(&store, NoteKind::Page, "P", &root);
    assert!(matches!(store.move_note(&page.id, Some("missing"), None), Err(CoreError::NotFound { .. })));
    assert_eq!(get(&store, &page).parent_id, Some(root.id));
}

// ─── Duplicating ────────────────────────────────────────────────────────────

#[test]
fn duplicating_a_page_copies_its_content_and_puts_the_copy_last() {
    let (_dir, store) = open();
    let root = notebook(&store);
    let page = add(&store, NoteKind::Page, "Plan", &root);
    store.set_note_icon(&page.id, Some("📌")).unwrap();
    store.save_document(&page.id, &doc_with("body"), None).unwrap();
    add(&store, NoteKind::Page, "other", &root);
    let child = add(&store, NoteKind::Page, "child", &page);

    let copy = store.duplicate_note(&page.id).unwrap();
    assert_ne!(copy.id, page.id);
    assert_eq!(copy.title, "Plan (copy)");
    assert_eq!(copy.icon.as_deref(), Some("📌"));
    assert_eq!(copy.parent_id, Some(root.id.clone()));
    assert_eq!(children(&store, &root), vec!["Plan", "other", "Plan (copy)"]);
    assert_eq!(store.get_document(&copy.id).unwrap().unwrap().content, doc_with("body"));
    assert_eq!(get(&store, &child).parent_id, Some(page.id), "child notes are not copied");
    assert_eq!(children(&store, &copy), Vec::<String>::new());
}
