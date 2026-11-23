using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;
using Block = Heracles.Editor.Block;

namespace Heracles.Tests.Editor;

// The Mac's EditorBlockActionTests and EditorPasteboardTests, and what the
// mouse does. A clipboard here is a DataObject of the test's own: the
// system's is never read or written. Nothing here moves the mouse either:
// each test calls what a mouse handler calls.

public sealed class EditorBlockActionTests
{
    [Fact]
    public void CheckingATaskChangesTheDocument() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Todo("t", "Book ferry"));
        harness.Editor.SetChecked("t", true);
        Assert.Equal("done#t Book ferry", harness.Outline);
        Assert.Equal(1, harness.Host.Changes);
        harness.Editor.Undo();
        Assert.Equal("todo#t Book ferry", harness.Outline);
    });

    [Fact]
    public void FoldingAToggleMovesTheCaretOutOfWhatItHides() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Toggle("g", P("s", "Summary"), P("h", "Hidden")));
        harness.Caret("h", 2);
        harness.Editor.SetOpen("g", false);
        Assert.Equal("toggle(closed)#g\n  p#s Summary\n  p#h Hidden", harness.Outline);
        Assert.Equal("s|7", harness.Selection);
        Assert.Equal(Visibility.Collapsed, harness.Page.BlockViewFor("h")!.Visibility);
    });

    [Fact]
    public void DuplicateMoveAndRemove() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"));
        harness.Editor.Duplicate("a");
        Assert.Equal("p#a A\np#n1 A\np#b B", harness.Outline);
        Assert.Equal("[n1]", harness.Selection);
        Assert.True(harness.Editor.Move(["b"], DropTarget.Before("a")));
        Assert.Equal("p#b B\np#a A\np#n1 A", harness.Outline);
        harness.Editor.Remove(["a", "n1"]);
        Assert.Equal("p#b B", harness.Outline);
        harness.Editor.Undo();
        harness.Editor.Undo();
        Assert.Equal("p#a A\np#n1 A\np#b B", harness.Outline);
    });

    [Fact]
    public void TurningTheBlockBeingTypedInKeepsTheCaret() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Caret("a", 3);
        harness.Editor.TurnCurrentBlock(BlockTarget.Heading(2));
        Assert.Equal("h2#a Hello", harness.Outline);
        Assert.Equal("a|3", harness.Selection);
        Assert.Same(harness.Page.TextBoxFor("a"), harness.Focused);
        harness.Editor.TurnCurrentBlock(BlockTarget.Code);
        Assert.Equal("code#a Hello", harness.Outline);
        Assert.NotNull(harness.Focused);
        Assert.Same(harness.Page.TextBoxFor("a"), harness.Focused);
        Assert.True(harness.Focused.IsCode);
    });

    [Fact]
    public void ARevealedBlockIsFoundByEitherOfAListItemsIds() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), Toggle("g", false, P("s", "S"), Bullet("b", "B")));
        Assert.True(harness.Editor.Reveal("a"));
        Assert.True(harness.Editor.Reveal("b-p"));
        // It was folded away, so its toggle is now open.
        Assert.True(harness.Editor.Document.Block("g")?.IsOpen);
        Assert.False(harness.Editor.Reveal("missing"));
    });

    [Fact]
    public void DroppingImageFilesStoresThemAndMakesBlocks() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"));
        var folder = Directory.CreateTempSubdirectory("heracles-test-").FullName;
        try
        {
            var file = Path.Combine(folder, "Map of the coast.png");
            File.WriteAllBytes(file, [0x89, 0x50, 0x4E, 0x47]);
            var other = Path.Combine(folder, "notes.txt");
            File.WriteAllText(other, "x");

            Assert.True(harness.Editor.InsertImages([file], DropTarget.Before("b")));
            Assert.Equal("p#a A\nimage#n1\np#b B", harness.Outline);
            Assert.Equal(["image/png"], harness.Host.Stored.Select(stored => stored.Mime));
            var image = harness.Editor.Document.Block("n1");
            Assert.Equal("heracles-image://local/image1", image?.ImageSource);
            Assert.Equal(JsonValue.Of("Map of the coast"), image?.Attrs["alt"]);
            Assert.Null(BlockEditorController.ImageType(other));
            Assert.Equal("image/jpeg", BlockEditorController.ImageType(@"C:\pictures\Shore.JPG"));
            // A file that is not a picture, or is not there, makes nothing.
            Assert.False(harness.Editor.InsertImages([other, Path.Combine(folder, "gone.png")], null));
            Assert.Equal("p#a A\nimage#n1\np#b B", harness.Outline);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    // Windows

    [Fact]
    public void TheHandlesMenuActsOnItsBlock() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"));
        harness.Editor.AddBlock("a");
        Assert.Equal("p#a A\np#n1\np#b B", harness.Outline);
        Assert.Equal("n1|0", harness.Selection);
        harness.Editor.Turn("b", BlockTarget.Quote);
        Assert.Equal("p#a A\np#n1\nquote#n2\n  p#b B", harness.Outline);
        harness.Editor.CopyLinkToBlock("b");
        Assert.Equal(["b"], harness.Host.CopiedLinks);
        // What the menu offers depends on the block.
        Assert.Contains("Turn Into", harness.Editor.BlockMenu("a").Select(item => item.Title));
        Assert.Equal(
            ["Duplicate", "Copy Link to Block", "", "Delete"],
            harness.Editor.BlockMenu(harness.Editor.Document.Blocks[2].Children[0].Id).Select(item => item.Title).SkipWhile(title => title != "Duplicate"));
        harness.Editor.Load(Doc(Divider("d"), P("z")).Json);
        Assert.DoesNotContain("Turn Into", harness.Editor.BlockMenu("d").Select(item => item.Title));
        Assert.Empty(harness.Editor.BlockMenu("missing"));
        // Choosing from it does the thing.
        harness.Editor.BlockMenu("d").Single(item => item.Title == "Delete").Run!();
        Assert.Equal("p#z", harness.Outline);
        var turn = harness.Editor.BlockMenu("z").Single(item => item.Title == "Turn Into");
        turn.Inside!.Single(item => item.Title == "Heading 2").Run!();
        Assert.Equal("h2#z", harness.Outline);
    });

    [Fact]
    public void ACodeBlocksLanguageAndACalloutsIconAreKept() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Code("c", "x = 1"), Callout("k", P("p", "Hot")));
        harness.Editor.SetLanguage("c", "python");
        harness.Editor.SetEmoji("k", "🔥");
        Assert.Equal("python", harness.Editor.Document.Block("c")?.Language);
        Assert.Equal("🔥", harness.Editor.Document.Block("k")?.Emoji);
        Assert.Equal(2, harness.Host.Changes);
        harness.Editor.Undo();
        Assert.Equal("💡", harness.Editor.Document.Block("k")?.Emoji);
        // The same again is not a change.
        harness.Editor.SetLanguage("c", "python");
        Assert.Equal(3, harness.Host.Changes);
    });

    [Fact]
    public void DraggingADividerBetweenColumnsIsOneStepToUndo() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B")), Column("c3", P("c", "C"))));
        var row = harness.Editor.Document.Blocks[0];
        var view = harness.View<ColumnsBlockView>(row.Id);
        var before = harness.Frame(row.Children[0].Id).Width;
        harness.Editor.BeginUndoGroup();
        view.Resize(after: 0, by: 60, from: [1, 1, 1]);
        view.Resize(after: 0, by: 120, from: [1, 1, 1]);
        harness.Page.UpdateLayout();
        var widths = row.Children.Select(column => column.Width).ToArray();
        Assert.True(widths[0] > 1 && widths[1] < 1);
        Assert.Equal(1, widths[2]);
        Assert.Equal(2, widths[0] + widths[1], 2);
        Assert.Equal(before + 120, harness.Frame(row.Children[0].Id).Width, 0);
        // A column is never squeezed to nothing.
        view.Resize(after: 0, by: 5000, from: [1, 1, 1]);
        Assert.Equal(0.36, harness.Editor.Document.Blocks[0].Children[1].Width, 2);
        harness.Editor.Undo();
        Assert.All(harness.Editor.Document.Blocks[0].Children, column => Assert.Equal(1, column.Width));
        Assert.False(harness.Editor.CanUndo);
    });

    [Fact]
    public void ATasksBoxAndATogglesArrowTellTheDocument() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Todo("t", "Task"), Toggle("g", P("s", "Summary"), P("h", "Hidden")));
        var box = Descendants<CheckBox>(harness.View<TextBlockView>("t")).Single();
        Assert.False(box.IsChecked);
        box.IsChecked = true;
        Assert.Equal("done#t Task", harness.Outline.Split('\n')[0]);
        // Shown from the document, the box does not tell the document back.
        harness.Editor.Undo();
        Assert.False(box.IsChecked);
        Assert.Equal(2, harness.Host.Changes);

        var arrow = Descendants<Button>(harness.View<ToggleBlockView>("g")).Single();
        arrow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(harness.Editor.Document.Block("g")!.IsOpen);
        Assert.Equal(Visibility.Collapsed, harness.Page.BlockViewFor("h")!.Visibility);

        // A note that cannot be edited: the box is shown and cannot be ticked.
        harness.Editor.IsEditable = false;
        Assert.False(box.IsEnabled);
    });

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) yield return found;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }
}

