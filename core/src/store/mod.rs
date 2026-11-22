//! The store: one SQLite database and one image folder, behind one lock.
//!
//! Every user action is one write transaction. Either all of it is stored or
//! none of it is, and its changes reach subscribers only after it commits.

mod images;
mod notes;
mod schema;
mod search;
mod tasks;

pub(crate) use images::{image_hash, percent_decode};
pub(crate) use tasks::string_list;

use crate::changes::{Change, ChangeObserver};
use crate::error::{CoreError, Result};
use rusqlite::{Connection, OptionalExtension, Transaction, TransactionBehavior};
use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard, PoisonError};

/// The database file inside the data directory. The Electron app's file is
/// `heracles.sqlite`, so the two can share a directory.
pub const DATABASE_FILE: &str = "heracles.db";

/// Owns the data. One per process, shared by every window.
#[derive(uniffi::Object)]
pub struct Store {
    connection: Mutex<Connection>,
    data_dir: PathBuf,
    feed: Feed,
}

impl Store {
    /// Open the library in `data_dir`, creating it on first use.
    pub fn open(data_dir: impl AsRef<Path>) -> Result<Store> {
        let data_dir = data_dir.as_ref().to_path_buf();
        std::fs::create_dir_all(&data_dir)?;
        let mut connection = Connection::open(data_dir.join(DATABASE_FILE))?;
        schema::prepare(&mut connection)?;

        let store = Store { connection: Mutex::new(connection), data_dir, feed: Feed::default() };
        store.write(|tx, changes| notes::ensure_notebook(tx, changes))?;
        Ok(store)
    }

}

#[uniffi::export]
impl Store {
    /// Start receiving changes. The returned number cancels the subscription.
    pub fn subscribe(&self, observer: Arc<dyn ChangeObserver>) -> u64 {
        let id = self.feed.next_subscription.fetch_add(1, Ordering::Relaxed) + 1;
        lock(&self.feed.observers).push((id, observer));
        id
    }

    pub fn unsubscribe(&self, subscription: u64) {
        lock(&self.feed.observers).retain(|(id, _)| *id != subscription);
    }
}

impl Store {
    pub(crate) fn data_dir(&self) -> &Path {
        &self.data_dir
    }

    /// Run a read against the database.
    pub(crate) fn read<T>(&self, work: impl FnOnce(&Connection) -> Result<T>) -> Result<T> {
        work(&lock(&self.connection))
    }

    /// Run one user action as one transaction. `work` records what it
    /// touched in `Changes`. If it fails, nothing is stored and nothing is
    /// announced.
    pub(crate) fn write<T>(&self, work: impl FnOnce(&Transaction, &mut Changes) -> Result<T>) -> Result<T> {
        let value = {
            let mut connection = lock(&self.connection);
            let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
            let mut changes = Changes::default();
            let value = work(&tx, &mut changes)?;
            tx.commit()?;
            // Queued while the database lock is still held, so batches line
            // up in commit order.
            self.feed.enqueue(changes.into_batch());
            value
        };
        self.feed.deliver();
        Ok(value)
    }
}

/// Lock a mutex, carrying on if another thread panicked while holding it. A
/// transaction that was open at the time has been rolled back, so the data
/// behind the lock is still consistent.
fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(PoisonError::into_inner)
}

/// What one write touched. Collected during the transaction and turned into
/// a batch for the feed once it commits.
#[derive(Default)]
pub(crate) struct Changes {
    notes: Vec<String>,
    deleted: Vec<String>,
    documents: Vec<(String, i64, Option<String>)>,
    boards: Vec<String>,
}

impl Changes {
    pub(crate) fn note(&mut self, id: &str) {
        if !self.notes.iter().any(|known| known == id) {
            self.notes.push(id.to_string());
        }
    }

