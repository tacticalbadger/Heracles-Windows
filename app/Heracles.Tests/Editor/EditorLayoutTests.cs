using System.Diagnostics;
using System.Windows;
using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;
using Block = Heracles.Editor.Block;

namespace Heracles.Tests.Editor;

/// <summary>
/// A note on the page: where its blocks are, which block a point is in, and
/// where a dropped block would land. The Mac's <c>EditorLayoutTests</c>, and
/// after them what is particular to Windows: the one text box.
/// </summary>
public sealed class EditorLayoutTests
{
    [Fact]
    public void BlocksAreStackedDownThePageInOrder() => Sta.Run(() =>
    {
        var harness = new EditorHarness(
            H(1, "h", "Title"), P("a", string.Concat(Enumerable.Repeat("long ", 120))), Bullet("b", "B", Bullet("c", "C")),
            Quote("q", P("x", "Quoted")), Code("k", "let x = 1"), Divider("d"), P("z", "End"));
        var bottom = 0.0;
        foreach (var id in new[] { "h", "a", "b", "q", "k", "d", "z" })
        {
            var frame = harness.Frame(id);
            Assert.True(frame.Top >= bottom, $"{id} starts below the block before it");
            Assert.True(frame.Height > 0, $"{id} has a height");
            bottom = frame.Bottom;
        }
        // A paragraph that wraps is as tall as its lines.
        Assert.True(harness.Frame("a").Height > harness.Frame("z").Height * 3);
        // What is inside a block is drawn inside it.
        Assert.True(harness.Frame("b").Contains(harness.Frame("c")));
        Assert.True(harness.Frame("c").Left > harness.Frame("b").Left);
        Assert.True(harness.Page.ActualHeight >= bottom);
    });

    [Fact]
    public void ColumnsSitSideBySideByTheirWidths() => Sta.Run(() =>
    {
        var wide = Column("c1", P("a", "A"));
        wide.Width = 2;
        var harness = new EditorHarness(Columns("r", wide, Column("c2", P("b", "B"))));
        // Columns have no stored ids, so they were given new ones on loading.
        var row = harness.Editor.Document.Blocks[0];
        var left = harness.Frame(row.Children[0].Id);
        var right = harness.Frame(row.Children[1].Id);
        Assert.True(left.Right < right.Left);
        Assert.True(Math.Abs(left.Width - right.Width * 2) < 2);
        Assert.Equal(left.Top, right.Top);
    });

