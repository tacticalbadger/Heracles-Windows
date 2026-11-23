using System.Windows;
using System.Windows.Documents;
using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;
using Block = Heracles.Editor.Block;

namespace Heracles.Tests.Editor;

/// <summary>
/// The text box of one block: offsets in the model's counting, where the
/// caret is among the lines, and its height against static text.
/// </summary>
public sealed class BlockTextBoxTests
{
    private static BlockTextBox Box(Block block, double width = 500)
    {
        var box = new BlockTextBox { BlockId = block.Id };
        box.Show(block);
        Views.Column(width, box);
        return box;
    }

    /// <summary>ab, bold cd, a line break, italic e😀f.</summary>
    private static Block Marked()
    {
        var block = P("a", "abcd\ne😀f");
        block.Text = block.Text.Set(TextMark.Bold, true, 2, 4).Set(TextMark.Italic, true, 5, 9);
        return block;
    }

    private static string Words(int count) => string.Concat(Enumerable.Repeat("word ", count));

    [Fact]
    public void ItShowsTheBlocksTextAndReadsItBack() => Sta.Run(() =>
    {
        var block = Marked();
        var box = Box(block);
        Assert.Equal("abcd\ne😀f", box.Text);
        Assert.Equal(9, box.Length);
        Assert.Equal(block.Text, box.Read());
        Assert.Single(box.Document.Blocks);
    });

    [Fact]
    public void EveryOffsetIsAPlaceInTheBoxAndBack() => Sta.Run(() =>
    {
        var box = Box(Marked());
        for (var offset = 0; offset <= 9; offset++)
        {
            // Between the two halves of the emoji is not a place: it is the emoji's start.
            var expected = offset == 7 ? 6 : offset;
            Assert.Equal(expected, box.OffsetOf(box.PointerAt(offset)));
        }
        Assert.Equal(0, box.OffsetOf(box.PointerAt(-5)));
        Assert.Equal(9, box.OffsetOf(box.PointerAt(99)));
    });

    [Fact]
    public void TheSelectionIsSetAndReadInTheModelsOffsets() => Sta.Run(() =>
    {
        var box = Box(Marked());
        box.Select(1, 6);
        Assert.Equal((1, 6), box.SelectedRange);
        Assert.Equal("bcd\r\ne", box.Selection.Text);
        for (var offset = 0; offset <= 9; offset++)
        {
            if (offset == 7) continue;
            box.Select(offset, offset);
            Assert.Equal((offset, offset), box.SelectedRange);
        }
    });

    [Fact]
    public void EitherSideOfALineBreakIsOnItsOwnLine() => Sta.Run(() =>
    {
        var box = Box(Marked());
        box.Select(4, 4);
        Assert.True(box.CaretIsOnFirstLine);
        Assert.False(box.CaretIsOnLastLine);
        box.Select(5, 5);
        Assert.False(box.CaretIsOnFirstLine);
        Assert.True(box.CaretIsOnLastLine);
    });

    [Fact]
    public void TheCaretKnowsWhetherItIsOnTheFirstOrLastLine() => Sta.Run(() =>
    {
        var text = Words(60);
        var box = Box(P("a", text), width: 300);
        box.Select(0, 0);
        Assert.True(box.CaretIsOnFirstLine);
        Assert.False(box.CaretIsOnLastLine);
        box.Select(150, 150);
        Assert.False(box.CaretIsOnFirstLine);
        Assert.False(box.CaretIsOnLastLine);
        box.Select(text.Length, text.Length);
        Assert.False(box.CaretIsOnFirstLine);
        Assert.True(box.CaretIsOnLastLine);

        var one = Box(P("b", "Hello world"));
        one.Select(3, 3);
        Assert.True(one.CaretIsOnFirstLine);
        Assert.True(one.CaretIsOnLastLine);
        var empty = Box(P("c"));
        Assert.True(empty.CaretIsOnFirstLine);
        Assert.True(empty.CaretIsOnLastLine);
    });

