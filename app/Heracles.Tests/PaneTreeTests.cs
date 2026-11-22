using Heracles.Workspaces;

namespace Heracles.Tests;

public sealed class PaneTreeTests
{
    /// <summary>
    /// The tree as text: a leaf is its tabs with the active one starred, a
    /// split is its children in brackets, `h` side by side or `v` stacked.
    /// </summary>
    private static string Shape(PaneNode node) => node switch
    {
        PaneLeaf leaf => "(" + string.Join(" ", leaf.Tabs.Select(tab => tab == leaf.ActiveTab ? tab + "*" : tab)) + ")",
        PaneSplit split => (split.Axis == SplitAxis.Horizontal ? "h" : "v") + "[" + string.Join(" ", split.Children.Select(Shape)) + "]",
        _ => "?",
    };

    private static PaneTree Opened(params string[] notes) =>
        notes.Aggregate(new PaneTree(), (tree, note) => tree.Open(note));

    [Fact]
    public void ANewTreeIsOneEmptyFocusedPane()
    {
        var tree = new PaneTree();
        Assert.Equal("()", Shape(tree.Root));
        Assert.Single(tree.Leaves);
        Assert.Equal(tree.Leaves[0].Id, tree.FocusedLeafId);
        Assert.Null(tree.ActiveNoteId);
    }

    [Fact]
    public void OpeningANoteAddsATabOnceAndActivatesIt()
    {
        var tree = Opened("a", "b", "a");
        Assert.Equal("(a* b)", Shape(tree.Root));
        Assert.Equal("a", tree.ActiveNoteId);
    }

    [Fact]
    public void ClosingTheActiveTabActivatesItsNeighbour()
    {
        var tree = Opened("a", "b", "c");
        var pane = tree.FocusedLeafId;

        tree = tree.Activate("b", pane).CloseTab("b", pane);
        Assert.Equal("(a c*)", Shape(tree.Root)); // the tab that slides into its place

        tree = tree.CloseTab("c", pane);
        Assert.Equal("(a*)", Shape(tree.Root)); // or the one before, at the end

        tree = tree.CloseTab("a", pane);
        Assert.Equal("()", Shape(tree.Root)); // the only pane stays, empty
    }

    [Fact]
    public void ClosingATabThatIsNotActiveLeavesTheActiveOneAlone()
    {
        var tree = Opened("a", "b", "c");
        tree = tree.CloseTab("a", tree.FocusedLeafId);
        Assert.Equal("(b c*)", Shape(tree.Root));
    }

    [Fact]
    public void SplittingPutsANewFocusedPaneOnTheSideAsked()
    {
        var tree = Opened("a");
        var first = tree.FocusedLeafId;

        tree = tree.Split(first, SplitTarget.Right, "b", out var right);
        Assert.Equal("h[(a*) (b*)]", Shape(tree.Root));
        Assert.Equal(right, tree.FocusedLeafId);

        tree = tree.Split(first, SplitTarget.Up, "c", out _);
        Assert.Equal("h[v[(c*) (a*)] (b*)]", Shape(tree.Root));

        tree = tree.Split(right, SplitTarget.Left, null, out _);
        Assert.Equal("h[v[(c*) (a*)] h[() (b*)]]", Shape(tree.Root));

        tree = tree.Split(right, SplitTarget.Down, "d", out _);
        Assert.Equal("h[v[(c*) (a*)] h[() v[(b*) (d*)]]]", Shape(tree.Root));
    }

    [Fact]
    public void ASplitStartsInHalves()
    {
        var tree = new PaneTree();
        tree = tree.Split(tree.FocusedLeafId, SplitTarget.Right, null, out _);
        var split = Assert.IsType<PaneSplit>(tree.Root);
        Assert.Equal(new[] { 0.5, 0.5 }, split.Ratios.ToArray());
    }

    [Fact]
    public void ClosingAPaneGivesItsSpaceToTheOthers()
    {
        var tree = Opened("a");
        var first = tree.FocusedLeafId;
        tree = tree.Split(first, SplitTarget.Right, "b", out var second);
        tree = tree.Split(second, SplitTarget.Right, "c", out var third);
        Assert.Equal("h[(a*) h[(b*) (c*)]]", Shape(tree.Root));

        tree = tree.CloseLeaf(third);
        Assert.Equal("h[(a*) (b*)]", Shape(tree.Root)); // a split left with one pane is replaced by it
        Assert.NotEqual(third, tree.FocusedLeafId);
        Assert.NotNull(tree.Leaf(tree.FocusedLeafId)); // focus moves to a pane that still exists

        tree = tree.CloseLeaf(first);
        Assert.Equal("(b*)", Shape(tree.Root));
        tree = tree.CloseLeaf(second);
        Assert.Equal("()", Shape(tree.Root)); // the last pane is emptied, never removed
    }

