import HeraclesCore
import SwiftUI

/// The panes of a window, drawn from its tree: splits within splits, down
/// to the panes themselves.
struct PaneNodeView: View {
    let node: PaneNode
    let workspace: Workspace

    var body: some View {
        switch node {
        case .leaf(let leaf):
            PaneLeafView(leaf: leaf, workspace: workspace)
        case .split(let split):
            PaneSplitView(split: split, workspace: workspace)
        }
    }
}

private struct PaneSplitView: View {
    let split: PaneSplit
    let workspace: Workspace
    /// The shares as they were when a divider drag began.
    @State private var ratiosAtDragStart: [Double]?

    private static let dividerThickness: CGFloat = 5
    /// No pane can be dragged smaller than this share of its split.
    private static let smallestShare = 0.12

    var body: some View {
        GeometryReader { geometry in
            let horizontal = split.axis == .horizontal
            let length = horizontal ? geometry.size.width : geometry.size.height
            let room = max(0, length - Self.dividerThickness * CGFloat(split.children.count - 1))
            let layout = horizontal ? AnyLayout(HStackLayout(spacing: 0)) : AnyLayout(VStackLayout(spacing: 0))
            layout {
                ForEach(Array(split.children.enumerated()), id: \.element.id) { index, child in
                    let share = room * (index < split.ratios.count ? split.ratios[index] : 0)
                    // Type-erased because a pane view contains pane views.
                    AnyView(PaneNodeView(node: child, workspace: workspace))
                        .frame(width: horizontal ? share : nil, height: horizontal ? nil : share)
                    if index < split.children.count - 1 {
                        divider(after: index, horizontal: horizontal, room: room)
                    }
                }
            }
        }
    }

    private func divider(after index: Int, horizontal: Bool, room: CGFloat) -> some View {
        Rectangle()
            .fill(.separator)
            .frame(width: horizontal ? Self.dividerThickness : nil, height: horizontal ? nil : Self.dividerThickness)
            .contentShape(Rectangle())
            .onHover { inside in
                if inside {
                    (horizontal ? NSCursor.resizeLeftRight : NSCursor.resizeUpDown).push()
                } else {
                    NSCursor.pop()
                }
            }
            .gesture(
                DragGesture(minimumDistance: 1, coordinateSpace: .global)
                    .onChanged { drag in
                        let start = ratiosAtDragStart ?? split.ratios
                        ratiosAtDragStart = start
                        guard room > 0, index + 1 < start.count else { return }
                        let moved = Double((horizontal ? drag.translation.width : drag.translation.height) / room)
                        // The two panes beside the divider trade space. The rest keep theirs.
                        let pair = start[index] + start[index + 1]
                        let first = min(max(start[index] + moved, Self.smallestShare), pair - Self.smallestShare)
                        var ratios = start
                        ratios[index] = first
                        ratios[index + 1] = pair - first
                        workspace.tree.setRatios(split.id, ratios)
                    }
                    .onEnded { _ in ratiosAtDragStart = nil })
    }
}

private struct PaneLeafView: View {
    @Environment(AppModel.self) private var model
    let leaf: PaneLeaf
    let workspace: Workspace

    private var isFocused: Bool { workspace.tree.focusedLeafID == leaf.id }
    private var hasNeighbours: Bool { workspace.tree.leaves.count > 1 }

    var body: some View {
        VStack(spacing: 0) {
            if !leaf.tabs.isEmpty {
                tabStrip
                Divider()
            }
            if let note = leaf.activeTab.flatMap({ model.library.note($0) }) {
                NoteDetailView(note: note, workspace: workspace, leafID: leaf.id)
            } else {
                ContentUnavailableView(
                    "No Note Open", systemImage: "doc.text",
                    description: Text("Choose a note in the sidebar, or press ⌘K to find one."))
                    .contentShape(Rectangle())
                    .onTapGesture { workspace.tree.focus(leaf.id) }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var tabStrip: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 2) {
                ForEach(Array(leaf.tabs.enumerated()), id: \.element) { index, noteID in
                    if let note = model.library.note(noteID) {
                        tab(note, at: index)
                    }
                }
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        // The focused pane's strip is tinted when there is more than one pane.
        .background(isFocused && hasNeighbours ? AnyShapeStyle(.tint.opacity(0.08)) : AnyShapeStyle(.clear))
        .dropDestination(for: String.self) { items, _ in
            moveDroppedTab(items, to: leaf.tabs.count)
        }
    }

    private func tab(_ note: Note, at index: Int) -> some View {
        let isActive = leaf.activeTab == note.id
        return HStack(spacing: 4) {
            NoteLabel(note: note)
                .font(.callout)
                .foregroundStyle(isActive ? .primary : .secondary)
            Button {
                workspace.tree.closeTab(note.id, in: leaf.id)
            } label: {
                Image(systemName: "xmark").font(.caption2)
            }
            .buttonStyle(.plain)
            .foregroundStyle(.secondary)
            .help("Close tab")
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(isActive ? AnyShapeStyle(.quaternary) : AnyShapeStyle(.clear), in: RoundedRectangle(cornerRadius: 6))
        .contentShape(Rectangle())
        .onTapGesture { workspace.tree.activate(note.id, in: leaf.id) }
        .draggable(TabDrag(leafID: leaf.id, noteID: note.id).text)
        .dropDestination(for: String.self) { items, _ in
            moveDroppedTab(items, to: index)
        }
        .contextMenu {
            Button("Split Right") { workspace.tree.split(leaf.id, .right, opening: note.id) }
            Button("Split Down") { workspace.tree.split(leaf.id, .down, opening: note.id) }
            Divider()
            Button("Close Tab") { workspace.tree.closeTab(note.id, in: leaf.id) }
            Button("Close Other Tabs") {
                for other in leaf.tabs where other != note.id {
                    workspace.tree.closeTab(other, in: leaf.id)
                }
            }
            if hasNeighbours {
                Button("Close Pane") { workspace.tree.closeLeaf(leaf.id) }
            }
        }
    }

    private func moveDroppedTab(_ items: [String], to index: Int) -> Bool {
        guard let drag = items.lazy.compactMap(TabDrag.init(text:)).first else { return false }
        workspace.tree.moveTab(drag.noteID, from: drag.leafID, to: leaf.id, at: index)
        return true
    }
}

/// A tab being dragged, carried as text so it can cross between panes.
struct TabDrag: Equatable {
    var leafID: String
    var noteID: String

    private static let prefix = "heracles-tab:"

    init(leafID: String, noteID: String) {
        self.leafID = leafID
        self.noteID = noteID
    }

    init?(text: String) {
        guard text.hasPrefix(Self.prefix) else { return nil }
        let parts = text.dropFirst(Self.prefix.count).split(separator: "|", maxSplits: 1).map(String.init)
        guard parts.count == 2 else { return nil }
        self.init(leafID: parts[0], noteID: parts[1])
    }

    var text: String { "\(Self.prefix)\(leafID)|\(noteID)" }
}
