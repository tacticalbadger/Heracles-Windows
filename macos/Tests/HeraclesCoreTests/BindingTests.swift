import Foundation
import Testing

@testable import HeraclesCore

/// These tests go through the generated bindings into the Rust core. The
/// core's own behaviour is tested in Rust. What is checked here is that each
/// kind of value survives the crossing: records, optionals, nested optionals,
/// bytes, errors, and callbacks.
struct BindingTests {
    let directory: URL
    let store: Store

    init() throws {
        directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("heracles-binding-tests-\(UUID().uuidString)")
        store = try Store(dataDir: directory.path)
    }

    private func document(_ text: String) -> String {
        #"{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"\#(text)"}]}]}"#
    }

    @Test func aNewLibraryHasItsDefaultNotebook() throws {
        let notes = try store.listNotes()
        #expect(notes.count == 1)
        #expect(notes[0].kind == .notebook)
        #expect(notes[0].parentId == nil)
        #expect(FileManager.default.fileExists(atPath: directory.appendingPathComponent("heracles.db").path))
    }

    @Test func recordsWithDefaultsAndOptionalsCross() throws {
        let page = try store.createNote(new: NewNote(title: "Trip", icon: "🧭"))
        #expect(page.kind == .page)
        #expect(page.title == "Trip")
        #expect(page.icon == "🧭")

        let cleared = try store.setNoteIcon(id: page.id, icon: nil)
        #expect(cleared.icon == nil)

        let section = try store.createNote(new: NewNote(kind: .section, title: "Plans"))
        let moved = try store.moveNote(id: page.id, parentId: section.id, beforeId: nil)
        #expect(moved.parentId == section.id)
    }

    @Test func documentsSaveAndSearch() throws {
        let page = try store.createNote(new: NewNote(title: "Trip"))
        let saved = try store.saveDocument(noteId: page.id, content: document("pack the lantern"), origin: "test")
        #expect(saved.revision == 1)
        #expect(try store.getDocument(noteId: page.id) == saved)

        let hits = try store.searchNotes(query: "lant", limit: 5)
        #expect(hits.map(\.note.id) == [page.id])
        #expect(hits[0].snippet.filter(\.matched).map(\.text) == ["lantern"])
    }

    @Test func errorsArriveAsTheirCase() throws {
        #expect {
            try store.renameNote(id: "missing", title: "x")
        } throws: { error in
            guard case CoreError.NotFound = error else { return false }
            return true
        }
        let notebook = try store.listNotes()[0]
        #expect {
            try store.deleteNote(id: notebook.id)
        } throws: { error in
            guard case CoreError.LastNotebook = error else { return false }
            return (error as? LocalizedError)?.errorDescription?.contains("last notebook") == true
        }
    }

    @Test func nestedOptionalsTellClearingFromLeavingAlone() throws {
        let list = try store.createNote(new: NewNote(kind: .todoList))
        let column = try store.listColumns(todoListId: list.id)[0]
        let task = try store.createTask(new: NewTask(todoListId: list.id, columnId: column.id, title: "Book ferry"))
        #expect(task.priority == 4)

        let dated = try store.updateTask(
            id: task.id,
            patch: TaskPatch(dueDate: .some("2026-12-01"), labels: ["travel"], location: .some("Harbour")))
        #expect(dated.dueDate == "2026-12-01")
        #expect(dated.labels == ["travel"])

        let cleared = try store.updateTask(id: task.id, patch: TaskPatch(dueDate: .some(nil)))
        #expect(cleared.dueDate == nil)
        #expect(cleared.location == "Harbour", "a field left out of the patch is untouched")

        let moved = try store.moveTask(id: task.id, columnId: column.id, parentTaskId: nil, beforeId: nil)
        #expect(moved.columnId == column.id)
    }

    @Test func imageBytesCross() throws {
        let bytes = Data([0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF])
        let image = try store.storeImage(bytes: bytes, mime: "image/png")
        #expect(image.url == "\(imageUrlScheme())://local/\(image.hash)")
        let path = try #require(try store.imagePath(hashOrUrl: image.url))
        #expect(try Data(contentsOf: URL(fileURLWithPath: path)) == bytes)
    }

    @Test func aSwiftObserverReceivesTheChangeFeed() throws {
        final class Recorder: ChangeObserver, @unchecked Sendable {
            private let lock = NSLock()
            private var batches: [[Change]] = []
            func onChanges(changes: [Change]) {
                lock.withLock { batches.append(changes) }
            }
            func take() -> [[Change]] {
                lock.withLock {
                    defer { batches = [] }
                    return batches
                }
            }
        }
        let recorder = Recorder()
        let subscription = store.subscribe(observer: recorder)

        let page = try store.createNote(new: NewNote())
        #expect(recorder.take() == [[.notesChanged(ids: [page.id])]])

        _ = try store.saveDocument(noteId: page.id, content: document("x"), origin: "editor-1")
        #expect(
            recorder.take() == [[
                .notesChanged(ids: [page.id]),
                .documentChanged(noteId: page.id, revision: 1, origin: "editor-1"),
            ]])

        store.unsubscribe(subscription: subscription)
        _ = try store.createNote(new: NewNote())
        #expect(recorder.take().isEmpty)
    }
}
