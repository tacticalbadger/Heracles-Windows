//! Convert HTML into a document.
//!
//! This reads two kinds of input with one set of rules: the HTML the Electron
//! app stored for each note, and arbitrary HTML from an import or a paste.
//!
//! The output is built only from the node types, marks, and attributes named
//! in this file. Nothing is copied through from the input, so scripts, event
//! handlers, and `javascript:` URLs cannot survive a conversion.

use super::{BLOCK_ID_ATTR, Document, Mark, Node};
use ego_tree::NodeRef;
use ego_tree::iter::Edge;
use scraper::node::Element;
use scraper::{Html, Node as HtmlNode};
use serde_json::Value;
use std::cell::Cell;

/// Choices that differ between migrating stored notes and importing a file.
#[derive(Debug, Clone, Copy, Default)]
pub struct HtmlOptions {
    /// Turn `h5` and `h6` into level 4 headings, as the import command always
    /// has. Stored notes keep the level they were saved with.
    pub collapse_deep_headings: bool,
}

/// Convert HTML into a document. Never fails: input that maps onto nothing
/// yields a document with one empty paragraph.
pub fn html_to_doc(html: &str, options: HtmlOptions) -> Document {
    let dom = Html::parse_document(html);
    let converter = Converter { options, depth: Cell::new(0) };
    let mut blocks = converter.container_blocks(dom.tree.root(), &[]);
    if blocks.is_empty() {
        blocks.push(Node::new("paragraph"));
    }
    let mut document = Document { root: Node::new("doc").with_content(blocks) };
    document.limit_depth();
    document
}

/// How many elements deep the converter follows the HTML. It walks the tree
/// by recursion, so without a limit a file nested deeply enough would run it
/// out of stack and take the app down with it. Real pages stay far below this.
const MAX_ELEMENT_DEPTH: usize = 200;

type Dom<'a> = NodeRef<'a, HtmlNode>;

/// Elements whose content is never shown as part of a note.
const SKIPPED: &[&str] = &[
    "applet", "area", "audio", "base", "button", "canvas", "col", "colgroup", "embed", "frame",
    "frameset", "head", "iframe", "input", "link", "map", "math", "meta", "noscript", "object",
    "option", "script", "select", "source", "style", "svg", "template", "textarea", "title",
    "track", "video",
];

/// Elements with no node of their own that still start a new line. Their
/// content is read as blocks, and they break the paragraph around them.
const CONTAINERS: &[&str] = &[
    "address", "article", "aside", "body", "caption", "center", "dd", "details", "dialog", "dir",
    "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "header", "hgroup",
    "html", "li", "main", "menu", "nav", "section", "summary", "table", "tbody", "td", "tfoot",
    "th", "thead", "tr",
];

const LINK_SCHEMES: &[&str] = &[
    "http", "https", "ftp", "ftps", "mailto", "tel", "callto", "sms", "cid", "xmpp", "heracles",
];

const IMAGE_SCHEMES: &[&str] = &["http", "https", "file", "heracles-image"];

/// The order the editor keeps marks in. Link registers with a higher priority
/// than the starter kit marks, so it comes first.
const MARK_ORDER: &[&str] = &["link", "bold", "code", "italic", "strike"];

/// What converting one piece of HTML yields, in document order.
enum Item {
    /// Text or a hard break, to be gathered into the paragraph around it.
    Inline(Node),
    /// A finished block. It ends the paragraph before it.
    Block(Node),
    /// A line boundary with nothing to show for it, such as an empty `div`.
    Break,
}

struct Converter {
    options: HtmlOptions,
    /// How many elements deep the conversion currently is.
    depth: Cell<usize>,
}

impl Converter {
    fn child_items(&self, parent: Dom, marks: &[Mark], out: &mut Vec<Item>) {
        for child in parent.children() {
            self.node_items(child, marks, out);
        }
    }

    fn node_items(&self, node: Dom, marks: &[Mark], out: &mut Vec<Item>) {
        match node.value() {
            HtmlNode::Text(text) => out.push(Item::Inline(Node::text(&**text, marks.to_vec()))),
            HtmlNode::Element(element) => self.element_items(node, element, marks, out),
            _ => {}
        }
    }

