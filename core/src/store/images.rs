//! The image store: files named by the hash of their bytes.
//!
//! Documents refer to an image by URL and never hold its bytes, so a page
//! full of screenshots costs nothing to list, search, or save.

use super::{Store, now, query_ids, query_optional, timestamp};
use crate::doc::Node;
use crate::error::{CoreError, Result};
use crate::model::{IMAGE_URL_SCHEME, StoredImage};
use base64::Engine;
use rusqlite::{Connection, params};
use sha2::{Digest, Sha256};
use std::path::PathBuf;

/// The folder inside the data directory that holds the image files.
const IMAGES_DIR: &str = "images";

fn image_url(hash: &str) -> String {
    format!("{IMAGE_URL_SCHEME}://local/{hash}")
}

/// The hash a lookup names, given either the hash itself or an image URL.
/// Anything that is not exactly a lowercase SHA-256 is rejected, which is
/// also what keeps a lookup from naming a path outside the image folder.
pub(crate) fn image_hash(hash_or_url: &str) -> Option<&str> {
    let prefix = format!("{IMAGE_URL_SCHEME}://local/");
    let hash = hash_or_url.strip_prefix(&prefix).unwrap_or(hash_or_url);
    let valid = hash.len() == 64 && hash.bytes().all(|byte| matches!(byte, b'0'..=b'9' | b'a'..=b'f'));
    valid.then_some(hash)
}

fn is_image_mime(mime: &str) -> bool {
    mime.strip_prefix("image/").is_some_and(|subtype| {
        !subtype.is_empty() && subtype.chars().all(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '.' | '+' | '-'))
    })
}

/// The bytes and type of an image embedded as a `data:` URL, or `None` if
/// `url` is not one or cannot be decoded.
fn decode_data_url(url: &str) -> Option<(String, Vec<u8>)> {
    let rest = url.get(..5).filter(|scheme| scheme.eq_ignore_ascii_case("data:")).map(|_| &url[5..])?;
    let (header, payload) = rest.split_once(',')?;
    let mut parameters = header.split(';').map(str::trim);
    let mime = parameters.next()?.to_ascii_lowercase();
    if !is_image_mime(&mime) {
        return None;
    }
    let bytes = if parameters.any(|parameter| parameter.eq_ignore_ascii_case("base64")) {
        let compact: Vec<u8> = percent_decode(payload).into_iter().filter(|byte| !byte.is_ascii_whitespace()).collect();
        let engines = [base64::engine::general_purpose::STANDARD, base64::engine::general_purpose::STANDARD_NO_PAD];
        engines.iter().find_map(|engine| engine.decode(&compact).ok())?
    } else {
        percent_decode(payload)
    };
    (!bytes.is_empty()).then_some((mime, bytes))
}

pub(crate) fn percent_decode(text: &str) -> Vec<u8> {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        let escaped = (bytes[index] == b'%')
            .then(|| text.get(index + 1..index + 3))
            .flatten()
            .and_then(|hex| u8::from_str_radix(hex, 16).ok());
        match escaped {
            Some(byte) => {
                out.push(byte);
                index += 3;
            }
            None => {
                out.push(bytes[index]);
                index += 1;
            }
        }
    }
    out
}

impl Store {
    fn image_file(&self, hash: &str) -> PathBuf {
        // Two levels, so no single folder ends up with every image in it.
        self.data_dir.join(IMAGES_DIR).join(&hash[..2]).join(hash)
    }

    fn find_image(&self, connection: &Connection, hash_or_url: &str) -> Result<Option<StoredImage>> {
        let Some(hash) = image_hash(hash_or_url) else { return Ok(None) };
        query_optional(connection, "SELECT hash, mime, byte_count FROM images WHERE hash = ?1", [hash], |row| {
            let hash: String = row.get(0)?;
            Ok(StoredImage { url: image_url(&hash), hash, mime: row.get(1)?, byte_count: row.get(2)? })
        })
    }

    /// Write an image's file and its row. For use inside a transaction.
    fn put_image(&self, connection: &Connection, bytes: &[u8], mime: &str) -> Result<StoredImage> {
        let mime = mime.trim().to_ascii_lowercase();
        if !is_image_mime(&mime) {
            return Err(CoreError::invalid(format!("`{mime}` is not an image type")));
        }
        if bytes.is_empty() {
            return Err(CoreError::invalid("an image cannot be empty"));
        }
        let hash: String = Sha256::digest(bytes).iter().map(|byte| format!("{byte:02x}")).collect();

        let file = self.image_file(&hash);
        if !file.exists() {
            let folder = file.parent().expect("an image file always has a folder");
            std::fs::create_dir_all(folder)?;
            // Written under another name first, so a crash cannot leave half
            // an image under a name that promises all of it.
            let partial = folder.join(format!("{hash}.{}.partial", uuid::Uuid::new_v4()));
            std::fs::write(&partial, bytes)?;
            std::fs::rename(&partial, &file)?;
        }
        connection.execute(
            "INSERT OR IGNORE INTO images (hash, mime, byte_count, created_at) VALUES (?1, ?2, ?3, ?4)",
            params![hash, mime, bytes.len() as i64, now()],
        )?;
        Ok(self.find_image(connection, &hash)?.expect("the image row was just written"))
    }

