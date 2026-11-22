//! The block document model.
//!
//! A document is stored as JSON in the shape ProseMirror uses for its own
//! documents (`{"type": "doc", "content": [...]}`), with the node and mark
//! names of the Heracles editor schema. The web editor loads and saves this
//! JSON as is, and a native editor can read the same documents.
//!
//! Nodes are kept generic (a type name plus attributes) instead of one Rust
//! type per block, so a document written by a newer editor survives a round
//! trip through an older core without losing nodes it does not know.

pub mod html;
pub mod markdown;

use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

/// A mark on a run of text: bold, italic, strike, code, or link.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Mark {
    #[serde(rename = "type")]
    pub kind: String,
    #[serde(default, skip_serializing_if = "Map::is_empty")]
    pub attrs: Map<String, Value>,
}

/// One node of a document: a block, an inline node, or a run of text.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Node {
    #[serde(rename = "type")]
    pub kind: String,
    #[serde(default, skip_serializing_if = "Map::is_empty")]
    pub attrs: Map<String, Value>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub content: Vec<Node>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub marks: Vec<Mark>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub text: Option<String>,
}

/// A whole document. The root node always has the type `doc`.
#[derive(Debug, Clone, PartialEq)]
pub struct Document {
    pub root: Node,
}

/// Why a string could not be read as a document.
#[derive(Debug, thiserror::Error)]
pub enum DocumentError {
    #[error("document is not valid JSON: {0}")]
    Json(#[from] serde_json::Error),
    #[error("document root has type `{0}`, expected `doc`")]
    WrongRoot(String),
}

/// How many blocks deep a converted document may nest. Stored JSON is read
/// back with a nesting limit of 128, and each block costs two levels of it.
const MAX_DEPTH: usize = 40;

/// The attribute that holds a block's id.
pub const BLOCK_ID_ATTR: &str = "blockId";

/// The node types that carry a block id. Block links point at these.
pub const BLOCK_ID_TYPES: [&str; 10] = [
    "paragraph",
    "heading",
    "blockquote",
    "listItem",
    "taskItem",
    "horizontalRule",
    "codeMirrorBlock",
    "calloutBlock",
    "toggleBlock",
    "image",
];

impl Mark {
    pub fn new(kind: &str) -> Mark {
        Mark { kind: kind.to_string(), attrs: Map::new() }
    }

    pub fn with_attr(mut self, name: &str, value: impl Into<Value>) -> Mark {
        self.attrs.insert(name.to_string(), value.into());
        self
    }
}

impl Node {
    pub fn new(kind: &str) -> Node {
        Node {
            kind: kind.to_string(),
            attrs: Map::new(),
            content: Vec::new(),
            marks: Vec::new(),
            text: None,
        }
    }

    /// A run of text. ProseMirror rejects empty text nodes, so callers must
    /// not keep one whose text is empty.
    pub fn text(text: impl Into<String>, marks: Vec<Mark>) -> Node {
        Node { text: Some(text.into()), marks, ..Node::new("text") }
    }

    pub fn with_attr(mut self, name: &str, value: impl Into<Value>) -> Node {
        self.attrs.insert(name.to_string(), value.into());
        self
    }

    pub fn with_content(mut self, content: Vec<Node>) -> Node {
        self.content = content;
        self
    }

    pub fn is_text(&self) -> bool {
        self.text.is_some()
    }

    /// True for nodes that sit inside a line of text.
    fn is_inline(&self) -> bool {
        self.is_text() || self.kind == "hardBreak"
    }

    fn attr_str(&self, name: &str) -> Option<&str> {
        self.attrs.get(name).and_then(Value::as_str).filter(|value| !value.is_empty())
    }

