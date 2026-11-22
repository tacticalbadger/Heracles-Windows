//! Markdown import and export.

use heracles_core::doc::html::{HtmlOptions, html_to_doc};
use heracles_core::doc::markdown::{doc_to_markdown, markdown_to_doc};
use serde_json::{Value, json};

fn blocks(markdown: &str) -> Value {
    let doc: Value = serde_json::from_str(&markdown_to_doc(markdown).to_json()).unwrap();
    doc["content"].clone()
}

fn text(value: &str) -> Value {
    json!({"type": "text", "text": value})
}

fn paragraph(value: &str) -> Value {
    json!({"type": "paragraph", "content": [text(value)]})
}

/// Markdown written from HTML, for building export cases from the editor's
/// own markup.
fn markdown_of(html: &str) -> String {
    doc_to_markdown(&html_to_doc(html, HtmlOptions::default()))
}

// ─── Import ─────────────────────────────────────────────────────────────────

#[test]
fn headings_and_inline_marks() {
    assert_eq!(
        blocks("# Title\n\nSome **bold**, *italic*, ~~gone~~, `code` and [a link](https://example.com).\n"),
        json!([
            {"type": "heading", "attrs": {"level": 1}, "content": [text("Title")]},
            {"type": "paragraph", "content": [
                text("Some "),
                {"type": "text", "text": "bold", "marks": [{"type": "bold"}]},
                text(", "),
                {"type": "text", "text": "italic", "marks": [{"type": "italic"}]},
                text(", "),
                {"type": "text", "text": "gone", "marks": [{"type": "strike"}]},
                text(", "),
                {"type": "text", "text": "code", "marks": [{"type": "code"}]},
                text(" and "),
                {"type": "text", "text": "a link", "marks": [{"type": "link", "attrs": {
                    "href": "https://example.com", "target": "_blank",
                    "rel": "noopener noreferrer nofollow", "class": null
                }}]},
                text("."),
            ]},
        ])
    );
}

#[test]
fn deep_headings_become_level_four_as_import_always_has() {
    let levels: Vec<i64> = blocks("#### four\n\n##### five\n\n###### six\n")
        .as_array()
        .unwrap()
        .iter()
        .map(|block| block["attrs"]["level"].as_i64().unwrap())
        .collect();
    assert_eq!(levels, vec![4, 4, 4]);
}

#[test]
fn lists_nest_and_ordered_lists_keep_their_start() {
    assert_eq!(
        blocks("- one\n- two\n  - inner\n\n3. three\n4. four\n"),
        json!([
            {"type": "bulletList", "content": [
                {"type": "listItem", "content": [paragraph("one")]},
                {"type": "listItem", "content": [
                    paragraph("two"),
                    {"type": "bulletList", "content": [{"type": "listItem", "content": [paragraph("inner")]}]},
                ]},
            ]},
            {"type": "orderedList", "attrs": {"start": 3}, "content": [
                {"type": "listItem", "content": [paragraph("three")]},
                {"type": "listItem", "content": [paragraph("four")]},
            ]},
        ])
    );
}

#[test]
fn checkbox_items_become_a_task_list() {
    assert_eq!(
        blocks("- [x] done\n- [ ] open\n"),
        json!([{"type": "taskList", "content": [
            {"type": "taskItem", "attrs": {"checked": true}, "content": [paragraph("done")]},
            {"type": "taskItem", "attrs": {"checked": false}, "content": [paragraph("open")]},
        ]}])
    );
}

#[test]
fn fenced_code_keeps_its_language_and_text() {
    assert_eq!(
        blocks("```rust\nfn main() {\n    println!(\"<hi>\");\n}\n```\n\n```\nplain\n```\n"),
        json!([
            {"type": "codeMirrorBlock", "attrs": {"language": "rust", "code": "fn main() {\n    println!(\"<hi>\");\n}\n"}},
            {"type": "codeMirrorBlock", "attrs": {"language": "plaintext", "code": "plain\n"}},
        ])
    );
}

#[test]
fn images_become_image_blocks_not_links() {
    assert_eq!(
        blocks("![A map](https://example.com/map.png \"The map\")\n\nSee ![inline](pics/a.png) here.\n"),
        json!([
            {"type": "image", "attrs": {"src": "https://example.com/map.png", "alt": "A map", "title": "The map"}},
            paragraph("See"),
            {"type": "image", "attrs": {"src": "pics/a.png", "alt": "inline", "title": null}},
            paragraph("here."),
        ])
    );
}

#[test]
fn quotes_rules_and_hard_breaks() {
    assert_eq!(
        blocks("> quoted\n\n---\n\nline one\\\nline two\n"),
        json!([
            {"type": "blockquote", "content": [paragraph("quoted")]},
            {"type": "horizontalRule"},
            {"type": "paragraph", "content": [text("line one"), {"type": "hardBreak"}, text("line two")]},
        ])
    );
}

#[test]
fn tables_keep_their_text() {
    let value = blocks("| a | b |\n|---|---|\n| c | d |\n");
    let texts: Vec<&str> =
        value.as_array().unwrap().iter().map(|block| block["content"][0]["text"].as_str().unwrap()).collect();
    assert_eq!(texts, vec!["a", "b", "c", "d"]);
}

#[test]
fn html_inside_markdown_goes_through_the_same_filter() {
    assert_eq!(
        blocks("before\n\n<script>alert(1)</script>\n\n<div onclick=\"x()\">kept <b>text</b></div>\n"),
        json!([
            paragraph("before"),
            {"type": "paragraph", "content": [text("kept "), {"type": "text", "text": "text", "marks": [{"type": "bold"}]}]},
        ])
    );
}

