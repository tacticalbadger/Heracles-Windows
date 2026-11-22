//! Bringing files in as notes, and writing notes out as files.

use crate::doc::html::{HtmlOptions, html_to_doc, url_scheme};
use crate::doc::markdown::{doc_to_markdown, markdown_to_doc};
use crate::doc::{Document, Node};
use crate::error::{CoreError, Result};
use crate::model::{NewNote, Note, NoteKind};
use crate::store::{Store, image_hash, percent_decode};
use std::path::{Path, PathBuf};

/// The largest picture an import will read in.
const MAX_IMAGE_BYTES: u64 = 64 * 1024 * 1024;

/// The picture types an import reads in, by file extension. A file of any
/// other type is never read, whatever the document says about it.
const IMAGE_TYPES: &[(&str, &str)] = &[
    ("png", "image/png"),
    ("jpg", "image/jpeg"),
    ("jpeg", "image/jpeg"),
    ("gif", "image/gif"),
    ("webp", "image/webp"),
    ("svg", "image/svg+xml"),
    ("bmp", "image/bmp"),
    ("tif", "image/tiff"),
    ("tiff", "image/tiff"),
    ("avif", "image/avif"),
    ("heic", "image/heic"),
];

/// Convert HTML into document JSON, by the same rules as an import. For
/// content pasted into the editor.
#[uniffi::export]
pub fn html_to_document(html: &str) -> String {
    html_to_doc(html, HtmlOptions { collapse_deep_headings: true }).to_json()
}

/// The file a document's image `src` names, if it names a local one.
fn local_image_file(src: &str, folder: &Path) -> Option<PathBuf> {
    let path = match url_scheme(src).as_deref() {
        Some("file") => file_url_path(&src[5..])?,
        Some(_) => return None,
        None => folder.join(decoded(src.split(['?', '#']).next().unwrap_or(src))),
    };
    let extension = path.extension()?.to_str()?.to_ascii_lowercase();
    IMAGE_TYPES.iter().any(|(known, _)| *known == extension).then_some(path)
}

/// The path a `file:` URL names, given what follows the scheme. `None` when
/// the URL names a file on another machine: only this one's files are read.
fn file_url_path(rest: &str) -> Option<PathBuf> {
    let path = match rest.strip_prefix("//") {
        Some(after) => {
            let (host, path) = after.split_at(after.find('/').unwrap_or(after.len()));
            if !host.is_empty() && !host.eq_ignore_ascii_case("localhost") {
                return None;
            }
            path
        }
        None => rest,
    };
    let path = decoded(path);
    // Windows writes a drive as `file:///C:/…`, with a slash before the
    // drive letter that is not part of the path.
    let bytes = path.as_bytes();
    let has_drive = bytes.len() >= 3 && bytes[0] == b'/' && bytes[1].is_ascii_alphabetic() && bytes[2] == b':';
    Some(PathBuf::from(if cfg!(windows) && has_drive { &path[1..] } else { &path[..] }))
}

fn decoded(path: &str) -> String {
    String::from_utf8_lossy(&percent_decode(path)).into_owned()
}

fn mime_for(path: &Path) -> Option<&'static str> {
    let extension = path.extension()?.to_str()?.to_ascii_lowercase();
    IMAGE_TYPES.iter().find(|(known, _)| *known == extension).map(|(_, mime)| *mime)
}

fn extension_for(mime: &str) -> &'static str {
    IMAGE_TYPES.iter().find(|(_, known)| *known == mime).map_or("bin", |(extension, _)| extension)
}

impl Store {
    /// Read the local pictures a freshly converted document refers to into
    /// the image store. One that is missing, too large, or not a picture is
    /// left as the document wrote it.
    fn adopt_local_images(&self, node: &mut Node, folder: &Path) {
        if node.kind == "image" {
            let file = node.attrs.get("src").and_then(|src| src.as_str()).and_then(|src| local_image_file(src, folder));
            let stored = file.and_then(|file| {
                let small_enough = std::fs::metadata(&file).is_ok_and(|meta| meta.len() <= MAX_IMAGE_BYTES);
                let bytes = small_enough.then(|| std::fs::read(&file).ok()).flatten()?;
                self.store_image(&bytes, mime_for(&file)?).ok()
            });
            if let Some(image) = stored {
                node.attrs.insert("src".to_string(), image.url.into());
            }
        }
        for child in &mut node.content {
            self.adopt_local_images(child, folder);
        }
    }

