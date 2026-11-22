import Foundation
import HeraclesCore
import Observation

/// The latest save of a note's content, as the change feed reported it.
public struct DocumentChange: Equatable, Sendable {
    public var revision: Int64
    /// Whatever the saver passed. An editor passes its own id, so it can
    /// tell its own save from another window's.
    public var origin: String?
}

/// The app's one view of the library, shared by every window.
///
/// Nothing here is the source of truth. The core is. This model keeps a copy
/// of the note list for the views to read, and replaces it whenever the
/// core's change feed says something changed, whoever changed it. That is
/// what keeps two windows in step without either knowing about the other.
@MainActor
@Observable
public final class AppModel {
    public let store: Store
    public private(set) var library: Library
    /// Why the last action failed, in words for the user. `nil` after a success.
    public var lastError: String?
    /// A link the app was asked to open. The frontmost window takes it.
    public var pendingLink: AppLink?

    private var documentChanges: [String: DocumentChange] = [:]
    private var boardVersions: [String: Int] = [:]
    /// Called after each batch of changes, for work that follows the data
    /// without being shown: rescheduling reminders.
    @ObservationIgnored var afterChange: (() -> Void)?
    @ObservationIgnored private var feed: FeedObserver?
    @ObservationIgnored private let editors = NSHashTable<EditorCoordinator>.weakObjects()

    public init(store: Store) throws {
        self.store = store
        library = Library(notes: try store.listNotes())
        let feed = FeedObserver { [weak self] changes in self?.apply(changes) }
        self.feed = feed
        _ = store.subscribe(observer: feed)
    }

    // MARK: The change feed

    private func apply(_ changes: [Change]) {
        var notesChanged = false
        for change in changes {
            switch change {
            case .notesChanged:
                notesChanged = true
            case .notesDeleted(let ids):
                notesChanged = true
                for id in ids {
                    documentChanges[id] = nil
                    boardVersions[id] = nil
                }
            case .documentChanged(let noteID, let revision, let origin):
                // Batches from other threads can arrive out of step with
                // ones made here. The revision says which is newer.
                if revision >= documentChanges[noteID]?.revision ?? 0 {
                    documentChanges[noteID] = DocumentChange(revision: revision, origin: origin)
                }
            case .boardChanged(let todoListID):
                boardVersions[todoListID, default: 0] += 1
            }
        }
        if notesChanged, let notes = try? store.listNotes() {
            library = Library(notes: notes)
        }
        afterChange?()
    }

    /// The latest save of a note's content this session, if there was one.
    public func documentChange(for noteID: String) -> DocumentChange? { documentChanges[noteID] }

    /// A number that goes up each time a todo list's board changes. A board
    /// view reloads when it does.
    public func boardVersion(for todoListID: String) -> Int { boardVersions[todoListID] ?? 0 }

    // MARK: Actions

    /// Run a write. A failure is kept in `lastError` for the window to show.
    @discardableResult
    func attempt<T>(_ work: () throws -> T) -> T? {
        do {
            let value = try work()
            lastError = nil
            return value
        } catch {
            lastError = (error as? LocalizedError)?.errorDescription ?? String(describing: error)
            return nil
        }
    }

    @discardableResult
    public func createNote(_ kind: NoteKind, in parentID: String?, title: String? = nil) -> Note? {
        attempt { try store.createNote(new: NewNote(kind: kind, parentId: parentID, title: title)) }
    }

    public func rename(_ id: String, to title: String) {
        attempt { try store.renameNote(id: id, title: title) }
    }

    public func setIcon(_ id: String, to icon: String?) {
        attempt { try store.setNoteIcon(id: id, icon: icon) }
    }

    public func setFavorited(_ id: String, _ favorited: Bool) {
        attempt { try store.setNoteFavorited(id: id, favorited: favorited) }
    }

    public func setArchived(_ id: String, _ archived: Bool) {
        attempt { try store.setNoteArchived(id: id, archived: archived) }
    }

    public func trash(_ id: String) {
        attempt { try store.trashNote(id: id) }
    }

    public func restore(_ id: String) {
        attempt { try store.restoreNote(id: id) }
    }

    /// Delete for good. Returns every id that went, so views can let go of them.
    @discardableResult
    public func delete(_ id: String) -> [String] {
        attempt { try store.deleteNote(id: id) } ?? []
    }

    @discardableResult
    public func emptyTrash() -> [String] {
        attempt { try store.emptyTrash() } ?? []
    }

    public func move(_ id: String, to parentID: String?, before siblingID: String? = nil) {
        attempt { try store.moveNote(id: id, parentId: parentID, beforeId: siblingID) }
    }

    @discardableResult
    public func duplicate(_ id: String) -> Note? {
        attempt { try store.duplicateNote(id: id) }
    }

    public func document(for noteID: String) -> StoredDocument? {
        attempt { try store.getDocument(noteId: noteID) } ?? nil
    }

    public func saveDocument(_ noteID: String, content: String, origin: String?) {
        attempt { try store.saveDocument(noteId: noteID, content: content, origin: origin) }
    }

    public func search(_ query: String) -> [SearchHit] {
        (try? store.searchNotes(query: query, limit: 50)) ?? []
    }

    // MARK: Editors

    func register(_ editor: EditorCoordinator) {
        editors.add(editor)
    }

    /// Have every open editor send any change it is still holding, and wait
    /// until each has. Called before the app quits, so the last thing typed
    /// is saved.
    public func flushEditors() async {
        for editor in editors.allObjects {
            await editor.flush()
        }
    }
}

/// Carries the core's change feed onto the main actor. The core calls
/// `onChanges` on whichever thread made the write.
private final class FeedObserver: ChangeObserver, @unchecked Sendable {
    private let handler: @MainActor ([Change]) -> Void

    init(handler: @escaping @MainActor ([Change]) -> Void) {
        self.handler = handler
    }

    func onChanges(changes: [Change]) {
        if Thread.isMainThread {
            // Already there, so the views are current by the time the call
            // that made the write returns.
            MainActor.assumeIsolated { handler(changes) }
        } else {
            let handler = handler
            Task { @MainActor in handler(changes) }
        }
    }
}