public sealed class EditorClipboardTests
{
    private static DataObject Board(string? text = null)
    {
        var board = new DataObject();
        if (text is not null) board.SetData(DataFormats.UnicodeText, text);
        return board;
    }

    private static string? Text(IDataObject? board) => board?.GetData(DataFormats.UnicodeText) as string;

    [Fact]
    public void CopiedBlocksPasteBackAsBlocksAndElsewhereAsText() => Sta.Run(() =>
    {
        var harness = new EditorHarness(H(2, "h", "Title"), Bullet("b", "Item", Bullet("c", "Child")), P("z"));
        harness.Editor.Select(EditorSelection.Blocks(["h", "b"]));
        var board = harness.Editor.CopySelection();
        Assert.NotNull(board);
        // For other apps, with lines that end as Windows ends them.
        Assert.Equal("## Title\r\n- Item\r\n  - Child", Text(board));

        harness.Caret("z", 0);
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal(
            "h2#h Title\nbullet#b Item\n  bullet#c Child\nh2#n1 Title\nbullet#n2 Item\n  bullet#n4 Child", harness.Outline);
        // With text selected there is nothing for the editor to copy: the text box copies its own.
        Assert.Null(harness.Editor.CopySelection());
    });

    [Fact]
    public void CopiedTextKeepsItsMarksWhenPastedBack() => Sta.Run(() =>
    {
        var block = P("a", "Hello world");
        block.Text = block.Text.Set(TextMark.Bold, true, 6, 11);
        var harness = new EditorHarness(block, P("b", "to: "));
        var board = Board("world");
        harness.Editor.Select(EditorSelection.Text("a", 6, 11));
        harness.Editor.AddCopiedText(board);
        Assert.True(board.GetDataPresent(BlockEditorController.ClipboardFormat));
        Assert.Equal("world", Text(board));

        harness.Caret("b", 4);
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal("[to: ][b:world]", Shape(harness.Editor.Document.Blocks[1].Text));
        Assert.Equal("b|9", harness.Selection);
        // Pasting as plain text leaves the marks behind.
        Assert.True(harness.Editor.Paste(board, plainOnly: true));
        Assert.Equal("[to: ][b:world][world]", Shape(harness.Editor.Document.Blocks[1].Text));
    });