    /// The children of `parent` as a list of blocks. Loose text and inline
    /// elements between blocks are gathered into paragraphs.
    fn container_blocks(&self, parent: Dom, marks: &[Mark]) -> Vec<Node> {
        let mut items = Vec::new();
        self.child_items(parent, marks, &mut items);
        group_items(items)
    }

    /// Like `container_blocks`, for nodes that must hold at least one block.
    fn required_blocks(&self, parent: Dom, marks: &[Mark]) -> Vec<Node> {
        let mut blocks = self.container_blocks(parent, marks);
        if blocks.is_empty() {
            blocks.push(Node::new("paragraph"));
        }
        blocks
    }

    fn element_items(&self, node: Dom, element: &Element, outer: &[Mark], out: &mut Vec<Item>) {
        let depth = self.depth.get();
        if depth >= MAX_ELEMENT_DEPTH {
            // Too deep to follow. Keep the words and let the structure go.
            let text = subtree_text(node);
            out.push(Item::Inline(Node::text(text, outer.to_vec())));
            return;
        }
        self.depth.set(depth + 1);
        self.convert_element(node, element, outer, out);
        self.depth.set(depth);
    }

    fn convert_element(&self, node: Dom, element: &Element, outer: &[Mark], out: &mut Vec<Item>) {
        let name = element.name();
        if SKIPPED.contains(&name) {
            return;
        }
        let marks = marks_for_element(element, outer);
        let marks = marks.as_slice();

        match name {
            "br" => out.push(Item::Inline(Node::new("hardBreak"))),
            "hr" => out.push(Item::Block(with_block_id(Node::new("horizontalRule"), element))),
            "img" => out.extend(image_node(element).map(Item::Block)),
            "p" => self.text_block(Node::new("paragraph"), node, element, marks, out),
            "h1" | "h2" | "h3" | "h4" | "h5" | "h6" => {
                let mut level = i64::from(name.as_bytes()[1] - b'0');
                if self.options.collapse_deep_headings {
                    level = level.min(4);
                }
                let heading = Node::new("heading").with_attr("level", level);
                self.text_block(heading, node, element, marks, out);
            }
            "blockquote" => {
                let quote = with_block_id(Node::new("blockquote"), element);
                out.push(Item::Block(quote.with_content(self.required_blocks(node, marks))));
            }
            "ul" | "ol" => match self.list(node, element, marks) {
                Some(list) => out.push(Item::Block(list)),
                None => out.push(Item::Break),
            },
            "pre" => out.push(Item::Block(code_block(node, element))),
            "div" if element.attr("data-callout").is_some() => {
                let emoji = element.attr("data-emoji").filter(|value| !value.is_empty());
                let callout = Node::new("calloutBlock").with_attr("emoji", emoji.unwrap_or("💡"));
                let callout = with_block_id(callout, element);
                out.push(Item::Block(callout.with_content(self.required_blocks(node, marks))));
            }
            "div" if element.attr("data-toggle").is_some() => {
                let open = element.attr("data-open") != Some("false");
                let toggle = with_block_id(Node::new("toggleBlock").with_attr("open", open), element);
                out.push(Item::Block(toggle.with_content(self.required_blocks(node, marks))));
            }
            "div" if element.attr("data-type") == Some("columns") => self.columns(node, marks, out),
            _ if CONTAINERS.contains(&name) => {
                out.push(Item::Break);
                out.extend(self.container_blocks(node, marks).into_iter().map(Item::Block));
                out.push(Item::Break);
            }
            // Everything else is inline and has no node of its own: `span`,
            // `u`, `sub`, and the mark elements, whose marks are already in
            // `marks`.
            _ => self.child_items(node, marks, out),
        }
    }

