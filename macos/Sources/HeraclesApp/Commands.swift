import AppKit
import HeraclesCore
import SwiftUI

extension FocusedValues {
    /// The workspace of the window in front, for the menu bar to act on.
    @Entry var workspace: Workspace?
}

/// The menu bar. Every item acts on the window in front.
struct HeraclesCommands: Commands {
    @FocusedValue(\.workspace) private var workspace

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("New Page") { workspace?.createNote(.page) }
                .keyboardShortcut("n")
            Button("New Section") { workspace?.createNote(.section) }
                .keyboardShortcut("n", modifiers: [.command, .shift])
            Button("New To-Do List") { workspace?.createNote(.todoList) }
                .keyboardShortcut("t", modifiers: [.command, .shift])
            Button("New Notebook") { workspace?.createNote(.notebook) }
            Divider()
            Button("Quick Open…") { workspace?.isSwitcherShown = true }
                .keyboardShortcut("k")
            Divider()
            Button("Close Tab") { workspace?.closeActiveTab() }
                .keyboardShortcut("w")
        }
        CommandGroup(replacing: .importExport) {
            Button("Import…") { workspace?.chooseFilesToImport() }
                .keyboardShortcut("i", modifiers: [.command, .shift])
            Button("Export as Markdown…") { workspace?.chooseExportDestination() }
                .keyboardShortcut("e", modifiers: [.command, .shift])
                .disabled(workspace?.activeNote == nil)
        }
        CommandMenu("Note") {
            Group {
                Button(workspace?.activeNote?.favorited == true ? "Remove from Favorites" : "Add to Favorites") {
                    workspace?.toggleFavoriteActive()
                }
                .keyboardShortcut("f", modifiers: [.command, .shift])
                Button("Duplicate") { workspace?.duplicateActive() }
                    .keyboardShortcut("d")
                Button("Copy Link") { workspace?.copyLinkToActive() }
                    .keyboardShortcut("c", modifiers: [.command, .shift])
                Divider()
                // No shortcut: ⌘⌫ is "delete to the start of the line" while typing.
                Button("Move to Trash") { workspace?.trashActive() }
            }
            .disabled(workspace?.activeNote == nil)
        }
        CommandMenu("Format") {
            Button("Bold") { format(.mark(.bold)) }
                .keyboardShortcut("b")
            Button("Italic") { format(.mark(.italic)) }
                .keyboardShortcut("i")
            Button("Strikethrough") { format(.mark(.strike)) }
                .keyboardShortcut("x", modifiers: [.command, .shift])
            Button("Code") { format(.mark(.code)) }
                .keyboardShortcut("e")
            // Command-K, while text is selected. Otherwise it is Quick Open.
            Button("Link…") { format(.link) }
            Divider()
            Button("Text") { format(.block(.paragraph)) }
                .keyboardShortcut("0", modifiers: [.command, .option])
            Button("Heading 1") { format(.block(.heading(1))) }
                .keyboardShortcut("1", modifiers: [.command, .option])
            Button("Heading 2") { format(.block(.heading(2))) }
                .keyboardShortcut("2", modifiers: [.command, .option])
            Button("Heading 3") { format(.block(.heading(3))) }
                .keyboardShortcut("3", modifiers: [.command, .option])
            Divider()
            Button("Bullet List") { format(.block(.bullet)) }
                .keyboardShortcut("8", modifiers: [.command, .shift])
            Button("Numbered List") { format(.block(.numbered)) }
                .keyboardShortcut("7", modifiers: [.command, .shift])
            Button("Task List") { format(.block(.todo)) }
                .keyboardShortcut("9", modifiers: [.command, .shift])
            Divider()
            Button("Quote") { format(.block(.quote)) }
            Button("Callout") { format(.block(.callout)) }
            Button("Toggle") { format(.block(.toggle)) }
            Button("Code Block") { format(.block(.code)) }
        }
        CommandGroup(after: .sidebar) {
            Button("Split Right") { workspace?.splitFocused(.right) }
                .keyboardShortcut("\\")
            Button("Split Down") { workspace?.splitFocused(.down) }
                .keyboardShortcut("\\", modifiers: [.command, .shift])
            Divider()
        }
    }
}

/// Send a formatting command to whichever editor has the keyboard. It
/// travels the way menu commands do on the Mac, to the view being typed in,
/// so it lands in the right pane of the right window without being told.
@MainActor
private func format(_ format: EditorFormat) {
    NSApp.sendAction(#selector(BlockTextView.applyEditorFormat(_:)), to: nil, from: FormatRequest(format))
}
