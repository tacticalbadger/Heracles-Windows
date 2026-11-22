//! HTML to document conversion: the rules that carry stored notes and
//! imported files into the block model.

use heracles_core::doc::html::{HtmlOptions, html_to_doc};
use serde_json::{Value, json};

fn convert(html: &str) -> Value {
    serde_json::from_str(&html_to_doc(html, HtmlOptions::default()).to_json()).unwrap()
}

fn blocks(html: &str) -> Value {
    convert(html)["content"].clone()
}

const LINK_REL: &str = "noopener noreferrer nofollow";

#[test]
fn empty_input_is_one_empty_paragraph() {
    assert_eq!(convert(""), json!({"type": "doc", "content": [{"type": "paragraph"}]}));
    assert_eq!(convert("   \n "), json!({"type": "doc", "content": [{"type": "paragraph"}]}));
}

#[test]
fn empty_paragraph_is_kept_as_a_blank_line() {
    assert_eq!(
        blocks("<p>a</p><p></p><p>b</p>"),
        json!([
            {"type": "paragraph", "content": [{"type": "text", "text": "a"}]},
            {"type": "paragraph"},
            {"type": "paragraph", "content": [{"type": "text", "text": "b"}]},
        ])
    );
}

#[test]
fn paragraph_keeps_block_id_and_marks() {
    assert_eq!(
        blocks(r#"<p data-block-id="b1">Hello <strong>bold</strong> <em>it</em> <s>gone</s></p>"#),
        json!([{
            "type": "paragraph",
            "attrs": {"blockId": "b1"},
            "content": [
                {"type": "text", "text": "Hello "},
                {"type": "text", "text": "bold", "marks": [{"type": "bold"}]},
                {"type": "text", "text": " "},
                {"type": "text", "text": "it", "marks": [{"type": "italic"}]},
                {"type": "text", "text": " "},
                {"type": "text", "text": "gone", "marks": [{"type": "strike"}]},
            ]
        }])
    );
}

#[test]
fn headings_keep_their_level() {
    assert_eq!(
        blocks("<h2>Title</h2><h6>Deep</h6>"),
        json!([
            {"type": "heading", "attrs": {"level": 2}, "content": [{"type": "text", "text": "Title"}]},
            {"type": "heading", "attrs": {"level": 6}, "content": [{"type": "text", "text": "Deep"}]},
        ])
    );
}

#[test]
fn import_option_collapses_deep_headings_to_level_four() {
    let doc = html_to_doc("<h5>a</h5><h6>b</h6><h3>c</h3>", HtmlOptions { collapse_deep_headings: true });
    let value: Value = serde_json::from_str(&doc.to_json()).unwrap();
    let levels: Vec<i64> = value["content"]
        .as_array()
        .unwrap()
        .iter()
        .map(|block| block["attrs"]["level"].as_i64().unwrap())
        .collect();
    assert_eq!(levels, vec![4, 4, 3]);
}

#[test]
fn nested_bullet_list() {
    assert_eq!(
        blocks(r#"<ul><li data-block-id="i1"><p>a</p></li><li><p>b</p><ul><li><p>c</p></li></ul></li></ul>"#),
        json!([{
            "type": "bulletList",
            "content": [
                {"type": "listItem", "attrs": {"blockId": "i1"}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "a"}]}
                ]},
                {"type": "listItem", "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "b"}]},
                    {"type": "bulletList", "content": [
                        {"type": "listItem", "content": [
                            {"type": "paragraph", "content": [{"type": "text", "text": "c"}]}
                        ]}
                    ]}
                ]},
            ]
        }])
    );
}

#[test]
fn ordered_list_keeps_start_and_wraps_bare_text() {
    assert_eq!(
        blocks(r#"<ol start="3"><li>x</li></ol>"#),
        json!([{
            "type": "orderedList",
            "attrs": {"start": 3},
            "content": [{"type": "listItem", "content": [
                {"type": "paragraph", "content": [{"type": "text", "text": "x"}]}
            ]}]
        }])
    );
}

#[test]
fn list_item_that_starts_with_a_list_gets_a_leading_paragraph() {
    assert_eq!(
        blocks("<ul><li><ul><li>inner</li></ul></li></ul>"),
        json!([{
            "type": "bulletList",
            "content": [{"type": "listItem", "content": [
                {"type": "paragraph"},
                {"type": "bulletList", "content": [{"type": "listItem", "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "inner"}]}
                ]}]}
            ]}]
        }])
    );
}

