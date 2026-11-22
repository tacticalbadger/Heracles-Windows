import Foundation
import HeraclesCore
import Testing

@testable import HeraclesApp

/// The model against a real core in a throwaway folder.
@MainActor
struct AppModelTests {
    let store: Store
    let model: AppModel

    init() throws {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("heracles-model-tests-\(UUID().uuidString)")
        store = try Store(dataDir: directory.path)
        model = try AppModel(store: store)
    }

    private var notebook: Note { model.library.notebooks[0].note }

    private func document(_ text: String) -> String {
        #"{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"\#(text)"}]}]}"#
    }

    @Test func itStartsWithTheLibraryAsStored() {
        #expect(model.library.notebooks.count == 1)
        #expect(notebook.kind == .notebook)
    }

    @Test func itsOwnActionsShowUpInTheLibrary() throws {
        let page = try #require(model.createNote(.page, in: notebook.id))
        #expect(model.library.notebooks[0].children.map(\.id) == [page.id])

        model.rename(page.id, to: "Renamed")
        #expect(model.library.note(page.id)?.title == "Renamed")

        model.trash(page.id)
        #expect(model.library.notebooks[0].children.isEmpty)
        #expect(model.library.trashed.map(\.id) == [page.id])
    }

    @Test func aWriteMadeElsewhereShowsUpWithoutAsking() throws {
        // Another window, or the editor, writes straight to the core.
        let page = try store.createNote(new: NewNote(title: "From elsewhere"))
        #expect(model.library.note(page.id)?.title == "From elsewhere")

        _ = try store.deleteNote(id: page.id)
        #expect(model.library.note(page.id) == nil)
    }

    @Test func aWriteFromAnotherThreadArrivesOnTheMainActor() async throws {
        let store = self.store
        let id = try await Task.detached { try store.createNote(new: NewNote(title: "Background")).id }.value
        // The change is delivered on the writing thread and hops to the main actor.
        for _ in 0..<50 where model.library.note(id) == nil {
            try await Task.sleep(for: .milliseconds(10))
        }
        #expect(model.library.note(id)?.title == "Background")
    }

    @Test func savedDocumentsAreAnnouncedWithWhoSavedThem() throws {
        let page = try #require(model.createNote(.page, in: notebook.id))
        #expect(model.documentChange(for: page.id) == nil)

        model.saveDocument(page.id, content: document("one"), origin: "editor-A")
        #expect(model.documentChange(for: page.id) == DocumentChange(revision: 1, origin: "editor-A"))

        _ = try store.saveDocument(noteId: page.id, content: document("two"), origin: nil)
        #expect(model.documentChange(for: page.id) == DocumentChange(revision: 2, origin: nil))
    }

    @Test func boardChangesBumpThatBoardOnly() throws {
        let list = try #require(model.createNote(.todoList, in: notebook.id))
        let other = try #require(model.createNote(.todoList, in: notebook.id))
        let before = (model.boardVersion(for: list.id), model.boardVersion(for: other.id))

        let column = try store.listColumns(todoListId: list.id)[0]
        _ = try store.createTask(new: NewTask(todoListId: list.id, columnId: column.id, title: "x"))

        #expect(model.boardVersion(for: list.id) == before.0 + 1)
        #expect(model.boardVersion(for: other.id) == before.1)
    }

    @Test func aFailedActionIsReportedAndChangesNothing() {
        #expect(model.lastError == nil)
        model.rename("missing", to: "x")
        #expect(model.lastError?.contains("does not exist") == true)

        model.delete(notebook.id)
        #expect(model.lastError?.contains("last notebook") == true)
        #expect(model.library.notebooks.count == 1)
    }

    @Test func deletingANoteReportsEveryIDThatWent() throws {
        let page = try #require(model.createNote(.page, in: notebook.id))
        let child = try #require(model.createNote(.page, in: page.id))
        #expect(Set(model.delete(page.id)) == [page.id, child.id])
        #expect(model.library.note(child.id) == nil)
    }
}
