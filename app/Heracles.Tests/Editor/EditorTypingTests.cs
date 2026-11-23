using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;
using Block = Heracles.Editor.Block;

namespace Heracles.Tests.Editor;

// The Mac's EditorTypingTests, EditorShortcutTests and EditorNavigationTests,
// with the real text boxes, and after each what is particular to Windows.
// A key is pressed with Press: Enter for the Mac's insertNewline, Shift+Enter
// for a line break, Ctrl+Enter to leave a code block.

public sealed class EditorTypingTests
{
    [Fact]
    public void TypingReachesTheDocumentAndTheHost() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Caret("a", 5);
        Assert.Same(harness.Page.TextBoxFor("a"), harness.Focused);
        harness.Type(" world");
        Assert.Equal("p#a Hello world", harness.Outline);
        Assert.Equal("a|11", harness.Selection);
        Assert.Equal(6, harness.Host.Changes);
    });

    [Fact]
    public void OpeningADocumentChangesNothing() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), Bullet("b", "B"));
        harness.Caret("a", 2);
        harness.Caret("b", 1);
        Assert.Equal(0, harness.Host.Changes);
        Assert.False(harness.Editor.CanUndo);
    });

    [Fact]
    public void ReturnSplitsTheBlockAndTheCaretFollows() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello world"));
        harness.Caret("a", 5);
        harness.Press(Key.Enter);
        Assert.Equal("p#a Hello\np#n1  world", harness.Outline);
        Assert.Equal("n1|0", harness.Selection);
        Assert.Same(harness.Page.TextBoxFor("n1"), harness.Focused);
        Assert.Equal("Hello", harness.Text("a"));
        Assert.Equal(" world", harness.Text("n1"));
        harness.Type("!");
        Assert.Equal("p#a Hello\np#n1 ! world", harness.Outline);
    });

    [Fact]
    public void BackspaceAtTheStartJoinsTheBlockAbove() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), P("b", "World"));
        harness.Caret("b", 0);
        harness.Press(Key.Back);
        Assert.Equal("p#a HelloWorld", harness.Outline);
        Assert.Equal("a|5", harness.Selection);
        Assert.Equal("HelloWorld", harness.Text("a"));
        Assert.Same(harness.Page.TextBoxFor("a"), harness.Focused);
    });

    [Fact]
    public void BackspaceInsideTheTextIsTheTextBoxesOwn() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Caret("a", 3);
        harness.Press(Key.Back);
        Assert.Equal("p#a Helo", harness.Outline);
        Assert.Equal("a|2", harness.Selection);
    });

    [Fact]
    public void ForwardDeleteAtTheEndPullsTheNextBlockIn() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), P("b", "World"));
        harness.Caret("a", 5);
        harness.Press(Key.Delete);
        Assert.Equal("p#a HelloWorld", harness.Outline);
        Assert.Equal("a|5", harness.Selection);
    });

    [Fact]
    public void TabIndentsAListItemAndShiftTabBringsItBack() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Bullet("a", "A"), Bullet("b", "B"));
        harness.Caret("b", 1);
        harness.Press(Key.Tab);
        Assert.Equal("bullet#a A\n  bullet#b B", harness.Outline);
        Assert.Equal("b|1", harness.Selection);
        Assert.Same(harness.Page.TextBoxFor("b"), harness.Focused);
        harness.Press(Key.Tab, ModifierKeys.Shift);
        Assert.Equal("bullet#a A\nbullet#b B", harness.Outline);
        // A tab is never typed into prose.
        harness.Press(Key.Tab);
        harness.Press(Key.Tab);
        Assert.Equal("B", harness.Text("b"));
    });

    [Fact]
    public void ALineBreakStaysInsideTheBlock() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "one"));
        harness.Caret("a", 3);
        harness.Press(Key.Enter, ModifierKeys.Shift);
        harness.Type("two");
        Assert.Equal("p#a one⏎two", harness.Outline);
        Assert.Equal("a|7", harness.Selection);
        Assert.True(SameJson(
            harness.Editor.Json,
            """
            {"type":"doc","content":[{"type":"paragraph","attrs":{"blockId":"a"},"content":[
              {"type":"text","text":"one"},{"type":"hardBreak"},{"type":"text","text":"two"}]}]}
            """));
        Assert.Single(harness.Focused!.Document.Blocks);
    });

    [Fact]
    public void TypingKeepsTheMarksOfTheTextItJoins() => Sta.Run(() =>
    {
        var block = P("a", "bold");
        block.Text = block.Text.Set(TextMark.Bold, true, 0, 4);
        var harness = new EditorHarness(block);
        harness.Caret("a", 4);
        harness.Type("er");
        Assert.Equal("[b:bolder]", Shape(harness.Editor.Document.Blocks[0].Text));
        // And the text box shows it so.
        Assert.Equal("[b:bolder]", Shape(harness.Focused!.Read()));
    });

    [Fact]
    public void TypingAfterALinkIsNotPartOfIt() => Sta.Run(() =>
    {
        var block = P("a", "site");
        block.Text = block.Text.SetLink(new TextLink("https://example.com"), 0, 4);
        var harness = new EditorHarness(block);
        harness.Caret("a", 4);
        harness.Type(" x");
        Assert.Equal("[<https://example.com>:site][ x]", Shape(harness.Editor.Document.Blocks[0].Text));
        // Inside it, it is.
        harness.Caret("a", 2);
        harness.Type("-");
        Assert.Equal("[<https://example.com>:si-te][ x]", Shape(harness.Editor.Document.Blocks[0].Text));
        Assert.Equal("[<https://example.com>:si-te][ x]", Shape(harness.Focused!.Read()));
    });

    [Fact]
    public void CodeIsTypedAsWrittenAndKeepsItsIndent() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Code("c", "if x {"));
        harness.Caret("c", 6);
        harness.Press(Key.Enter);
        harness.Press(Key.Tab);
        harness.Type("y()");
        harness.Press(Key.Enter);
        harness.Type("z");
        Assert.Equal("if x {\n  y()\n  z", harness.Editor.Document.Blocks[0].Text.String);
        Assert.StartsWith("code#c", harness.Outline);
        // No shortcuts inside code.
        harness.Type(" **b**");
        Assert.EndsWith("**b**", harness.Editor.Document.Blocks[0].Text.String);
        // The way out of a code block: a new line below it.
        harness.Press(Key.Enter, ModifierKeys.Control);
        Assert.Equal("n1|0", harness.Selection);
        Assert.EndsWith("\np#n1", harness.Outline);
    });

    [Fact]
    public void ATrashedNoteCannotBeTypedIn() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Editor.IsEditable = false;
        harness.Caret("a", 5);
        harness.Type("!");
        harness.Press(Key.Enter);
        harness.Press(Key.Back);
        harness.Editor.Toggle(TextMark.Bold);
        harness.Editor.InsertText("more");
        Assert.Equal("p#a Hello", harness.Outline);
        Assert.Equal(0, harness.Host.Changes);
        Assert.False(harness.Editor.CanUndo);
    });

    // Windows

    [Fact]
    public void TypingOverASelectionReplacesIt() => Sta.Run(() =>
    {
        var block = P("a", "Hello world");
        block.Text = block.Text.Set(TextMark.Bold, true, 0, 5);
        var harness = new EditorHarness(block);
        harness.Editor.Select(EditorSelection.Text("a", 0, 5));
        harness.Type("J");
        Assert.Equal("p#a J world", harness.Outline);
        // What takes the place of bold text is bold.
        Assert.Equal("[b:J][ world]", Shape(harness.Editor.Document.Blocks[0].Text));
        Assert.Equal("a|1", harness.Selection);
        harness.Editor.Undo();
        Assert.Equal("p#a Hello world", harness.Outline);
        Assert.Equal("a|0-5", harness.Selection);
    });

    [Fact]
    public void AnEmojiIsTypedWhole() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "ok"));
        harness.Caret("a", 2);
        harness.Type(" 😀!");
        Assert.Equal("p#a ok 😀!", harness.Outline);
        Assert.Equal("a|6", harness.Selection);
        // One emoji over another: the two share half their code, which a careless comparison would split.
        harness.Editor.Select(EditorSelection.Text("a", 3, 5));
        harness.Editor.Toggle(TextMark.Bold);
        harness.Editor.Select(EditorSelection.Text("a", 3, 5));
        harness.Type("😁");
        var text = harness.Editor.Document.Blocks[0].Text;
        Assert.Equal("ok 😁!", text.String);
        Assert.All(text.Spans, span => Assert.False(char.IsLowSurrogate(span.Text[0]) || char.IsHighSurrogate(span.Text[^1]), Shape(text)));
    });

    [Fact]
    public void WhatWasTypedIsFoundFromWhereTheCaretEndedUp()
    {
        Assert.Equal((1, 1, "x"), BlockEditorController.Diff("abc", "axbc", 2));
        Assert.Equal((1, 2, ""), BlockEditorController.Diff("abc", "ac", 1));
        Assert.Equal((1, 2, "x"), BlockEditorController.Diff("abc", "axc", 2));
        Assert.Equal((0, 3, ""), BlockEditorController.Diff("abc", "", 0));
        Assert.Equal((0, 0, "abc"), BlockEditorController.Diff("", "abc", 3));
        // The same letter on both sides of the caret: only the caret says where the new one went.
        Assert.Equal((2, 2, "c"), BlockEditorController.Diff("cccc", "ccccc", 3));
        Assert.Equal((0, 0, "c"), BlockEditorController.Diff("cccc", "ccccc", 1));
        // Forward delete leaves the caret before what went.
        Assert.Equal((1, 3, ""), BlockEditorController.Diff("abcd", "ad", 1));
        // A caret that is not where the change ended still gives a change that turns the old text into the new.
        var (start, end, inserted) = BlockEditorController.Diff("hello world", "hello brave world", 0);
        Assert.Equal("hello brave world", "hello world"[..start] + inserted + "hello world"[end..]);
        // Two emoji that share their first half are replaced whole.
        Assert.Equal((0, 2, "😁"), BlockEditorController.Diff("😀", "😁", 2));
        Assert.Equal((1, 3, "😁"), BlockEditorController.Diff("a😀", "a😁", 3));
    }

    [Fact]
    public void AShortcutThatChangesTheKindOfViewDoesNotStopTheTyping() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("```rust let x");
        Assert.Equal("code#a let x", harness.Outline);
        Assert.Equal("rust", harness.Editor.Document.Blocks[0].Language);
        Assert.True(harness.Focused!.IsCode);
        Assert.Equal("a|5", harness.Selection);
        Assert.IsType<CodeBlockView>(harness.Page.BlockViewFor("a"));
        // Three dashes are a divider, with a line to carry on in.
        harness.Press(Key.Enter, ModifierKeys.Control);
        harness.Type("---");
        Assert.Equal("code#a let x\ndivider#n1\np#n2", harness.Outline);
        Assert.Equal("n2|0", harness.Selection);
        harness.Type("on");
        Assert.Equal("on", harness.Text("n2"));
    });

    [Fact]
    public void TextIsTypedForTheAppAtTheCaretOrTheEnd() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Editor.InsertText("More");
        Assert.Equal("p#a Hello\np#n1 More", harness.Outline);
        Assert.Equal("n1|4", harness.Selection);
        harness.Caret("a", 5);
        harness.Editor.InsertText(", you");
        Assert.Equal("p#a Hello, you\np#n1 More", harness.Outline);
        Assert.Equal("Hello, you", harness.Text("a"));
    });

    [Fact]
    public void TypingInALongNoteStaysQuick() => Sta.Run(() =>
    {
        var blocks = Enumerable.Range(0, 600)
            .Select(index => P($"p{index}", $"Paragraph {index} with a little text in it to lay out.")).ToArray();
        var harness = new EditorHarness(P("a", "Start"));
        harness.Editor.Load(new EditorDocument(blocks).Json);
        harness.Layout();
        harness.Caret("p300", 5);
        var watch = Stopwatch.StartNew();
        for (var key = 0; key < 50; key++)
        {
            harness.Type("x");
            // What the window does before each redraw.
            harness.Page.UpdateLayout();
        }
        watch.Stop();
        Assert.StartsWith("Parag" + new string('x', 50), harness.Editor.Document.Block("p300")!.Text.String);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"50 keystrokes in a 600-block note took {watch.Elapsed}");
    });
}