#[test]
fn stored_task_list_markup() {
    let html = concat!(
        r#"<ul data-type="taskList">"#,
        r#"<li data-block-id="t1" data-checked="true" data-type="taskItem">"#,
        r#"<label><input type="checkbox" checked="checked"><span></span></label><div><p>done</p></div></li>"#,
        r#"<li data-checked="false" data-type="taskItem"><label><input type="checkbox"><span></span></label>"#,
        r#"<div><p>open</p></div></li></ul>"#
    );
    assert_eq!(
        blocks(html),
        json!([{
            "type": "taskList",
            "content": [
                {"type": "taskItem", "attrs": {"checked": true, "blockId": "t1"}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "done"}]}
                ]},
                {"type": "taskItem", "attrs": {"checked": false}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "open"}]}
                ]},
            ]
        }])
    );
}

#[test]
fn checkbox_list_items_become_a_task_list() {
    assert_eq!(
        blocks(r#"<ul><li><input type="checkbox" checked> a</li><li><input type="checkbox"> b</li></ul>"#),
        json!([{
            "type": "taskList",
            "content": [
                {"type": "taskItem", "attrs": {"checked": true}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "a"}]}
                ]},
                {"type": "taskItem", "attrs": {"checked": false}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "b"}]}
                ]},
            ]
        }])
    );
}

#[test]
fn stored_code_block_reads_language_and_code_attributes() {
    let html = concat!(
        r#"<pre language="rust" code="fn main() {&#10;    println!(&quot;hi&quot;);&#10;}" "#,
        r#"data-block-id="c1" data-code-mirror="true" data-language="rust" data-code="ignored"></pre>"#
    );
    assert_eq!(
        blocks(html),
        json!([{
            "type": "codeMirrorBlock",
            "attrs": {
                "language": "rust",
                "code": "fn main() {\n    println!(\"hi\");\n}",
                "blockId": "c1"
            }
        }])
    );
}

#[test]
fn plain_pre_becomes_a_code_block_with_the_sniffed_language() {
    assert_eq!(
        blocks("<pre><code class=\"hljs language-python\">print(1)\n  x = 2\n</code></pre><pre>raw  text</pre>"),
        json!([
            {"type": "codeMirrorBlock", "attrs": {"language": "python", "code": "print(1)\n  x = 2\n"}},
            {"type": "codeMirrorBlock", "attrs": {"language": "plaintext", "code": "raw  text"}},
        ])
    );
}

#[test]
fn callout_and_toggle_blocks() {
    let html = concat!(
        r#"<div data-emoji="🔥" data-block-id="k1" data-callout="true" class="callout-block"><p>hot</p></div>"#,
        r#"<div data-callout="true"><p>default</p></div>"#,
        r#"<div data-open="false" data-toggle="true" class="toggle-block"><p>summary</p><p>body</p></div>"#,
        r#"<div data-toggle="true"></div>"#
    );
    assert_eq!(
        blocks(html),
        json!([
            {"type": "calloutBlock", "attrs": {"emoji": "🔥", "blockId": "k1"}, "content": [
                {"type": "paragraph", "content": [{"type": "text", "text": "hot"}]}
            ]},
            {"type": "calloutBlock", "attrs": {"emoji": "💡"}, "content": [
                {"type": "paragraph", "content": [{"type": "text", "text": "default"}]}
            ]},
            {"type": "toggleBlock", "attrs": {"open": false}, "content": [
                {"type": "paragraph", "content": [{"type": "text", "text": "summary"}]},
                {"type": "paragraph", "content": [{"type": "text", "text": "body"}]},
            ]},
            {"type": "toggleBlock", "attrs": {"open": true}, "content": [{"type": "paragraph"}]},
        ])
    );
}

