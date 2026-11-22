import HeraclesCore

/// A note in the sidebar tree, with the notes beneath it.
public struct LibraryNode: Identifiable, Equatable, Sendable {
    public let note: Note
    public var children: [LibraryNode]

    public var id: String { note.id }

    /// `nil` for a leaf, which is how `OutlineGroup` knows not to draw a
    /// disclosure arrow.
    public var disclosedChildren: [LibraryNode]? { children.isEmpty ? nil : children }
}

/// The notes arranged the way the sidebar shows them. Built from the flat
/// list the core returns. It is a value, so a view can keep one and compare.
public struct Library: Equatable, Sendable {
    /// The visible tree. A note in the trash or the archive is left out,
    /// along with everything under it.
    public let notebooks: [LibraryNode]
    public let favorites: [Note]
    public let archived: [Note]
    /// The top of each trashed subtree. Restoring or deleting one of these
    /// takes what is under it along.
    public let trashed: [Note]

    private let byID: [String: Note]

    public init(notes: [Note] = []) {
        byID = Dictionary(notes.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        let byID = byID

        let inOrder: (Note, Note) -> Bool = { a, b in
            if a.position != b.position { return a.position < b.position }
            let titles = a.title.localizedCaseInsensitiveCompare(b.title)
            return titles == .orderedSame ? a.id < b.id : titles == .orderedAscending
        }
        let byTitle: (Note, Note) -> Bool = { a, b in
            let titles = a.title.localizedCaseInsensitiveCompare(b.title)
            return titles == .orderedSame ? a.id < b.id : titles == .orderedAscending
        }

        let visible = notes.filter { !$0.trashed && !$0.archived }
        let children = Dictionary(grouping: visible.filter { $0.parentId != nil }, by: { $0.parentId! })
        // Built from the notebooks down, so a note that no notebook leads to
        // is simply never reached.
        func node(_ note: Note) -> LibraryNode {
            LibraryNode(note: note, children: (children[note.id] ?? []).sorted(by: inOrder).map(node))
        }
        notebooks = visible.filter { $0.kind == .notebook && $0.parentId == nil }.sorted(by: inOrder).map(node)

        favorites = visible.filter(\.favorited).sorted(by: byTitle)
        archived = notes.filter { $0.archived && !$0.trashed }.sorted(by: byTitle)
        trashed = notes
            .filter { note in
                guard note.trashed else { return false }
                guard let parent = note.parentId.flatMap({ byID[$0] }) else { return true }
                return !parent.trashed
            }
            .sorted { $0.updatedAt > $1.updatedAt }
    }

    public func note(_ id: String) -> Note? { byID[id] }

    /// The note and its ancestors, outermost first.
    public func path(to id: String) -> [Note] {
        var path: [Note] = []
        var seen: Set<String> = []
        var next = byID[id]
        while let note = next, seen.insert(note.id).inserted {
            path.append(note)
            next = note.parentId.flatMap { byID[$0] }
        }
        return path.reversed()
    }

    public func notebook(containing id: String) -> Note? {
        path(to: id).first { $0.kind == .notebook }
    }

    public static func == (a: Library, b: Library) -> Bool { a.byID == b.byID }
}
