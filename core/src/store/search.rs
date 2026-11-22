//! Full-text search over titles and document text.

use super::Store;
use super::notes::note_from_row;
use crate::error::Result;
use crate::model::{SearchHit, SnippetPart};
use rusqlite::params;

/// How much more a word in the title counts than the same word in the body.
const TITLE_WEIGHT: f64 = 8.0;

/// The longest snippet, in words.
const SNIPPET_WORDS: u32 = 16;

/// Control characters SQLite wraps each matched word in. They are cut back
/// out by `snippet_parts` and never leave this file.
const MATCH_START: char = '\u{2}';
const MATCH_END: char = '\u{3}';

/// Turn what the user typed into an FTS5 query: every word must match, each
/// as the start of a word in the note. "gar pl" finds "Garden plan".
///
/// The words are split out here and each is quoted, so nothing the user types
/// is read as FTS5 syntax. A stray quote or a trailing `AND` cannot cause an
/// error, and `title:x` is two words, not a column filter.
fn match_expression(query: &str) -> Option<String> {
    let words: Vec<&str> =
        query.split(|ch: char| !ch.is_alphanumeric()).filter(|word| !word.is_empty()).collect();
    if words.is_empty() {
        return None;
    }
    let prefixes: Vec<String> = words.iter().map(|word| format!("\"{word}\"*")).collect();
    Some(prefixes.join(" "))
}

/// Cut a marked-up snippet into matched and unmatched parts. Line breaks
/// between blocks become spaces, since a snippet is shown on one line.
fn snippet_parts(marked: &str) -> Vec<SnippetPart> {
    let mut parts = Vec::new();
    let mut matched = false;
    for piece in marked.replace('\n', " ").split([MATCH_START, MATCH_END]) {
        if !piece.is_empty() {
            parts.push(SnippetPart { text: piece.to_string(), matched });
        }
        // The markers alternate, so each one flips between the two kinds.
        matched = !matched;
    }
    parts
}

#[uniffi::export]
impl Store {
    /// Notes matching every word of `query`, best match first. Each word
    /// matches as a prefix, so results follow the typing. Notes in the trash
    /// are left out.
    pub fn search_notes(&self, query: &str, limit: u32) -> Result<Vec<SearchHit>> {
        let Some(expression) = match_expression(query) else {
            return Ok(Vec::new());
        };
        self.read(|connection| {
            let mut statement = connection.prepare(
                "SELECT note.id, note.kind, note.parent_id, note.title, note.icon, note.position,
                        note.favorited, note.archived, note.trashed, note.created_at, note.updated_at,
                        snippet(notes_fts, 1, ?2, ?3, '…', ?4)
                 FROM notes_fts
                 JOIN notes note ON note.key = notes_fts.rowid
                 WHERE notes_fts MATCH ?1 AND note.trashed = 0
                 ORDER BY bm25(notes_fts, ?5, 1.0), note.updated_at DESC
                 LIMIT ?6",
            )?;
            let hits = statement
                .query_map(
                    params![
                        expression,
                        MATCH_START.to_string(),
                        MATCH_END.to_string(),
                        SNIPPET_WORDS,
                        TITLE_WEIGHT,
                        limit
                    ],
                    |row| {
                        let marked: String = row.get(11)?;
                        Ok(SearchHit { note: note_from_row(row)?, snippet: snippet_parts(&marked) })
                    },
                )?
                .collect::<rusqlite::Result<_>>()?;
            Ok(hits)
        })
    }
}

#[cfg(test)]
mod tests {
    use super::match_expression;

    #[test]
    fn each_word_is_quoted_and_matched_as_a_prefix() {
        assert_eq!(match_expression("red  apples").as_deref(), Some("\"red\"* \"apples\"*"));
        assert_eq!(match_expression("title:x AND \"y").as_deref(), Some("\"title\"* \"x\"* \"AND\"* \"y\"*"));
    }

    #[test]
    fn a_query_with_no_words_has_no_expression() {
        assert_eq!(match_expression(" \"* ():- "), None);
    }
}
