//! Markdown in and out.

use super::html::{HtmlOptions, html_to_doc};
use super::{Document, Node};
use pulldown_cmark::{Options, Parser};
use serde_json::Value;

/// Convert Markdown into a document. Never fails.
///
/// Markdown is rendered to HTML and that HTML is converted, so a Markdown
/// file and an HTML file meet the same rules: the same node types, the same
/// filtering of raw HTML, and images as image blocks.
pub fn markdown_to_doc(markdown: &str) -> Document {
    let options = Options::ENABLE_TABLES | Options::ENABLE_STRIKETHROUGH | Options::ENABLE_TASKLISTS;
    let mut html = String::with_capacity(markdown.len() * 2);
    pulldown_cmark::html::push_html(&mut html, Parser::new_ext(markdown, options));
    html_to_doc(&html, HtmlOptions { collapse_deep_headings: true })
}

/// Write a document as Markdown. What Markdown cannot say is written as the
/// nearest thing it can: a callout as a quote, columns one after another.
pub fn doc_to_markdown(document: &Document) -> String {
    let body = blocks(&document.root.content);
    if body.is_empty() { body } else { body + "\n" }
}

/// The bullet or number style of a list, so that two lists in a row can be
/// given different ones. Markdown reads two lists with the same style as one.
#[derive(Clone, Copy, PartialEq)]
enum ListStyle {
    Bullet(char),
    Number(char),
}

/// Render blocks, separated by blank lines. Blocks with nothing to show,
/// such as empty paragraphs, are left out.
fn blocks(nodes: &[Node]) -> String {
    let mut parts: Vec<String> = Vec::new();
    let mut previous_list: Option<ListStyle> = None;
    for node in nodes {
        let style = match node.kind.as_str() {
            "bulletList" | "taskList" => Some(match previous_list {
                Some(ListStyle::Bullet('-')) => ListStyle::Bullet('*'),
                _ => ListStyle::Bullet('-'),
            }),
            "orderedList" => Some(match previous_list {
                Some(ListStyle::Number('.')) => ListStyle::Number(')'),
                _ => ListStyle::Number('.'),
            }),
            _ => None,
        };
        let rendered = match style {
            Some(style) => list(node, style),
            None => block(node),
        };
        if !rendered.is_empty() {
            parts.push(rendered);
            previous_list = style;
        }
    }
    parts.join("\n\n")
}

fn block(node: &Node) -> String {
    match node.kind.as_str() {
        "paragraph" => escape_line_starts(&inline(&node.content)),
        "heading" => {
            let level = node.attrs.get("level").and_then(Value::as_u64).unwrap_or(1).clamp(1, 6);
            format!("{} {}", "#".repeat(level as usize), inline(&node.content).replace('\n', " "))
        }
        "blockquote" => quote(&blocks(&node.content)),
        "horizontalRule" => "---".to_string(),
        "codeMirrorBlock" => code_fence(node),
        "image" => image(node),
        "calloutBlock" => {
            let emoji = attr(node, "emoji").unwrap_or("💡");
            quote(&format!("{emoji} {}", blocks(&node.content)))
        }
        "toggleBlock" => {
            let (summary, rest) = match node.content.split_first() {
                Some((first, rest)) => (plain_text(first), blocks(rest)),
                None => (String::new(), String::new()),
            };
            let summary = summary.replace('&', "&amp;").replace('<', "&lt;").replace('\n', " ");
            let rest = if rest.is_empty() { String::new() } else { format!("{rest}\n\n") };
            format!("<details>\n<summary>{summary}</summary>\n\n{rest}</details>")
        }
        // Columns, and any block this version does not know, are written as
        // their contents.
        _ if node.content.iter().any(|child| child.is_text() || child.kind == "hardBreak") => {
            escape_line_starts(&inline(&node.content))
        }
        _ => blocks(&node.content),
    }
}

fn list(node: &Node, style: ListStyle) -> String {
    let start = node.attrs.get("start").and_then(Value::as_i64).unwrap_or(1);
    let items: Vec<String> = node
        .content
        .iter()
        .enumerate()
        .map(|(index, item)| {
            let marker = match style {
                ListStyle::Bullet(bullet) => format!("{bullet} "),
                ListStyle::Number(delimiter) => format!("{}{delimiter} ", start + index as i64),
            };
            let checkbox = match item.attrs.get("checked").and_then(Value::as_bool) {
                Some(true) if item.kind == "taskItem" => "[x] ",
                Some(false) | None if item.kind == "taskItem" => "[ ] ",
                _ => "",
            };
            format!("{marker}{checkbox}{}", indent_after_first_line(&list_item_body(item), marker.len()))
        })
        .collect();
    items.join("\n")
}

/// The blocks of a list item. A list nested straight under the item's text
/// follows it directly. Anything else is set off by a blank line.
fn list_item_body(item: &Node) -> String {
    let mut body = String::new();
    let mut previous_list: Option<ListStyle> = None;
    for (index, child) in item.content.iter().enumerate() {
        let is_list = matches!(child.kind.as_str(), "bulletList" | "orderedList" | "taskList");
        let rendered = if is_list {
            let style = match (child.kind.as_str(), previous_list) {
                ("orderedList", Some(ListStyle::Number('.'))) => ListStyle::Number(')'),
                ("orderedList", _) => ListStyle::Number('.'),
                (_, Some(ListStyle::Bullet('-'))) => ListStyle::Bullet('*'),
                _ => ListStyle::Bullet('-'),
            };
            previous_list = Some(style);
            list(child, style)
        } else {
            previous_list = None;
            block(child)
        };
        if rendered.is_empty() {
            continue;
        }
        if !body.is_empty() {
            body.push_str(if is_list && index == 1 { "\n" } else { "\n\n" });
        }
        body.push_str(&rendered);
    }
    body
}

