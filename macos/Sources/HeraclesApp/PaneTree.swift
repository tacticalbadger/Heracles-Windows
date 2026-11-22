import Foundation

/// How a split lays out its panes: side by side, or one above another.
public enum SplitAxis: String, Codable, Sendable {
    case horizontal
    case vertical
}

/// Which side of a pane a new pane appears on.
public enum SplitTarget: Sendable {
    case right, down, left, up

    var axis: SplitAxis { self == .right || self == .left ? .horizontal : .vertical }
    var comesAfter: Bool { self == .right || self == .down }
}

/// One pane: an ordered row of tabs, each a note, with one showing.
public struct PaneLeaf: Identifiable, Equatable, Codable, Sendable {
    public var id: String
    public var tabs: [String]
    public var activeTab: String?

    init(tabs: [String] = [], activeTab: String? = nil) {
        id = UUID().uuidString
        self.tabs = tabs
        self.activeTab = activeTab
    }

    /// Remove a tab. If it was showing, the tab that slides into its place
    /// shows next, or the one before it at the end of the row.
    mutating func close(_ noteID: String) {
        guard let index = tabs.firstIndex(of: noteID) else { return }
        tabs.remove(at: index)
        if activeTab == noteID {
            activeTab = index < tabs.count ? tabs[index] : tabs.last
        }
    }
}

/// Panes sharing a space, each with a share of it.
public struct PaneSplit: Identifiable, Equatable, Codable, Sendable {
    public var id: String
    public var axis: SplitAxis
    public var children: [PaneNode]
    /// Each child's share of the space. They add up to 1.
    public var ratios: [Double]
}

public indirect enum PaneNode: Identifiable, Equatable, Codable, Sendable {
    case leaf(PaneLeaf)
    case split(PaneSplit)

    public var id: String {
        switch self {
        case .leaf(let leaf): leaf.id
        case .split(let split): split.id
        }
    }
}

/// One window's panes. A value: every change makes a new tree, which is what
/// lets a window save its layout and compare it.
public struct PaneTree: Equatable, Codable, Sendable {
    public private(set) var root: PaneNode
    /// The pane the next note opens in.
    public private(set) var focusedLeafID: String

    public init() {
        let leaf = PaneLeaf()
        root = .leaf(leaf)
        focusedLeafID = leaf.id
    }

    public var leaves: [PaneLeaf] { Self.leaves(of: root) }

    public func leaf(_ id: String) -> PaneLeaf? { leaves.first { $0.id == id } }

    /// The note showing in the focused pane.
    public var activeNoteID: String? { leaf(focusedLeafID)?.activeTab }

    private static func leaves(of node: PaneNode) -> [PaneLeaf] {
        switch node {
        case .leaf(let leaf): [leaf]
        case .split(let split): split.children.flatMap(leaves)
        }
    }

    // MARK: Changes

    public mutating func focus(_ leafID: String) {
        if leaf(leafID) != nil { focusedLeafID = leafID }
    }

    /// Show a note in a pane, adding a tab for it if it has none there.
    public mutating func open(_ noteID: String, in leafID: String? = nil) {
        let target = leafID.flatMap { leaf($0)?.id } ?? focusedLeafID
        update(target) { leaf in
            if !leaf.tabs.contains(noteID) { leaf.tabs.append(noteID) }
            leaf.activeTab = noteID
        }
        focusedLeafID = target
    }

    public mutating func activate(_ noteID: String, in leafID: String) {
        guard leaf(leafID)?.tabs.contains(noteID) == true else { return }
        update(leafID) { $0.activeTab = noteID }
        focusedLeafID = leafID
    }

    /// Close a tab. A pane left with no tabs closes too, unless it is the
    /// only one.
    public mutating func closeTab(_ noteID: String, in leafID: String) {
        update(leafID) { $0.close(noteID) }
        if leaf(leafID)?.tabs.isEmpty == true, leaves.count > 1 {
            closeLeaf(leafID)
        }
    }

    /// Put a new pane beside an existing one, and focus it. Returns its id.
    @discardableResult
    public mutating func split(_ leafID: String, _ target: SplitTarget, opening noteID: String?) -> String {
        let added = PaneLeaf(tabs: noteID.map { [$0] } ?? [], activeTab: noteID)
        root = Self.replacing(leafID, in: root) { existing in
            let children: [PaneNode] = target.comesAfter ? [.leaf(existing), .leaf(added)] : [.leaf(added), .leaf(existing)]
            return .split(PaneSplit(id: UUID().uuidString, axis: target.axis, children: children, ratios: [0.5, 0.5]))
        }
        if leaf(added.id) != nil { focusedLeafID = added.id }
        return added.id
    }

