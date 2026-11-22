import AppKit
import HeraclesCore
import Observation
import UniformTypeIdentifiers

/// One window's state: its panes, and the actions its menus and toolbar
/// perform. The notes themselves live in the shared `AppModel`.
@MainActor
@Observable
public final class Workspace {
    public let model: AppModel
    public var tree = PaneTree()
    public var isSwitcherShown = false
    /// For each note opened by a link to one of its blocks, the block to
    /// bring into view once the note is showing.
    private var reveals: [String: String] = [:]

    public init(model: AppModel) {
        self.model = model
    }

    public var activeNote: Note? {
        tree.activeNoteID.flatMap { model.library.note($0) }
    }

    // MARK: Opening and closing

    /// Show a note in the focused pane, or in a new pane beside it.
    public func open(_ noteID: String, inSplit target: SplitTarget? = nil) {
        guard model.library.note(noteID) != nil else { return }
        if let target {
            tree.split(tree.focusedLeafID, target, opening: noteID)
        } else {
            tree.open(noteID)
        }
    }

    public func closeActiveTab() {
        if let active = tree.activeNoteID {
            tree.closeTab(active, in: tree.focusedLeafID)
        } else {
            tree.closeLeaf(tree.focusedLeafID)
        }
    }

    /// Open a second pane showing the same note, to look at two parts of it
    /// or to pick another note there.
    public func splitFocused(_ target: SplitTarget) {
        tree.split(tree.focusedLeafID, target, opening: tree.activeNoteID)
    }

    /// Let go of notes that no longer exist. Called whenever the library
    /// changes, whichever window changed it.
    public func libraryChanged() {
        let open = Set(tree.leaves.flatMap(\.tabs))
        let gone = open.filter { model.library.note($0) == nil }
        if !gone.isEmpty {
            tree.remove(notes: gone)
        }
        reveals = reveals.filter { model.library.note($0.key) != nil }
    }

    public func follow(_ link: AppLink) {
        guard model.library.note(link.noteID) != nil else { return }
        open(link.noteID)
        reveals[link.noteID] = link.blockID
    }

    public func blockToReveal(in noteID: String) -> String? { reveals[noteID] }

    public func didReveal(in noteID: String) {
        reveals[noteID] = nil
    }

    // MARK: Creating

    /// Where a new note goes: inside the open note if that can hold notes,
    /// beside it if not, and in the first notebook when nothing is open.
    private var parentForNewNotes: String? {
        guard let active = activeNote, !active.trashed else { return nil }
        return active.kind == .todoList ? active.parentId : active.id
    }

    @discardableResult
    public func createNote(_ kind: NoteKind) -> Note? {
        guard let note = model.createNote(kind, in: parentForNewNotes) else { return nil }
        if kind != .notebook {
            tree.open(note.id)
        }
        return note
    }

    // MARK: Import and export

    /// Import Markdown and HTML files as pages, and show the last one.
    @discardableResult
    public func importFiles(_ files: [URL]) -> [Note] {
        let parent = parentForNewNotes
        let imported = files.compactMap { file in
            model.attempt { try model.store.importFile(path: file.path, parentId: parent) }
        }
        if let last = imported.last {
            tree.open(last.id)
        }
        return imported
    }

    @discardableResult
    public func exportActiveNote(to destination: URL) -> Bool {
        guard let note = activeNote else { return false }
        return model.attempt { try model.store.exportMarkdown(noteId: note.id, destination: destination.path) } != nil
    }

    public func chooseFilesToImport() {
        let panel = NSOpenPanel()
        panel.title = "Import"
        panel.prompt = "Import"
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        let extensions = ["md", "markdown", "mdown", "mkd", "mkdn", "txt", "html", "htm"]
        panel.allowedContentTypes = extensions.compactMap { UTType(filenameExtension: $0) }
        if panel.runModal() == .OK {
            importFiles(panel.urls)
        }
    }

    public func chooseExportDestination() {
        guard let note = activeNote else { return }
        let panel = NSSavePanel()
        panel.title = "Export as Markdown"
        panel.nameFieldStringValue = "\(note.displayTitle).md"
        panel.allowedContentTypes = [UTType(filenameExtension: "md") ?? .plainText]
        if panel.runModal() == .OK, let destination = panel.url {
            exportActiveNote(to: destination)
        }
    }

    // MARK: The quick switcher

    /// What the switcher lists: the most recently changed notes until
    /// something is typed, then the notes that match it.
    public func switcherResults(for query: String) -> [Note] {
        if query.trimmingCharacters(in: .whitespaces).isEmpty {
            // The core lists notes most recently changed first.
            let notes = (try? model.store.listNotes()) ?? []
            return Array(notes.lazy.filter { !$0.trashed && $0.kind != .notebook }.prefix(20))
        }
        return model.search(query).map(\.note).filter { $0.kind != .notebook }
    }

    // MARK: Actions on the open note

    public func trashActive() {
        if let note = activeNote { model.trash(note.id) }
    }

    public func duplicateActive() {
        if let note = activeNote, let copy = model.duplicate(note.id) { tree.open(copy.id) }
    }

    public func toggleFavoriteActive() {
        if let note = activeNote { model.setFavorited(note.id, !note.favorited) }
    }

    public func copyLinkToActive() {
        guard let note = activeNote else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(AppLink(noteID: note.id, blockID: nil).url.absoluteString, forType: .string)
    }
}
