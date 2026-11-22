//! Full-text search over titles and document text.

use heracles_core::{NewNote, Note, Store};
use serde_json::json;
use tempfile::TempDir;

fn open() -> (TempDir, Store) {
    let dir = tempfile::tempdir().unwrap();
    let store = Store::open(dir.path()).unwrap();
    (dir, store)
}

fn doc(paragraphs: &[&str]) -> String {
    let content: Vec<_> = paragraphs
        .iter()
        .map(|text| json!({"type": "paragraph", "content": [{"type": "text", "text": text}]}))
        .collect();
    json!({"type": "doc", "content": content}).to_string()
}

fn page(store: &Store, title: &str, body: &[&str]) -> Note {
    store
        .create_note(NewNote {
            title: Some(title.to_string()),
            content: Some(doc(body)),
            ..NewNote::default()
        })
        .unwrap()
}

fn child(store: &Store, parent: &Note, title: &str, body: &[&str]) -> Note {
    store
        .create_note(NewNote {
            title: Some(title.to_string()),
            parent_id: Some(parent.id.clone()),
            content: Some(doc(body)),
            ..NewNote::default()
        })
        .unwrap()
}

/// Titles of the notes a query finds, best match first.
fn find(store: &Store, query: &str) -> Vec<String> {
    store.search_notes(query, 20).unwrap().into_iter().map(|hit| hit.note.title).collect()
}

#[test]
fn finds_words_in_the_body_and_in_the_title() {
    let (_dir, store) = open();
    page(&store, "Groceries", &["Buy oat milk and bread"]);
    page(&store, "Garden plan", &["Plant tomatoes in May"]);

    assert_eq!(find(&store, "tomatoes"), vec!["Garden plan"]);
    assert_eq!(find(&store, "groceries"), vec!["Groceries"]);
    assert!(find(&store, "zeppelin").is_empty());
}

#[test]
fn every_word_must_match() {
    let (_dir, store) = open();
    page(&store, "A", &["red apples and green pears"]);
    page(&store, "B", &["red bricks"]);

    assert_eq!(find(&store, "red pears"), vec!["A"]);
    assert_eq!(find(&store, "pears red"), vec!["A"]);
}

#[test]
fn each_word_matches_as_a_prefix_so_results_follow_typing() {
    let (_dir, store) = open();
    page(&store, "Garden plan", &["Plant tomatoes"]);

    assert_eq!(find(&store, "tom"), vec!["Garden plan"]);
    assert_eq!(find(&store, "gard pl"), vec!["Garden plan"]);
    assert_eq!(find(&store, "tom gar"), vec!["Garden plan"]);
    assert!(find(&store, "arden").is_empty(), "a word matches from its start, not its middle");
}

#[test]
fn matching_ignores_case_and_accents() {
    let (_dir, store) = open();
    page(&store, "Café Müller", &["Crème brûlée"]);

    assert_eq!(find(&store, "cafe muller"), vec!["Café Müller"]);
    assert_eq!(find(&store, "CREME BRULEE"), vec!["Café Müller"]);
}

#[test]
fn a_title_match_ranks_above_a_body_match() {
    let (_dir, store) = open();
    page(&store, "Meeting notes", &["We talked about the budget at length, budget budget"]);
    page(&store, "Budget", &["Numbers for next year"]);

    assert_eq!(find(&store, "budget"), vec!["Budget", "Meeting notes"]);
}

#[test]
fn notes_in_the_trash_are_left_out_and_archived_notes_are_kept() {
    let (_dir, store) = open();
    let trashed = page(&store, "Trashed", &["findme"]);
    let archived = page(&store, "Archived", &["findme"]);
    page(&store, "Plain", &["findme"]);
    store.trash_note(&trashed.id).unwrap();
    store.set_note_archived(&archived.id, true).unwrap();

    let mut found = find(&store, "findme");
    found.sort();
    assert_eq!(found, vec!["Archived", "Plain"]);

    store.restore_note(&trashed.id).unwrap();
    assert_eq!(find(&store, "findme").len(), 3);
}

#[test]
fn results_follow_edits_and_renames() {
    let (_dir, store) = open();
    let note = page(&store, "Draft", &["first version"]);

    store.save_document(&note.id, &doc(&["second attempt"]), None).unwrap();
    assert!(find(&store, "first").is_empty());
    assert_eq!(find(&store, "attempt"), vec!["Draft"]);

    store.rename_note(&note.id, "Final").unwrap();
    assert!(find(&store, "draft").is_empty());
    assert_eq!(find(&store, "final"), vec!["Final"]);
}