#[test]
fn columns_keep_their_widths() {
    let html = concat!(
        r#"<div data-type="columns" class="columns">"#,
        r#"<div data-width="2" data-type="column" class="column"><p>a</p></div>"#,
        r#"<div data-width="nonsense" data-type="column" class="column"><p>b</p></div></div>"#
    );
    assert_eq!(
        blocks(html),
        json!([{
            "type": "columnBlock",
            "content": [
                {"type": "column", "attrs": {"width": 2.0}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "a"}]}
                ]},
                {"type": "column", "attrs": {"width": 1.0}, "content": [
                    {"type": "paragraph", "content": [{"type": "text", "text": "b"}]}
                ]},
            ]
        }])
    );
}

#[test]
fn a_lone_column_is_unwrapped() {
    let html = concat!(
        r#"<div data-type="columns"><div data-type="column"><p>only</p></div>"#,
        r#"<div data-type="column"></div></div>"#
    );
    assert_eq!(
        blocks(html),
        json!([{"type": "paragraph", "content": [{"type": "text", "text": "only"}]}])
    );
}

#[test]
fn image_block_keeps_source_alt_and_title() {
    assert_eq!(
        blocks(r#"<img src="https://example.com/a.png" alt="A" data-block-id="m1" class="editor-image" onerror="x()">"#),
        json!([{
            "type": "image",
            "attrs": {"src": "https://example.com/a.png", "alt": "A", "title": null, "blockId": "m1"}
        }])
    );
}

#[test]
fn data_url_image_source_is_preserved_for_the_image_store() {
    let value = blocks(r#"<img src="data:image/png;base64,AAAA">"#);
    assert_eq!(value[0]["attrs"]["src"], json!("data:image/png;base64,AAAA"));
}

#[test]
fn image_inside_a_paragraph_splits_it() {
    assert_eq!(
        blocks(r#"<p data-block-id="p1">before <img src="https://example.com/a.png"> after</p>"#),
        json!([
            {"type": "paragraph", "attrs": {"blockId": "p1"}, "content": [{"type": "text", "text": "before"}]},
            {"type": "image", "attrs": {"src": "https://example.com/a.png", "alt": null, "title": null}},
            {"type": "paragraph", "content": [{"type": "text", "text": "after"}]},
        ])
    );
}

#[test]
fn images_with_unsafe_or_missing_sources_are_dropped() {
    assert_eq!(
        blocks(r#"<p>x</p><img src="javascript:alert(1)"><img><img src="data:text/html;base64,AAAA">"#),
        json!([{"type": "paragraph", "content": [{"type": "text", "text": "x"}]}])
    );
}

#[test]
fn horizontal_rule_and_hard_break() {
    assert_eq!(
        blocks(r#"<p>a<br>b</p><hr data-block-id="h1">"#),
        json!([
            {"type": "paragraph", "content": [
                {"type": "text", "text": "a"},
                {"type": "hardBreak"},
                {"type": "text", "text": "b"},
            ]},
            {"type": "horizontalRule", "attrs": {"blockId": "h1"}},
        ])
    );
}

#[test]
fn link_mark_uses_the_editor_defaults() {
    assert_eq!(
        blocks(r#"<p><a href="https://example.com/x" target="_self" class="evil" onclick="x()">link</a></p>"#),
        json!([{"type": "paragraph", "content": [{
            "type": "text",
            "text": "link",
            "marks": [{"type": "link", "attrs": {
                "href": "https://example.com/x", "target": "_blank", "rel": LINK_REL, "class": null
            }}]
        }]}])
    );
}

#[test]
fn links_with_unsafe_targets_keep_their_text_only() {
    assert_eq!(
        blocks(r#"<p><a href="javascript:alert(1)">a</a><a href=" JaVaScRiPt:alert(1)">b</a><a href="vbscript:x">c</a></p>"#),
        json!([{"type": "paragraph", "content": [{"type": "text", "text": "abc"}]}])
    );
}

#[test]
fn app_links_and_relative_links_are_kept() {
    let value = blocks(r#"<p><a href="heracles://note/abc">n</a> <a href="/docs/page">r</a> <a href="mailto:a@b.c">m</a></p>"#);
    let hrefs: Vec<&str> = value[0]["content"]
        .as_array()
        .unwrap()
        .iter()
        .filter_map(|node| node["marks"][0]["attrs"]["href"].as_str())
        .collect();
    assert_eq!(hrefs, vec!["heracles://note/abc", "/docs/page", "mailto:a@b.c"]);
}

#[test]
fn scripts_styles_frames_and_event_handlers_do_not_survive() {
    let html = concat!(
        r#"<p onclick="steal()" style="color:red">hi</p><script>alert(1)</script>"#,
        r#"<style>p{}</style><iframe src="https://example.com"></iframe><object data="x"></object>"#,
        r#"<embed src="x"><noscript>no</noscript>"#
    );
    assert_eq!(
        blocks(html),
        json!([{"type": "paragraph", "content": [{"type": "text", "text": "hi"}]}])
    );
}

#[test]
fn whitespace_is_collapsed_like_a_browser_would() {
    assert_eq!(
        blocks("<p>  a \n\t b  <strong> c </strong>  </p>\n\n<p>d</p>\n"),
        json!([
            {"type": "paragraph", "content": [
                {"type": "text", "text": "a b "},
                {"type": "text", "text": "c", "marks": [{"type": "bold"}]},
            ]},
            {"type": "paragraph", "content": [{"type": "text", "text": "d"}]},
        ])
    );
}

#[test]
fn non_breaking_spaces_and_entities_are_kept() {
    assert_eq!(
        blocks("<p>a&nbsp;&nbsp;b &amp; c &lt;tag&gt;</p>"),
        json!([{"type": "paragraph", "content": [{"type": "text", "text": "a\u{a0}\u{a0}b & c <tag>"}]}])
    );
}

#[test]
fn bare_text_and_inline_elements_are_wrapped_in_a_paragraph() {
    assert_eq!(
        blocks("hello <b>w</b>"),
        json!([{"type": "paragraph", "content": [
            {"type": "text", "text": "hello "},
            {"type": "text", "text": "w", "marks": [{"type": "bold"}]},
        ]}])
    );
}

#[test]
fn generic_containers_become_paragraph_boundaries() {
    assert_eq!(
        blocks("<section><div>a</div><div>b</div>c<article><p>d</p></article></section>"),
        json!([
            {"type": "paragraph", "content": [{"type": "text", "text": "a"}]},
            {"type": "paragraph", "content": [{"type": "text", "text": "b"}]},
            {"type": "paragraph", "content": [{"type": "text", "text": "c"}]},
            {"type": "paragraph", "content": [{"type": "text", "text": "d"}]},
        ])
    );
}

#[test]
fn tables_flatten_to_one_paragraph_per_cell() {
    assert_eq!(
        blocks("<table><thead><tr><th>h</th></tr></thead><tbody><tr><td>a</td><td>b</td></tr></tbody></table>"),
        json!([
            {"type": "paragraph", "content": [{"type": "text", "text": "h"}]},
            {"type": "paragraph", "content": [{"type": "text", "text": "a"}]},
            {"type": "paragraph", "content": [{"type": "text", "text": "b"}]},
        ])
    );
}

#[test]
fn code_mark_excludes_every_other_mark() {
    assert_eq!(
        blocks(r#"<p><strong><code>x</code></strong><code><em>y</em></code><a href="https://e.com"><code>z</code></a></p>"#),
        json!([{"type": "paragraph", "content": [
            {"type": "text", "text": "xyz", "marks": [{"type": "code"}]}
        ]}])
    );
}

#[test]
fn adjacent_runs_with_the_same_marks_merge() {
    assert_eq!(
        blocks("<p><b>a</b><b>b</b><i>c</i></p>"),
        json!([{"type": "paragraph", "content": [
            {"type": "text", "text": "ab", "marks": [{"type": "bold"}]},
            {"type": "text", "text": "c", "marks": [{"type": "italic"}]},
        ]}])
    );
}

#[test]
fn marks_are_ordered_as_the_editor_orders_them() {
    assert_eq!(
        blocks(r#"<p><s><em><strong><a href="https://e.com">x</a></strong></em></s></p>"#),
        json!([{"type": "paragraph", "content": [{
            "type": "text",
            "text": "x",
            "marks": [
                {"type": "link", "attrs": {"href": "https://e.com", "target": "_blank", "rel": LINK_REL, "class": null}},
                {"type": "bold"},
                {"type": "italic"},
                {"type": "strike"},
            ]
        }]}])
    );
}

#[test]
fn style_attributes_carry_marks_and_can_cancel_a_bold_wrapper() {
    // Google Docs wraps a whole paste in <b style="font-weight:normal">.
    let html = concat!(
        r#"<b style="font-weight:normal;"><p>plain</p>"#,
        r#"<p><span style="font-weight: 700">heavy</span> "#,
        r#"<span style="font-style:italic; text-decoration: underline line-through">both</span></p></b>"#
    );
    assert_eq!(
        blocks(html),
        json!([
            {"type": "paragraph", "content": [{"type": "text", "text": "plain"}]},
            {"type": "paragraph", "content": [
                {"type": "text", "text": "heavy", "marks": [{"type": "bold"}]},
                {"type": "text", "text": " "},
                {"type": "text", "text": "both", "marks": [{"type": "italic"}, {"type": "strike"}]},
            ]},
        ])
    );
}

#[test]
fn marks_from_an_inline_wrapper_reach_the_blocks_inside_it() {
    assert_eq!(
        blocks("<em><p>a</p><h1>b</h1></em>"),
        json!([
            {"type": "paragraph", "content": [{"type": "text", "text": "a", "marks": [{"type": "italic"}]}]},
            {"type": "heading", "attrs": {"level": 1}, "content": [
                {"type": "text", "text": "b", "marks": [{"type": "italic"}]}
            ]},
        ])
    );
}

#[test]
fn blockquote_holds_blocks() {
    assert_eq!(
        blocks(r#"<blockquote data-block-id="q1">quoted<p>more</p></blockquote><blockquote></blockquote>"#),
        json!([
            {"type": "blockquote", "attrs": {"blockId": "q1"}, "content": [
                {"type": "paragraph", "content": [{"type": "text", "text": "quoted"}]},
                {"type": "paragraph", "content": [{"type": "text", "text": "more"}]},
            ]},
            {"type": "blockquote", "content": [{"type": "paragraph"}]},
        ])
    );
}

#[test]
fn a_full_html_document_uses_only_its_body() {
    let html = "<!doctype html><html><head><title>T</title><meta charset=utf-8></head><body><h1>Hi</h1></body></html>";
    assert_eq!(
        blocks(html),
        json!([{"type": "heading", "attrs": {"level": 1}, "content": [{"type": "text", "text": "Hi"}]}])
    );
}

#[test]
fn absurdly_deep_nesting_is_flattened_instead_of_overflowing() {
    // A hostile or broken file can nest far deeper than any real page.
    let depth = 4_000;
    let html = format!("{}deep text{}", "<blockquote><div>".repeat(depth), "</div></blockquote>".repeat(depth));
    let doc = html_to_doc(&html, HtmlOptions::default());

    assert!(doc.plain_text().contains("deep text"), "the text itself must survive");
    // What comes out must be something the store can read back.
    let reread = heracles_core::doc::Document::from_json(&doc.to_json());
    assert!(reread.is_ok(), "{:?}", reread.err());
}

#[test]
fn ordinary_nesting_is_not_flattened() {
    let html = format!("{}inner{}", "<blockquote>".repeat(12), "</blockquote>".repeat(12));
    let mut node = &convert(&html)["content"][0];
    for _ in 0..11 {
        assert_eq!(node["type"], json!("blockquote"));
        node = &node["content"][0];
    }
    assert_eq!(node["content"][0]["content"][0]["text"], json!("inner"));
}