    /// Close a pane. The others take up its space. The last pane is emptied
    /// in place, since a window always has one.
    public mutating func closeLeaf(_ leafID: String) {
        root = Self.pruning(root) { $0.id == leafID ? nil : $0 } ?? .leaf(PaneLeaf())
        repairFocus()
    }

    public mutating func setRatios(_ splitID: String, _ ratios: [Double]) {
        func apply(_ node: PaneNode) -> PaneNode {
            guard case .split(var split) = node else { return node }
            if split.id == splitID, split.ratios.count == ratios.count {
                split.ratios = ratios
            } else {
                split.children = split.children.map(apply)
            }
            return .split(split)
        }
        root = apply(root)
    }

    /// Take notes that no longer exist out of every pane. A pane this
    /// empties is closed.
    public mutating func remove(notes: Set<String>) {
        root = Self.pruning(root) { leaf in
            guard leaf.tabs.contains(where: notes.contains) else { return leaf }
            var leaf = leaf
            for note in leaf.tabs where notes.contains(note) { leaf.close(note) }
            return leaf.tabs.isEmpty ? nil : leaf
        } ?? .leaf(PaneLeaf())
        repairFocus()
    }

    /// Keep only tabs for notes in `notes`. For a layout restored from an
    /// earlier session, whose notes may have gone since.
    public mutating func keep(notes: Set<String>) {
        remove(notes: Set(leaves.flatMap(\.tabs)).subtracting(notes))
    }

    /// Move a tab to a place in its own pane or another. Out-of-range places
    /// mean the end.
    public mutating func moveTab(_ noteID: String, from sourceID: String, to destinationID: String, at index: Int) {
        guard leaf(sourceID)?.tabs.contains(noteID) == true, leaf(destinationID) != nil else { return }
        if sourceID != destinationID {
            update(sourceID) { $0.close(noteID) }
        }
        update(destinationID) { leaf in
            leaf.tabs.removeAll { $0 == noteID }
            leaf.tabs.insert(noteID, at: (0...leaf.tabs.count).contains(index) ? index : leaf.tabs.count)
            leaf.activeTab = noteID
        }
        if sourceID != destinationID, leaf(sourceID)?.tabs.isEmpty == true {
            closeLeaf(sourceID)
        }
        focus(destinationID)
    }

    // MARK: Helpers

    private mutating func update(_ leafID: String, _ change: (inout PaneLeaf) -> Void) {
        root = Self.replacing(leafID, in: root) { leaf in
            var leaf = leaf
            change(&leaf)
            return .leaf(leaf)
        }
    }

    private mutating func repairFocus() {
        if leaf(focusedLeafID) == nil, let first = leaves.first {
            focusedLeafID = first.id
        }
    }

    private static func replacing(_ leafID: String, in node: PaneNode, with make: (PaneLeaf) -> PaneNode) -> PaneNode {
        switch node {
        case .leaf(let leaf):
            return leaf.id == leafID ? make(leaf) : node
        case .split(var split):
            split.children = split.children.map { replacing(leafID, in: $0, with: make) }
            return .split(split)
        }
    }

    /// Rebuild the tree, letting `keep` change or drop each leaf. A split
    /// left with one child is replaced by it, and one left with none goes.
    private static func pruning(_ node: PaneNode, keep: (PaneLeaf) -> PaneLeaf?) -> PaneNode? {
        switch node {
        case .leaf(let leaf):
            return keep(leaf).map(PaneNode.leaf)
        case .split(var split):
            var children: [PaneNode] = []
            var ratios: [Double] = []
            for (index, child) in split.children.enumerated() {
                guard let kept = pruning(child, keep: keep) else { continue }
                children.append(kept)
                ratios.append(index < split.ratios.count ? split.ratios[index] : 1 / Double(split.children.count))
            }
            guard children.count > 1 else { return children.first }
            let total = ratios.reduce(0, +)
            split.children = children
            split.ratios = ratios.map { total > 0 ? $0 / total : 1 / Double(children.count) }
            return .split(split)
        }
    }
}