    /// A paragraph or heading. These hold only inline content, so a block
    /// found inside one (the editor's images are blocks) splits it in two.
    fn text_block(&self, template: Node, node: Dom, element: &Element, marks: &[Mark], out: &mut Vec<Item>) {
        let mut items = Vec::new();
        self.child_items(node, marks, &mut items);

        // Only the first piece keeps the block id, so ids stay unique.
        let mut next = Some(with_block_id(template.clone(), element));
        let mut pieces = Vec::new();
        let mut run = Vec::new();
        let mut flush = |run: &mut Vec<Node>, pieces: &mut Vec<Node>| {
            let content = normalize_inline(std::mem::take(run));
            if !content.is_empty() {
                let piece = next.take().unwrap_or_else(|| template.clone());
                pieces.push(piece.with_content(content));
            }
        };
        for item in items {
            match item {
                Item::Inline(inline) => run.push(inline),
                Item::Block(block) => {
                    flush(&mut run, &mut pieces);
                    pieces.push(block);
                }
                Item::Break => flush(&mut run, &mut pieces),
            }
        }
        flush(&mut run, &mut pieces);

        // An empty paragraph is a blank line the writer put there. Keep it.
        if pieces.is_empty() {
            pieces.extend(next.take());
        }
        out.extend(pieces.into_iter().map(Item::Block));
    }

    fn list(&self, node: Dom, element: &Element, marks: &[Mark]) -> Option<Node> {
        let is_task = element.attr("data-type") == Some("taskList")
            || node.children().any(|child| match child.value() {
                HtmlNode::Element(li) if li.name() == "li" => {
                    li.attr("data-type") == Some("taskItem") || checkbox_state(child).is_some()
                }
                _ => false,
            });
        let new_item = |content: Vec<Node>| {
            let item = if is_task {
                Node::new("taskItem").with_attr("checked", false)
            } else {
                Node::new("listItem")
            };
            item.with_content(with_leading_paragraph(content))
        };

        let mut items: Vec<Node> = Vec::new();
        // Content that sits in the list outside any `li`.
        let mut stray: Vec<Item> = Vec::new();
        let flush_stray = |stray: &mut Vec<Item>, items: &mut Vec<Node>| {
            let blocks = group_items(std::mem::take(stray));
            if !blocks.is_empty() {
                items.push(new_item(blocks));
            }
        };

        for child in node.children() {
            match child.value() {
                HtmlNode::Element(li) if li.name() == "li" => {
                    flush_stray(&mut stray, &mut items);
                    items.push(self.list_item(child, li, marks, is_task));
                }
                // A list placed directly inside a list belongs to the item
                // before it.
                HtmlNode::Element(inner) if matches!(inner.name(), "ul" | "ol") => {
                    flush_stray(&mut stray, &mut items);
                    if let Some(nested) = self.list(child, inner, marks) {
                        match items.last_mut() {
                            Some(previous) => previous.content.push(nested),
                            None => items.push(new_item(vec![nested])),
                        }
                    }
                }
                _ => self.node_items(child, marks, &mut stray),
            }
        }
        flush_stray(&mut stray, &mut items);

        if items.is_empty() {
            return None;
        }
        let list = if is_task {
            Node::new("taskList")
        } else if element.name() == "ol" {
            let start = element.attr("start").and_then(|value| value.trim().parse::<i64>().ok());
            Node::new("orderedList").with_attr("start", start.unwrap_or(1))
        } else {
            Node::new("bulletList")
        };
        Some(list.with_content(items))
    }

    fn list_item(&self, node: Dom, element: &Element, marks: &[Mark], is_task: bool) -> Node {
        let item = if is_task {
            let checked = match element.attr("data-checked") {
                Some(value) => value == "true" || value.is_empty(),
                None => checkbox_state(node).unwrap_or(false),
            };
            Node::new("taskItem").with_attr("checked", checked)
        } else {
            Node::new("listItem")
        };
        let content = with_leading_paragraph(self.container_blocks(node, marks));
        with_block_id(item, element).with_content(content)
    }