#[test]
fn empty_markdown_is_an_empty_document() {
    assert_eq!(blocks(""), json!([{"type": "paragraph"}]));
}

// ─── Export ─────────────────────────────────────────────────────────────────

#[test]
fn common_blocks_are_written_in_plain_markdown() {
    let html = concat!(
        "<h2>Plan</h2><p>Pack the <strong>lantern</strong> and <em>rope</em>, not the <s>stove</s>. ",
        r#"Run <code>ls -la</code>. See <a href="https://example.com/a">the site</a>.</p>"#,
        "<blockquote><p>quoted</p><p>twice</p></blockquote><hr><p>line one<br>line two</p>"
    );
    assert_eq!(
        markdown_of(html),
        concat!(
            "## Plan\n\n",
            "Pack the **lantern** and *rope*, not the ~~stove~~. Run `ls -la`. See [the site](https://example.com/a).\n\n",
            "> quoted\n>\n> twice\n\n",
            "---\n\n",
            "line one\\\nline two\n"
        )
    );
}

#[test]
fn lists_are_written_with_their_nesting() {
    let html = concat!(
        "<ul><li><p>one</p></li><li><p>two</p><ol start=\"3\"><li><p>three</p></li><li><p>four</p></li></ol></li></ul>",
        r#"<ul data-type="taskList"><li data-type="taskItem" data-checked="true"><p>done</p></li>"#,
        r#"<li data-type="taskItem" data-checked="false"><p>open</p><p>more</p></li></ul>"#
    );
    assert_eq!(
        markdown_of(html),
        concat!(
            "- one\n",
            "- two\n",
            "  3. three\n",
            "  4. four\n\n",
            // A second list in a row takes the other bullet. With the same
            // one, Markdown would read the two as a single list.
            "* [x] done\n",
            "* [ ] open\n\n",
            "  more\n"
        )
    );
}

#[test]
fn code_blocks_get_a_fence_longer_than_anything_inside() {
    let html = concat!(
        r#"<pre data-code-mirror="true" language="rust" code="fn main() {}"></pre>"#,
        r#"<pre data-code-mirror="true" language="plaintext" code="a ``` b&#10;"></pre>"#
    );
    assert_eq!(markdown_of(html), "```rust\nfn main() {}\n```\n\n````\na ``` b\n````\n");
}

#[test]
fn images_callouts_toggles_and_columns_have_a_markdown_form() {
    let html = concat!(
        r#"<img src="https://example.com/a.png" alt="A map" title="The map"><img src="pics/b c.png">"#,
        r#"<div data-callout="true" data-emoji="🔥"><p>Hot</p><p>Very</p></div>"#,
        r#"<div data-toggle="true"><p>Summary</p><p>Hidden</p></div>"#,
        r#"<div data-type="columns"><div data-type="column"><p>left</p></div><div data-type="column"><p>right</p></div></div>"#
    );
    assert_eq!(
        markdown_of(html),
        concat!(
            "![A map](https://example.com/a.png \"The map\")\n\n",
            "![](<pics/b c.png>)\n\n",
            "> 🔥 Hot\n>\n> Very\n\n",
            "<details>\n<summary>Summary</summary>\n\nHidden\n\n</details>\n\n",
            "left\n\n",
            "right\n"
        )
    );
}

#[test]
fn text_that_looks_like_markdown_is_escaped_so_it_reads_back_the_same() {
    let lines = [
        "# not a heading",
        "2 * 3 * 4 and _under_scores_ and `ticks` and [brackets](x)",
        "- not a list",
        "1. not a numbered list",
        "> not a quote",
        "a <b>tag</b> & an ampersand",
        "back\\slash",
        "+ plus and --- dashes",
    ];
    let html: String = lines.iter().map(|line| format!("<p>{}</p>", line.replace('&', "&amp;").replace('<', "&lt;"))).collect();
    let original = html_to_doc(&html, HtmlOptions::default());

    let reread = markdown_to_doc(&doc_to_markdown(&original));

    assert_eq!(reread.plain_text(), lines.join("\n"));
    assert_eq!(reread.to_json(), original.to_json(), "no stray marks, lists or headings");
}

#[test]
fn marks_keep_their_spaces_outside_the_markers() {
    assert_eq!(markdown_of("<p>a<strong> bold </strong>b <em><strong>both</strong></em></p>"), "a **bold** b ***both***\n");
}

#[test]
fn inline_code_with_backticks_uses_a_longer_marker() {
    assert_eq!(markdown_of("<p><code>a `b` c</code></p>"), "``a `b` c``\n");
}

#[test]
fn a_document_survives_export_and_import() {
    let markdown = concat!(
        "# Trip\n\n",
        "Pack the **lantern**.\n\n",
        "- one\n",
        "- two\n",
        "  - inner\n\n",
        "* [x] done\n",
        "* [ ] open\n\n",
        "```rust\nfn main() {}\n```\n\n",
        "> quoted\n\n",
        "![A map](https://example.com/a.png)\n"
    );
    assert_eq!(doc_to_markdown(&markdown_to_doc(markdown)), markdown);
}

#[test]
fn an_empty_document_exports_as_nothing() {
    assert_eq!(markdown_of(""), "");
    assert_eq!(markdown_of("<p></p><p></p>"), "");
}