#[test]
fn deleted_notes_and_everything_under_them_stop_matching() {
    let (_dir, store) = open();
    let parent = page(&store, "Parent", &["findme"]);
    let middle = child(&store, &parent, "Middle", &["findme"]);
    child(&store, &middle, "Leaf", &["findme"]);
    page(&store, "Bystander", &["findme"]);

    store.delete_note(&parent.id).unwrap();
    assert_eq!(find(&store, "findme"), vec!["Bystander"]);

    let doomed = page(&store, "Doomed", &["findme"]);
    child(&store, &doomed, "Doomed child", &["findme"]);
    store.trash_note(&doomed.id).unwrap();
    store.empty_trash().unwrap();
    assert_eq!(find(&store, "findme"), vec!["Bystander"]);
}

#[test]
fn code_blocks_and_image_descriptions_are_searchable() {
    let (_dir, store) = open();
    let content = json!({"type": "doc", "content": [
        {"type": "codeMirrorBlock", "attrs": {"language": "rust", "code": "fn frobnicate() {}"}},
        {"type": "image", "attrs": {"src": "https://example.com/a.png", "alt": "wiring diagram", "title": null}},
    ]});
    store
        .create_note(NewNote { title: Some("Tech".into()), content: Some(content.to_string()), ..NewNote::default() })
        .unwrap();

    assert_eq!(find(&store, "frobnicate"), vec!["Tech"]);
    assert_eq!(find(&store, "wiring"), vec!["Tech"]);
    assert!(find(&store, "example").is_empty(), "image URLs are not text");
}

#[test]
fn a_duplicate_is_searchable_like_its_original() {
    let (_dir, store) = open();
    let note = page(&store, "Recipe", &["saffron risotto"]);
    store.duplicate_note(&note.id).unwrap();

    let mut found = find(&store, "saffron");
    found.sort();
    assert_eq!(found, vec!["Recipe", "Recipe (copy)"]);
}

#[test]
fn the_snippet_shows_the_match_in_context() {
    let (_dir, store) = open();
    page(&store, "Trip", &["Pack the tent", "Remember the lantern for the campsite", "Buy fuel"]);

    let hits = store.search_notes("lant", 5).unwrap();
    let snippet = &hits[0].snippet;
    let matched: Vec<&str> = snippet.iter().filter(|part| part.matched).map(|part| part.text.as_str()).collect();
    assert_eq!(matched, vec!["lantern"]);
    let whole: String = snippet.iter().map(|part| part.text.as_str()).collect();
    assert!(whole.contains("tent Remember the lantern for the campsite Buy"), "blocks join with spaces: {whole:?}");
    assert!(snippet.iter().all(|part| !part.text.is_empty()));
}

#[test]
fn a_title_only_match_still_gives_a_snippet_of_the_text() {
    let (_dir, store) = open();
    page(&store, "Lantern", &["Nothing about lights here"]);

    let hits = store.search_notes("lantern", 5).unwrap();
    assert_eq!(hits[0].snippet.len(), 1);
    assert!(!hits[0].snippet[0].matched);
    assert_eq!(hits[0].snippet[0].text, "Nothing about lights here");
}

#[test]
fn the_limit_caps_the_number_of_results() {
    let (_dir, store) = open();
    for index in 0..5 {
        page(&store, &format!("Note {index}"), &["common word"]);
    }
    assert_eq!(store.search_notes("common", 3).unwrap().len(), 3);
    assert_eq!(store.search_notes("common", 0).unwrap().len(), 0);
}

#[test]
fn queries_with_nothing_to_search_for_find_nothing() {
    let (_dir, store) = open();
    page(&store, "Note", &["text"]);
    for query in ["", "   ", "\"", "---", "* ( ) :", "\"\""] {
        assert!(find(&store, query).is_empty(), "query {query:?}");
    }
}

#[test]
fn search_syntax_in_a_query_is_read_as_plain_words() {
    let (_dir, store) = open();
    // A title no query word below is a prefix of.
    page(&store, "Entry", &["cats AND dogs, title: unknown (maybe)"]);

    assert_eq!(find(&store, "cats AND dogs"), vec!["Entry"]);
    assert_eq!(find(&store, "\"cats dogs"), vec!["Entry"]);
    assert_eq!(find(&store, "title:unknown"), vec!["Entry"]);
    assert_eq!(find(&store, "(maybe)"), vec!["Entry"]);
    assert_eq!(find(&store, "cat*"), vec!["Entry"]);
    assert!(find(&store, "cats NOT dogs").is_empty(), "NOT is a word here, and the note lacks it");
    assert!(find(&store, "body:cats").is_empty(), "a column filter is not syntax here");
}