    /// A row of columns. Empty columns are dropped, and a row left with one
    /// column is replaced by that column's content, as the editor does.
    fn columns(&self, node: Dom, marks: &[Mark], out: &mut Vec<Item>) {
        let new_column = |width: f64, content: Vec<Node>| {
            Node::new("column").with_attr("width", width).with_content(content)
        };
        let mut columns: Vec<Node> = Vec::new();
        let mut stray: Vec<Item> = Vec::new();
        let flush_stray = |stray: &mut Vec<Item>, columns: &mut Vec<Node>| {
            let blocks = group_items(std::mem::take(stray));
            if !blocks.is_empty() {
                columns.push(new_column(1.0, blocks));
            }
        };

        for child in node.children() {
            match child.value() {
                HtmlNode::Element(column) if column.attr("data-type") == Some("column") => {
                    flush_stray(&mut stray, &mut columns);
                    let content = self.container_blocks(child, marks);
                    if !content.is_empty() {
                        let width = column
                            .attr("data-width")
                            .and_then(|value| value.trim().parse::<f64>().ok())
                            .filter(|width| width.is_finite() && *width > 0.0);
                        columns.push(new_column(width.unwrap_or(1.0), content));
                    }
                }
                _ => self.node_items(child, marks, &mut stray),
            }
        }
        flush_stray(&mut stray, &mut columns);

        out.push(Item::Break);
        if columns.len() >= 2 {
            out.push(Item::Block(Node::new("columnBlock").with_content(columns)));
        } else {
            let lone = columns.into_iter().flat_map(|column| column.content);
            out.extend(lone.map(Item::Block));
        }
        out.push(Item::Break);
    }
}

/// All the text under a node, read without recursion, leaving out elements
/// whose content is never shown.
fn subtree_text(node: Dom) -> String {
    let is_skipped = |node: Dom| matches!(node.value(), HtmlNode::Element(el) if SKIPPED.contains(&el.name()));
    let mut text = String::new();
    let mut skipping = 0usize;
    for edge in node.traverse() {
        match edge {
            Edge::Open(inner) if is_skipped(inner) => skipping += 1,
            Edge::Close(inner) if is_skipped(inner) => skipping -= 1,
            Edge::Open(inner) if skipping == 0 => {
                if let HtmlNode::Text(piece) = inner.value() {
                    text.push_str(piece);
                    text.push(' ');
                }
            }
            _ => {}
        }
    }
    text
}

/// Turn a mixed list of items into blocks: each stretch of inline items
/// becomes a paragraph, unless it holds no text.
fn group_items(items: Vec<Item>) -> Vec<Node> {
    let mut blocks = Vec::new();
    let mut run = Vec::new();
    let flush = |run: &mut Vec<Node>, blocks: &mut Vec<Node>| {
        let content = normalize_inline(std::mem::take(run));
        if content.iter().any(Node::is_text) {
            blocks.push(Node::new("paragraph").with_content(content));
        }
    };
    for item in items {
        match item {
            Item::Inline(inline) => run.push(inline),
            Item::Block(block) => {
                flush(&mut run, &mut blocks);
                blocks.push(block);
            }
            Item::Break => flush(&mut run, &mut blocks),
        }
    }
    flush(&mut run, &mut blocks);
    blocks
}

/// List items must begin with a paragraph.
fn with_leading_paragraph(mut content: Vec<Node>) -> Vec<Node> {
    if content.first().is_none_or(|first| first.kind != "paragraph") {
        content.insert(0, Node::new("paragraph"));
    }
    content
}

/// Collapse whitespace the way a browser lays out text, drop empty text
/// nodes, and join neighbours that carry the same marks.
fn normalize_inline(run: Vec<Node>) -> Vec<Node> {
    let mut out: Vec<Node> = Vec::new();
    for node in run {
        let Some(text) = &node.text else {
            out.push(node);
            continue;
        };
        let mut text = collapse_whitespace(text);
        if text.starts_with(' ') {
            let follows_a_gap = match out.last() {
                None => true,
                Some(previous) => match &previous.text {
                    Some(before) => before.ends_with(' '),
                    None => previous.kind == "hardBreak",
                },
            };
            if follows_a_gap {
                text.remove(0);
            }
        }
        if text.is_empty() {
            continue;
        }
        match out.last_mut() {
            Some(Node { text: Some(before), marks, .. }) if *marks == node.marks => {
                before.push_str(&text);
            }
            _ => out.push(Node::text(text, node.marks)),
        }
    }
    while let Some(Node { text: Some(last), .. }) = out.last_mut() {
        let kept = last.trim_end_matches(' ').len();
        last.truncate(kept);
        if kept > 0 {
            break;
        }
        out.pop();
    }
    out
}