    [Fact]
    public void AColumnAcrossThePageFindsItsPlaceOnTheFirstOrLastLine() => Sta.Run(() =>
    {
        var one = Box(P("a", "Hello world"));
        Assert.Equal(0, one.OffsetNearest(0, onFirstLine: true));
        Assert.Equal(11, one.OffsetNearest(10_000, onFirstLine: true));
        one.Select(7, 7);
        Assert.Equal(7, one.OffsetNearest(one.CaretX, onFirstLine: true));
        Assert.Equal(7, one.OffsetNearest(one.CaretX, onFirstLine: false));

        var text = Words(60);
        var wrapped = Box(P("b", text), width: 300);
        Assert.Equal(0, wrapped.OffsetNearest(0, onFirstLine: true));
        var lastLineStart = wrapped.OffsetNearest(0, onFirstLine: false);
        Assert.InRange(lastLineStart, text.Length - 80, text.Length - 1);
        Assert.Equal(text.Length, wrapped.OffsetNearest(10_000, onFirstLine: false));
        // The same column on the first line is far from the end.
        Assert.InRange(wrapped.OffsetNearest(120, onFirstLine: true), 5, 60);
    });

    [Fact]
    public void APointInTheBoxIsAnOffset() => Sta.Run(() =>
    {
        var box = Box(P("a", "Hello world"));
        Assert.Equal(0, box.OffsetAt(new Point(0, 4)));
        Assert.Equal(11, box.OffsetAt(new Point(480, 4)));
        box.Select(5, 5);
        Assert.Equal(5, box.OffsetAt(new Point(box.CaretX, 8)));
    });

    public static TheoryData<string> Samples => new()
    {
        "empty", "short", "wrap", "marks", "breaks", "trailing break", "emoji", "cjk", "long word", "heading 1",
        "heading 2", "heading 3", "heading 4", "code", "done",
    };

