//! The one error type every core call returns.

use crate::doc::DocumentError;

pub type Result<T> = std::result::Result<T, CoreError>;

// Crosses to Swift as its case plus the message below.
#[derive(Debug, thiserror::Error, uniffi::Error)]
#[uniffi(flat_error)]
pub enum CoreError {
    /// The id names nothing. `kind` says what was looked for.
    #[error("{kind} `{id}` does not exist")]
    NotFound { kind: String, id: String },
    /// The request cannot be carried out as given.
    #[error("{0}")]
    Invalid(String),
    /// Every other notebook is gone, and content must live in a notebook.
    #[error("the last notebook cannot be deleted")]
    LastNotebook,
    #[error("database error: {0}")]
    Database(#[from] rusqlite::Error),
    #[error("file error: {0}")]
    Io(#[from] std::io::Error),
    #[error(transparent)]
    Document(#[from] DocumentError),
}

impl CoreError {
    pub(crate) fn not_found(kind: &str, id: &str) -> CoreError {
        CoreError::NotFound { kind: kind.to_string(), id: id.to_string() }
    }

    pub(crate) fn invalid(message: impl Into<String>) -> CoreError {
        CoreError::Invalid(message.into())
    }
}