/// Replace each run of ASCII whitespace with one space. A non-breaking space
/// is not whitespace here, as in a browser.
fn collapse_whitespace(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut in_gap = false;
    for ch in text.chars() {
        if matches!(ch, ' ' | '\t' | '\n' | '\r' | '\u{c}') {
            if !in_gap {
                out.push(' ');
            }
            in_gap = true;
        } else {
            out.push(ch);
            in_gap = false;
        }
    }
    out
}

fn with_block_id(node: Node, element: &Element) -> Node {
    match element.attr("data-block-id").filter(|id| !id.is_empty()) {
        Some(id) => node.with_attr(BLOCK_ID_ATTR, id),
        None => node,
    }
}

fn image_node(element: &Element) -> Option<Node> {
    let src = element.attr("src")?.trim();
    let allowed = match url_scheme(src) {
        _ if src.is_empty() => false,
        None => true,
        Some(scheme) if scheme == "data" => is_image_data_url(src),
        Some(scheme) => IMAGE_SCHEMES.contains(&scheme.as_str()),
    };
    if !allowed {
        return None;
    }
    let optional = |name: &str| match element.attr(name).filter(|value| !value.is_empty()) {
        Some(value) => Value::String(value.to_string()),
        None => Value::Null,
    };
    let image = Node::new("image")
        .with_attr("src", src)
        .with_attr("alt", optional("alt"))
        .with_attr("title", optional("title"));
    Some(with_block_id(image, element))
}

fn is_image_data_url(url: &str) -> bool {
    url.get(..11).is_some_and(|prefix| prefix.eq_ignore_ascii_case("data:image/"))
}

fn code_block(node: Dom, element: &Element) -> Node {
    let (language, code) = if element.attr("data-code-mirror").is_some() {
        // The editor writes each value twice. `language` and `code` are the
        // pair it reads back.
        let language = element.attr("language").or(element.attr("data-language"));
        let code = element.attr("code").or(element.attr("data-code"));
        (language.unwrap_or("javascript").to_string(), code.unwrap_or("").to_string())
    } else {
        let inner = node.descendants().find(|candidate| {
            matches!(candidate.value(), HtmlNode::Element(code) if code.name() == "code")
        });
        let language = inner
            .and_then(|code| code.value().as_element().and_then(sniff_language))
            .or_else(|| sniff_language(element));
        let code: String = inner
            .unwrap_or(node)
            .descendants()
            .filter_map(|descendant| descendant.value().as_text().map(|text| &**text))
            .collect();
        (language.unwrap_or_else(|| "plaintext".to_string()), code)
    };
    let block = Node::new("codeMirrorBlock").with_attr("language", language).with_attr("code", code);
    with_block_id(block, element)
}

/// The language named by a `language-*` or `lang-*` class.
fn sniff_language(element: &Element) -> Option<String> {
    element.classes().find_map(|class| {
        let language = class.strip_prefix("language-").or_else(|| class.strip_prefix("lang-"))?;
        (!language.is_empty()).then(|| language.to_string())
    })
}

/// Whether a list item holds a checkbox, and if so whether it is ticked.
/// Markdown renderers put it directly in the `li`, or in its first `p`.
fn checkbox_state(li: Dom) -> Option<bool> {
    let as_checkbox = |node: Dom| match node.value() {
        HtmlNode::Element(input) if input.name() == "input" => input
            .attr("type")
            .is_some_and(|kind| kind.eq_ignore_ascii_case("checkbox"))
            .then(|| input.attr("checked").is_some()),
        _ => None,
    };
    li.children().find_map(|child| {
        as_checkbox(child).or_else(|| match child.value() {
            HtmlNode::Element(wrapper) if matches!(wrapper.name(), "p" | "label") => {
                child.children().find_map(as_checkbox)
            }
            _ => None,
        })
    })
}