    [Fact]
    public void TheBlockUnderAPointIsFoundByItsRow() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), Bullet("b", "B", Bullet("c", "C")), P("z", "Z"));
        var page = harness.Page;
        var child = harness.Frame("c");
        // In the margin beside a block counts as on it.
        Assert.Equal("c", page.BlockViewAt(new Point(5, child.Top + child.Height / 2))?.Block.Id);
        Assert.Equal("b", page.BlockViewAt(new Point(child.Left + child.Width / 2, harness.Frame("b").Top + 4))?.Block.Id);
        Assert.Null(page.BlockViewAt(new Point(child.Left + child.Width / 2, harness.Frame("z").Bottom + 300)));
    });

    [Fact]
    public void ACalloutIsFoundAsAWholeFromTheMarginAndItsBlocksFromInside() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), Callout("k", P("x", "X"), P("y", "Y")), Quote("q", P("z", "Z")));
        var page = harness.Page;
        var inner = harness.Frame("y");
        var middle = inner.Top + inner.Height / 2;
        Assert.Equal("k", page.BlockViewAt(new Point(5, middle))?.Block.Id);
        Assert.Equal("y", page.BlockViewAt(new Point(inner.Left + inner.Width / 2, middle))?.Block.Id);
        var quoted = harness.Frame("z");
        Assert.Equal("q", page.BlockViewAt(new Point(5, quoted.Top + quoted.Height / 2))?.Block.Id);
        // So a block dragged to the margin lands beside the callout, not in it.
        Assert.Equal(DropTarget.After("k"), page.DropTargetAt(new Point(5, harness.Frame("k").Bottom - 2), ["a"]));
    });

    [Fact]
    public void ADropLandsBeforeAfterOrBesideTheBlockUnderIt() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"), Bullet("c", "C", Bullet("d", "D")), P("z", "Z"));
        var page = harness.Page;
        var b = harness.Frame("b");
        Assert.Equal(DropTarget.Before("b"), page.DropTargetAt(new Point(b.Left + 10, b.Top + 2), ["z"]));
        Assert.Equal(DropTarget.After("b"), page.DropTargetAt(new Point(b.Left + 10, b.Bottom - 2), ["z"]));
        Assert.Equal(DropTarget.RightOf("b"), page.DropTargetAt(new Point(b.Right - 10, b.Top + b.Height / 2), ["z"]));
        // Just under an item that holds others is at the head of them.
        var c = harness.Frame("c");
        var own = harness.View<TextBlockView>("c").OwnRowHeight;
        Assert.Equal(DropTarget.Before("d"), page.DropTargetAt(new Point(c.Left + 10, c.Top + own - 2), ["z"]));
        // A block is never dropped on itself or inside itself.
        Assert.NotEqual(DropTarget.Before("c"), page.DropTargetAt(new Point(c.Left + 10, c.Top + 2), ["c"]));
        // Past the end of the note is after the last block.
        Assert.Equal(DropTarget.After("z"), page.DropTargetAt(new Point(b.Left + 10, harness.Frame("z").Bottom + 200), ["a"]));
        // Picture files dropped on the page never make columns.
        Assert.Equal(
            DropTarget.After("b"), page.DropTargetAt(new Point(b.Right - 10, b.Bottom - 2), [], allowColumns: false));
    });

    [Fact]
    public void ALongNoteOpensQuickly() => Sta.Run(() =>
    {
        var blocks = Enumerable.Range(0, 600)
            .Select(index => P($"p{index}", $"Paragraph {index} with a little text in it to lay out.")).ToArray();
        var json = new EditorDocument(blocks).Json;
        var watch = Stopwatch.StartNew();
        var editor = new BlockEditorController(BlockIds.Sequential());
        Views.Layout(editor.ScrollView);
        editor.Load(json);
        var height = editor.Page.ActualHeight;
        watch.Stop();
        Assert.True(height > 600 * 20, $"every block was laid out (the page is {height} tall)");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"600 blocks loaded and laid out in {watch.Elapsed}");
    });

    // From here on: what is particular to Windows.

    [Fact]
    public void EveryKindOfBlockIsShownByAViewOfItsKind() => Sta.Run(() =>
    {
        var unknown = new Block("u", BlockKind.Unknown) { Raw = JsonValue.Parse("""{"type":"mystery","attrs":{"blockId":"u"}}""") };
        var harness = new EditorHarness(
            H(2, "h", "Title"), P("p", "Para"), Bullet("b", "Bullet"), Numbered("n", "One"), Todo("t", "Task", true),
            Code("c", "x = 1", "python"), Quote("q", P("qp", "Quoted")), Callout("k", P("kp", "Hot")),
            Toggle("g", false, P("gs", "Summary"), P("gh", "Hidden")), Columns("r", Column("c1", P("l", "L")), Column("c2", P("m", "M"))),
            Divider("d"), Image("i"), unknown);
        Assert.IsType<TextBlockView>(harness.Page.BlockViewFor("h"));
        Assert.IsType<TextBlockView>(harness.Page.BlockViewFor("t"));
        Assert.IsType<CodeBlockView>(harness.Page.BlockViewFor("c"));
        Assert.IsType<QuoteBlockView>(harness.Page.BlockViewFor("q"));
        Assert.IsType<CalloutBlockView>(harness.Page.BlockViewFor("k"));
        Assert.IsType<ToggleBlockView>(harness.Page.BlockViewFor("g"));
        Assert.IsType<ColumnsBlockView>(harness.Page.BlockViewFor(harness.Editor.Document.Blocks[9].Id));
        Assert.IsType<DividerBlockView>(harness.Page.BlockViewFor("d"));
        Assert.IsType<ImageBlockView>(harness.Page.BlockViewFor("i"));
        Assert.IsType<UnknownBlockView>(harness.Page.BlockViewFor("u"));
        Assert.Equal("Title", harness.Text("h"));
        Assert.Equal("x = 1", harness.Text("c"));
        Assert.Equal("Hot", harness.Text("kp"));
        // A closed toggle shows its first block only.
        // (Without a window WPF calls nothing visible, so it is the views' own word that is read.)
        Assert.Equal(Visibility.Visible, harness.Page.BlockViewFor("gs")!.Visibility);
        Assert.Equal(Visibility.Collapsed, harness.Page.BlockViewFor("gh")!.Visibility);
        Assert.NotNull(harness.Page.FrameOf("gs"));
        Assert.Null(harness.Page.FrameOf("gh"));
        Assert.True(harness.Frame("g").Height < harness.Frame("q").Height * 2, "the closed toggle is one block tall");
        // The picture was asked for.
        Assert.Equal(["heracles-image://local/abc"], harness.Host.Loaded);
        // Showing a note is not changing it.
        Assert.Equal(0, harness.Host.Changes);
    });

    [Fact]
    public void NumberedItemsCountFromWhereTheirListStarts() => Sta.Run(() =>
    {
        var third = Numbered("n3", "Three");
        var restarted = Numbered("m1", "Again");
        restarted.StartsList = true;
        restarted.ListAttrs["start"] = JsonValue.Of(7);
        var harness = new EditorHarness(
            Numbered("n1", "One"), Numbered("n2", "Two", Numbered("i1", "Inner")), third, restarted, Numbered("m2", "More"),
            P("p", "Para"), Numbered("o1", "Over"));
        int Number(string id) => harness.Page.BlockViewFor(id)!.Ordinal;
        Assert.Equal([1, 2, 1, 3, 7, 8, 1], new[] { "n1", "n2", "i1", "n3", "m1", "m2", "o1" }.Select(Number));
    });

    [Fact]
    public void OnlyTheBlockWithTheCaretHasATextBox() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), Bullet("b", "World"), Code("c", "x"));
        Assert.Null(harness.Focused);
        Assert.Null(harness.Page.TextBoxFor("a"));
        Assert.Equal("none", harness.Selection);

        harness.Caret("a", 2);
        Assert.NotNull(harness.Focused);
        Assert.Same(harness.Focused, harness.Page.TextBoxFor("a"));
        Assert.Equal("a", harness.Focused.BlockId);
        Assert.Equal("a|2", harness.Selection);
        Assert.Equal((2, 2), harness.Focused.SelectedRange);
        Assert.Null(harness.Page.TextBoxFor("b"));

        harness.Caret("b", 99);
        Assert.Same(harness.Focused, harness.Page.TextBoxFor("b"));
        Assert.Null(harness.Page.TextBoxFor("a"));
        // An offset beyond the text is brought back into it.
        Assert.Equal("b|5", harness.Selection);
        Assert.Equal("Hello", harness.Text("a"));
        Assert.Equal("World", harness.Text("b"));

        harness.Editor.Select(EditorSelection.Text("c", 0, 1));
        Assert.True(harness.Focused!.IsCode);
        Assert.Equal("c|0-1", harness.Selection);
        Assert.Equal(1, harness.Host.Focuses > 0 ? 1 : 0);
        Assert.Equal(0, harness.Host.Changes);
    });

    [Fact]
    public void GivingABlockTheTextBoxMovesNothing() => Sta.Run(() =>
    {
        var lengthy = string.Concat(Enumerable.Repeat("a paragraph of some length ", 20));
        var marked = P("m", lengthy);
        marked.Text = marked.Text.Set(TextMark.Bold, true, 10, 200).Set(TextMark.Code, true, 220, 260);
        var harness = new EditorHarness(
            H(1, "h", "A title"), P("p", lengthy), marked, Bullet("b", "An item " + lengthy, Bullet("c", "Inner")),
            Todo("t", "A task", true), Numbered("n", "One"), Quote("q", P("x", "Quoted " + lengthy)), Code("k", "let x = 1\n  + 2"),
            Callout("o", P("y", "Hot")), P("e"), P("z", "End 💡"));
        var ids = new[] { "h", "p", "m", "b", "c", "t", "n", "q", "x", "k", "o", "y", "e", "z" };
        foreach (var width in new[] { 800.0, 420.0 })
        {
            harness.Editor.Select(EditorSelection.None);
            harness.Layout(width);
            var before = ids.ToDictionary(id => id, harness.Frame);
            var height = harness.Page.ActualHeight;
            foreach (var id in new[] { "h", "p", "m", "b", "c", "t", "n", "x", "k", "y", "e", "z" })
            {
                harness.Caret(id, 0);
                harness.Layout(width);
                Assert.NotNull(harness.Page.TextBoxFor(id));
                foreach (var other in ids)
                {
                    var now = harness.Frame(other);
                    Assert.True(
                        Math.Abs(now.Top - before[other].Top) < 0.01 && Math.Abs(now.Height - before[other].Height) < 0.01
                        && Math.Abs(now.Left - before[other].Left) < 0.01 && Math.Abs(now.Width - before[other].Width) < 0.01,
                        $"at {width}, with the caret in {id}, {other} went from {before[other]} to {now}");
                }
                Assert.Equal(height, harness.Page.ActualHeight, 2);
            }
        }
    });

    [Fact]
    public void SelectingWholeBlocksMarksThemAndGivesUpTheTextBox() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"), P("c", "C"));
        harness.Caret("a", 1);
        harness.Editor.Select(EditorSelection.Blocks(["b", "missing", "c"]));
        Assert.Equal("[b c]", harness.Selection);
        Assert.Equal(["b", "c"], harness.Page.SelectedBlocks);
        Assert.Null(harness.Focused);
        Assert.Null(harness.Page.TextBoxFor("a"));

        harness.Caret("c", 0);
        Assert.Empty(harness.Page.SelectedBlocks);
        Assert.Equal("c|0", harness.Selection);

        harness.Editor.Select(EditorSelection.None);
        Assert.Equal("none", harness.Selection);
    });

    [Fact]
    public void ABlockKeepsItsViewFromOneLoadToTheNext() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), P("b", "World"), P("c", "Gone"));
        var a = harness.Page.BlockViewFor("a");
        var b = harness.Page.BlockViewFor("b");
        harness.Caret("b", 3);
        // The same note, saved elsewhere: b is changed, c is gone, d is new, and a and b have changed places.
        harness.Editor.Load(Doc(P("b", "World!"), H(2, "d", "New"), P("a", "Hello")).Json);
        harness.Layout();
        Assert.Same(a, harness.Page.BlockViewFor("a"));
        Assert.Same(b, harness.Page.BlockViewFor("b"));
        Assert.Null(harness.Page.BlockViewFor("c"));
        Assert.Equal("World!", harness.Text("b"));
        Assert.Equal("New", harness.Text("d"));
        Assert.True(harness.Frame("b").Top < harness.Frame("d").Top);
        Assert.True(harness.Frame("d").Top < harness.Frame("a").Top);
        // Without the keyboard, the caret is not put back.
        Assert.Equal("none", harness.Selection);
        Assert.Equal(0, harness.Host.Changes);
        // Text that is not a document is refused, and the note stays as it was.
        Assert.False(harness.Editor.Load("not a document"));
        Assert.Equal("p#b World!\nh2#d New\np#a Hello", harness.Outline);
    });

    [Fact]
    public void AViewAskedAboutABlockThatHasGoneAnswersNothing() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"));
        harness.Caret("a", 1);
        Assert.Null(harness.Page.BlockViewFor("missing"));
        Assert.Null(harness.Page.TextBoxFor("missing"));
        Assert.Null(harness.Page.EnsureTextBox("missing"));
        Assert.Null(harness.Page.FrameOf("missing"));
        Assert.NotNull(harness.Page.DropTargetAt(new Point(100, 20), ["missing"]));
        harness.Editor.Select(EditorSelection.Caret("missing", 0));
        Assert.Equal("a|1", harness.Selection);
        harness.Editor.Select(EditorSelection.Blocks(["missing"]));
        Assert.Equal("a|1", harness.Selection);
        // A divider has no text to put a caret in.
        harness.Editor.Load(Doc(Divider("d")).Json);
        harness.Editor.Select(EditorSelection.Caret("d", 0));
        Assert.Equal("none", harness.Selection);
    });

    [Fact]
    public void ANoteThatCannotBeEditedCanStillBeRead() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), Todo("t", "Task"));
        harness.Editor.IsEditable = false;
        harness.Layout();
        harness.Editor.Select(EditorSelection.Text("a", 1, 4));
        Assert.Equal("a|1-4", harness.Selection);
        Assert.True(harness.Focused!.IsReadOnly);
        harness.Type("!");
        Assert.Equal("Hello", harness.Text("a"));
        Assert.Equal("p#a Hello\ntodo#t Task", harness.Outline);
        harness.Editor.IsEditable = true;
        Assert.False(harness.Focused!.IsReadOnly);
    });

    [Fact]
    public void TheTextColumnIsNoWiderThanIsGoodToRead() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"));
        harness.Layout(1400);
        var wide = harness.Frame("a");
        Assert.Equal(EditorTheme.ContentWidth, wide.Width, 1);
        Assert.Equal((1400 - EditorTheme.ContentWidth) / 2, wide.Left, 0);
        harness.Layout(500);
        var narrow = harness.Frame("a");
        Assert.Equal(EditorTheme.Gutter, narrow.Left, 0);
        Assert.True(narrow.Width <= 500 - EditorTheme.Gutter * 2 + 1);
        // Never shorter than the window, so a click anywhere below the text lands on the page.
        Assert.True(harness.Page.ActualHeight >= 600 - 1);
    });
}
