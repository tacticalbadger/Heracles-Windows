import HeraclesCore
import Testing

@testable import HeraclesApp

private func note(
    _ id: String, _ kind: NoteKind = .page, in parent: String? = nil, at position: Int64 = 0,
    favorited: Bool = false, archived: Bool = false, trashed: Bool = false, title: String? = nil
) -> Note {
    Note(
        id: id, kind: kind, parentId: parent, title: title ?? id, icon: nil, position: position,
        favorited: favorited, archived: archived, trashed: trashed,
        createdAt: "2026-01-01T00:00:00.000Z", updatedAt: "2026-01-01T00:00:00.000Z")
}

/// The ids of a tree, written as nested lists: "a[b[c] d]".
private func outline(_ nodes: [LibraryNode]) -> String {
    nodes.map { node in
        node.children.isEmpty ? node.id : "\(node.id)[\(outline(node.children))]"
    }.joined(separator: " ")
}

struct LibraryTests {
    @Test func theTreeFollowsParentsAndManualOrder() {
        let library = Library(notes: [
            note("page-b", in: "section", at: 1),
            note("nb-2", .notebook, at: 1),
            note("section", .section, in: "nb-1", at: 0),
            note("page-a", in: "section", at: 0),
            note("nb-1", .notebook, at: 0),
            note("child", in: "page-a", at: 0),
            note("list", .todoList, in: "nb-1", at: 1),
        ])
        #expect(outline(library.notebooks) == "nb-1[section[page-a[child] page-b] list] nb-2")
    }

    @Test func notesThatSharePositionAreOrderedByTitle() {
        let library = Library(notes: [
            note("nb", .notebook),
            note("z", in: "nb", at: 0, title: "zebra"),
            note("a", in: "nb", at: 0, title: "Apple"),
        ])
        #expect(outline(library.notebooks) == "nb[a z]")
    }

    @Test func trashedAndArchivedNotesLeaveTheTreeWithEverythingUnderThem() {
        let library = Library(notes: [
            note("nb", .notebook),
            note("kept", in: "nb", at: 0),
            note("trashed", in: "nb", at: 1, trashed: true),
            note("under-trashed", in: "trashed", trashed: true),
            note("archived", in: "nb", at: 2, archived: true),
            note("under-archived", in: "archived"),
        ])
        #expect(outline(library.notebooks) == "nb[kept]")
        #expect(library.archived.map(\.id) == ["archived"])
    }

    @Test func theTrashListsOnlyTheTopOfEachTrashedSubtree() {
        let library = Library(notes: [
            note("nb", .notebook),
            note("gone", in: "nb", trashed: true),
            note("gone-child", in: "gone", trashed: true),
            note("alive", in: "nb"),
            note("gone-leaf", in: "alive", trashed: true),
        ])
        #expect(library.trashed.map(\.id).sorted() == ["gone", "gone-leaf"])
    }

    @Test func favoritesAreListedByTitleAndSkipTheTrash() {
        let library = Library(notes: [
            note("nb", .notebook),
            note("b", in: "nb", favorited: true, title: "Beta"),
            note("a", in: "nb", favorited: true, title: "alpha"),
            note("t", in: "nb", favorited: true, trashed: true),
        ])
        #expect(library.favorites.map(\.id) == ["a", "b"])
    }

    @Test func aNoteCanBeLookedUpWithItsAncestors() {
        let library = Library(notes: [
            note("nb", .notebook),
            note("section", .section, in: "nb"),
            note("page", in: "section"),
        ])
        #expect(library.note("page")?.id == "page")
        #expect(library.note("missing") == nil)
        #expect(library.path(to: "page").map(\.id) == ["nb", "section", "page"])
        #expect(library.path(to: "missing").isEmpty)
        #expect(library.notebook(containing: "page")?.id == "nb")
    }

    @Test func aLoopInTheDataCannotHangTheTree() {
        // The core's schema forbids this. A view model should still survive it.
        let library = Library(notes: [
            note("nb", .notebook),
            note("a", in: "b"),
            note("b", in: "a"),
        ])
        #expect(outline(library.notebooks) == "nb")
        #expect(library.path(to: "a").count <= 2)
    }
}