    fn collect_plain_text(&self, lines: &mut Vec<String>) {
        match self.kind.as_str() {
            "codeMirrorBlock" => lines.extend(self.attr_str("code").map(String::from)),
            "image" => {
                let described: Vec<&str> =
                    ["alt", "title"].iter().filter_map(|name| self.attr_str(name)).collect();
                if !described.is_empty() {
                    lines.push(described.join(" "));
                }
            }
            _ if self.content.iter().any(Node::is_inline) => {
                let mut line = String::new();
                for child in &self.content {
                    match &child.text {
                        Some(text) => line.push_str(text),
                        None if child.kind == "hardBreak" => line.push('\n'),
                        None => {}
                    }
                }
                if !line.trim().is_empty() {
                    lines.push(line);
                }
            }
            _ => {
                for child in &self.content {
                    child.collect_plain_text(lines);
                }
            }
        }
    }

    /// Flatten whatever lies more than `MAX_DEPTH` blocks deep into plain
    /// paragraphs. See [`Document::limit_depth`].
    fn limit_depth(&mut self, depth: usize) {
        if self.content.iter().all(Node::is_inline) {
            return;
        }
        // Lists and column rows can only hold items and columns, so the cut
        // is made one level further in, at something that holds any block.
        let holds_any_block = matches!(
            self.kind.as_str(),
            "doc" | "blockquote" | "listItem" | "taskItem" | "calloutBlock" | "toggleBlock" | "column"
        );
        if depth >= MAX_DEPTH && holds_any_block {
            let mut lines = Vec::new();
            for child in &self.content {
                child.collect_plain_text(&mut lines);
            }
            self.content = lines
                .into_iter()
                .map(|line| Node::new("paragraph").with_content(vec![Node::text(line, Vec::new())]))
                .collect();
            if self.content.is_empty() {
                self.content.push(Node::new("paragraph"));
            }
            return;
        }
        for child in &mut self.content {
            child.limit_depth(depth + 1);
        }
    }

    fn assign_block_ids(&mut self, seen: &mut std::collections::HashSet<String>) {
        if BLOCK_ID_TYPES.contains(&self.kind.as_str()) {
            let current = self.attr_str(BLOCK_ID_ATTR).map(String::from);
            let id = match current {
                Some(id) if !seen.contains(&id) => id,
                _ => {
                    let id = uuid::Uuid::new_v4().to_string();
                    self.attrs.insert(BLOCK_ID_ATTR.to_string(), Value::String(id.clone()));
                    id
                }
            };
            seen.insert(id);
        }
        for child in &mut self.content {
            child.assign_block_ids(seen);
        }
    }
}

impl Document {
    /// A document holding one empty paragraph, which is what a new page shows.
    pub fn empty() -> Document {
        Document { root: Node::new("doc").with_content(vec![Node::new("paragraph")]) }
    }

    /// Read a document from its stored JSON.
    pub fn from_json(json: &str) -> Result<Document, DocumentError> {
        let root: Node = serde_json::from_str(json)?;
        if root.kind != "doc" {
            return Err(DocumentError::WrongRoot(root.kind));
        }
        Ok(Document { root })
    }

    /// The JSON to store, and to hand to an editor.
    pub fn to_json(&self) -> String {
        serde_json::to_string(&self.root).expect("a document always serializes")
    }

    /// The text a reader sees, one block per line, for search indexing.
    pub fn plain_text(&self) -> String {
        let mut lines = Vec::new();
        self.root.collect_plain_text(&mut lines);
        lines.join("\n")
    }

    /// Keep the document shallow enough to be stored and read back. Blocks
    /// nested deeper than any person writes them keep their text and lose
    /// their structure.
    pub(crate) fn limit_depth(&mut self) {
        self.root.limit_depth(0);
    }

    /// Give every block that carries an id a unique one, keeping the ids that
    /// are already there. Mirrors what the editor does on each change, so
    /// block links keep working after a migration or an import.
    pub fn ensure_block_ids(&mut self) {
        let mut seen = std::collections::HashSet::new();
        self.root.assign_block_ids(&mut seen);
    }
}
