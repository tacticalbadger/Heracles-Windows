import HeraclesCore
import SwiftUI

/// One note in a pane: its icon and title, then its content. A todo list
/// shows its board, and every other kind shows the editor.
struct NoteDetailView: View {
    @Environment(AppModel.self) private var model
    let note: Note
    let workspace: Workspace
    let leafID: String
    @State private var title = ""
    @State private var choosingIcon = false
    @FocusState private var titleFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 8) {
                Button {
                    choosingIcon = true
                } label: {
                    if let icon = note.icon, !icon.isEmpty {
                        Text(icon).font(.largeTitle)
                    } else {
                        Image(systemName: "face.smiling").font(.title2).foregroundStyle(.tertiary)
                    }
                }
                .buttonStyle(.plain)
                .disabled(note.trashed)
                .help("Choose an icon")
                .popover(isPresented: $choosingIcon, arrowEdge: .bottom) {
                    IconPicker(current: note.icon) { icon in
                        model.setIcon(note.id, to: icon)
                        choosingIcon = false
                    }
                }
                TextField("Untitled", text: $title)
                    .textFieldStyle(.plain)
                    .font(.largeTitle.bold())
                    .focused($titleFocused)
                    .disabled(note.trashed)
                    .onSubmit(commitTitle)
            }
            // A page's title sits over the editor's text column, which is
            // centered once the pane is wider than a comfortable line.
            .frame(maxWidth: note.kind == .todoList ? .infinity : EditorTheme.contentWidth, alignment: .leading)
            .padding(.horizontal, note.kind == .todoList ? 32 : EditorTheme.gutter)
            .frame(maxWidth: .infinity)
            .padding(.top, 20)
            .padding(.bottom, 4)

            if note.kind == .todoList {
                BoardView(todoList: note)
            } else {
                NoteEditor(
                    noteID: note.id, editable: !note.trashed,
                    change: model.documentChange(for: note.id), model: model,
                    reveal: workspace.blockToReveal(in: note.id),
                    onRevealed: { workspace.didReveal(in: note.id) },
                    onFocus: { workspace.tree.focus(leafID) })
            }
        }
        .onAppear { title = note.title }
        .onChange(of: note.id) { title = note.title }
        .onChange(of: note.title) { if !titleFocused { title = note.title } }
        .onChange(of: titleFocused) {
            if titleFocused {
                workspace.tree.focus(leafID)
            } else {
                commitTitle()
            }
        }
    }

    private func commitTitle() {
        if title != note.title {
            model.rename(note.id, to: title)
        }
    }
}

/// A grid of icons to give a note, and a way to take its icon away.
struct IconPicker: View {
    let current: String?
    let choose: (String?) -> Void
    @State private var typed = ""

    private static let groups: [(String, [String])] = [
        ("Notes", ["📝", "📓", "📔", "📒", "📚", "📖", "🗒️", "📄", "📌", "📎", "🔖", "✏️"]),
        ("Work", ["💼", "📊", "📈", "🗂️", "📁", "🧾", "💡", "🎯", "✅", "⏰", "📅", "🔧"]),
        ("Life", ["🏠", "🧭", "✈️", "🚗", "🍳", "🛒", "💰", "🎁", "🎵", "🎬", "🏃", "🌱"]),
        ("Signs", ["⭐", "🔥", "❤️", "⚠️", "❓", "🔒", "🚀", "🌍", "☀️", "🌙", "🧪", "🐛"]),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(Self.groups, id: \.0) { name, icons in
                Text(name).font(.caption).foregroundStyle(.secondary)
                LazyVGrid(columns: Array(repeating: GridItem(.fixed(30), spacing: 4), count: 6), spacing: 4) {
                    ForEach(icons, id: \.self) { icon in
                        Button(icon) { choose(icon) }
                            .buttonStyle(.plain)
                            .font(.title2)
                            .frame(width: 30, height: 30)
                            .background(icon == current ? AnyShapeStyle(.tint.opacity(0.25)) : AnyShapeStyle(.clear), in: RoundedRectangle(cornerRadius: 6))
                    }
                }
            }
            Divider()
            HStack {
                // Any emoji at all, from the keyboard or the emoji panel.
                TextField("Or type one", text: $typed)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit {
                        if let first = typed.trimmingCharacters(in: .whitespaces).first { choose(String(first)) }
                    }
                Button("Remove") { choose(nil) }.disabled(current == nil)
            }
        }
        .padding(12)
        .frame(width: 230)
    }
}