    [Fact]
    public void CuttingBlocksRemovesThem() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"));
        harness.Editor.Select(EditorSelection.Blocks(["a"]));
        var board = harness.Editor.CutSelection();
        Assert.Equal("p#b B", harness.Outline);
        Assert.Equal("A", Text(board));
        // Nothing is cut from a note that cannot be edited.
        harness.Editor.Select(EditorSelection.Blocks(["b"]));
        harness.Editor.IsEditable = false;
        Assert.Null(harness.Editor.CutSelection());
        Assert.Equal("p#b B", harness.Outline);
    });

    [Fact]
    public void RichTextFromElsewhereIsConvertedByTheApp() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Host.Converted =
            """
            {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"From the web"}]},
              {"type":"paragraph","content":[{"type":"text","text":"Body"}]}]}
            """;
        var board = Board("From the web\nBody");
        board.SetData(DataFormats.Html, "<h2 onclick='x()'>From the web</h2><p>Body</p>");
        harness.Caret("a", 0);
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal(["<h2 onclick='x()'>From the web</h2><p>Body</p>"], harness.Host.Html);
        Assert.Equal("h2#n1 From the web\np#n2 Body", harness.Outline);
    });

    [Fact]
    public void TheClipboardsOwnWrappingOfAPageIsTakenOff()
    {
        // What a browser on Windows puts on the clipboard: a header of
        // offsets, then the page with the copied part marked.
        var wrapped = string.Join("\r\n",
            "Version:0.9", "StartHTML:0000000105", "EndHTML:0000000246", "StartFragment:0000000141", "EndFragment:0000000210",
            "<html><body>", "<!--StartFragment--><h2>From the web</h2><p>Body</p><!--EndFragment-->", "</body></html>");
        Assert.Equal("<h2>From the web</h2><p>Body</p>", BlockEditorController.HtmlFragment(wrapped));
        Assert.Equal("<p>bare</p>", BlockEditorController.HtmlFragment("<p>bare</p>"));
        Assert.Equal("<html><p>no marks</p></html>", BlockEditorController.HtmlFragment("Version:1.0\r\nStartHTML:1\r\n<html><p>no marks</p></html>"));
    }

    [Fact]
    public void AnAddressPastedOverSelectedTextLinksIt() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "see the site"), P("b"));
        var board = Board("https://example.com/x");
        harness.Editor.Select(EditorSelection.Text("a", 8, 12));
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal("[see the ][<https://example.com/x>:site]", Shape(harness.Editor.Document.Blocks[0].Text));
        // Pasted where nothing is selected, it is the link's own text.
        harness.Caret("b", 0);
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal("[<https://example.com/x>:https://example.com/x]", Shape(harness.Editor.Document.Blocks[1].Text));
    });

    [Fact]
    public void PlainTextOfSeveralLinesBecomesParagraphs() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Start"));
        harness.Caret("a", 5);
        // Windows ends its lines with two characters.
        Assert.True(harness.Editor.Paste(Board("one\r\n\r\ntwo")));
        Assert.Equal("p#a Start\np#n1 one\np#n2 two", harness.Outline);
        // A clipboard with nothing the editor can use.
        Assert.False(harness.Editor.Paste(Board()));
        Assert.Equal("p#a Start\np#n1 one\np#n2 two", harness.Outline);
    });

    [Fact]
    public void APictureOnTheClipboardBecomesAnImageBlock() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"));
        var board = Board();
        board.SetData("PNG", new MemoryStream([0x89, 0x50, 0x4E, 0x47]));
        harness.Caret("a", 1);
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal("p#a A\nimage#n1", harness.Outline);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], Assert.Single(harness.Host.Stored).Data);

        // A picture as Windows itself holds one is stored as a PNG.
        var pixels = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var bitmap = Board();
        bitmap.SetData(DataFormats.Bitmap, pixels);
        Assert.True(harness.Editor.Paste(bitmap));
        Assert.Equal(2, harness.Host.Stored.Count);
        Assert.Equal("image/png", harness.Host.Stored[1].Mime);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], harness.Host.Stored[1].Data.Take(4));
    });

    [Fact]
    public void PictureFilesCopiedInExplorerArePastedAsPictures() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"));
        var folder = Directory.CreateTempSubdirectory("heracles-test-").FullName;
        try
        {
            var file = Path.Combine(folder, "Shore.jpg");
            File.WriteAllBytes(file, [0xFF, 0xD8, 0xFF]);
            // A file copied in Explorer also carries its name as text.
            var board = Board(file);
            board.SetData(DataFormats.FileDrop, new[] { file });
            harness.Caret("a", 1);
            Assert.True(harness.Editor.Paste(board));
            Assert.Equal("p#a A\nimage#n1", harness.Outline);
            Assert.Equal("image/jpeg", Assert.Single(harness.Host.Stored).Mime);
            Assert.Equal(JsonValue.Of("Shore"), harness.Editor.Document.Block("n1")?.Attrs["alt"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void IntoCodeTextGoesExactlyAsItIs() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Code("c", "a"));
        var board = Board("\n\n  b\n");
        board.SetData(DataFormats.Html, "<p>b</p>");
        harness.Caret("c", 1);
        Assert.True(harness.Editor.Paste(board));
        Assert.Equal("a\n\n  b\n", harness.Editor.Document.Blocks[0].Text.String);
        Assert.Empty(harness.Host.Html);
        Assert.Equal("a\n\n  b\n", harness.Text("c"));
    });

    [Fact]
    public void ANoteThatCannotBeEditedTakesNoPaste() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"));
        harness.Editor.IsEditable = false;
        harness.Caret("a", 1);
        Assert.False(harness.Editor.Paste(Board("more")));
        Assert.Equal("p#a A", harness.Outline);
    });
}

