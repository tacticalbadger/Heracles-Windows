//! Importing files as notes and exporting notes as files.

use heracles_core::{CoreError, NewNote, NoteKind, Store, html_to_document};
use serde_json::{Value, json};
use std::path::{Path, PathBuf};
use tempfile::TempDir;

struct Setup {
    _dir: TempDir,
    store: Store,
    /// Where the files to import, and the exports, live.
    files: PathBuf,
}

fn setup() -> Setup {
    let dir = tempfile::tempdir().unwrap();
    let store = Store::open(dir.path().join("library")).unwrap();
    let files = dir.path().join("files");
    std::fs::create_dir_all(&files).unwrap();
    Setup { _dir: dir, store, files }
}

fn write(path: &Path, content: impl AsRef<[u8]>) -> String {
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(path, content).unwrap();
    path.to_string_lossy().into_owned()
}

fn blocks(store: &Store, note_id: &str) -> Value {
    let content = store.get_document(note_id).unwrap().unwrap().content;
    let doc: Value = serde_json::from_str(&content).unwrap();
    doc["content"].clone()
}

const PNG: &[u8] = b"\x89PNG\r\n\x1a\nnot really a picture";

// ─── Import ─────────────────────────────────────────────────────────────────

#[test]
fn a_markdown_file_becomes_a_page_named_for_the_file() {
    let setup = setup();
    let section = setup
        .store
        .create_note(NewNote { kind: Some(NoteKind::Section), ..NewNote::default() })
        .unwrap();
    let path = write(&setup.files.join("Trip plan.md"), "# Packing\n\n- [x] lantern\n");

    let note = setup.store.import_file(&path, Some(&section.id)).unwrap();

    assert_eq!(note.title, "Trip plan");
    assert_eq!(note.kind, NoteKind::Page);
    assert_eq!(note.parent_id, Some(section.id));
    let content = blocks(&setup.store, &note.id);
    assert_eq!(content[0]["type"], json!("heading"));
    assert_eq!(content[1]["type"], json!("taskList"));
    assert!(content[0]["attrs"]["blockId"].is_string(), "imported blocks get ids, so they can be linked to");
    assert_eq!(setup.store.search_notes("lantern", 5).unwrap()[0].note.id, note.id);
}

#[test]
fn an_html_file_is_read_as_html_and_filtered() {
    let setup = setup();
    let path = write(
        &setup.files.join("page.HTML"),
        "<html><body><h6>Deep</h6><p>Text</p><script>alert(1)</script></body></html>",
    );

    let note = setup.store.import_file(&path, None).unwrap();

    assert_eq!(note.title, "page");
    let notebook = setup.store.list_notes().unwrap().into_iter().find(|n| n.kind == NoteKind::Notebook).unwrap();
    assert_eq!(note.parent_id, Some(notebook.id), "with no parent given it goes in the first notebook");
    let content = blocks(&setup.store, &note.id);
    assert_eq!(content.as_array().unwrap().len(), 2);
    assert_eq!(content[0]["attrs"]["level"], json!(4));
}

#[test]
fn images_beside_the_file_come_into_the_image_store() {
    let setup = setup();
    write(&setup.files.join("pics/map one.png"), PNG);
    write(&setup.files.join("abs.png"), b"another picture");
    let on_disk = setup.files.join("abs.png").to_string_lossy().replace('\\', "/");
    let absolute = format!("file:///{}", on_disk.trim_start_matches('/'));
    let markdown = format!(
        "![relative](pics/map%20one.png)\n\n![absolute]({absolute})\n\n![remote](https://example.com/a.png)\n\n![missing](pics/nope.png)\n"
    );
    let path = write(&setup.files.join("note.md"), markdown);

    let note = setup.store.import_file(&path, None).unwrap();

    let content = blocks(&setup.store, &note.id);
    let sources: Vec<&str> =
        content.as_array().unwrap().iter().map(|block| block["attrs"]["src"].as_str().unwrap()).collect();
    assert!(sources[0].starts_with("heracles-image://local/"), "{sources:?}");
    assert!(sources[1].starts_with("heracles-image://local/"), "{sources:?}");
    assert_eq!(sources[2], "https://example.com/a.png");
    assert_eq!(sources[3], "pics/nope.png", "a picture that is not there is left as written");

    let stored = setup.store.get_image(sources[0]).unwrap().unwrap();
    assert_eq!(stored.mime, "image/png");
    assert_eq!(std::fs::read(setup.store.image_path(sources[0]).unwrap().unwrap()).unwrap(), PNG);
    assert!(setup.store.collect_unused_images(0).unwrap().is_empty(), "imported images are in use");
}

#[test]
fn only_image_files_are_ever_read_in() {
    let setup = setup();
    write(&setup.files.join("secret.txt"), "not a picture");
    let path = write(&setup.files.join("note.md"), "![x](secret.txt)\n\n![y](../library/heracles.db)\n");

    let note = setup.store.import_file(&path, None).unwrap();

    let content = blocks(&setup.store, &note.id);
    assert_eq!(content[0]["attrs"]["src"], json!("secret.txt"));
    assert_eq!(content[1]["attrs"]["src"], json!("../library/heracles.db"));
    assert!(setup.store.collect_unused_images(0).unwrap().is_empty(), "nothing was stored");
}