fn indent_after_first_line(text: &str, width: usize) -> String {
    let indent = " ".repeat(width);
    let mut lines = text.split('\n');
    let mut out = lines.next().unwrap_or_default().to_string();
    for line in lines {
        out.push('\n');
        if !line.is_empty() {
            out.push_str(&indent);
            out.push_str(line);
        }
    }
    out
}

fn quote(text: &str) -> String {
    let lines: Vec<String> =
        text.split('\n').map(|line| if line.is_empty() { ">".to_string() } else { format!("> {line}") }).collect();
    lines.join("\n")
}

fn code_fence(node: &Node) -> String {
    let code = attr(node, "code").unwrap_or("");
    let language = attr(node, "language").filter(|language| *language != "plaintext").unwrap_or("");
    let fence = "`".repeat(longest_run(code, '`').max(2) + 1);
    let newline = if code.ends_with('\n') || code.is_empty() { "" } else { "\n" };
    format!("{fence}{language}\n{code}{newline}{fence}")
}

fn image(node: &Node) -> String {
    let Some(src) = attr(node, "src") else { return String::new() };
    let alt = attr(node, "alt").map(escape).unwrap_or_default();
    let title = match attr(node, "title") {
        Some(title) => format!(" \"{}\"", title.replace('\\', "\\\\").replace('"', "\\\"")),
        None => String::new(),
    };
    format!("![{alt}]({}{title})", link_target(src))
}

/// A URL as a link destination. One with spaces or brackets is wrapped in
/// angle brackets, which is the form Markdown has for those.
fn link_target(url: &str) -> String {
    if url.contains([' ', '(', ')', '<', '>']) {
        format!("<{}>", url.replace('<', "%3C").replace('>', "%3E"))
    } else {
        url.to_string()
    }
}

fn attr<'a>(node: &'a Node, name: &str) -> Option<&'a str> {
    node.attrs.get(name).and_then(Value::as_str).filter(|value| !value.is_empty())
}

fn plain_text(node: &Node) -> String {
    Document { root: Node::new("doc").with_content(vec![node.clone()]) }.plain_text()
}

fn longest_run(text: &str, of: char) -> usize {
    let mut longest = 0;
    let mut current = 0;
    for ch in text.chars() {
        current = if ch == of { current + 1 } else { 0 };
        longest = longest.max(current);
    }
    longest
}

/// Render the text of a paragraph or heading with its marks.
fn inline(nodes: &[Node]) -> String {
    let mut out = String::new();
    for node in nodes {
        let Some(text) = &node.text else {
            if node.kind == "hardBreak" {
                out.push_str("\\\n");
            }
            continue;
        };
        let text = text.replace('\n', " ");
        let has = |kind: &str| node.marks.iter().any(|mark| mark.kind == kind);

        let marked = if has("code") {
            // Code is written as it is. The marker only has to be longer than
            // any run of backticks inside.
            let ticks = "`".repeat(longest_run(&text, '`') + 1);
            let pad = if text.starts_with('`') || text.ends_with('`') { " " } else { "" };
            format!("{ticks}{pad}{text}{pad}{ticks}")
        } else {
            // Markers must hug the words, so the spaces go outside them.
            let core = text.trim();
            let lead = &text[..text.len() - text.trim_start().len()];
            let trail = &text[text.trim_end().len()..];
            let emphasis = match (has("bold"), has("italic")) {
                (true, true) => "***",
                (true, false) => "**",
                (false, true) => "*",
                (false, false) => "",
            };
            let strike = if has("strike") { "~~" } else { "" };
            if core.is_empty() {
                text.clone()
            } else {
                format!("{lead}{strike}{emphasis}{}{emphasis}{strike}{trail}", escape(core))
            }
        };

        let href = node
            .marks
            .iter()
            .find(|mark| mark.kind == "link")
            .and_then(|link| link.attrs.get("href"))
            .and_then(Value::as_str);
        match href {
            Some(href) => out.push_str(&format!("[{}]({})", marked.trim(), link_target(href))),
            None => out.push_str(&marked),
        }
    }
    out
}

/// Escape the characters Markdown would read as markup anywhere in a line.
fn escape(text: &str) -> String {
    let mut out = String::with_capacity(text.len() + 8);
    for ch in text.chars() {
        if matches!(ch, '\\' | '`' | '*' | '_' | '[' | ']' | '<' | '&' | '~') {
            out.push('\\');
        }
        out.push(ch);
    }
    out
}

/// Escape what Markdown would read as markup only at the start of a line: a
/// heading, a quote, a list item, or a rule.
fn escape_line_starts(text: &str) -> String {
    let lines: Vec<String> = text
        .split('\n')
        .map(|line| {
            let bytes = line.as_bytes();
            let digits = bytes.iter().take_while(|byte| byte.is_ascii_digit()).count();
            let followed_by_gap = |index: usize| matches!(bytes.get(index), None | Some(b' '));
            let only = |allowed: &[u8]| !bytes.is_empty() && bytes.iter().all(|byte| allowed.contains(byte));
            if matches!(bytes.first(), Some(b'#' | b'>'))
                || (matches!(bytes.first(), Some(b'-' | b'+')) && followed_by_gap(1))
                || only(b"-= ")
            {
                format!("\\{line}")
            } else if digits > 0 && matches!(bytes.get(digits), Some(b'.' | b')')) && followed_by_gap(digits + 1) {
                format!("{}\\{}", &line[..digits], &line[digits..])
            } else {
                line.to_string()
            }
        })
        .collect();
    lines.join("\n")
}
