import HeraclesCore
import SwiftUI

/// The quick switcher: type part of a note's name or text, press Return to
/// open it. ⌘Return opens it in a new pane.
struct SwitcherView: View {
    let workspace: Workspace
    @State private var query = ""
    @State private var highlighted = 0
    @FocusState private var fieldFocused: Bool

    var body: some View {
        let results = workspace.switcherResults(for: query)
        VStack(spacing: 0) {
            TextField("Find a note…", text: $query)
                .textFieldStyle(.plain)
                .font(.title3)
                .padding(12)
                .focused($fieldFocused)
                .onSubmit { open(results, split: NSEvent.modifierFlags.contains(.command)) }
                .onKeyPress(.downArrow) { move(1, in: results) }
                .onKeyPress(.upArrow) { move(-1, in: results) }
                .onKeyPress(.escape) {
                    workspace.isSwitcherShown = false
                    return .handled
                }
            Divider()
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        if results.isEmpty {
                            Text("No notes match").foregroundStyle(.secondary).padding(12)
                        }
                        ForEach(Array(results.enumerated()), id: \.element.id) { index, note in
                            row(note, isHighlighted: index == highlighted)
                                .id(index)
                                .onTapGesture {
                                    highlighted = index
                                    open(results, split: NSEvent.modifierFlags.contains(.command))
                                }
                        }
                    }
                }
                .frame(maxHeight: 320)
                .onChange(of: highlighted) { proxy.scrollTo(highlighted) }
            }
        }
        .frame(width: 520)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(.separator))
        .shadow(radius: 24, y: 8)
        .onAppear { fieldFocused = true }
        .onChange(of: query) { highlighted = 0 }
    }

    private func row(_ note: Note, isHighlighted: Bool) -> some View {
        HStack {
            NoteLabel(note: note)
            Spacer()
            // Where the note lives, to tell two with the same name apart.
            Text(workspace.model.library.path(to: note.id).dropLast().map(\.displayTitle).joined(separator: " › "))
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(isHighlighted ? AnyShapeStyle(.tint.opacity(0.2)) : AnyShapeStyle(.clear))
        .contentShape(Rectangle())
    }

    private func move(_ step: Int, in results: [Note]) -> KeyPress.Result {
        guard !results.isEmpty else { return .handled }
        highlighted = min(max(highlighted + step, 0), results.count - 1)
        return .handled
    }

    private func open(_ results: [Note], split: Bool) {
        guard results.indices.contains(highlighted) else { return }
        workspace.open(results[highlighted].id, inSplit: split ? .right : nil)
        workspace.isSwitcherShown = false
    }
}