    /// Move every image embedded in a document into the store, leaving its
    /// URL behind. An embedded image that cannot be decoded is left as it is.
    pub(crate) fn externalize_images(&self, connection: &Connection, node: &mut Node) -> Result<()> {
        if node.kind == "image" {
            let embedded = node.attrs.get("src").and_then(|src| src.as_str()).and_then(decode_data_url);
            if let Some((mime, bytes)) = embedded {
                let image = self.put_image(connection, &bytes, &mime)?;
                node.attrs.insert("src".to_string(), image.url.into());
            }
        }
        for child in &mut node.content {
            self.externalize_images(connection, child)?;
        }
        Ok(())
    }

}

#[uniffi::export]
impl Store {
    /// Add an image and return its record. Storing the same bytes again
    /// returns the same record.
    pub fn store_image(&self, bytes: &[u8], mime: &str) -> Result<StoredImage> {
        self.write(|tx, _| self.put_image(tx, bytes, mime))
    }

    /// The record for a stored image, by hash or by URL.
    pub fn get_image(&self, hash_or_url: &str) -> Result<Option<StoredImage>> {
        self.read(|connection| self.find_image(connection, hash_or_url))
    }

    /// Where a stored image's file is, by hash or by URL.
    pub fn image_path(&self, hash_or_url: &str) -> Result<Option<String>> {
        let Some(image) = self.get_image(hash_or_url)? else { return Ok(None) };
        let file = self.image_file(&image.hash);
        Ok(file.exists().then(|| file.to_string_lossy().into_owned()))
    }

    /// Delete images that no document uses and that were stored more than
    /// `min_age_seconds` ago. Returns the hashes removed.
    ///
    /// Run this at launch, not during a session: an image cut from a page is
    /// unused until the cut is undone.
    pub fn collect_unused_images(&self, min_age_seconds: u64) -> Result<Vec<String>> {
        let age = time::Duration::seconds(i64::try_from(min_age_seconds).unwrap_or(i64::MAX));
        let cutoff = timestamp(time::OffsetDateTime::now_utc().saturating_sub(age));
        let removed = self.write(|tx, _| {
            let unused = query_ids(
                tx,
                "SELECT hash FROM images
                 WHERE created_at <= ?1 AND hash NOT IN (SELECT hash FROM document_images)
                 ORDER BY hash",
                [&cutoff],
            )?;
            for hash in &unused {
                tx.execute("DELETE FROM images WHERE hash = ?1", [hash])?;
            }
            Ok(unused)
        })?;
        // Files go only after the rows are gone for good. A file left behind
        // by a failure here is harmless. A row without its file is not.
        for hash in &removed {
            match std::fs::remove_file(self.image_file(hash)) {
                Err(error) if error.kind() != std::io::ErrorKind::NotFound => return Err(error.into()),
                _ => {}
            }
        }
        Ok(removed)
    }
}

/// The hashes of the stored images a document shows.
pub(crate) fn referenced_images(node: &Node, hashes: &mut Vec<String>) {
    if node.kind == "image" {
        let hash = node.attrs.get("src").and_then(|src| src.as_str()).and_then(|src| {
            // Only a URL counts. A bare hash in `src` is not an image link.
            src.contains("://").then(|| image_hash(src)).flatten()
        });
        if let Some(hash) = hash.filter(|hash| !hashes.iter().any(|known| known == hash)) {
            hashes.push(hash.to_string());
        }
    }
    for child in &node.content {
        referenced_images(child, hashes);
    }
}

#[cfg(test)]
mod tests {
    use super::{decode_data_url, image_hash, percent_decode};

    #[test]
    fn data_urls_decode_with_or_without_padding_and_whitespace() {
        assert_eq!(decode_data_url("data:image/png;base64,YWJj"), Some(("image/png".into(), b"abc".to_vec())));
        assert_eq!(decode_data_url("DATA:IMAGE/PNG;BASE64,YW\nJjZA=="), Some(("image/png".into(), b"abcd".to_vec())));
        assert_eq!(decode_data_url("data:image/png;base64,YWJjZA"), Some(("image/png".into(), b"abcd".to_vec())));
        assert_eq!(
            decode_data_url("data:image/svg+xml;charset=utf-8,%3Csvg/%3E"),
            Some(("image/svg+xml".into(), b"<svg/>".to_vec()))
        );
    }

    #[test]
    fn data_urls_that_are_not_images_do_not_decode() {
        for url in ["data:text/html;base64,YWJj", "data:;base64,YWJj", "data:image/png", "https://x/a.png", "dat"] {
            assert_eq!(decode_data_url(url), None, "{url}");
        }
    }

    #[test]
    fn percent_decoding_leaves_malformed_escapes_alone() {
        assert_eq!(percent_decode("a%20b%zz%4"), b"a b%zz%4".to_vec());
    }

    #[test]
    fn only_a_lowercase_sha256_names_an_image() {
        let hash = "ab".repeat(32);
        assert_eq!(image_hash(&hash), Some(hash.as_str()));
        assert_eq!(image_hash(&format!("heracles-image://local/{hash}")), Some(hash.as_str()));
        assert_eq!(image_hash(&hash.to_uppercase()), None);
        assert_eq!(image_hash(&format!("heracles-image://local/../{hash}")), None);
        assert_eq!(image_hash(&hash[1..]), None);
    }
}