#[test]
fn importing_a_file_that_is_not_there_fails_and_creates_nothing() {
    let setup = setup();
    let missing = setup.files.join("nope.md");
    assert!(matches!(setup.store.import_file(&missing.to_string_lossy(), None), Err(CoreError::Io(_))));
    assert_eq!(setup.store.list_notes().unwrap().len(), 1);
}

#[test]
fn a_file_with_no_name_to_speak_of_is_untitled() {
    let setup = setup();
    let path = write(&setup.files.join(".md"), "text\n");
    assert_eq!(setup.store.import_file(&path, None).unwrap().title, "Untitled");
}

// ─── Export ─────────────────────────────────────────────────────────────────

#[test]
fn a_note_is_written_as_markdown() {
    let setup = setup();
    let content = json!({"type": "doc", "content": [
        {"type": "heading", "attrs": {"level": 1}, "content": [{"type": "text", "text": "Packing"}]},
        {"type": "paragraph", "content": [{"type": "text", "text": "Bring the lantern."}]},
    ]});
    let note = setup
        .store
        .create_note(NewNote { title: Some("Trip".into()), content: Some(content.to_string()), ..NewNote::default() })
        .unwrap();
    let destination = setup.files.join("out/Trip.md");

    setup.store.export_markdown(&note.id, &destination.to_string_lossy()).unwrap();

    assert_eq!(std::fs::read_to_string(&destination).unwrap(), "# Packing\n\nBring the lantern.\n");
    assert!(!setup.files.join("out/Trip.assets").exists(), "no folder for pictures when there are none");
}

#[test]
fn exported_pictures_are_copied_beside_the_file() {
    let setup = setup();
    let image = setup.store.store_image(PNG, "image/png").unwrap();
    let content = json!({"type": "doc", "content": [
        {"type": "image", "attrs": {"src": image.url, "alt": "Map", "title": null}},
        {"type": "image", "attrs": {"src": "https://example.com/a.png", "alt": null, "title": null}},
    ]});
    let note = setup.store.create_note(NewNote { content: Some(content.to_string()), ..NewNote::default() }).unwrap();
    let destination = setup.files.join("My trip.md");

    setup.store.export_markdown(&note.id, &destination.to_string_lossy()).unwrap();

    let copied = setup.files.join("My trip.assets").join(format!("{}.png", image.hash));
    assert_eq!(std::fs::read(&copied).unwrap(), PNG);
    assert_eq!(
        std::fs::read_to_string(&destination).unwrap(),
        format!("![Map](<My trip.assets/{}.png>)\n\n![](https://example.com/a.png)\n", image.hash)
    );
}

#[test]
fn an_exported_note_imports_back_with_its_text_and_pictures() {
    let setup = setup();
    let image = setup.store.store_image(PNG, "image/png").unwrap();
    let content = json!({"type": "doc", "content": [
        {"type": "paragraph", "content": [{"type": "text", "text": "Before the map"}]},
        {"type": "image", "attrs": {"src": image.url, "alt": "Map", "title": null}},
        {"type": "taskList", "content": [{"type": "taskItem", "attrs": {"checked": true}, "content": [
            {"type": "paragraph", "content": [{"type": "text", "text": "done"}]}
        ]}]},
    ]});
    let note = setup.store.create_note(NewNote { content: Some(content.to_string()), ..NewNote::default() }).unwrap();
    let destination = setup.files.join("Round trip.md");
    setup.store.export_markdown(&note.id, &destination.to_string_lossy()).unwrap();

    let back = setup.store.import_file(&destination.to_string_lossy(), None).unwrap();

    assert_eq!(back.title, "Round trip");
    let imported = blocks(&setup.store, &back.id);
    assert_eq!(imported[0]["content"][0]["text"], json!("Before the map"));
    assert_eq!(imported[1]["attrs"]["src"], json!(image.url), "the same bytes are the same stored image");
    assert_eq!(imported[2]["content"][0]["attrs"]["checked"], json!(true));
}

#[test]
fn exporting_a_note_that_does_not_exist_fails_and_writes_nothing() {
    let setup = setup();
    let destination = setup.files.join("nothing.md");
    assert!(matches!(
        setup.store.export_markdown("missing", &destination.to_string_lossy()),
        Err(CoreError::NotFound { .. })
    ));
    assert!(!destination.exists());
}

// ─── Paste ──────────────────────────────────────────────────────────────────

#[test]
fn pasted_html_is_converted_by_the_import_rules() {
    let json: Value = serde_json::from_str(&html_to_document(
        r#"<h6 onclick="x()">Deep</h6><p>Hi <a href="javascript:alert(1)">there</a></p><script>alert(1)</script>"#,
    ))
    .unwrap();
    assert_eq!(
        json,
        json!({"type": "doc", "content": [
            {"type": "heading", "attrs": {"level": 4}, "content": [{"type": "text", "text": "Deep"}]},
            {"type": "paragraph", "content": [{"type": "text", "text": "Hi there"}]},
        ]})
    );
}