    pub(crate) fn notes<'a>(&mut self, ids: impl IntoIterator<Item = &'a String>) {
        for id in ids {
            self.note(id);
        }
    }

    pub(crate) fn deleted<'a>(&mut self, ids: impl IntoIterator<Item = &'a String>) {
        for id in ids {
            if !self.deleted.contains(id) {
                self.deleted.push(id.clone());
            }
        }
    }

    pub(crate) fn document(&mut self, note_id: &str, revision: i64, origin: Option<&str>) {
        self.documents.push((note_id.to_string(), revision, origin.map(String::from)));
    }

    pub(crate) fn board(&mut self, todo_list_id: &str) {
        if !self.boards.iter().any(|known| known == todo_list_id) {
            self.boards.push(todo_list_id.to_string());
        }
    }

    fn into_batch(self) -> Vec<Change> {
        let Changes { mut notes, deleted, documents, mut boards } = self;
        // A note deleted in this write has nothing left to refresh.
        notes.retain(|id| !deleted.contains(id));
        boards.retain(|id| !deleted.contains(id));

        let mut batch = Vec::new();
        if !notes.is_empty() {
            batch.push(Change::NotesChanged { ids: notes });
        }
        for (note_id, revision, origin) in documents {
            if !deleted.contains(&note_id) {
                batch.push(Change::DocumentChanged { note_id, revision, origin });
            }
        }
        for todo_list_id in boards {
            batch.push(Change::BoardChanged { todo_list_id });
        }
        if !deleted.is_empty() {
            batch.push(Change::NotesDeleted { ids: deleted });
        }
        batch
    }
}

/// Delivers batches to observers in commit order, one at a time, with no
/// lock held while an observer runs.
#[derive(Default)]
struct Feed {
    observers: Mutex<Vec<(u64, Arc<dyn ChangeObserver>)>>,
    next_subscription: AtomicU64,
    queue: Mutex<Queue>,
}

#[derive(Default)]
struct Queue {
    batches: VecDeque<Vec<Change>>,
    delivering: bool,
}

impl Feed {
    fn enqueue(&self, batch: Vec<Change>) {
        if !batch.is_empty() {
            lock(&self.queue).batches.push_back(batch);
        }
    }

    /// Hand queued batches to the observers. If another thread is already
    /// delivering, or this is a write made from inside an observer, the batch
    /// stays queued and the delivery in progress picks it up.
    fn deliver(&self) {
        loop {
            let batch = {
                let mut queue = lock(&self.queue);
                if queue.delivering {
                    return;
                }
                let Some(batch) = queue.batches.pop_front() else { return };
                queue.delivering = true;
                batch
            };
            // Clears the flag even if an observer panics.
            let _delivering = Delivering(&self.queue);
            let observers: Vec<Arc<dyn ChangeObserver>> =
                lock(&self.observers).iter().map(|(_, observer)| Arc::clone(observer)).collect();
            for observer in observers {
                observer.on_changes(batch.clone());
            }
        }
    }
}

struct Delivering<'a>(&'a Mutex<Queue>);

impl Drop for Delivering<'_> {
    fn drop(&mut self) {
        lock(self.0).delivering = false;
    }
}

/// The current time in UTC as JavaScript's `toISOString` writes it, which is
/// the format the Electron app stored.
pub(crate) fn now() -> String {
    timestamp(time::OffsetDateTime::now_utc())
}

/// A UTC time in the stored format. These strings sort in time order.
pub(crate) fn timestamp(time: time::OffsetDateTime) -> String {
    format!(
        "{:04}-{:02}-{:02}T{:02}:{:02}:{:02}.{:03}Z",
        time.year(),
        u8::from(time.month()),
        time.day(),
        time.hour(),
        time.minute(),
        time.second(),
        time.millisecond()
    )
}

pub(crate) fn new_id() -> String {
    uuid::Uuid::new_v4().to_string()
}

/// Fetch one optional row, turning "no rows" into `None`.
pub(crate) fn query_optional<T>(
    connection: &Connection,
    sql: &str,
    params: impl rusqlite::Params,
    map: impl FnOnce(&rusqlite::Row<'_>) -> rusqlite::Result<T>,
) -> Result<Option<T>> {
    Ok(connection.query_row(sql, params, map).optional()?)
}

/// Fetch a list of ids.
pub(crate) fn query_ids(connection: &Connection, sql: &str, params: impl rusqlite::Params) -> Result<Vec<String>> {
    let mut statement = connection.prepare(sql)?;
    let ids = statement.query_map(params, |row| row.get(0))?.collect::<rusqlite::Result<Vec<String>>>()?;
    Ok(ids)
}

impl From<CoreError> for rusqlite::Error {
    /// Lets a row mapper report a core error through rusqlite's own type.
    fn from(error: CoreError) -> rusqlite::Error {
        rusqlite::Error::ToSqlConversionFailure(Box::new(error))
    }
}