/// The marks in force inside `element`: those of its parent, plus any it
/// adds through its tag or its `style` attribute.
fn marks_for_element(element: &Element, outer: &[Mark]) -> Vec<Mark> {
    let mut marks = outer.to_vec();
    let style = |property: &str| style_value(element, property);
    let weight = style("font-weight");
    let slant = style("font-style");

    match element.name() {
        "strong" => add_mark(&mut marks, Mark::new("bold")),
        // Google Docs wraps a paste in <b style="font-weight:normal">.
        "b" if weight.as_deref() != Some("normal") => add_mark(&mut marks, Mark::new("bold")),
        "em" => add_mark(&mut marks, Mark::new("italic")),
        "i" if slant.as_deref() != Some("normal") => add_mark(&mut marks, Mark::new("italic")),
        "s" | "del" | "strike" => add_mark(&mut marks, Mark::new("strike")),
        "code" => add_mark(&mut marks, Mark::new("code")),
        "a" => {
            if let Some(href) = element.attr("href").map(str::trim).filter(|href| is_allowed_link(href)) {
                // The target, rel, and class are the editor's defaults. The
                // values in the source are not carried over.
                let link = Mark::new("link")
                    .with_attr("href", href)
                    .with_attr("target", "_blank")
                    .with_attr("rel", "noopener noreferrer nofollow")
                    .with_attr("class", Value::Null);
                add_mark(&mut marks, link);
            }
        }
        _ => {}
    }

    if weight.as_deref().is_some_and(is_bold_weight) {
        add_mark(&mut marks, Mark::new("bold"));
    }
    if slant.as_deref() == Some("italic") {
        add_mark(&mut marks, Mark::new("italic"));
    }
    let struck = |property: &str| style(property).is_some_and(|value| value.contains("line-through"));
    if struck("text-decoration") || struck("text-decoration-line") {
        add_mark(&mut marks, Mark::new("strike"));
    }
    marks
}

/// Add a mark the way the editor's schema allows. Inline code excludes every
/// other mark, whichever of the two comes first.
fn add_mark(marks: &mut Vec<Mark>, mark: Mark) {
    if marks.iter().any(|existing| existing.kind == "code") {
        return;
    }
    if mark.kind == "code" {
        marks.clear();
    }
    marks.retain(|existing| existing.kind != mark.kind);
    marks.push(mark);
    marks.sort_by_key(|mark| {
        MARK_ORDER.iter().position(|kind| *kind == mark.kind).unwrap_or(MARK_ORDER.len())
    });
}

fn is_bold_weight(weight: &str) -> bool {
    matches!(weight, "bold" | "bolder") || weight.parse::<u32>().is_ok_and(|number| number >= 500)
}

/// One property from an inline `style` attribute, lowercased.
fn style_value(element: &Element, property: &str) -> Option<String> {
    element.attr("style")?.split(';').find_map(|declaration| {
        let (name, value) = declaration.split_once(':')?;
        name.trim().eq_ignore_ascii_case(property).then(|| value.trim().to_ascii_lowercase())
    })
}

fn is_allowed_link(href: &str) -> bool {
    match url_scheme(href) {
        _ if href.is_empty() => false,
        Some(scheme) => LINK_SCHEMES.contains(&scheme.as_str()),
        None => true,
    }
}

/// The scheme of a URL, lowercased, or `None` for a relative URL. Browsers
/// ignore whitespace and control characters inside a scheme, so
/// `java\nscript:` is read as `javascript:` here too.
pub(crate) fn url_scheme(url: &str) -> Option<String> {
    let cleaned: String = url.chars().filter(|ch| *ch > ' ' && *ch != '\u{7f}').collect();
    let (scheme, _) = cleaned.split_once(':')?;
    let mut chars = scheme.chars();
    let valid = chars.next().is_some_and(|first| first.is_ascii_alphabetic())
        && chars.all(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '+' | '.' | '-'));
    valid.then(|| scheme.to_ascii_lowercase())
}
