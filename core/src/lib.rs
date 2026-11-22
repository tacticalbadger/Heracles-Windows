//! heracles-core: the data layer of the native Heracles app.
//!
//! The Swift interface never touches SQLite or note content directly. It calls
//! this crate, which owns storage, the block document model, search, import,
//! the image store, and the change feed that keeps every window current.

uniffi::setup_scaffolding!();

pub mod changes;
pub mod doc;
pub mod error;
mod ffi;
mod legacy;
pub mod model;
pub mod store;
mod transfer;

pub use changes::{Change, ChangeObserver};
pub use error::{CoreError, Result};
pub use model::*;
pub use store::Store;
pub use transfer::html_to_document;