    /// Create a page from a Markdown or HTML file. The page is named for the
    /// file, and images the file refers to beside it are brought into the
    /// image store.
    pub fn import_file(&self, path: &str, parent_id: Option<&str>) -> Result<Note> {
        let path = Path::new(path);
        let text = String::from_utf8_lossy(&std::fs::read(path)?).into_owned();
        let is_html = path
            .extension()
            .and_then(|extension| extension.to_str())
            .is_some_and(|extension| matches!(extension.to_ascii_lowercase().as_str(), "html" | "htm"));
        let mut document: Document = if is_html {
            html_to_doc(&text, HtmlOptions { collapse_deep_headings: true })
        } else {
            markdown_to_doc(&text)
        };
        self.adopt_local_images(&mut document.root, path.parent().unwrap_or(Path::new(".")));
        document.ensure_block_ids();

        // ".md" has no name before the dot. Rust calls the whole of it the stem.
        let title = path
            .file_stem()
            .and_then(|stem| stem.to_str())
            .map(str::trim)
            .filter(|stem| !stem.is_empty() && !stem.starts_with('.'));
        self.create_note(NewNote {
            kind: Some(NoteKind::Page),
            parent_id: parent_id.map(String::from),
            title: title.map(String::from),
            icon: None,
            content: Some(document.to_json()),
        })
    }

    /// Write a note's content to `destination` as Markdown. Its images are
    /// copied into a folder beside the file and linked from there.
    pub fn export_markdown(&self, note_id: &str, destination: &str) -> Result<()> {
        let stored = self.get_document(note_id)?.ok_or_else(|| CoreError::not_found("note", note_id))?;
        let mut document = Document::from_json(&stored.content)?;

        let destination = Path::new(destination);
        let folder = destination.parent().unwrap_or(Path::new("."));
        std::fs::create_dir_all(folder)?;
        let assets = destination.with_extension("assets");
        self.copy_images_out(&mut document.root, &assets)?;
        std::fs::write(destination, doc_to_markdown(&document))?;
        Ok(())
    }

    /// Copy each stored image a document shows into `assets`, and point the
    /// document at the copy by a path relative to the exported file.
    fn copy_images_out(&self, node: &mut Node, assets: &Path) -> Result<()> {
        if node.kind == "image" {
            let src = node.attrs.get("src").and_then(|src| src.as_str()).unwrap_or_default();
            let image = if image_hash(src).is_some() { self.get_image(src)? } else { None };
            let file = match &image {
                Some(image) => self.image_path(&image.hash)?,
                None => None,
            };
            if let (Some(image), Some(file)) = (image, file) {
                std::fs::create_dir_all(assets)?;
                let name = format!("{}.{}", image.hash, extension_for(&image.mime));
                std::fs::copy(file, assets.join(&name))?;
                let folder = assets.file_name().map(|folder| folder.to_string_lossy()).unwrap_or_default();
                node.attrs.insert("src".to_string(), format!("{folder}/{name}").into());
            }
        }
        for child in &mut node.content {
            self.copy_images_out(child, assets)?;
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_file_url_with_another_machine_in_it_is_not_local() {
        assert_eq!(file_url_path("//fileserver/share/pic.png"), None);
    }

    #[test]
    fn percent_encoding_is_undone() {
        let path = file_url_path("///tmp/my%20pic.png").unwrap();
        assert!(path.to_string_lossy().ends_with("my pic.png"));
    }

    #[cfg(unix)]
    #[test]
    fn unix_file_urls_name_absolute_paths() {
        for url in ["///tmp/a/pic.png", "//localhost/tmp/a/pic.png", "/tmp/a/pic.png"] {
            assert_eq!(file_url_path(url), Some(PathBuf::from("/tmp/a/pic.png")), "{url}");
        }
    }

    #[cfg(windows)]
    #[test]
    fn windows_file_urls_name_a_drive() {
        for url in ["///C:/Users/a/pic.png", "//localhost/C:/Users/a/pic.png", "/C:/Users/a/pic.png"] {
            assert_eq!(file_url_path(url), Some(PathBuf::from("C:/Users/a/pic.png")), "{url}");
        }
        assert_eq!(file_url_path("///c:/a/pic.png"), Some(PathBuf::from("c:/a/pic.png")));
    }
}
