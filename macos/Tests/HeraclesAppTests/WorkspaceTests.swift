import Foundation
import HeraclesCore
import Testing

@testable import HeraclesApp

/// One window's state against a real core in a throwaway folder.
@MainActor
struct WorkspaceTests {
    let directory: URL
    let store: Store
    let model: AppModel
    let workspace: Workspace

    init() throws {
        directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("heracles-workspace-tests-\(UUID().uuidString)")
        store = try Store(dataDir: directory.appendingPathComponent("library").path)
        model = try AppModel(store: store)
        workspace = Workspace(model: model)
    }

    private var notebook: Note { model.library.notebooks[0].note }

    @Test func aNewNoteOpensInTheFocusedPane() throws {
        let page = try #require(workspace.createNote(.page))
        #expect(workspace.tree.activeNoteID == page.id)
        #expect(page.parentId == notebook.id, "with nothing selected it goes in the first notebook")

        let inside = try #require(workspace.createNote(.page))
        #expect(inside.parentId == page.id, "then inside the note that is open")

        let list = try #require(workspace.createNote(.todoList))
        let beside = try #require(workspace.createNote(.page))
        #expect(beside.parentId == list.parentId, "a todo list cannot hold notes, so the new one goes beside it")
    }

    @Test func openingWithSplitPutsTheNoteInANewPane() throws {
        let a = try #require(workspace.createNote(.page))
        let b = try #require(model.createNote(.page, in: notebook.id))
        workspace.open(b.id, inSplit: .right)
        #expect(workspace.tree.leaves.map(\.tabs) == [[a.id], [b.id]])
        #expect(workspace.tree.activeNoteID == b.id)
    }

    @Test func notesDeletedAnywhereLeaveThisWindowsPanes() throws {
        let a = try #require(workspace.createNote(.page))
        let b = try #require(model.createNote(.page, in: notebook.id))
        workspace.open(b.id, inSplit: .right)

        // Deleted through the core, as another window would.
        _ = try store.deleteNote(id: b.id)
        workspace.libraryChanged()

        #expect(workspace.tree.leaves.map(\.tabs) == [[a.id]])
    }

    @Test func closingTheActiveTabUsesTheFocusedPane() throws {
        let a = try #require(workspace.createNote(.page))
        let b = try #require(model.createNote(.page, in: notebook.id))
        workspace.open(b.id)
        workspace.closeActiveTab()
        #expect(workspace.tree.activeNoteID == a.id)
    }

    @Test func aLinkOpensItsNoteAndRemembersTheBlockToShow() throws {
        let page = try #require(model.createNote(.page, in: notebook.id))
        workspace.follow(AppLink(noteID: page.id, blockID: "block-7"))
        #expect(workspace.tree.activeNoteID == page.id)
        #expect(workspace.blockToReveal(in: page.id) == "block-7")

        workspace.follow(AppLink(noteID: "missing", blockID: nil))
        #expect(workspace.tree.activeNoteID == page.id, "a link to nothing changes nothing")
    }

    @Test func filesAreImportedIntoTheOpenNoteAndTheLastOneIsShown() throws {
        let section = try #require(workspace.createNote(.section))
        let folder = directory.appendingPathComponent("files")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let first = folder.appendingPathComponent("One.md")
        let second = folder.appendingPathComponent("Two.html")
        try "# One\n".write(to: first, atomically: true, encoding: .utf8)
        try "<p>Two</p>".write(to: second, atomically: true, encoding: .utf8)

        let imported = workspace.importFiles([first, second])

        #expect(imported.map(\.title) == ["One", "Two"])
        #expect(imported.allSatisfy { $0.parentId == section.id })
        #expect(workspace.tree.activeNoteID == imported[1].id)
    }

    @Test func theOpenNoteIsExportedAsMarkdown() throws {
        let page = try #require(workspace.createNote(.page))
        model.saveDocument(
            page.id,
            content: #"{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Exported"}]}]}"#,
            origin: nil)
        let destination = directory.appendingPathComponent("out/Page.md")

        #expect(workspace.exportActiveNote(to: destination))
        #expect(try String(contentsOf: destination, encoding: .utf8) == "Exported\n")
    }

    @Test func theSwitcherListsRecentNotesAndThenSearches() throws {
        let old = try #require(model.createNote(.page, in: notebook.id, title: "Older lantern notes"))
        Thread.sleep(forTimeInterval: 0.005)
        let recent = try #require(model.createNote(.page, in: notebook.id, title: "Newer"))
        model.trash(try #require(model.createNote(.page, in: notebook.id, title: "Trashed")).id)

        let recents = workspace.switcherResults(for: "  ").map(\.id)
        #expect(Array(recents.prefix(2)) == [recent.id, old.id], "most recently changed first")
        #expect(!recents.contains(notebook.id), "notebooks are not something to switch to")
        #expect(workspace.switcherResults(for: "trashed").isEmpty)
        #expect(workspace.switcherResults(for: "lant").map(\.id) == [old.id])
    }
}