public sealed class EditorMouseTests
{
    [Fact]
    public void AClickInStaticTextPutsTheCaretWhereItWas() => Sta.Run(() =>
    {
        var text = string.Concat(Enumerable.Repeat("word ", 40));
        var harness = new EditorHarness(P("a", "Hello world"), P("b", text));
        // Where the letters are is asked of a text box, then the block is static again.
        harness.Caret("a", 7);
        var x = harness.Focused!.CaretX;
        harness.Caret("b", 150);
        var far = harness.Focused!.PointerAt(150).GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
        harness.Editor.Select(EditorSelection.None);
        Assert.Null(harness.Page.TextBoxFor("a"));

        var before = harness.Frame("b");
        harness.Editor.ActivateAt("a", new Point(x, 8));
        Assert.Equal("a|7", harness.Selection);
        Assert.Same(harness.Page.TextBoxFor("a"), harness.Focused);
        Assert.Equal(before, harness.Frame("b"));

        harness.Editor.ActivateAt("b", new Point(far.X, far.Y + far.Height / 2));
        Assert.Equal("b|150", harness.Selection);
        Assert.Null(harness.Page.TextBoxFor("a"));

        // Twice selects the word, three times the block's text.
        harness.Editor.ActivateAt("a", new Point(x, 8), clicks: 2);
        Assert.Equal("a|6-11", harness.Selection);
        harness.Editor.ActivateAt("a", new Point(x, 8), clicks: 3);
        Assert.Equal("a|0-11", harness.Selection);
        // A block with no text, or none at all, takes no caret.
        harness.Editor.ActivateAt("missing", new Point(0, 0));
        Assert.Equal("a|0-11", harness.Selection);
    });