public sealed class EditorShortcutTests
{
    [Fact]
    public void MarkdownAtTheStartOfALineMakesTheBlock() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("# ");
        Assert.Equal("h1#a", harness.Outline);
        Assert.Equal("", harness.Text("a"));
        harness.Type("Title");
        harness.Press(Key.Enter);
        harness.Type("- one");
        harness.Press(Key.Enter);
        harness.Type("two");
        harness.Press(Key.Enter);
        harness.Press(Key.Enter);
        harness.Type("[] task");
        Assert.Equal("h1#a Title\nbullet#n1 one\nbullet#n3 two\ntodo#n5 task", harness.Outline);
    });

    [Fact]
    public void APairOfStarsMakesBoldAsItIsClosed() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("so **very** good");
        Assert.Equal("[so ][b:very][ good]", Shape(harness.Editor.Document.Blocks[0].Text));
        Assert.Equal("so very good", harness.Text("a"));
    });

    [Fact]
    public void UndoGivesBackWhatWasTyped() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("# ");
        Assert.Equal("h1#a", harness.Outline);
        harness.Editor.Undo();
        Assert.Equal("p#a # ", harness.Outline);
        Assert.Equal("a|2", harness.Selection);
        harness.Editor.Undo();
        Assert.Equal("p#a", harness.Outline);
        harness.Editor.Redo();
        harness.Editor.Redo();
        Assert.Equal("h1#a", harness.Outline);
    });

    [Fact]
    public void TypingIsUndoneARunAtATime() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Caret("a", 5);
        harness.Type(" world");
        harness.Press(Key.Enter);
        harness.Type("more");
        Assert.Equal("p#a Hello world\np#n1 more", harness.Outline);
        harness.Editor.Undo();
        Assert.Equal("p#a Hello world\np#n1", harness.Outline);
        harness.Editor.Undo();
        Assert.Equal("p#a Hello world", harness.Outline);
        Assert.Equal("a|11", harness.Selection);
        Assert.Equal("Hello world", harness.Text("a"));
        harness.Editor.Undo();
        Assert.Equal("p#a Hello", harness.Outline);
        Assert.False(harness.Editor.CanUndo);
        harness.Editor.Redo();
        Assert.Equal("p#a Hello world", harness.Outline);
        Assert.Equal("Hello world", harness.Text("a"));
        Assert.True(harness.Editor.CanRedo);
    });

    [Fact]
    public void TheSlashMenuTurnsTheBlockIntoWhatIsChosen() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("/");
        Assert.Equal(SlashCommand.All.Count, harness.Editor.SlashCommands.Count);
        Assert.Equal(Visibility.Visible, harness.Page.SlashMenu.Visibility);
        harness.Type("hea");
        Assert.Equal(["Heading 1", "Heading 2", "Heading 3", "Heading 4"], harness.Editor.SlashCommands.Select(command => command.Title));
        harness.Press(Key.Down);
        harness.Press(Key.Enter);
        Assert.Equal("h2#a", harness.Outline);
        Assert.Empty(harness.Editor.SlashCommands);
        Assert.NotEqual(Visibility.Visible, harness.Page.SlashMenu.Visibility);
        Assert.Equal("a|0", harness.Selection);
    });

    [Fact]
    public void TheSlashMenuOnlyOpensWhereAWordCouldStart() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "1"));
        harness.Caret("a", 1);
        harness.Type("/2");
        Assert.Empty(harness.Editor.SlashCommands);
        harness.Type(" /");
        Assert.NotEmpty(harness.Editor.SlashCommands);
        // Escape closes it and leaves the text alone.
        harness.Press(Key.Escape);
        Assert.Empty(harness.Editor.SlashCommands);
        Assert.Equal("p#a 1/2 /", harness.Outline);
        Assert.Equal("a|5", harness.Selection);
    });

    [Fact]
    public void TheSlashMenuClosesWhenNothingMatchesAndASpaceIsTyped() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("/zzz");
        // It stays open to say that nothing matches.
        Assert.Empty(harness.Page.SlashMenu.Items);
        Assert.Equal(Visibility.Visible, harness.Page.SlashMenu.Visibility);
        harness.Type(" x");
        Assert.NotEqual(Visibility.Visible, harness.Page.SlashMenu.Visibility);
        Assert.Equal("p#a /zzz x", harness.Outline);
    });

    [Fact]
    public void TheSlashMenuIsPlacedUnderTheSlashAndGoesWithItsBlock() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "First"), P("b", "Second"));
        harness.Caret("b", 6);
        harness.Type(" /");
        harness.Page.UpdateLayout();
        var menu = harness.Page.SlashMenu;
        var frame = menu.TransformToAncestor(harness.Page).TransformBounds(new Rect(menu.RenderSize));
        Assert.True(frame.Top >= harness.Frame("b").Bottom, "the menu is below the line the slash is on");
        Assert.True(frame.Height > 100 && frame.Width > 200);
        Assert.Equal(["Divider"], SlashCommand.Matching("div").Select(command => command.Title));
        // Names that begin with what was typed come before names that only hold it.
        Assert.Equal(["Divider", "Code"], SlashCommand.Matching("de").Select(command => command.Title));
        Assert.Equal(["Toggle"], SlashCommand.Matching("TO").Select(command => command.Title));
        Assert.Equal(["Text", "Task list", "Toggle"], SlashCommand.Matching("t").Take(3).Select(command => command.Title));
        // Moving to another block closes it.
        harness.Caret("a", 0);
        Assert.Empty(harness.Editor.SlashCommands);
        // Choosing by the mouse is choosing by index.
        harness.Caret("b", 8);
        harness.Press(Key.Back);
        harness.Type("/qu");
        harness.Editor.ChooseSlashCommand(0);
        Assert.Equal("p#a First\nquote#n1\n  p#b Second ", harness.Outline);
    });

    [Fact]
    public void CommandKeysMarkTheSelection() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello world"));
        harness.Editor.Select(EditorSelection.Text("a", 0, 5));
        harness.Editor.Toggle(TextMark.Bold);
        Assert.Equal("[b:Hello][ world]", Shape(harness.Editor.Document.Blocks[0].Text));
        Assert.Equal("a|0-5", harness.Selection);
        harness.Editor.Toggle(TextMark.Bold);
        Assert.Equal("[Hello world]", Shape(harness.Editor.Document.Blocks[0].Text));
        // With nothing selected, the mark is for what is typed next.
        harness.Caret("a", 11);
        harness.Editor.Toggle(TextMark.Italic);
        harness.Type("!!");
        Assert.Equal("[Hello world][i:!!]", Shape(harness.Editor.Document.Blocks[0].Text));
    });

    [Fact]
    public void TheKeysForMarksAndBlocksAreTheMacsWithCtrl() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello world"));
        harness.Editor.Select(EditorSelection.Text("a", 0, 5));
        harness.Press(Key.B, ModifierKeys.Control);
        harness.Press(Key.I, ModifierKeys.Control);
        Assert.Equal("[bi:Hello][ world]", Shape(harness.Editor.Document.Blocks[0].Text));
        harness.Press(Key.X, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("[bis:Hello][ world]", Shape(harness.Editor.Document.Blocks[0].Text));
        // Code stands alone.
        harness.Press(Key.E, ModifierKeys.Control);
        Assert.Equal("[c:Hello][ world]", Shape(harness.Editor.Document.Blocks[0].Text));
        harness.Press(Key.D2, ModifierKeys.Control | ModifierKeys.Alt);
        Assert.Equal("h2#a Hello world", harness.Outline);
        Assert.Equal("a|0-5", harness.Selection);
        harness.Press(Key.D8, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("bullet#a Hello world", harness.Outline);
        harness.Press(Key.D7, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("numbered#a Hello world", harness.Outline);
        harness.Press(Key.D9, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("todo#a Hello world", harness.Outline);
        harness.Press(Key.D0, ModifierKeys.Control | ModifierKeys.Alt);
        Assert.Equal("p#a Hello world", harness.Outline);
        // Undo and redo, by Ctrl+Z, and by Ctrl+Y or Ctrl+Shift+Z.
        harness.Press(Key.Z, ModifierKeys.Control);
        Assert.Equal("todo#a Hello world", harness.Outline);
        harness.Press(Key.Y, ModifierKeys.Control);
        Assert.Equal("p#a Hello world", harness.Outline);
        harness.Press(Key.Z, ModifierKeys.Control);
        harness.Press(Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("p#a Hello world", harness.Outline);
        // With nothing to link, Ctrl+K is left for the window's Quick Open.
        harness.Caret("a", 3);
        Assert.False(harness.Focused!.OfferKey(Key.K, ModifierKeys.Control));
        // And a key that is nobody's in the editor travels on.
        Assert.False(harness.Focused!.OfferKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(harness.Focused!.OfferKey(Key.Q, ModifierKeys.None));
    });

    [Fact]
    public void ALinkIsSetOnTheSelectionAndRemovedFromTheCaret() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "see the site"));
        harness.Editor.Select(EditorSelection.Text("a", 8, 12));
        harness.Editor.SetLink("example.com");
        Assert.Equal("[see the ][<https://example.com>:site]", Shape(harness.Editor.Document.Blocks[0].Text));
        harness.Caret("a", 10);
        harness.Editor.SetLink(null);
        Assert.Equal("[see the site]", Shape(harness.Editor.Document.Blocks[0].Text));
    });

    [Fact]
    public void ATypedAddressIsLinkedAndTheNextWordIsNot() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a"));
        harness.Caret("a", 0);
        harness.Type("see https://example.com now");
        Assert.Equal(
            "[see ][<https://example.com>:https://example.com][ now]", Shape(harness.Editor.Document.Blocks[0].Text));
    });

    [Fact]
    public void OnlyLinksTheAppCanOpenAreAccepted()
    {
        Assert.Equal("https://example.com", BlockEditorController.CleanAddress("example.com"));
        Assert.Equal("https://a.example/x", BlockEditorController.CleanAddress("  https://a.example/x  "));
        Assert.Equal("mailto:a@b.example", BlockEditorController.CleanAddress("mailto:a@b.example"));
        Assert.Equal("heracles://note/abc", BlockEditorController.CleanAddress("heracles://note/abc"));
        Assert.Equal("https://example.com:8080/x", BlockEditorController.CleanAddress("example.com:8080/x"));
        Assert.Equal("https://localhost:3000", BlockEditorController.CleanAddress("localhost:3000"));
        Assert.Null(BlockEditorController.CleanAddress("javascript:alert(1)"));
        Assert.Null(BlockEditorController.CleanAddress("file:///etc/passwd"));
        Assert.Null(BlockEditorController.CleanAddress("data:text/html,x"));
        Assert.Null(BlockEditorController.CleanAddress("two words"));
        Assert.Null(BlockEditorController.CleanAddress("   "));
    }

    [Fact]
    public void ClickingALinkAsksTheAppToOpenIt() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "site"));
        harness.Editor.OpenLink("https://example.com");
        harness.Editor.OpenLink("");
        Assert.Equal(["https://example.com"], harness.Host.Opened);
    });
}