    [Fact]
    public void ClosingThePanesLastTabClosesThePaneWhenThereAreOthers()
    {
        var tree = Opened("a");
        tree = tree.Split(tree.FocusedLeafId, SplitTarget.Right, "b", out var second);
        tree = tree.CloseTab("b", second);
        Assert.Equal("(a*)", Shape(tree.Root));
    }

    [Fact]
    public void RatiosAreSetOnlyWhenTheyFitTheSplit()
    {
        var tree = new PaneTree();
        tree = tree.Split(tree.FocusedLeafId, SplitTarget.Right, null, out _);
        var split = Assert.IsType<PaneSplit>(tree.Root);

        tree = tree.SetRatios(split.Id, [0.3, 0.7]).SetRatios(split.Id, [1]).SetRatios("missing", [0.9, 0.1]);

        Assert.Equal(new[] { 0.3, 0.7 }, Assert.IsType<PaneSplit>(tree.Root).Ratios.ToArray());
    }

    [Fact]
    public void DeletedNotesLeaveEveryPane()
    {
        var tree = Opened("a", "b");
        var first = tree.FocusedLeafId;
        tree = tree.Split(first, SplitTarget.Right, "b", out _).Split(first, SplitTarget.Down, "c", out _);
        Assert.Equal("h[v[(a b*) (c*)] (b*)]", Shape(tree.Root));

        tree = tree.Remove(new HashSet<string> { "b", "c" });
        Assert.Equal("(a*)", Shape(tree.Root)); // panes left with nothing are closed

        tree = tree.Remove(new HashSet<string> { "a" });
        Assert.Equal("()", Shape(tree.Root));
        Assert.NotNull(tree.Leaf(tree.FocusedLeafId));
    }

    [Fact]
    public void ATabCanMoveWithinAPaneAndBetweenPanes()
    {
        var tree = Opened("a", "b", "c");
        var first = tree.FocusedLeafId;
        tree = tree.Split(first, SplitTarget.Right, "x", out var second);

        tree = tree.MoveTab("c", first, first, 0);
        Assert.Equal("h[(c* a b) (x*)]", Shape(tree.Root));

        tree = tree.MoveTab("a", first, second, 0);
        Assert.Equal("h[(c* b) (a* x)]", Shape(tree.Root));
        Assert.Equal(second, tree.FocusedLeafId);
    }

    [Fact]
    public void TheTreeSurvivesBeingSavedAndRestored()
    {
        var tree = Opened("a");
        tree = tree.Split(tree.FocusedLeafId, SplitTarget.Down, "b", out _);
        var split = Assert.IsType<PaneSplit>(tree.Root);
        tree = tree.SetRatios(split.Id, [0.25, 0.75]);

        var restored = PaneTree.FromJson(tree.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(tree.ToJson(), restored.ToJson());
        Assert.Equal("v[(a*) (b*)]", Shape(restored.Root));
        Assert.Equal(tree.FocusedLeafId, restored.FocusedLeafId);
        Assert.Equal(new[] { 0.25, 0.75 }, Assert.IsType<PaneSplit>(restored.Root).Ratios.ToArray());
    }

    [Fact]
    public void ARestoredTreeDropsNotesThatNoLongerExist()
    {
        var tree = Opened("kept");
        tree = tree.Split(tree.FocusedLeafId, SplitTarget.Right, "gone", out _);
        tree = tree.Keep(new HashSet<string> { "kept" });
        Assert.Equal("(kept*)", Shape(tree.Root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"focused":"x","root":{"split":{"id":"s","axis":"h","children":[],"ratios":[]}}}""")]
    public void WhatIsNotATreeIsNotRestored(string? json)
    {
        Assert.Null(PaneTree.FromJson(json));
    }

    [Fact]
    public void ARestoredTreeWhoseFocusedPaneIsMissingFocusesOneThatExists()
    {
        var restored = PaneTree.FromJson("""{"focused":"nowhere","root":{"leaf":{"id":"p1","tabs":["a"],"active":"a"}}}""");

        Assert.NotNull(restored);
        Assert.Equal("p1", restored.FocusedLeafId);
        Assert.Equal("a", restored.ActiveNoteId);
    }

    [Fact]
    public void ChangingATreeLeavesTheOneItCameFromAsItWas()
    {
        var before = Opened("a");
        var after = before.Open("b");

        Assert.Equal("(a*)", Shape(before.Root));
        Assert.Equal("(a b*)", Shape(after.Root));
    }
}
