import HeraclesCore
import SwiftUI

extension Note {
    /// The symbol shown for a note with no icon of its own.
    var symbol: String {
        switch kind {
        case .notebook: "books.vertical"
        case .section: "folder"
        case .page: "doc.text"
        case .todoList: "checklist"
        }
    }

    var displayTitle: String { title.isEmpty ? "Untitled" : title }
}

struct NoteLabel: View {
    let note: Note

    var body: some View {
        Label {
            Text(note.displayTitle).lineLimit(1)
        } icon: {
            if let icon = note.icon, !icon.isEmpty {
                Text(icon)
            } else {
                Image(systemName: note.symbol)
            }
        }
    }
}

struct SidebarView: View {
    @Environment(AppModel.self) private var model
    @Binding var selection: String?
    let searchText: String
    @State private var renaming: Note?
    @State private var newTitle = ""

    var body: some View {
        List(selection: $selection) {
            if searchText.trimmingCharacters(in: .whitespaces).isEmpty {
                library
            } else {
                results
            }
        }
        .listStyle(.sidebar)
        .alert("Rename", isPresented: isRenaming, presenting: renaming) { note in
            TextField("Title", text: $newTitle)
            Button("Rename") { model.rename(note.id, to: newTitle) }
            Button("Cancel", role: .cancel) {}
        }
    }

    private var isRenaming: Binding<Bool> {
        Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })
    }

    @ViewBuilder private var library: some View {
        if !model.library.favorites.isEmpty {
            Section("Favorites") {
                ForEach(model.library.favorites, id: \.id) { note in
                    row(note)
                }
            }
        }
        ForEach(model.library.notebooks) { notebook in
            Section {
                OutlineGroup(notebook.children, children: \.disclosedChildren) { node in
                    row(node.note)
                }
            } header: {
                NoteLabel(note: notebook.note)
                    .contextMenu { notebookMenu(notebook.note) }
            }
        }
        if !model.library.archived.isEmpty {
            Section("Archive") {
                ForEach(model.library.archived, id: \.id) { note in
                    row(note)
                }
            }
        }
        if !model.library.trashed.isEmpty {
            Section("Trash") {
                ForEach(model.library.trashed, id: \.id) { note in
                    row(note)
                }
            }
        }
    }

    @ViewBuilder private var results: some View {
        let hits = model.search(searchText)
        if hits.isEmpty {
            Text("No notes match").foregroundStyle(.secondary)
        }
        ForEach(hits, id: \.note.id) { hit in
            VStack(alignment: .leading, spacing: 2) {
                NoteLabel(note: hit.note)
                snippet(hit).font(.caption).foregroundStyle(.secondary).lineLimit(2)
            }
            .tag(hit.note.id)
        }
    }

    private func snippet(_ hit: SearchHit) -> Text {
        hit.snippet.reduce(Text("")) { text, part in
            text + (part.matched ? Text(part.text).bold().foregroundStyle(.primary) : Text(part.text))
        }
    }

    private func row(_ note: Note) -> some View {
        NoteLabel(note: note)
            .tag(note.id)
            .contextMenu { noteMenu(note) }
    }

    private func startRenaming(_ note: Note) {
        newTitle = note.title
        renaming = note
    }

    private func create(_ kind: NoteKind, in parent: Note) {
        if let note = model.createNote(kind, in: parent.id) {
            selection = note.id
        }
    }

    @ViewBuilder private func notebookMenu(_ notebook: Note) -> some View {
        Button("New Page") { create(.page, in: notebook) }
        Button("New Section") { create(.section, in: notebook) }
        Button("New To-Do List") { create(.todoList, in: notebook) }
        Divider()
        Button("Rename…") { startRenaming(notebook) }
        Button("Delete Notebook", role: .destructive) { model.delete(notebook.id) }
    }

    @ViewBuilder private func noteMenu(_ note: Note) -> some View {
        if note.trashed {
            Button("Put Back") { model.restore(note.id) }
            Button("Delete Permanently", role: .destructive) {
                if model.delete(note.id).contains(where: { $0 == selection }) { selection = nil }
            }
            Divider()
            Button("Empty Trash", role: .destructive) {
                if model.emptyTrash().contains(where: { $0 == selection }) { selection = nil }
            }
        } else {
            if note.kind != .todoList {
                Button("New Page Inside") { create(.page, in: note) }
                Button("New To-Do List Inside") { create(.todoList, in: note) }
                Divider()
            }
            Button("Rename…") { startRenaming(note) }
            Button("Duplicate") {
                if let copy = model.duplicate(note.id) { selection = copy.id }
            }
            Button(note.favorited ? "Remove from Favorites" : "Add to Favorites") {
                model.setFavorited(note.id, !note.favorited)
            }
            Button("Copy Link") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(AppLink(noteID: note.id, blockID: nil).url.absoluteString, forType: .string)
            }
            Menu("Move to Notebook") {
                ForEach(model.library.notebooks) { notebook in
                    Button(notebook.note.displayTitle) { model.move(note.id, to: notebook.id) }
                }
            }
            Divider()
            Button(note.archived ? "Unarchive" : "Archive") { model.setArchived(note.id, !note.archived) }
            Button("Move to Trash", role: .destructive) { model.trash(note.id) }
        }
    }
}
