//! The image store, and how documents hand their images to it.

use heracles_core::{CoreError, IMAGE_URL_SCHEME, NewNote, Note, Store};
use serde_json::{Value, json};
use std::path::Path;
use tempfile::TempDir;

/// The smallest PNG there is: one transparent pixel.
const PNG_BASE64: &str =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

fn png_bytes() -> Vec<u8> {
    use base64::Engine;
    base64::engine::general_purpose::STANDARD.decode(PNG_BASE64).unwrap()
}

fn open() -> (TempDir, Store) {
    let dir = tempfile::tempdir().unwrap();
    let store = Store::open(dir.path()).unwrap();
    (dir, store)
}

fn page(store: &Store) -> Note {
    store.create_note(NewNote::default()).unwrap()
}

fn image_doc(src: &str) -> String {
    json!({"type": "doc", "content": [
        {"type": "paragraph", "content": [{"type": "text", "text": "before"}]},
        {"type": "image", "attrs": {"src": src, "alt": "A pixel", "title": null, "blockId": "img-1"}},
    ]})
    .to_string()
}

fn stored_image_attrs(store: &Store, note: &Note) -> Value {
    let content = store.get_document(&note.id).unwrap().unwrap().content;
    let doc: Value = serde_json::from_str(&content).unwrap();
    doc["content"][1]["attrs"].clone()
}

#[test]
fn a_stored_image_is_named_by_the_hash_of_its_bytes() {
    let (dir, store) = open();
    let bytes = png_bytes();
    let image = store.store_image(&bytes, "image/png").unwrap();

    assert_eq!(image.hash.len(), 64);
    assert!(image.hash.chars().all(|ch| matches!(ch, '0'..='9' | 'a'..='f')));
    assert_eq!(image.mime, "image/png");
    assert_eq!(image.byte_count, bytes.len() as i64);
    assert_eq!(image.url, format!("{IMAGE_URL_SCHEME}://local/{}", image.hash));

    let path = store.image_path(&image.hash).unwrap().unwrap();
    assert!(Path::new(&path).starts_with(dir.path()));
    assert_eq!(std::fs::read(&path).unwrap(), bytes);
    assert_eq!(store.image_path(&image.url).unwrap(), Some(path), "the URL finds it too");
    assert_eq!(store.get_image(&image.url).unwrap(), Some(image));
}

