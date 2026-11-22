import HeraclesCore
import SwiftUI

/// One window: the sidebar, and the panes beside it. Every window shares the
/// same model, so what one changes the others show.
public struct RootView: View {
    private let model: AppModel
    @State private var workspace: Workspace
    @State private var searchText = ""
    /// The pane layout, kept between launches for each window.
    @SceneStorage("heracles.panes") private var savedPanes = ""
    private let initialNote: String?

    /// `selecting` is the note the window opens on, if any.
    public init(model: AppModel, selecting noteID: String? = nil) {
        self.model = model
        initialNote = noteID
        _workspace = State(initialValue: Workspace(model: model))
    }

    public var body: some View {
        @Bindable var model = model
        NavigationSplitView {
            SidebarView(selection: sidebarSelection, searchText: searchText)
                .searchable(text: $searchText, placement: .sidebar, prompt: "Search notes")
                .navigationSplitViewColumnWidth(min: 200, ideal: 260)
                .toolbar {
                    ToolbarItem {
                        Menu("New", systemImage: "plus") {
                            Button("Page") { workspace.createNote(.page) }
                            Button("Section") { workspace.createNote(.section) }
                            Button("To-Do List") { workspace.createNote(.todoList) }
                            Divider()
                            Button("Notebook") { workspace.createNote(.notebook) }
                            Divider()
                            Button("Import…") { workspace.chooseFilesToImport() }
                        }
                    }
                }
        } detail: {
            PaneNodeView(node: workspace.tree.root, workspace: workspace)
                .toolbar { noteToolbar }
        }
        .overlay(alignment: .top) {
            if workspace.isSwitcherShown {
                // A click anywhere outside the switcher puts it away.
                ZStack(alignment: .top) {
                    Color.black.opacity(0.001).onTapGesture { workspace.isSwitcherShown = false }
                    SwitcherView(workspace: workspace).padding(.top, 60)
                }
            }
        }
        .environment(model)
        .focusedSceneValue(\.workspace, workspace)
        .navigationTitle(workspace.activeNote?.displayTitle ?? "Heracles")
        .onAppear(perform: restorePanes)
        .onChange(of: workspace.tree) { savePanes() }
        .onOpenURL { url in
            if let link = AppLink(url) { model.pendingLink = link }
        }
        .onChange(of: model.pendingLink, initial: true) {
            // A link opens in whichever window sees it first.
            if let link = model.pendingLink {
                workspace.follow(link)
                model.pendingLink = nil
            }
        }
        // A note deleted here or in another window leaves this window's panes.
        .onChange(of: model.library) { workspace.libraryChanged() }
        .alert("Something went wrong", isPresented: Binding(get: { model.lastError != nil }, set: { _ in model.lastError = nil })) {
            Button("OK") {}
        } message: {
            Text(model.lastError ?? "")
        }
    }

    /// The sidebar highlights the note in the focused pane. Choosing a note
    /// opens it there, or in a new pane beside it with ⌘ held.
    private var sidebarSelection: Binding<String?> {
        Binding(
            get: { workspace.tree.activeNoteID },
            set: { chosen in
                guard let chosen else { return }
                workspace.open(chosen, inSplit: NSEvent.modifierFlags.contains(.command) ? .right : nil)
            })
    }

    @ToolbarContentBuilder private var noteToolbar: some ToolbarContent {
        ToolbarItemGroup {
            if let note = workspace.activeNote {
                if note.trashed {
                    Button("Put Back", systemImage: "arrow.uturn.backward") { model.restore(note.id) }
                } else {
                    Button(
                        note.favorited ? "Remove from Favorites" : "Add to Favorites",
                        systemImage: note.favorited ? "star.fill" : "star"
                    ) { workspace.toggleFavoriteActive() }
                    Button("Export as Markdown…", systemImage: "square.and.arrow.up") {
                        workspace.chooseExportDestination()
                    }
                }
            }
            Button("Split Right", systemImage: "rectangle.split.2x1") { workspace.splitFocused(.right) }
            Button("Quick Open", systemImage: "magnifyingglass") { workspace.isSwitcherShown = true }
        }
    }

    private func restorePanes() {
        if let data = savedPanes.data(using: .utf8), !data.isEmpty,
            var tree = try? JSONDecoder().decode(PaneTree.self, from: data)
        {
            // Notes deleted since the layout was saved have no tab to come back to.
            tree.keep(notes: Set(((try? model.store.listNotes()) ?? []).map(\.id)))
            workspace.tree = tree
        }
        if let initialNote {
            workspace.open(initialNote)
        }
    }

    private func savePanes() {
        if let data = try? JSONEncoder().encode(workspace.tree) {
            savedPanes = String(decoding: data, as: UTF8.self)
        }
    }
}