    [Fact]
    public void AClickOnALinkOpensIt() => Sta.Run(() =>
    {
        var block = P("a", "see the site now");
        block.Text = block.Text.SetLink(new TextLink("https://example.com"), 8, 12);
        var harness = new EditorHarness(block);
        Assert.True(harness.Editor.TextClicked("a", 10));
        Assert.Equal(["https://example.com"], harness.Host.Opened);
        Assert.True(harness.Editor.TextClicked("a", 8));
        Assert.False(harness.Editor.TextClicked("a", 3));
        Assert.False(harness.Editor.TextClicked("a", 14));
        Assert.False(harness.Editor.TextClicked("missing", 0));
        Assert.Equal(2, harness.Host.Opened.Count);
    });

    [Fact]
    public void ADragFromOneBlocksTextIntoAnotherSelectsWholeBlocks() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"), P("c", "C"), P("z", "Z"));
        Point Middle(string id)
        {
            var frame = harness.Frame(id);
            return new Point(frame.Left + 20, frame.Top + frame.Height / 2);
        }

        harness.Caret("b", 1);
        harness.Editor.BeginTextDrag("b");
        // Still inside the block it began in: the text box's own business.
        harness.Editor.ContinueTextDrag(Middle("b"));
        Assert.Empty(harness.Page.PendingBlocks);
        harness.Editor.ContinueTextDrag(Middle("c"));
        Assert.Equal(["b", "c"], harness.Page.PendingBlocks);
        // Past the end of the note the selection runs to that end, and back above it to the start.
        harness.Editor.ContinueTextDrag(new Point(100, harness.Frame("z").Bottom + 300));
        Assert.Equal(["b", "c", "z"], harness.Page.PendingBlocks);
        harness.Editor.ContinueTextDrag(Middle("a"));
        Assert.Equal(["a", "b"], harness.Page.PendingBlocks);
        Assert.Equal("b|1", harness.Selection);