#[test]
fn the_hash_is_the_sha256_of_the_bytes() {
    let (_dir, store) = open();
    let image = store.store_image(b"abc", "image/png").unwrap();
    // The SHA-256 test vector for "abc".
    assert_eq!(image.hash, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
}

#[test]
fn storing_the_same_bytes_twice_keeps_one_image() {
    let (dir, store) = open();
    let first = store.store_image(&png_bytes(), "image/png").unwrap();
    let second = store.store_image(&png_bytes(), "image/png").unwrap();
    assert_eq!(first, second);

    let files: Vec<_> = walk(&dir.path().join("images"));
    assert_eq!(files.len(), 1, "found {files:?}");
}

fn walk(dir: &Path) -> Vec<std::path::PathBuf> {
    let mut files = Vec::new();
    for entry in std::fs::read_dir(dir).into_iter().flatten().flatten() {
        if entry.path().is_dir() {
            files.extend(walk(&entry.path()));
        } else {
            files.push(entry.path());
        }
    }
    files
}

#[test]
fn only_images_with_content_can_be_stored() {
    let (_dir, store) = open();
    assert!(matches!(store.store_image(b"<html>", "text/html"), Err(CoreError::Invalid(_))));
    assert!(matches!(store.store_image(b"", "image/png"), Err(CoreError::Invalid(_))));
}

#[test]
fn lookups_that_name_no_image_find_nothing() {
    let (_dir, store) = open();
    store.store_image(&png_bytes(), "image/png").unwrap();
    let unknown = "0".repeat(64);
    for name in [unknown.as_str(), "", "nonsense", "../../etc/passwd", "heracles-image://local/../../x"] {
        assert_eq!(store.image_path(name).unwrap(), None, "lookup {name:?}");
        assert_eq!(store.get_image(name).unwrap(), None, "lookup {name:?}");
    }
}

#[test]
fn saving_a_document_moves_embedded_images_into_the_store() {
    let (_dir, store) = open();
    let note = page(&store);
    let data_url = format!("data:image/png;base64,{PNG_BASE64}");

    let saved = store.save_document(&note.id, &image_doc(&data_url), None).unwrap();

    assert!(!saved.content.contains("base64"), "the bytes must leave the document");
    let attrs = stored_image_attrs(&store, &note);
    let src = attrs["src"].as_str().unwrap();
    assert!(src.starts_with("heracles-image://local/"));
    assert_eq!(attrs["alt"], json!("A pixel"));
    assert_eq!(attrs["blockId"], json!("img-1"));

    let image = store.get_image(src).unwrap().unwrap();
    assert_eq!(image.mime, "image/png");
    assert_eq!(std::fs::read(store.image_path(src).unwrap().unwrap()).unwrap(), png_bytes());
}

#[test]
fn creating_a_note_with_embedded_images_moves_them_too() {
    let (_dir, store) = open();
    let data_url = format!("data:image/png;base64,{PNG_BASE64}");
    let note = store.create_note(NewNote { content: Some(image_doc(&data_url)), ..NewNote::default() }).unwrap();

    let src = stored_image_attrs(&store, &note)["src"].as_str().unwrap().to_string();
    assert!(store.get_image(&src).unwrap().is_some());
}

#[test]
fn images_nested_inside_other_blocks_are_moved() {
    let (_dir, store) = open();
    let note = page(&store);
    let data_url = format!("data:image/png;base64,{PNG_BASE64}");
    let nested = json!({"type": "doc", "content": [
        {"type": "columnBlock", "content": [
            {"type": "column", "attrs": {"width": 1}, "content": [
                {"type": "calloutBlock", "attrs": {"emoji": "💡"}, "content": [
                    {"type": "image", "attrs": {"src": data_url, "alt": null, "title": null}}
                ]}
            ]},
            {"type": "column", "attrs": {"width": 1}, "content": [{"type": "paragraph"}]},
        ]}
    ]});

    let saved = store.save_document(&note.id, &nested.to_string(), None).unwrap();
    assert!(!saved.content.contains("base64"));
    assert!(saved.content.contains("heracles-image://local/"));
}

#[test]
fn a_percent_encoded_data_url_is_moved_as_well() {
    let (_dir, store) = open();
    let note = page(&store);
    let svg = "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%2F%3E";

    store.save_document(&note.id, &image_doc(svg), None).unwrap();

    let src = stored_image_attrs(&store, &note)["src"].as_str().unwrap().to_string();
    let image = store.get_image(&src).unwrap().unwrap();
    assert_eq!(image.mime, "image/svg+xml");
    assert_eq!(
        std::fs::read_to_string(store.image_path(&src).unwrap().unwrap()).unwrap(),
        r#"<svg xmlns="http://www.w3.org/2000/svg"/>"#
    );
}

#[test]
fn sources_that_are_not_embedded_images_are_left_alone() {
    let (_dir, store) = open();
    let note = page(&store);
    for src in ["https://example.com/a.png", "data:image/png;base64,@@not base64@@", "data:image/png;base64,"] {
        store.save_document(&note.id, &image_doc(src), None).unwrap();
        assert_eq!(stored_image_attrs(&store, &note)["src"], json!(src));
    }
}

#[test]
fn unused_images_are_collected_and_used_ones_are_kept() {
    let (_dir, store) = open();
    let note = page(&store);
    let used = store.store_image(&png_bytes(), "image/png").unwrap();
    let unused = store.store_image(b"never placed in a document", "image/png").unwrap();
    store.save_document(&note.id, &image_doc(&used.url), None).unwrap();

    assert!(store.collect_unused_images(3600).unwrap().is_empty(), "too young to collect");
    assert_eq!(store.collect_unused_images(0).unwrap(), vec![unused.hash.clone()]);

    assert_eq!(store.get_image(&unused.hash).unwrap(), None);
    assert_eq!(store.image_path(&unused.hash).unwrap(), None);
    let kept = store.image_path(&used.hash).unwrap().unwrap();
    assert!(Path::new(&kept).exists());
}

#[test]
fn an_image_is_collected_once_no_document_uses_it_any_more() {
    let (_dir, store) = open();
    let note = page(&store);
    let other = page(&store);
    let image = store.store_image(&png_bytes(), "image/png").unwrap();
    store.save_document(&note.id, &image_doc(&image.url), None).unwrap();
    store.save_document(&other.id, &image_doc(&image.url), None).unwrap();

    // Removed from one document, still used by the other.
    store.save_document(&note.id, &image_doc("https://example.com/a.png"), None).unwrap();
    assert!(store.collect_unused_images(0).unwrap().is_empty());

    store.delete_note(&other.id).unwrap();
    assert_eq!(store.collect_unused_images(0).unwrap(), vec![image.hash]);
}

#[test]
fn a_duplicate_keeps_its_images_after_the_original_is_deleted() {
    let (_dir, store) = open();
    let note = page(&store);
    let image = store.store_image(&png_bytes(), "image/png").unwrap();
    store.save_document(&note.id, &image_doc(&image.url), None).unwrap();
    let copy = store.duplicate_note(&note.id).unwrap();

    store.delete_note(&note.id).unwrap();

    assert!(store.collect_unused_images(0).unwrap().is_empty());
    assert_eq!(stored_image_attrs(&store, &copy)["src"], json!(image.url));
}
