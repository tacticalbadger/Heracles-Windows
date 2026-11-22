import Foundation
import Testing

@testable import HeraclesApp

/// The tree as text: a leaf is its tabs with the active one starred, a
/// split is its children in brackets, `h` side by side or `v` stacked.
private func shape(_ node: PaneNode) -> String {
    switch node {
    case .leaf(let leaf):
        let tabs = leaf.tabs.map { $0 == leaf.activeTab ? "\($0)*" : $0 }
        return "(\(tabs.joined(separator: " ")))"
    case .split(let split):
        return "\(split.axis == .horizontal ? "h" : "v")[\(split.children.map(shape).joined(separator: " "))]"
    }
}

struct PaneTreeTests {
    @Test func aNewTreeIsOneEmptyFocusedPane() {
        let tree = PaneTree()
        #expect(shape(tree.root) == "()")
        #expect(tree.leaves.count == 1)
        #expect(tree.focusedLeafID == tree.leaves[0].id)
        #expect(tree.activeNoteID == nil)
    }

    @Test func openingANoteAddsATabOnceAndActivatesIt() {
        var tree = PaneTree()
        tree.open("a")
        tree.open("b")
        tree.open("a")
        #expect(shape(tree.root) == "(a* b)")
        #expect(tree.activeNoteID == "a")
    }

    @Test func closingTheActiveTabActivatesItsNeighbour() {
        var tree = PaneTree()
        for note in ["a", "b", "c"] { tree.open(note) }
        let pane = tree.focusedLeafID

        tree.activate("b", in: pane)
        tree.closeTab("b", in: pane)
        #expect(shape(tree.root) == "(a c*)", "the tab that slides into its place")

        tree.closeTab("c", in: pane)
        #expect(shape(tree.root) == "(a*)", "or the one before, at the end")

        tree.closeTab("a", in: pane)
        #expect(shape(tree.root) == "()", "the only pane stays, empty")
    }

    @Test func closingATabThatIsNotActiveLeavesTheActiveOneAlone() {
        var tree = PaneTree()
        for note in ["a", "b", "c"] { tree.open(note) }
        tree.closeTab("a", in: tree.focusedLeafID)
        #expect(shape(tree.root) == "(b c*)")
    }

    @Test func splittingPutsANewFocusedPaneOnTheSideAsked() {
        var tree = PaneTree()
        tree.open("a")
        let first = tree.focusedLeafID

        let right = tree.split(first, .right, opening: "b")
        #expect(shape(tree.root) == "h[(a*) (b*)]")
        #expect(tree.focusedLeafID == right)

        tree.split(first, .up, opening: "c")
        #expect(shape(tree.root) == "h[v[(c*) (a*)] (b*)]")

        tree.split(right, .left, opening: nil)
        #expect(shape(tree.root) == "h[v[(c*) (a*)] h[() (b*)]]")

        tree.split(right, .down, opening: "d")
        #expect(shape(tree.root) == "h[v[(c*) (a*)] h[() v[(b*) (d*)]]]")
    }

    @Test func aSplitStartsInHalves() {
        var tree = PaneTree()
        tree.split(tree.focusedLeafID, .right, opening: nil)
        guard case .split(let split) = tree.root else {
            Issue.record("expected a split")
            return
        }
        #expect(split.ratios == [0.5, 0.5])
    }

    @Test func closingAPaneGivesItsSpaceToTheOthers() {
        var tree = PaneTree()
        tree.open("a")
        let first = tree.focusedLeafID
        let second = tree.split(first, .right, opening: "b")
        let third = tree.split(second, .right, opening: "c")
        #expect(shape(tree.root) == "h[(a*) h[(b*) (c*)]]")

        tree.closeLeaf(third)
        #expect(shape(tree.root) == "h[(a*) (b*)]", "a split left with one pane is replaced by it")
        #expect(tree.focusedLeafID != third)
        #expect(tree.leaf(tree.focusedLeafID) != nil, "focus moves to a pane that still exists")

        tree.closeLeaf(first)
        #expect(shape(tree.root) == "(b*)")
        tree.closeLeaf(second)
        #expect(shape(tree.root) == "()", "the last pane is emptied, never removed")
    }

    @Test func closingThePanesLastTabClosesThePaneWhenThereAreOthers() {
        var tree = PaneTree()
        tree.open("a")
        let second = tree.split(tree.focusedLeafID, .right, opening: "b")
        tree.closeTab("b", in: second)
        #expect(shape(tree.root) == "(a*)")
    }

    @Test func ratiosAreSetOnlyWhenTheyFitTheSplit() {
        var tree = PaneTree()
        tree.split(tree.focusedLeafID, .right, opening: nil)
        guard case .split(let split) = tree.root else { return }

        tree.setRatios(split.id, [0.3, 0.7])
        tree.setRatios(split.id, [1])
        tree.setRatios("missing", [0.9, 0.1])

        guard case .split(let after) = tree.root else { return }
        #expect(after.ratios == [0.3, 0.7])
    }

    @Test func deletedNotesLeaveEveryPane() {
        var tree = PaneTree()
        for note in ["a", "b"] { tree.open(note) }
        let first = tree.focusedLeafID
        tree.split(first, .right, opening: "b")
        tree.split(first, .down, opening: "c")
        #expect(shape(tree.root) == "h[v[(a b*) (c*)] (b*)]")

        tree.remove(notes: ["b", "c"])
        #expect(shape(tree.root) == "(a*)", "panes left with nothing are closed")

        tree.remove(notes: ["a"])
        #expect(shape(tree.root) == "()")
        #expect(tree.leaf(tree.focusedLeafID) != nil)
    }

    @Test func aTabCanMoveWithinAPaneAndBetweenPanes() {
        var tree = PaneTree()
        for note in ["a", "b", "c"] { tree.open(note) }
        let first = tree.focusedLeafID
        let second = tree.split(first, .right, opening: "x")

        tree.moveTab("c", from: first, to: first, at: 0)
        #expect(shape(tree.root) == "h[(c* a b) (x*)]")

        tree.moveTab("a", from: first, to: second, at: 0)
        #expect(shape(tree.root) == "h[(c* b) (a* x)]")
        #expect(tree.focusedLeafID == second)
    }

    @Test func theTreeSurvivesBeingSavedAndRestored() throws {
        var tree = PaneTree()
        tree.open("a")
        tree.split(tree.focusedLeafID, .down, opening: "b")
        let restored = try JSONDecoder().decode(PaneTree.self, from: JSONEncoder().encode(tree))
        #expect(restored == tree)
    }

    @Test func aRestoredTreeDropsNotesThatNoLongerExist() {
        var tree = PaneTree()
        tree.open("kept")
        tree.split(tree.focusedLeafID, .right, opening: "gone")
        tree.keep(notes: ["kept"])
        #expect(shape(tree.root) == "(kept*)")
    }
}