public sealed class EditorNavigationTests
{
    [Fact]
    public void TheArrowKeysCrossFromBlockToBlockKeepingTheColumn() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello world"), P("b", "Hello world!"), P("c", "Hi"));
        harness.Caret("a", 7);
        harness.Press(Key.Down);
        Assert.Equal("b|7", harness.Selection);
        Assert.Same(harness.Page.TextBoxFor("b"), harness.Focused);
        // A short line does not lose the column for the lines after it.
        harness.Press(Key.Down);
        Assert.Equal("c|2", harness.Selection);
        harness.Press(Key.Up);
        Assert.Equal("b|7", harness.Selection);
        harness.Press(Key.Up);
        Assert.Equal("a|7", harness.Selection);
        // Nothing above the first block: the key is the text box's own.
        Assert.False(harness.Focused!.OfferKey(Key.Up, ModifierKeys.None));
    });

    [Fact]
    public void InsideAParagraphOfSeveralLinesTheArrowsAreTheTextBoxesOwn() => Sta.Run(() =>
    {
        var lengthy = string.Concat(Enumerable.Repeat("word ", 80));
        var harness = new EditorHarness(P("a", lengthy), P("b", "next"));
        Assert.True(harness.Frame("a").Height > EditorTheme.LineHeight(P("a")) * 2);
        harness.Caret("a", 3);
        harness.Press(Key.Down);
        Assert.StartsWith("a|", harness.Selection);
        Assert.NotEqual("a|3", harness.Selection);
        // From the last line it leaves the block.
        harness.Caret("a", lengthy.Length);
        harness.Press(Key.Down);
        Assert.StartsWith("b|", harness.Selection);
    });

    [Fact]
    public void LeftAndRightCrossAtTheEnds() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), Divider("d"), P("b", "World"));
        harness.Caret("a", 5);
        harness.Press(Key.Right);
        Assert.Equal("[d]", harness.Selection);
        Assert.Null(harness.Focused);
        Assert.True(harness.Editor.HandleBlockKey(Key.Down, ModifierKeys.None));
        Assert.Equal("b|0", harness.Selection);
        harness.Press(Key.Left);
        Assert.Equal("[d]", harness.Selection);
        Assert.True(harness.Editor.HandleBlockKey(Key.Up, ModifierKeys.None));
        Assert.Equal("a|5", harness.Selection);
    });

    [Fact]
    public void ShiftAndAnArrowAtAnEdgeSelectsWholeBlocks() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"), P("c", "C"));
        harness.Caret("b", 1);
        harness.Press(Key.Down, ModifierKeys.Shift);
        Assert.Equal("[b c]", harness.Selection);
        Assert.Equal(["b", "c"], harness.Page.SelectedBlocks);
        harness.Editor.HandleBlockKey(Key.Up, ModifierKeys.Shift);
        Assert.Equal("[b]", harness.Selection);
        harness.Editor.HandleBlockKey(Key.Up, ModifierKeys.Shift);
        Assert.Equal("[a b]", harness.Selection);
        harness.Editor.HandleBlockKey(Key.Back, ModifierKeys.None);
        Assert.Equal("p#c C", harness.Outline);
        Assert.Equal("c|0", harness.Selection);
    });

    [Fact]
    public void AShiftClickInAnotherBlockSelectsTheBlocksBetween() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "A"), P("b", "B"), P("c", "C"));
        harness.Caret("a", 1);
        // In the same block, the text box extends its own selection.
        Assert.False(harness.Editor.ExtendSelection("a"));
        Assert.True(harness.Editor.ExtendSelection("c"));
        Assert.Equal("[a b c]", harness.Selection);
        Assert.True(harness.Editor.ExtendSelection("b"));
        Assert.Equal("[a b]", harness.Selection);
    });

    [Fact]
    public void SelectAllTakesTheBlockThenTheNote() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), P("b", "World"));
        harness.Caret("a", 2);
        harness.Press(Key.A, ModifierKeys.Control);
        Assert.Equal("a|0-5", harness.Selection);
        harness.Press(Key.A, ModifierKeys.Control);
        Assert.Equal("[a b]", harness.Selection);
    });

    [Fact]
    public void EscapeSelectsTheBlockAndReturnGoesBackToTyping() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Caret("a", 2);
        harness.Press(Key.Escape);
        Assert.Equal("[a]", harness.Selection);
        harness.Editor.HandleBlockKey(Key.Enter, ModifierKeys.None);
        Assert.Equal("a|5", harness.Selection);
        Assert.Same(harness.Page.TextBoxFor("a"), harness.Focused);
        // Escape again lets go of the note altogether.
        harness.Press(Key.Escape);
        harness.Editor.HandleBlockKey(Key.Escape, ModifierKeys.None);
        Assert.Equal("none", harness.Selection);
    });

    [Fact]
    public void OnlyOneBlocksTextIsEverSelected() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), P("b", "World"));
        harness.Editor.Select(EditorSelection.Text("a", 0, 5));
        harness.Caret("b", 2);
        // On the Mac the first block's selection is collapsed. Here it has no text box left to hold one.
        Assert.Null(harness.Page.TextBoxFor("a"));
        Assert.Equal("b|2", harness.Selection);
    });

    [Fact]
    public void FocusingTheEndAddsALineOnlyWhenThereIsNone() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"));
        harness.Editor.FocusEnd();
        Assert.Equal("p#a Hello\np#n1", harness.Outline);
        Assert.Equal("n1|0", harness.Selection);
        harness.Editor.FocusEnd();
        Assert.Equal("p#a Hello\np#n1", harness.Outline);
    });

    // Windows

    [Fact]
    public void TheStartAndEndOfTheNoteAreAKeyAway() => Sta.Run(() =>
    {
        var harness = new EditorHarness(P("a", "Hello"), P("b", "Middle"), Bullet("c", "End"));
        harness.Caret("b", 3);
        harness.Press(Key.End, ModifierKeys.Control);
        Assert.Equal("c|3", harness.Selection);
        harness.Press(Key.Home, ModifierKeys.Control);
        Assert.Equal("a|0", harness.Selection);
    });

    [Fact]
    public void WholeBlocksAreIndentedAndMovedThroughByKeys() => Sta.Run(() =>
    {
        var harness = new EditorHarness(Bullet("a", "A"), Bullet("b", "B"), Bullet("c", "C"), P("z", "Z"));
        harness.Editor.SelectBlocks(["b", "c"], extending: false);
        Assert.Equal("[b c]", harness.Selection);
        harness.Editor.HandleBlockKey(Key.Tab, ModifierKeys.None);
        Assert.Equal("bullet#a A\n  bullet#b B\n  bullet#c C\np#z Z", harness.Outline);
        Assert.Equal("[b c]", harness.Selection);
        harness.Editor.HandleBlockKey(Key.Tab, ModifierKeys.Shift);
        Assert.Equal("bullet#a A\nbullet#b B\nbullet#c C\np#z Z", harness.Outline);
        // Down from a selection goes to the block after it, to type in.
        harness.Editor.HandleBlockKey(Key.Down, ModifierKeys.None);
        Assert.Equal("z|0", harness.Selection);
        // Everything, and then nothing beyond: back to typing where the selection ends.
        harness.Editor.SelectAllBlocks();
        Assert.Equal("[a b c z]", harness.Selection);
        harness.Editor.HandleBlockKey(Key.Down, ModifierKeys.None);
        Assert.Equal("z|1", harness.Selection);
        // A key that means nothing to a selection of blocks is not taken.
        harness.Editor.SelectBlocks(["a"], extending: false);
        Assert.False(harness.Editor.HandleBlockKey(Key.Q, ModifierKeys.None));
        // Undo works from a selection of blocks too.
        harness.Editor.DeleteSelectedBlocks();
        Assert.Equal("bullet#b B\nbullet#c C\np#z Z", harness.Outline);
        Assert.True(harness.Editor.HandleBlockKey(Key.Z, ModifierKeys.Control));
        Assert.Equal("bullet#a A\nbullet#b B\nbullet#c C\np#z Z", harness.Outline);
    });
}