    private static Block Sample(string name)
    {
        switch (name)
        {
            case "empty": return P("a");
            case "short": return P("a", "Hello world");
            case "wrap": return P("a", string.Concat(Enumerable.Repeat("word and another word ", 30)));
            case "marks":
                var marked = P("a", "Plain then " + string.Concat(Enumerable.Repeat("bold words here ", 20)) + "and code at the end of it");
                marked.Text = marked.Text.Set(TextMark.Bold, true, 11, 200).Set(TextMark.Italic, true, 100, 260)
                    .Set(TextMark.Code, true, marked.Text.Length - 20, marked.Text.Length)
                    .SetLink(new TextLink("https://example.com"), 0, 5);
                return marked;
            case "breaks": return P("a", "one\ntwo\n\nfour");
            case "trailing break": return P("a", "one\n");
            case "emoji":
                return P("a", "Pack the lantern 💡 and the notebook 📓 then tick ✅ and go 🧭 "
                    + string.Concat(Enumerable.Repeat("further on ", 20)));
            case "cjk": return P("a", string.Concat(Enumerable.Repeat("日本語のテキスト", 20)));
            case "long word": return P("a", new string('m', 200));
            case "heading 1": return H(1, "a", "A heading that is long enough to need more than the one line it would like");
            case "heading 2": return H(2, "a", "A heading that is long enough to need more than the one line it would like");
            case "heading 3": return H(3, "a", "A heading that is long enough to need more than the one line it would like");
            case "heading 4": return H(4, "a", "A heading that is long enough to need more than the one line it would like");
            case "code": return Code("a", "fn main() {\n    println!(\"a line of code long enough that it has to wrap somewhere\");\n}\n");
            default: return Todo("a", "A task that is done and so is struck through, at some length " + Words(12), true);
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ATextBoxAndStaticTextAreTheSameHeight(string sample) => Sta.Run(() =>
    {
        var block = Sample(sample);
        foreach (var width in new[] { 180.0, 260.0, 333.0, 500.0, 720.0 })
        {
            var box = new BlockTextBox();
            box.Show(block);
            var still = EditorTheme.StaticText(block);
            Views.Column(width, box, still);
            Assert.True(box.ActualHeight >= EditorTheme.LineHeight(block), $"{sample} at {width}: the box has a line's height");
            Assert.True(
                Math.Abs(box.ActualHeight - still.ActualHeight) < 0.01,
                $"{sample} at {width}: the text box is {box.ActualHeight} tall and the static text {still.ActualHeight}");
            // Whole lines, each of the height the theme gives.
            Assert.Equal(0, box.ActualHeight % EditorTheme.LineHeight(block), 3);
        }
    });

    [Fact]
    public void TheTextStartsWhereStaticTextDoes() => Sta.Run(() =>
    {
        // Static text is padded to match the room a text box's document
        // keeps at each side. If that room ever changes, the two part.
        var box = Box(P("a", "Hello"));
        Assert.Equal(new Thickness(EditorTheme.TextPadding, 0, EditorTheme.TextPadding, 0), box.Document.PagePadding);
        Assert.Equal(EditorTheme.TextPadding, box.PointerAt(0).GetCharacterRect(LogicalDirection.Forward).X, 3);
        Assert.Equal(EditorTheme.TextPadding, EditorTheme.StaticText(P("a", "Hello")).Padding.Left);
    });

    [Fact]
    public void TheTextAndTheCaretAskForTheThemesTextColour() => Sta.Run(() =>
    {
        // A text box's document takes its colour from nowhere by itself: in
        // the window it was black on a dark page, with every test passing.
        // No theme is loaded here, so all that can be said is that each of
        // the three is told to look the colour up.
        var box = Box(P("a", "Hello"));
        Assert.NotEqual(DependencyProperty.UnsetValue, box.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty));
        Assert.NotEqual(DependencyProperty.UnsetValue, box.ReadLocalValue(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty));
        Assert.NotEqual(DependencyProperty.UnsetValue, box.Document.ReadLocalValue(TextElement.ForegroundProperty));
    });

    [Fact]
    public void WhatIsTypedArrivesOnceTheDispatcherHasRun() => Sta.Run(() =>
    {
        var box = Box(P("a", "Hello"));
        var changes = 0;
        box.TextChanged += (_, _) => changes++;
        box.Select(5, 5);
        Views.Type(box, "!?");
        Assert.Equal("Hello!?", box.Text);
        Assert.Equal((7, 7), box.SelectedRange);
        Assert.Equal(2, changes);
        box.Select(0, 5);
        Views.Type(box, "😀");
        Assert.Equal("😀!?", box.Text);
        Assert.Equal((2, 2), box.SelectedRange);
    });

    [Fact]
    public void ABoxThatCannotBeEditedTakesNoTyping() => Sta.Run(() =>
    {
        var box = Box(P("a", "Hello"));
        box.IsReadOnly = true;
        box.Select(5, 5);
        Views.Type(box, "!");
        Assert.Equal("Hello", box.Text);
    });

    [Fact]
    public void ShowingABlockIsOneChangeAndLeavesOneParagraph() => Sta.Run(() =>
    {
        var box = Box(P("a", "Hello"));
        var changes = 0;
        box.TextChanged += (_, _) => changes++;
        box.Show(Marked());
        Assert.Equal(1, changes);
        Assert.Equal("abcd\ne😀f", box.Text);
        Assert.Single(box.Document.Blocks);
        box.Show(Code("c", "let x\n  = 1"));
        Assert.True(box.IsCode);
        Assert.Equal("let x\n  = 1", box.Text);
        Assert.Equal("[let x\n  = 1]", Shape(box.Read()));
    });

    [Fact]
    public void TheBoxesOwnFormattingCommandsDoNothing() => Sta.Run(() =>
    {
        var block = Marked();
        var box = Box(block);
        box.Select(0, 4);
        foreach (var command in new[]
        {
            EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline,
            EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering, EditingCommands.EnterParagraphBreak,
            EditingCommands.EnterLineBreak, EditingCommands.AlignCenter, EditingCommands.IncreaseFontSize,
            EditingCommands.IncreaseIndentation, EditingCommands.TabForward,
        })
        {
            Assert.False(command.CanExecute(null, box), command.Name);
            command.Execute(null, box);
        }
        Views.Pump();
        Assert.Single(box.Document.Blocks);
        Assert.Equal(block.Text, box.Read());
        Assert.Equal("abcd\ne😀f", box.Text);
        // Its own undo is off: the editor keeps one history for the whole note.
        Assert.False(box.IsUndoEnabled);
    });

    [Fact]
    public void AKeyGoesToTheEditorFirst() => Sta.Run(() =>
    {
        var box = Box(P("a", "Hello"));
        var seen = new List<(System.Windows.Input.Key, System.Windows.Input.ModifierKeys)>();
        box.KeyHandler = (key, modifiers) =>
        {
            seen.Add((key, modifiers));
            return key == System.Windows.Input.Key.Enter;
        };
        Assert.True(box.OfferKey(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Shift));
        Assert.False(box.OfferKey(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.None));
        Assert.Equal(
            [(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Shift), (System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.None)],
            seen);
    });
}