        harness.Editor.EndTextDrag(Middle("a"));
        Assert.Empty(harness.Page.PendingBlocks);
        Assert.Equal("[a b]", harness.Selection);

        // Let go where it began, nothing but the caret.
        harness.Caret("c", 0);
        harness.Editor.BeginTextDrag("c");
        harness.Editor.EndTextDrag(Middle("c"));
        Assert.Equal("c|0", harness.Selection);
    });

    [Fact]
    public void AClickOnThePageItselfGoesToTheNearestSensiblePlace() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), Divider("d"), P("b", "World"));
        var a = harness.Frame("a");
        // Beside a block: the caret goes to the nearer end of its text.
        harness.Page.ClickAt(new Point(a.Left - 30, a.Top + 4), shift: false);
        Assert.Equal("a|0", harness.Selection);
        harness.Page.ClickAt(new Point(a.Right + 10, a.Top + 4), shift: false);
        Assert.Equal("a|5", harness.Selection);
        // Beside a block with no text: the block.
        var d = harness.Frame("d");
        harness.Page.ClickAt(new Point(d.Left - 30, d.Top + 4), shift: false);
        Assert.Equal("[d]", harness.Selection);
        // With Shift, from where the selection was.
        harness.Caret("a", 1);
        harness.Page.ClickAt(new Point(d.Left - 30, d.Top + 4), shift: true);
        Assert.Equal("[a d]", harness.Selection);
        // Below the note: carry on writing at its end.
        harness.Page.ClickAt(new Point(a.Left, harness.Frame("b").Bottom + 80), shift: false);
        Assert.Equal("p#a Hello\ndivider#d\np#b World\np#n1", harness.Outline);
        Assert.Equal("n1|0", harness.Selection);
    });

    [Fact]
    public void TheHandleFollowsThePointerFromBlockToBlock() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), Bullet("b", "B", Bullet("c", "C")), Quote("q", P("x", "X")));
        Assert.Null(harness.Page.HandleBlock);
        var c = harness.Frame("c");
        harness.Page.ShowHandle(new Point(c.Left + 5, c.Top + 5));
        Assert.Equal("c", harness.Page.HandleBlock);
        var handle = harness.Page.HandleFrame!.Value;
        Assert.True(handle.Right <= c.Left, "the handle is in the margin beside its block");
        Assert.True(handle.Top >= c.Top - 4 && handle.Bottom <= c.Bottom + 4);
        // From the margin, a quote as a whole.
        harness.Page.ShowHandle(new Point(5, harness.Frame("x").Top + 5));
        Assert.Equal("q", harness.Page.HandleBlock);
        harness.Page.ShowHandle(new Point(100, harness.Frame("q").Bottom + 400));
        Assert.Null(harness.Page.HandleBlock);
        // Typing puts it away, and a note that cannot be edited has none.
        harness.Page.ShowHandle(new Point(c.Left + 5, c.Top + 5));
        harness.Caret("a", 1);
        harness.Type("x");
        Assert.Null(harness.Page.HandleBlock);
        harness.Editor.IsEditable = false;
        harness.Page.PointerMoved(new Point(c.Left + 5, c.Top + 5));
        Assert.Null(harness.Page.HandleBlock);
    });

    [Fact]
    public void ABlockDraggedByItsHandleLandsWhereTheLineShows() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"), P("c", "C"));
        var a = harness.Frame("a");
        var c = harness.Frame("c");
        var start = new Point(a.Left - 20, a.Top + 8);
        harness.Page.BeginGripDrag("a", start);
        // A click is not a drag: nothing moves until the pointer has.
        harness.Page.ContinueGripDrag(new Point(start.X + 1, start.Y + 1));
        Assert.Null(harness.Page.DropLineFrame);
        harness.Page.ContinueGripDrag(new Point(c.Left + 20, c.Bottom - 2));
        Assert.Equal(["a"], harness.Page.PendingBlocks);
        var line = harness.Page.DropLineFrame!.Value;
        Assert.True(line.Top >= c.Bottom - 1 && line.Height <= 4, "the line is drawn under the block the drop would follow");
        Assert.True(harness.Page.EndGripDrag());
        Assert.Equal("p#b B\np#c C\np#a A", harness.Outline);
        Assert.Equal("[a]", harness.Selection);
        Assert.Null(harness.Page.DropLineFrame);
        Assert.Empty(harness.Page.PendingBlocks);

        // Let go without moving, it was a click: the block is selected, for its menu.
        var b = harness.Frame("b");
        harness.Page.BeginGripDrag("b", new Point(b.Left - 20, b.Top + 8));
        Assert.False(harness.Page.EndGripDrag());
        Assert.Equal("[b]", harness.Selection);
        Assert.Equal("p#b B\np#c C\np#a A", harness.Outline);

        // Several selected blocks move together, by the handle of any of them.
        harness.Editor.Select(EditorSelection.Blocks(["b", "c"]));
        var top = harness.Frame("b");
        harness.Page.BeginGripDrag("c", new Point(top.Left - 20, top.Top + 8));
        var last = harness.Frame("a");
        harness.Page.ContinueGripDrag(new Point(last.Left + 20, last.Bottom + 40));
        Assert.True(harness.Page.EndGripDrag());
        Assert.Equal("p#a A\np#b B\np#c C", harness.Outline);
    });

    [Fact]
    public void PictureFilesDraggedOverThePageShowWhereTheyWouldLand() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"));
        var folder = Directory.CreateTempSubdirectory("heracles-test-").FullName;
        try
        {
            var file = Path.Combine(folder, "Shore.png");
            File.WriteAllBytes(file, [0x89, 0x50, 0x4E, 0x47]);
            var b = harness.Frame("b");
            Assert.False(harness.Page.FilesDraggedOver([Path.Combine(folder, "notes.txt")], new Point(b.Left + 5, b.Top + 2)));
            Assert.Null(harness.Page.DropLineFrame);
            Assert.True(harness.Page.FilesDraggedOver([file], new Point(b.Right - 5, b.Top + 2)));
            Assert.NotNull(harness.Page.DropLineFrame);
            Assert.True(harness.Page.FilesDropped([file]));
            Assert.Equal("p#a A\nimage#n1\np#b B", harness.Outline);
            Assert.Equal("[n1]", harness.Selection);
            Assert.Null(harness.Page.DropLineFrame);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });
}
