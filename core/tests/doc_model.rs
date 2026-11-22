//! The document type itself: JSON round trips, search text, and block ids.

use heracles_core::doc::html::{HtmlOptions, html_to_doc};
use heracles_core::doc::{Document, DocumentError};
use serde_json::{Value, json};
use std::collections::HashSet;

fn value(doc: &Document) -> Value {
    serde_json::from_str(&doc.to_json()).unwrap()
}

#[test]
fn empty_document_is_one_empty_paragraph() {
    assert_eq!(
        value(&Document::empty()),
        json!({"type": "doc", "content": [{"type": "paragraph"}]})
    );
}

#[test]
fn json_round_trip_keeps_nodes_and_attributes_the_core_does_not_know() {
    let source = json!({
        "type": "doc",
        "content": [
            {"type": "futureBlock", "attrs": {"shape": "hexagon", "sides": 6, "nested": {"a": [1, 2]}},
             "content": [{"type": "text", "text": "x", "marks": [{"type": "glow", "attrs": {"level": 2}}]}]},
            {"type": "paragraph", "attrs": {"blockId": "p1", "futureAttr": true}},
        ]
    });
    let doc = Document::from_json(&source.to_string()).unwrap();
    assert_eq!(value(&doc), source);
}

#[test]
fn from_json_rejects_a_root_that_is_not_a_doc() {
    let error = Document::from_json(r#"{"type": "paragraph"}"#).unwrap_err();
    assert!(matches!(error, DocumentError::WrongRoot(ref kind) if kind == "paragraph"));
}

#[test]
fn from_json_rejects_text_that_is_not_json() {
    assert!(matches!(
        Document::from_json("<p>old html</p>").unwrap_err(),
        DocumentError::Json(_)
    ));
}

#[test]
fn plain_text_puts_each_block_on_its_own_line() {
    let html = concat!(
        "<h1>Title</h1><p>Hello <strong>bold</strong> world<br>next line</p>",
        "<ul><li><p>one</p></li><li><p>two</p></li></ul>",
        r#"<pre data-code-mirror="true" language="rust" code="fn main() {}"></pre>"#,
        r#"<img src="https://example.com/a.png" alt="A diagram">"#,
        r#"<div data-type="columns"><div data-type="column"><p>left</p></div>"#,
        r#"<div data-type="column"><p>right</p></div></div>"#
    );
    let doc = html_to_doc(html, HtmlOptions::default());
    assert_eq!(
        doc.plain_text(),
        "Title\nHello bold world\nnext line\none\ntwo\nfn main() {}\nA diagram\nleft\nright"
    );
}

#[test]
fn plain_text_of_an_empty_document_is_empty() {
    assert_eq!(Document::empty().plain_text(), "");
}

fn collect_block_ids(node: &Value, out: &mut Vec<(String, Option<String>)>) {
    if let Some(kind) = node["type"].as_str() {
        out.push((kind.to_string(), node["attrs"]["blockId"].as_str().map(String::from)));
    }
    if let Some(children) = node["content"].as_array() {
        for child in children {
            collect_block_ids(child, out);
        }
    }
}

#[test]
fn ensure_block_ids_fills_missing_ids_and_keeps_existing_ones() {
    let html = concat!(
        r#"<p data-block-id="keep-me">a</p><p>b</p><h1>c</h1><blockquote><p>d</p></blockquote>"#,
        r#"<ul><li><p>e</p></li></ul><ul data-type="taskList"><li data-type="taskItem"><p>f</p></li></ul>"#,
        r#"<hr><pre data-code-mirror="true"></pre><div data-callout="true"><p>g</p></div>"#,
        r#"<div data-toggle="true"><p>h</p></div><img src="https://example.com/a.png">"#
    );
    let mut doc = html_to_doc(html, HtmlOptions::default());
    doc.ensure_block_ids();

    let mut seen = Vec::new();
    collect_block_ids(&value(&doc), &mut seen);

    let with_ids = [
        "paragraph", "heading", "blockquote", "listItem", "taskItem", "horizontalRule",
        "codeMirrorBlock", "calloutBlock", "toggleBlock", "image",
    ];
    let without_ids = ["doc", "bulletList", "taskList", "text"];
    for (kind, id) in &seen {
        if with_ids.contains(&kind.as_str()) {
            assert!(id.as_deref().is_some_and(|id| !id.is_empty()), "{kind} should have an id");
        } else if without_ids.contains(&kind.as_str()) {
            assert!(id.is_none(), "{kind} should not have an id");
        } else {
            panic!("unexpected node type {kind}");
        }
    }

    let ids: Vec<&String> = seen.iter().filter_map(|(_, id)| id.as_ref()).collect();
    assert!(ids.contains(&&"keep-me".to_string()));
    assert_eq!(ids.len(), ids.iter().collect::<HashSet<_>>().len(), "ids must be unique");
}

#[test]
fn ensure_block_ids_replaces_a_duplicated_id_on_the_later_block() {
    let mut doc = html_to_doc(
        r#"<p data-block-id="same">a</p><p data-block-id="same">b</p>"#,
        HtmlOptions::default(),
    );
    doc.ensure_block_ids();
    let json = value(&doc);
    let first = json["content"][0]["attrs"]["blockId"].as_str().unwrap();
    let second = json["content"][1]["attrs"]["blockId"].as_str().unwrap();
    assert_eq!(first, "same");
    assert_ne!(second, "same");
    assert!(!second.is_empty());
}

#[test]
fn ensure_block_ids_changes_nothing_when_every_block_has_one() {
    let mut doc = html_to_doc(r#"<p data-block-id="a">x</p><hr data-block-id="b">"#, HtmlOptions::default());
    let before = doc.to_json();
    doc.ensure_block_ids();
    assert_eq!(doc.to_json(), before);
}
