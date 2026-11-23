using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;

namespace Heracles.Tests.Editor;

// The edits a note's blocks can undergo, test for test as the Mac has them
// (EditorDocumentTests.swift), and after those the cases the Windows plan
// adds: finding blocks, stale ids, a note emptied, and copies kept apart.

public sealed class SplitTests
{
    [Fact]
    public void SplitsAParagraphAtTheCaret()
    {
        var result = Edit(Doc(P("a", "Hello world")), (document, ids) => document.Split("a", 5, 5, ids));
        Assert.Equal("p#a Hello\np#n1  world", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void ASelectionIsReplacedByTheSplit()
    {
        var result = Edit(Doc(P("a", "Hello world")), (document, ids) => document.Split("a", 5, 11, ids));
        Assert.Equal("p#a Hello\np#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void AtTheStartTheTextKeepsItsBlockAndAnEmptyOneGoesAbove()
    {
        // A link to the block should still land on its text.
        var result = Edit(Doc(P("a", "Hello")), (document, ids) => document.Split("a", 0, 0, ids));
        Assert.Equal("p#n1\np#a Hello", result.Outline);
        Assert.Equal("a|0", result.Selection);
    }

    [Fact]
    public void TheSecondHalfKeepsItsMarks()
    {
        var document = Doc(P("a", "Hello world"));
        document.Blocks[0].Text = document.Blocks[0].Text.Set(TextMark.Bold, true, 6, 11);
        document.Split("a", 8, 8, BlockIds.Sequential());
        Assert.Equal("[Hello ][b:wo]", Shape(document.Blocks[0].Text));
        Assert.Equal("[b:rld]", Shape(document.Blocks[1].Text));
    }

    [Fact]
    public void AfterAHeadingComesAParagraph()
    {
        var result = Edit(Doc(H(2, "a", "Title")), (document, ids) => document.Split("a", 5, 5, ids));
        Assert.Equal("h2#a Title\np#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void AHeadingSplitInTheMiddleIsTwoHeadings()
    {
        var result = Edit(Doc(H(2, "a", "Title")), (document, ids) => document.Split("a", 2, 2, ids));
        Assert.Equal("h2#a Ti\nh2#n1 tle", result.Outline);
    }

    [Fact]
    public void AListItemSplitsIntoTwoItemsAndTheNewOneTakesTheChildren()
    {
        var document = Doc(Bullet("a", "Hello", Bullet("c", "child")));
        var selection = document.Split("a", 2, 2, BlockIds.Sequential());
        Assert.Equal("bullet#a He\nbullet#n1 llo\n  bullet#c child", Outline(document));
        Assert.Equal("n1|0", Shape(selection));
        Assert.Equal("n2", document.Blocks[1].ParagraphId);
    }

    [Fact]
    public void ANewTaskStartsUnchecked()
    {
        var result = Edit(Doc(Todo("a", "Done", true)), (document, ids) => document.Split("a", 4, 4, ids));
        Assert.Equal("done#a Done\ntodo#n1", result.Outline);
    }

    [Fact]
    public void AnEmptyListItemAtTheTopBecomesAParagraph()
    {
        var result = Edit(Doc(Bullet("a", "A"), Bullet("b")), (document, ids) => document.Split("b", 0, 0, ids));
        Assert.Equal("bullet#a A\np#b", result.Outline);
        Assert.Equal("b|0", result.Selection);
    }

    [Fact]
    public void AnEmptyNestedListItemMovesOutALevel()
    {
        var result = Edit(Doc(Bullet("a", "A", Bullet("b"))), (document, ids) => document.Split("b", 0, 0, ids));
        Assert.Equal("bullet#a A\nbullet#b", result.Outline);
        Assert.Equal("b|0", result.Selection);
    }

    [Fact]
    public void AnEmptyParagraphAtTheEndOfAQuoteLeavesIt()
    {
        var result = Edit(Doc(Quote("q", P("x", "Quoted"), P("y"))), (document, ids) => document.Split("y", 0, 0, ids));
        Assert.Equal("quote#q\n  p#x Quoted\np#y", result.Outline);
        Assert.Equal("y|0", result.Selection);
    }

    [Fact]
    public void LeavingAQuoteThatHoldsNothingElseRemovesIt()
    {
        var result = Edit(Doc(Quote("q", P("x"))), (document, ids) => document.Split("x", 0, 0, ids));
        Assert.Equal("p#x", result.Outline);
    }

    [Fact]
    public void AnEmptyParagraphAtTheEndOfAListItemLeavesTheItem()
    {
        var result = Edit(Doc(Bullet("a", "A", P("x"))), (document, ids) => document.Split("x", 0, 0, ids));
        Assert.Equal("bullet#a A\np#x", result.Outline);
    }

    [Fact]
    public void AParagraphInsideAQuoteSplitsInsideIt()
    {
        var result = Edit(Doc(Quote("q", P("x", "Quoted"))), (document, ids) => document.Split("x", 6, 6, ids));
        Assert.Equal("quote#q\n  p#x Quoted\n  p#n1", result.Outline);
    }
}

public sealed class DeleteBackwardTests
{
    [Fact]
    public void AHeadingBecomesAParagraph()
    {
        var result = Edit(Doc(H(1, "a", "Title")), (document, ids) => document.DeleteBackward("a", ids));
        Assert.Equal("p#a Title", result.Outline);
        Assert.Equal("a|0", result.Selection);
    }

    [Fact]
    public void AListItemBecomesAParagraphAndItsChildrenFollowIt()
    {
        var result = Edit(
            Doc(Todo("a", "A", true, Bullet("b", "B"))), (document, ids) => document.DeleteBackward("a", ids));
        Assert.Equal("p#a A\nbullet#b B", result.Outline);
        Assert.Equal("a|0", result.Selection);
    }

    [Fact]
    public void ANestedListItemBecomesAParagraphWhereItIs()
    {
        var result = Edit(Doc(Bullet("a", "A", Bullet("b", "B"))), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("bullet#a A\n  p#b B", result.Outline);
    }

    [Fact]
    public void AParagraphJoinsTheOneBefore()
    {
        var document = Doc(P("a", "Hello"), P("b", "World"));
        document.Blocks[1].Text = document.Blocks[1].Text.Set(TextMark.Bold, true, 0, 5);
        var selection = document.DeleteBackward("b", BlockIds.Sequential());
        Assert.Equal("p#a HelloWorld", Outline(document));
        Assert.Equal("[Hello][b:World]", Shape(document.Blocks[0].Text));
        Assert.NotNull(selection);
        Assert.Equal("a|5", Shape(selection));
    }

    [Fact]
    public void ItJoinsTheLastThingShownAboveIt()
    {
        var result = Edit(
            Doc(Bullet("a", "A", Bullet("c", "C")), P("b", "tail")), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("bullet#a A\n  bullet#c Ctail", result.Outline);
        Assert.Equal("c|1", result.Selection);
    }

    [Fact]
    public void AParagraphNestedInAnItemJoinsTheItemsText()
    {
        var result = Edit(Doc(Bullet("a", "A", P("b", "more"))), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("bullet#a Amore", result.Outline);
        Assert.Equal("a|1", result.Selection);
    }

    [Fact]
    public void NothingHappensAtTheVeryTop()
    {
        var result = Edit(Doc(P("a", "Hello")), (document, ids) => document.DeleteBackward("a", ids));
        Assert.Equal("p#a Hello", result.Outline);
        Assert.Equal("nil", result.Selection);
    }

    [Fact]
    public void TheFirstParagraphOfAQuoteLeavesIt()
    {
        var result = Edit(
            Doc(Quote("q", P("x", "One"), P("y", "Two"))), (document, ids) => document.DeleteBackward("x", ids));
        Assert.Equal("p#x One\nquote#q\n  p#y Two", result.Outline);
        Assert.Equal("x|0", result.Selection);
    }

    [Fact]
    public void AQuoteLeftEmptyIsRemoved()
    {
        var result = Edit(Doc(Quote("q", P("x", "One"))), (document, ids) => document.DeleteBackward("x", ids));
        Assert.Equal("p#x One", result.Outline);
    }

    [Fact]
    public void ABlockWithoutTextAboveIsSelectedInsteadOfJoined()
    {
        var result = Edit(Doc(Divider("d"), P("b", "tail")), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("divider#d\np#b tail", result.Outline);
        Assert.Equal("[d]", result.Selection);
    }

    [Fact]
    public void AnEmptyParagraphBelowABlockWithoutTextGoesAway()
    {
        var result = Edit(Doc(Image("i"), P("b")), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("image#i", result.Outline);
        Assert.Equal("[i]", result.Selection);
    }

    [Fact]
    public void BelowCodeTheCaretMovesIntoTheCode()
    {
        var result = Edit(Doc(Code("c", "let x"), P("b")), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("code#c let x", result.Outline);
        Assert.Equal("c|5", result.Selection);
        // Text is never poured into code.
        result = Edit(Doc(Code("c", "let x"), P("b", "tail")), (document, ids) => document.DeleteBackward("b", ids));
        Assert.Equal("code#c let x\np#b tail", result.Outline);
        Assert.Equal("c|5", result.Selection);
    }

    [Fact]
    public void AnEmptyCodeBlockBecomesAParagraph()
    {
        var result = Edit(Doc(Code("c")), (document, ids) => document.DeleteBackward("c", ids));
        Assert.Equal("p#c", result.Outline);
        result = Edit(Doc(Code("c", "x")), (document, ids) => document.DeleteBackward("c", ids));
        Assert.Equal("code#c x", result.Outline);
        Assert.Equal("nil", result.Selection);
    }

    [Fact]
    public void WhatAClosedToggleHidesIsSkipped()
    {
        var document = Doc(Toggle("t", false, P("s", "Sum"), P("h", "Hidden")), P("b", "tail"));
        var result = Edit(document, (edited, ids) => edited.DeleteBackward("b", ids));
        Assert.Equal("toggle(closed)#t\n  p#s Sumtail\n  p#h Hidden", result.Outline);
        Assert.Equal("s|3", result.Selection);
    }
}

public sealed class DeleteForwardTests
{
    [Fact]
    public void TheNextParagraphJoinsThisOne()
    {
        var result = Edit(Doc(P("a", "Hello"), P("b", "World")), (document, _) => document.DeleteForward("a"));
        Assert.Equal("p#a HelloWorld", result.Outline);
        Assert.Equal("a|5", result.Selection);
    }

    [Fact]
    public void TheChildrenOfAJoinedItemStayWhereItWas()
    {
        var result = Edit(
            Doc(P("a", "A"), Bullet("b", "B", Bullet("c", "C"))), (document, _) => document.DeleteForward("a"));
        Assert.Equal("p#a AB\nbullet#c C", result.Outline);
    }

    [Fact]
    public void AnItemCanTakeInItsOwnFirstChild()
    {
        var result = Edit(
            Doc(Bullet("a", "A", Bullet("b", "B", Bullet("c", "C")), Bullet("d", "D"))),
            (document, _) => document.DeleteForward("a"));
        Assert.Equal("bullet#a AB\n  bullet#c C\n  bullet#d D", result.Outline);
    }

    [Fact]
    public void ABlockWithoutTextBelowIsSelected()
    {
        var result = Edit(Doc(P("a", "A"), Divider("d")), (document, _) => document.DeleteForward("a"));
        Assert.Equal("p#a A\ndivider#d", result.Outline);
        Assert.Equal("[d]", result.Selection);
    }

    [Fact]
    public void NothingHappensAtTheVeryEndOrBeforeCode()
    {
        var result = Edit(Doc(P("a", "A")), (document, _) => document.DeleteForward("a"));
        Assert.Equal("nil", result.Selection);
        result = Edit(Doc(P("a", "A"), Code("c", "x")), (document, _) => document.DeleteForward("a"));
        Assert.Equal("p#a A\ncode#c x", result.Outline);
        Assert.Equal("nil", result.Selection);
    }
}

public sealed class IndentTests
{
    [Fact]
    public void AListItemMovesUnderTheItemBeforeIt()
    {
        var document = Doc(Bullet("a", "A"), Bullet("b", "B", Bullet("c", "C")));
        Assert.True(document.Indent("b"));
        Assert.Equal("bullet#a A\n  bullet#b B\n    bullet#c C", Outline(document));
    }

    [Fact]
    public void ItGoesAfterTheChildrenAlreadyThere()
    {
        var document = Doc(Bullet("a", "A", Bullet("x", "X")), Todo("b", "B"));
        Assert.True(document.Indent("b"));
        Assert.Equal("bullet#a A\n  bullet#x X\n  todo#b B", Outline(document));
    }

    [Fact]
    public void NothingToIndentUnder()
    {
        var document = Doc(Bullet("a", "A"), P("b", "B"), Bullet("c", "C"));
        Assert.False(document.Indent("a"));
        Assert.False(document.Indent("b"));
        Assert.False(document.Indent("c"));
        Assert.Equal("bullet#a A\np#b B\nbullet#c C", Outline(document));
    }

    [Fact]
    public void OutdentingTakesTheItemsBelowItAlong()
    {
        var document = Doc(Bullet("a", "A", Bullet("b", "B"), Bullet("c", "C")));
        Assert.True(document.Outdent("b"));
        Assert.Equal("bullet#a A\nbullet#b B\n  bullet#c C", Outline(document));
    }

    [Fact]
    public void OutdentingKeepsTheItemsOwnChildrenFirst()
    {
        var document = Doc(Bullet("a", "A", Bullet("b", "B", Bullet("x", "X")), Bullet("c", "C")));
        Assert.True(document.Outdent("b"));
        Assert.Equal("bullet#a A\nbullet#b B\n  bullet#x X\n  bullet#c C", Outline(document));
    }

    [Fact]
    public void NothingToOutdentFrom()
    {
        var document = Doc(Bullet("a", "A", P("x", "more"), Bullet("c", "C")), Quote("q", Bullet("d", "D")));
        Assert.False(document.Outdent("a"));
        Assert.False(document.Outdent("d"));
        // A paragraph cannot take the blocks below it along, so it only
        // moves out from the end.
        Assert.False(document.Outdent("x"));
        Assert.Equal("bullet#a A\n  p#x more\n  bullet#c C\nquote#q\n  bullet#d D", Outline(document));
    }

    [Fact]
    public void AParagraphAtTheEndOfAnItemMovesOut()
    {
        var document = Doc(Bullet("a", "A", P("x", "more")));
        Assert.True(document.Outdent("x"));
        Assert.Equal("bullet#a A\np#x more", Outline(document));
    }
}

public sealed class TurnIntoTests
{
    [Fact]
    public void TextBlocksChangeKindAndKeepTheirIdAndText()
    {
        var result = Edit(Doc(P("a", "Text")), (document, ids) => document.Turn("a", BlockTarget.Heading(2), ids));
        Assert.Equal("h2#a Text", result.Outline);
        Assert.Equal("a|0", result.Selection);
        result = Edit(Doc(H(3, "a", "Text")), (document, ids) => document.Turn("a", BlockTarget.Todo, ids));
        Assert.Equal("todo#a Text", result.Outline);
        result = Edit(Doc(Todo("a", "Text", true)), (document, ids) => document.Turn("a", BlockTarget.Bullet, ids));
        Assert.Equal("bullet#a Text", result.Outline);
    }

    [Fact]
    public void ANewListItemGetsAnIdForItsParagraph()
    {
        var document = Doc(P("a", "Text"));
        var ids = BlockIds.Sequential();
        document.Turn("a", BlockTarget.Numbered, ids);
        Assert.Equal("n1", document.Blocks[0].ParagraphId);
        Assert.Empty(document.Blocks[0].Attrs);
        document.Turn("a", BlockTarget.Heading(1), ids);
        Assert.Null(document.Blocks[0].ParagraphId);
        Assert.Equal(JsonValue.Of(1), Assert.Single(document.Blocks[0].Attrs).Value);
        Assert.Equal(1, document.Blocks[0].Level);
    }

    [Fact]
    public void ChildrenOfAnItemThatStopsBeingOneFollowIt()
    {
        var result = Edit(
            Doc(Bullet("a", "A", Bullet("b", "B")), P("z", "Z")),
            (document, ids) => document.Turn("a", BlockTarget.Paragraph, ids));
        Assert.Equal("p#a A\nbullet#b B\np#z Z", result.Outline);
    }

    [Fact]
    public void WrapsABlockInAQuoteCalloutOrToggle()
    {
        var result = Edit(Doc(P("a", "Text")), (document, ids) => document.Turn("a", BlockTarget.Quote, ids));
        Assert.Equal("quote#n1\n  p#a Text", result.Outline);
        Assert.Equal("a|0", result.Selection);
        result = Edit(Doc(Bullet("a", "Text")), (document, ids) => document.Turn("a", BlockTarget.Callout, ids));
        Assert.Equal("callout#n1\n  bullet#a Text", result.Outline);
        result = Edit(Doc(P("a", "Text")), (document, ids) => document.Turn("a", BlockTarget.Toggle, ids));
        Assert.Equal("toggle#n1\n  p#a Text", result.Outline);
    }

    [Fact]
    public void AContainerChangesKindOrLetsItsBlocksOut()
    {
        var result = Edit(
            Doc(Quote("q", P("a", "A"), P("b", "B"))), (document, ids) => document.Turn("q", BlockTarget.Callout, ids));
        Assert.Equal("callout#q\n  p#a A\n  p#b B", result.Outline);
        result = Edit(
            Doc(Quote("q", P("a", "A"), P("b", "B")), P("z")),
            (document, ids) => document.Turn("q", BlockTarget.Paragraph, ids));
        Assert.Equal("p#a A\np#b B\np#z", result.Outline);
        Assert.Equal("a|0", result.Selection);
    }

    [Fact]
    public void TurnsTextIntoCodeAndBack()
    {
        var document = Doc(P("a", "let x"));
        document.Blocks[0].Text = document.Blocks[0].Text.Set(TextMark.Bold, true, 0, 3);
        var ids = BlockIds.Sequential();
        document.Turn("a", BlockTarget.Code, ids);
        Assert.Equal("code#a let x", Outline(document));
        Assert.Equal("[let x]", Shape(document.Blocks[0].Text));
        Assert.Equal("plaintext", document.Blocks[0].Language);
        document.Turn("a", BlockTarget.Paragraph, ids);
        Assert.Equal("p#a let x", Outline(document));
        Assert.Empty(document.Blocks[0].Attrs);
    }

    [Fact]
    public void ADividerTakesTheBlocksPlaceAndAParagraphFollows()
    {
        var result = Edit(Doc(P("a")), (document, ids) => document.Turn("a", BlockTarget.Divider, ids));
        Assert.Equal("divider#a\np#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
        // Text is not thrown away: the divider goes below it.
        result = Edit(Doc(P("a", "Text")), (document, ids) => document.Turn("a", BlockTarget.Divider, ids));
        Assert.Equal("p#a Text\ndivider#n1\np#n2", result.Outline);
        Assert.Equal("n2|0", result.Selection);
    }
}

public sealed class RemoveTests
{
    [Fact]
    public void TheCaretGoesToTheEndOfTheBlockAbove()
    {
        var result = Edit(
            Doc(P("a", "Hello"), P("b", "B"), P("c", "C")), (document, ids) => document.Remove(["b"], ids));
        Assert.Equal("p#a Hello\np#c C", result.Outline);
        Assert.Equal("a|5", result.Selection);
    }

    [Fact]
    public void OrToTheStartOfTheBlockBelowWhenNothingIsAbove()
    {
        var result = Edit(Doc(P("a", "A"), P("b", "B")), (document, ids) => document.Remove(["a"], ids));
        Assert.Equal("p#b B", result.Outline);
        Assert.Equal("b|0", result.Selection);
    }

    [Fact]
    public void ANoteIsNeverLeftWithNoBlocks()
    {
        var result = Edit(Doc(P("a", "A"), Divider("d")), (document, ids) => document.Remove(["a", "d"], ids));
        Assert.Equal("p#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void RemovingABlockRemovesWhatIsInsideIt()
    {
        var result = Edit(
            Doc(P("z", "Z"), Bullet("a", "A", Bullet("b", "B")), Quote("q", P("x", "X"))),
            (document, ids) => document.Remove(["a", "x"], ids));
        Assert.Equal("p#z Z", result.Outline);
        Assert.Equal("z|1", result.Selection);
    }

    [Fact]
    public void ABlockWithoutTextAboveIsSelected()
    {
        var result = Edit(Doc(Divider("d"), P("b", "B")), (document, ids) => document.Remove(["b"], ids));
        Assert.Equal("divider#d", result.Outline);
        Assert.Equal("[d]", result.Selection);
    }

    [Fact]
    public void ARowLeftWithOneColumnStopsBeingARow()
    {
        var document = Doc(Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B"))), P("z"));
        var result = Edit(document, (edited, ids) => edited.Remove(["b"], ids));
        Assert.Equal("p#a A\np#z", result.Outline);
        Assert.Equal("a|1", result.Selection);
    }
}

public sealed class DuplicateTests
{
    [Fact]
    public void TheCopyFollowsTheOriginalWithIdsOfItsOwn()
    {
        var document = Doc(Bullet("a", "A", Bullet("b", "B")), P("z"));
        document.Blocks[0].StartsList = true;
        var copy = document.Duplicate("a", BlockIds.Sequential());
        Assert.Equal("n1", copy);
        Assert.Equal("^bullet#a A\n  bullet#b B\nbullet#n1 A\n  bullet#n3 B\np#z", Outline(document));
        Assert.Equal("n2", document.Blocks[1].ParagraphId);
        Assert.Equal("n4", document.Blocks[1].Children[0].ParagraphId);
        // The original is untouched, ids and all.
        Assert.Equal("a-p", document.Blocks[0].ParagraphId);
    }

    [Fact]
    public void ThereIsNothingToCopyWithoutTheBlock()
    {
        var document = Doc(P("a"));
        Assert.Null(document.Duplicate("missing", BlockIds.Sequential()));
    }
}

public sealed class MoveTests
{
    [Fact]
    public void MovesABlockBeforeOrAfterAnother()
    {
        var result = Edit(Doc(P("a", "A"), P("b", "B"), P("c", "C")), (document, ids) =>
        {
            document.Move(["c"], DropTarget.Before("a"), ids);
            return null;
        });
        Assert.Equal("p#c C\np#a A\np#b B", result.Outline);
        result = Edit(Doc(P("a", "A"), P("b", "B"), P("c", "C")), (document, ids) =>
        {
            document.Move(["a"], DropTarget.After("b"), ids);
            return null;
        });
        Assert.Equal("p#b B\np#a A\np#c C", result.Outline);
    }

    [Fact]
    public void ABlockMovesWithEverythingInsideIt()
    {
        var document = Doc(Bullet("a", "A", Bullet("b", "B")), Bullet("c", "C", Bullet("d", "D")));
        Assert.True(document.Move(["a"], DropTarget.After("d"), BlockIds.Sequential()));
        Assert.Equal("bullet#c C\n  bullet#d D\n  bullet#a A\n    bullet#b B", Outline(document));
    }

    [Fact]
    public void SeveralBlocksKeepTheirOrder()
    {
        var document = Doc(P("a", "A"), P("b", "B"), P("c", "C"), P("d", "D"));
        Assert.True(document.Move(["c", "d"], DropTarget.Before("a"), BlockIds.Sequential()));
        Assert.Equal("p#c C\np#d D\np#a A\np#b B", Outline(document));
    }

    [Fact]
    public void ABlockCannotMoveIntoItselfOrNextToItself()
    {
        var document = Doc(Bullet("a", "A", Bullet("b", "B")), P("z"));
        var ids = BlockIds.Sequential();
        Assert.False(document.Move(["a"], DropTarget.After("b"), ids));
        Assert.False(document.Move(["a"], DropTarget.Before("a"), ids));
        Assert.False(document.Move(["missing"], DropTarget.Before("a"), ids));
        Assert.False(document.Move(["a"], DropTarget.Before("missing"), ids));
        Assert.Equal("bullet#a A\n  bullet#b B\np#z", Outline(document));
    }

    [Fact]
    public void AMovedItemJoinsTheListItLandsIn()
    {
        var document = Doc(Bullet("a", "A"), P("x"), Bullet("b", "B"), Bullet("c", "C"));
        document.Blocks[3].StartsList = true;
        Assert.True(document.Move(["c"], DropTarget.After("a"), BlockIds.Sequential()));
        Assert.Equal("bullet#a A\nbullet#c C\np#x\nbullet#b B", Outline(document));
    }

    [Fact]
    public void DroppingBesideABlockMakesTwoColumns()
    {
        var document = Doc(P("a", "A"), P("b", "B"), P("z"));
        Assert.True(document.Move(["b"], DropTarget.RightOf("a"), BlockIds.Sequential()));
        Assert.Equal("columns#n1\n  column#n2\n    p#a A\n  column#n3\n    p#b B\np#z", Outline(document));

        document = Doc(P("a", "A"), P("b", "B"));
        Assert.True(document.Move(["b"], DropTarget.LeftOf("a"), BlockIds.Sequential()));
        Assert.Equal("columns#n1\n  column#n2\n    p#b B\n  column#n3\n    p#a A", Outline(document));
    }

    [Fact]
    public void DroppingBesideAColumnAddsAColumnToTheRow()
    {
        var document = Doc(Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B"))), P("c", "C"));
        Assert.True(document.Move(["c"], DropTarget.RightOf("a"), BlockIds.Sequential()));
        Assert.Equal(
            "columns#r\n  column#c1\n    p#a A\n  column#n1\n    p#c C\n  column#c2\n    p#b B", Outline(document));
    }

    [Fact]
    public void TakingTheLastBlockOutOfAColumnRemovesTheColumn()
    {
        var document = Doc(
            Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B")), Column("c3", P("c", "C"))), P("z"));
        var ids = BlockIds.Sequential();
        Assert.True(document.Move(["b"], DropTarget.After("z"), ids));
        Assert.Equal("columns#r\n  column#c1\n    p#a A\n  column#c3\n    p#c C\np#z\np#b B", Outline(document));
        // And a row with one column left is no longer a row.
        Assert.True(document.Move(["c"], DropTarget.After("z"), ids));
        Assert.Equal("p#a A\np#z\np#c C\np#b B", Outline(document));
    }

    [Fact]
    public void OnlyBlocksAtTheTopOrInAColumnCanHaveColumnsBesideThem()
    {
        var document = Doc(Quote("q", P("a", "A")), P("b", "B"));
        Assert.True(document.Move(["b"], DropTarget.RightOf("a"), BlockIds.Sequential()));
        Assert.Equal("quote#q\n  p#a A\n  p#b B", Outline(document));
    }

    [Fact]
    public void ARowCannotGoInsideAColumn()
    {
        var document = Doc(Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B"))), P("z", "Z"));
        var ids = BlockIds.Sequential();
        Assert.True(document.Move(["r"], DropTarget.After("z"), ids));
        Assert.Equal("p#z Z\ncolumns#r\n  column#c1\n    p#a A\n  column#c2\n    p#b B", Outline(document));
        // Its own columns are inside it, and another row's would nest rows.
        document = Doc(
            Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B"))),
            Columns("s", Column("d1", P("c", "C")), Column("d2", P("d", "D"))));
        var before = Outline(document);
        Assert.False(document.Move(["r"], DropTarget.After("c"), ids));
        Assert.False(document.Move(["r"], DropTarget.RightOf("c"), ids));
        Assert.Equal(before, Outline(document));
    }
}

public sealed class NormalizeTests
{
    [Fact]
    public void TidiesWhatAnEditCanLeaveBehind()
    {
        var document = Doc(
            Quote("q"), Columns("r", Column("c1"), Column("c2", P("b", "B"))), Callout("k", Toggle("t")));
        document.Normalize(BlockIds.Sequential());
        Assert.Equal("p#b B", Outline(document));
    }

    [Fact]
    public void ABlockThatCannotHoldBlocksLetsThemOut()
    {
        var document = Doc(new Block("a", BlockKind.Paragraph, new RichText("A"), [P("b", "B")]));
        document.Normalize(BlockIds.Sequential());
        Assert.Equal("p#a A\np#b B", Outline(document));
    }

    [Fact]
    public void AnEmptyDocumentGetsAParagraph()
    {
        var document = Doc();
        document.Normalize(BlockIds.Sequential());
        Assert.Equal("p#n1", Outline(document));
    }
}

public sealed class BlockShortcutTests
{
    private static (string Outline, string Selection) Typed(string text, int? caret = null, Block? block = null)
    {
        block ??= P("a");
        block.Text = new RichText(text);
        return Edit(Doc(block), (document, ids) => document.ApplyBlockShortcut("a", caret ?? text.Length, ids));
    }

    [Fact]
    public void HashesMakeAHeading()
    {
        Assert.Equal("h1#a", Typed("# ").Outline);
        Assert.Equal("h3#a", Typed("### ").Outline);
        Assert.Equal("h6#a", Typed("###### ").Outline);
        Assert.Equal("nil", Typed("####### ").Selection);
        Assert.Equal("a|0", Typed("# ").Selection);
    }

    [Fact]
    public void TheRestOfTheLineIsKept()
    {
        var result = Typed("## Existing text", caret: 3);
        Assert.Equal("h2#a Existing text", result.Outline);
        Assert.Equal("a|0", result.Selection);
    }

    [Fact]
    public void ADashStarOrPlusMakesABullet()
    {
        Assert.Equal("bullet#a", Typed("- ").Outline);
        Assert.Equal("bullet#a", Typed("* ").Outline);
        Assert.Equal("bullet#a", Typed("+ ").Outline);
    }

    [Fact]
    public void ANumberMakesANumberedItemThatStartsThere()
    {
        var document = Doc(P("a", "1. "));
        var ids = BlockIds.Sequential();
        document.ApplyBlockShortcut("a", 3, ids);
        Assert.Equal("numbered#a", Outline(document));
        Assert.Empty(document.Blocks[0].ListAttrs);

        document = Doc(P("a", "7) "));
        document.ApplyBlockShortcut("a", 3, ids);
        Assert.Equal("numbered#a", Outline(document));
        var start = Assert.Single(document.Blocks[0].ListAttrs);
        Assert.Equal("start", start.Key);
        Assert.Equal(JsonValue.Of(7), start.Value);
    }

    [Fact]
    public void BracketsMakeATask()
    {
        Assert.Equal("todo#a", Typed("[] ").Outline);
        Assert.Equal("todo#a", Typed("[ ] ").Outline);
        Assert.Equal("done#a", Typed("[x] ").Outline);
    }

    [Fact]
    public void AnAngleBracketMakesAQuote()
    {
        var result = Typed("> ");
        Assert.Equal("quote#n1\n  p#a", result.Outline);
        Assert.Equal("a|0", result.Selection);
    }

    [Fact]
    public void BackticksMakeACodeBlock()
    {
        Assert.Equal("code#a", Typed("``` ").Outline);
        var document = Doc(P("a", "```swift "));
        document.ApplyBlockShortcut("a", 9, BlockIds.Sequential());
        Assert.Equal("code#a", Outline(document));
        Assert.Equal("swift", document.Blocks[0].Language);
    }

    [Fact]
    public void ThreeDashesMakeADivider()
    {
        var result = Typed("---");
        Assert.Equal("divider#a\np#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
        // macOS turns two dashes into a long one as they are typed. Kept
        // here so that text typed on a Mac and finished on Windows behaves.
        result = Typed("—-");
        Assert.Equal("divider#a\np#n1", result.Outline);
    }

    [Fact]
    public void NothingHappensAwayFromTheStartOrInOtherBlocks()
    {
        Assert.Equal("nil", Typed("# title", caret: 7).Selection);
        Assert.Equal("nil", Typed("a # ").Selection);
        Assert.Equal("nil", Typed("#").Selection);
        Assert.Equal("nil", Typed("# ", block: H(2, "a")).Selection);
        Assert.Equal("nil", Typed("- ", block: Code("a")).Selection);
    }
}

public sealed class InlineShortcutTests
{
    private static string Typed(string text) =>
        new RichText(text).ApplyingInlineShortcut(text.Length) is { } result ? $"{Shape(result.Text)} {result.Caret}" : "nil";

    [Fact]
    public void StarsAndUnderscoresMakeBoldAndItalic()
    {
        Assert.Equal("[a ][b:bold] 6", Typed("a **bold**"));
        Assert.Equal("[b:bold] 4", Typed("__bold__"));
        Assert.Equal("[an ][i:italic] 9", Typed("an *italic*"));
        Assert.Equal("[i:it] 2", Typed("_it_"));
    }

    [Fact]
    public void BackticksMakeCodeAndTildesStrike()
    {
        Assert.Equal("[run ][c:ls -la] 10", Typed("run `ls -la`"));
        Assert.Equal("[s:gone] 4", Typed("~~gone~~"));
    }

    [Fact]
    public void AnUnfinishedOrUnlikelyPairIsLeftAlone()
    {
        Assert.Equal("nil", Typed("**bold*"));
        Assert.Equal("nil", Typed("2 * 3 *"));
        Assert.Equal("nil", Typed("snake_case_"));
        Assert.Equal("nil", Typed("**"));
        Assert.Equal("nil", Typed("* *"));
        Assert.Equal("nil", Typed("plain"));
    }

    [Fact]
    public void OnlyThePairThatEndsAtTheCaretCounts()
    {
        var text = new RichText("*a* b");
        Assert.Null(text.ApplyingInlineShortcut(5));
        var result = text.ApplyingInlineShortcut(3);
        Assert.NotNull(result);
        Assert.Equal("[i:a][ b]", Shape(result.Value.Text));
        Assert.Equal(1, result.Value.Caret);
        Assert.Equal(TextMark.Italic, result.Value.Mark);
    }

    [Fact]
    public void MarksAlreadyOnTheTextAreKept()
    {
        var text = new RichText("**go there**").SetLink(new TextLink("https://example.com"), 5, 10);
        var result = text.ApplyingInlineShortcut(12);
        Assert.NotNull(result);
        Assert.Equal("[b:go ][b<https://example.com>:there]", Shape(result.Value.Text));
    }

    [Fact]
    public void NothingIsReadInsideCode()
    {
        var text = new RichText("x *a*").Set(TextMark.Code, true, 0, 5);
        Assert.Null(text.ApplyingInlineShortcut(5));
    }
}

public sealed class AutolinkTests
{
    private static string Typed(string text) =>
        new RichText(text).Autolinked(text.Length) is { } linked ? Shape(linked) : "nil";

    [Fact]
    public void AnAddressBecomesALinkWhenTheSpaceAfterItIsTyped()
    {
        Assert.Equal(
            "[see ][<https://example.com/a?b=1>:https://example.com/a?b=1][ ]", Typed("see https://example.com/a?b=1 "));
        Assert.Equal("[<http://example.com>:http://example.com][ ]", Typed("http://example.com "));
    }

    [Fact]
    public void TheFullStopAfterAnAddressIsNotPartOfIt()
    {
        Assert.Equal("[at ][<https://example.com>:https://example.com][. ]", Typed("at https://example.com. "));
    }

    [Fact]
    public void OtherWordsAreLeftAlone()
    {
        Assert.Equal("nil", Typed("example.com "));
        Assert.Equal("nil", Typed("https:// "));
        Assert.Equal("nil", Typed("ftp://example.com "));
        Assert.Equal("nil", Typed("javascript:alert(1) "));
        Assert.Equal("nil", Typed("https://example.com"));
        Assert.Equal("nil", Typed(" "));
    }

    [Fact]
    public void ALinkOrCodeIsNotLinkedAgain()
    {
        var text = new RichText("https://example.com ").SetLink(new TextLink("https://other.example"), 0, 19);
        Assert.Null(text.Autolinked(20));
        text = new RichText("https://example.com ").Set(TextMark.Code, true, 0, 19);
        Assert.Null(text.Autolinked(20));
    }

    [Fact]
    public void TellsAWebAddressFromOtherText()
    {
        Assert.True(RichText.IsWebAddress("https://example.com/path"));
        Assert.False(RichText.IsWebAddress("https://example.com and more"));
        Assert.False(RichText.IsWebAddress("mailto:a@b.example"));
        Assert.False(RichText.IsWebAddress("not an address"));
    }
}

public sealed class PasteTests
{
    [Fact]
    public void OneParagraphGoesIntoTheLineBeingTyped()
    {
        var pasted = P("x", "new");
        pasted.Text = pasted.Text.Set(TextMark.Bold, true, 0, 3);
        var document = Doc(P("a", "Hello world"));
        var selection = document.Paste([pasted], EditorSelection.Text("a", 6, 11), BlockIds.Sequential());
        Assert.Equal("[Hello ][b:new]", Shape(document.Blocks[0].Text));
        Assert.Equal("a|9", Shape(selection));
    }

    [Fact]
    public void SeveralBlocksGoBelowTheBlockWithIdsOfTheirOwn()
    {
        var result = Edit(Doc(P("a", "Hello"), P("z")), (document, ids) =>
            document.Paste([H(1, "a", "Title"), Bullet("b", "B", Bullet("c", "C"))], EditorSelection.Caret("a", 2), ids));
        Assert.Equal("p#a Hello\nh1#n1 Title\nbullet#n2 B\n  bullet#n4 C\np#z", result.Outline);
        Assert.Equal("n4|1", result.Selection);
    }

    [Fact]
    public void AnEmptyParagraphIsReplacedByWhatIsPasted()
    {
        var result = Edit(
            Doc(P("a")), (document, ids) => document.Paste([H(2, "x", "Title")], EditorSelection.Caret("a", 0), ids));
        Assert.Equal("h2#n1 Title", result.Outline);
        Assert.Equal("n1|5", result.Selection);
    }

    [Fact]
    public void SelectedBlocksAreReplaced()
    {
        var result = Edit(Doc(P("a", "A"), P("b", "B"), P("c", "C")), (document, ids) =>
            document.Paste([P("x", "X"), P("y", "Y")], EditorSelection.Blocks(["a", "b"]), ids));
        Assert.Equal("p#n1 X\np#n2 Y\np#c C", result.Outline);
        Assert.Equal("n2|1", result.Selection);
    }

    [Fact]
    public void IntoCodeOnlyTheWordsGo()
    {
        var document = Doc(Code("c", "let x"));
        var selection = document.Paste(
            [P("x", " = 1"), Bullet("y", "two")], EditorSelection.Caret("c", 5), BlockIds.Sequential());
        Assert.Equal("let x = 1\ntwo", document.Blocks[0].Text.String);
        Assert.Equal("c|13", Shape(selection));
    }

    [Fact]
    public void ABlockWithoutTextCanBePastedToo()
    {
        var result = Edit(
            Doc(P("a", "A")), (document, ids) => document.Paste([Divider("d")], EditorSelection.Caret("a", 1), ids));
        Assert.Equal("p#a A\ndivider#n1", result.Outline);
        Assert.Equal("[n1]", result.Selection);
    }
}

public sealed class BlockSelectionTests
{
    private readonly EditorDocument document = Doc(
        P("a", "A"), Bullet("b", "B", Bullet("c", "C"), Bullet("d", "D")), Quote("q", P("e", "E")), P("f", "F"));

    [Fact]
    public void SelectsEveryBlockBetweenTwo()
    {
        Assert.Equal(["a", "b", "q", "f"], document.BlocksBetween("a", "f"));
        Assert.Equal(["a", "b", "q", "f"], document.BlocksBetween("f", "a"));
        Assert.Equal(["a"], document.BlocksBetween("a", "a"));
    }

    [Fact]
    public void ABlockStandsForEverythingInsideIt()
    {
        Assert.Equal(["b"], document.BlocksBetween("b", "d"));
        Assert.Equal(["c", "d", "q"], document.BlocksBetween("c", "e"));
        Assert.Equal(["d", "q", "f"], document.BlocksBetween("d", "f"));
    }

    [Fact]
    public void CopiesBlocksAsText()
    {
        var copied = Doc(
            H(2, "h", "Title"), P("p", "Para"), Bullet("b", "B", Todo("t", "T", true)), Numbered("n", "One"),
            Numbered("m", "Two"), Divider("d"), Code("c", "x = 1", "python"), Quote("q", P("e", "E")));
        Assert.Equal(
            Lines(
                """
                ## Title
                Para
                - B
                  - [x] T
                1. One
                2. Two
                ---
                ```python
                x = 1
                ```
                > E
                """),
            copied.PlainText(["h", "p", "b", "n", "m", "d", "c", "q"]));
    }
}

// From here on: what the Windows plan adds to the Mac's tests.

/// <summary>Finding blocks, and the order the caret meets them in.</summary>
public sealed class TreeTests
{
    private readonly EditorDocument document = Doc(
        P("a", "A"),
        Bullet("b", "B", Bullet("c", "C"), P("d", "D")),
        Quote("q", P("e", "E"), Divider("r")),
        Toggle("t", false, P("s", "Summary"), P("h", "Hidden")),
        Columns("w", Column("c1", P("l", "L")), Column("c2", Image("i"))));

    [Fact]
    public void FindsABlockAndWhatHoldsIt()
    {
        Assert.Equal("C", document.Block("c")?.Text.String);
        Assert.True(document.Contains("i"));
        Assert.False(document.Contains("missing"));
        Assert.Null(document.Block("missing"));
        Assert.Equal("b", document.Parent("c")?.Id);
        Assert.Equal("c2", document.Parent("i")?.Id);
        Assert.Null(document.Parent("a"));
        Assert.Null(document.Parent("missing"));
    }

    [Fact]
    public void EveryBlockIsReadDownThePage()
    {
        Assert.Equal(
            ["a", "b", "c", "d", "q", "e", "r", "t", "s", "h", "w", "c1", "l", "c2", "i"],
            document.AllBlocks.Select(block => block.Id));
    }

    [Fact]
    public void TheCaretStopsAtTextAndAtBlocksThatStandAlone()
    {
        // Not at a block that only holds others, and not at what a closed
        // toggle hides.
        Assert.Equal(["a", "b", "c", "d", "e", "r", "s", "l", "i"], document.Stops.Select(block => block.Id));
        Assert.Equal("d", document.StopBefore("e")?.Id);
        Assert.Equal("l", document.StopAfter("s")?.Id);
        Assert.Null(document.StopBefore("a"));
        Assert.Null(document.StopAfter("i"));
        // A hidden block is not a stop, so it has no neighbours.
        Assert.Null(document.StopBefore("h"));
        Assert.Null(document.StopAfter("h"));
    }

    [Fact]
    public void AnOpenToggleShowsEverythingInIt()
    {
        document.Update("t", toggle => toggle.IsOpen = true);
        Assert.Equal(["s", "h", "l"], document.Stops.Select(block => block.Id).SkipWhile(id => id != "s").Take(3));
    }

    [Fact]
    public void ABlockIsInsideItselfAndWhateverHoldsIt()
    {
        Assert.True(document.IsBlock("c", inside: "b"));
        Assert.True(document.IsBlock("b", inside: "b"));
        Assert.True(document.IsBlock("i", inside: "w"));
        Assert.False(document.IsBlock("b", inside: "c"));
        Assert.False(document.IsBlock("a", inside: "b"));
        Assert.False(document.IsBlock("missing", inside: "b"));
        Assert.False(document.IsBlock("c", inside: "missing"));
    }

    [Fact]
    public void UpdateChangesOneBlockWhereItIs()
    {
        document.Update("c", block => block.Text = new RichText("changed"));
        Assert.Equal("changed", document.Blocks[1].Children[0].Text.String);
    }

    [Fact]
    public void BlocksGoInBeforeAndAfterAnother()
    {
        Assert.True(document.InsertAfter([P("x", "X"), P("y", "Y")], "c"));
        Assert.True(document.InsertBefore([P("z", "Z")], "a"));
        Assert.Equal(["z", "a", "b", "c", "x", "y", "d"], document.AllBlocks.Select(block => block.Id).Take(7));
    }
}

/// <summary>
/// A view can hold an id, or an offset, that an edit elsewhere has made
/// stale. Nothing may change and nothing may throw.
/// </summary>
public sealed class StaleTargetTests
{
    private static EditorDocument Sample() =>
        Doc(P("a", "A"), Bullet("b", "B", Bullet("c", "C")), Quote("q", P("x", "X")));

    [Fact]
    public void AnEditAimedAtABlockThatIsNotThereChangesNothing()
    {
        var document = Sample();
        var outline = Outline(document);
        var json = document.Json;
        var ids = BlockIds.Sequential();

        Assert.Equal("none", Shape(document.Split("missing", 0, 0, ids)));
        Assert.Null(document.DeleteBackward("missing", ids));
        Assert.Null(document.DeleteForward("missing"));
        Assert.False(document.Indent("missing"));
        Assert.False(document.Outdent("missing"));
        Assert.Equal("none", Shape(document.Turn("missing", BlockTarget.Heading(1), ids)));
        Assert.Equal("none", Shape(document.Remove(["missing"], ids)));
        Assert.Equal("none", Shape(document.Remove([], ids)));
        Assert.Null(document.Duplicate("missing", ids));
        Assert.False(document.Move(["missing"], DropTarget.After("a"), ids));
        Assert.False(document.Move(["a"], DropTarget.RightOf("missing"), ids));
        Assert.False(document.Move([], DropTarget.After("a"), ids));
        Assert.Null(document.ApplyBlockShortcut("missing", 2, ids));
        Assert.Equal("missing|0", Shape(document.Paste([P("p", "P")], EditorSelection.Caret("missing", 0), ids)));
        Assert.Equal("[missing]", Shape(document.Paste([P("p", "P")], EditorSelection.Blocks(["missing"]), ids)));
        Assert.Equal("a|1", Shape(document.Paste([], EditorSelection.Caret("a", 1), ids)));
        Assert.Empty(document.BlocksBetween("missing", "a"));
        Assert.Empty(document.BlocksBetween("a", "missing"));
        Assert.Equal("", document.PlainText(["missing"]));
        Assert.False(document.InsertAfter([P("n")], "missing"));
        Assert.False(document.InsertBefore([P("n")], "missing"));
        document.Update("missing", block => block.Text = new RichText("changed"));

        Assert.Equal(outline, Outline(document));
        Assert.Equal(json, document.Json);
    }

    [Fact]
    public void AnIdGivenTwiceIsOneBlock()
    {
        var document = Doc(P("a", "A"), P("b", "B"), P("c", "C"));
        var ids = BlockIds.Sequential();
        Assert.True(document.Move(["c", "c"], DropTarget.Before("a"), ids));
        Assert.Equal("p#c C\np#a A\np#b B", Outline(document));
        Assert.Equal("a|1", Shape(document.Remove(["b", "b"], ids)));
        Assert.Equal("p#c C\np#a A", Outline(document));
        document.Paste([P("x", "X"), P("y", "Y")], EditorSelection.Blocks(["a", "a"]), ids);
        Assert.Equal("p#c C\np#n1 X\np#n2 Y", Outline(document));
    }

    [Fact]
    public void AnOffsetBeyondTheTextIsBroughtBackIntoIt()
    {
        var result = Edit(Doc(P("a", "Hello")), (document, ids) => document.Split("a", 99, 120, ids));
        Assert.Equal("p#a Hello\np#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);

        result = Edit(Doc(P("a", "Hello")), (document, ids) => document.Split("a", -4, -4, ids));
        Assert.Equal("p#n1\np#a Hello", result.Outline);

        result = Edit(Doc(P("a", "# ")), (document, ids) => document.ApplyBlockShortcut("a", 99, ids));
        Assert.Equal("p#a # ", result.Outline);
        Assert.Equal("nil", result.Selection);

        result = Edit(
            Doc(P("a", "Hello")), (document, ids) => document.Paste([P("x", "!")], EditorSelection.Caret("a", 99), ids));
        Assert.Equal("p#a Hello!", result.Outline);
        Assert.Equal("a|6", result.Selection);

        Assert.Null(new RichText("**b**").ApplyingInlineShortcut(-3));
        Assert.Equal("[b:b]", Shape(new RichText("**b**").ApplyingInlineShortcut(99)!.Value.Text));
        Assert.Null(new RichText("https://example.com ").Autolinked(-1));
    }
}

/// <summary>Whatever is removed or moved, a note keeps a block to type in.</summary>
public sealed class EmptiedDocumentTests
{
    [Fact]
    public void RemovingTheOnlyBlockLeavesAnEmptyParagraph()
    {
        var result = Edit(Doc(P("a", "A")), (document, ids) => document.Remove(["a"], ids));
        Assert.Equal("p#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void RemovingWhatHoldsEverythingLeavesAnEmptyParagraph()
    {
        var result = Edit(
            Doc(Quote("q", P("a", "A"), Bullet("b", "B", Bullet("c", "C")))), (document, ids) => document.Remove(["q"], ids));
        Assert.Equal("p#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void RemovingTheLastBlockInsideAContainerTakesTheContainerToo()
    {
        var result = Edit(Doc(Callout("k", P("a", "A"))), (document, ids) => document.Remove(["a"], ids));
        Assert.Equal("p#n1", result.Outline);
        Assert.Equal("n1|0", result.Selection);
    }

    [Fact]
    public void PastingSomethingEmptyOverEverythingStillLeavesAParagraph()
    {
        var result = Edit(
            Doc(P("a", "A")), (document, ids) => document.Paste([Quote("q")], EditorSelection.Blocks(["a"]), ids));
        Assert.Equal("p#n2", result.Outline);
    }

    [Fact]
    public void TheOnlyBlockHasNowhereToMoveTo()
    {
        var document = Doc(P("a", "A"));
        var ids = BlockIds.Sequential();
        Assert.False(document.Move(["a"], DropTarget.After("a"), ids));
        Assert.False(document.Move(["a"], DropTarget.LeftOf("a"), ids));
        Assert.Equal("p#a A", Outline(document));
    }

    [Fact]
    public void AMoveThatEmptiesWhatHeldTheBlockKeepsTheBlock()
    {
        var document = Doc(Columns("r", Column("c1", P("a", "A")), Column("c2", P("b", "B"))));
        Assert.True(document.Move(["a"], DropTarget.After("b"), BlockIds.Sequential()));
        Assert.Equal("p#b B\np#a A", Outline(document));

        document = Doc(Quote("q", P("a", "A")), P("z", "Z"));
        Assert.True(document.Move(["a"], DropTarget.After("z"), BlockIds.Sequential()));
        Assert.Equal("p#z Z\np#a A", Outline(document));
    }
}

/// <summary>A copy is a copy: changing it must not reach what it was made from.</summary>
public sealed class CopyTests
{
    [Fact]
    public void ChangingADuplicateLeavesTheOriginalAlone()
    {
        var document = Doc(Numbered("a", "A", Bullet("b", "B")));
        document.Blocks[0].Attrs["kept"] = JsonValue.Of("as it was");
        document.Blocks[0].ListAttrs["start"] = JsonValue.Of(3);
        var copy = document.Duplicate("a", BlockIds.Sequential());
        Assert.NotNull(copy);

        document.Update(copy, block =>
        {
            block.Text = new RichText("changed");
            block.Attrs["kept"] = JsonValue.Of("changed");
            block.ListAttrs["start"] = JsonValue.Of(9);
            block.Children[0].Text = new RichText("changed too");
            block.Children.Add(P("extra"));
        });

        var original = document.Blocks[0];
        Assert.Equal("A", original.Text.String);
        Assert.Equal(JsonValue.Of("as it was"), original.Attrs["kept"]);
        Assert.Equal(JsonValue.Of(3), original.ListAttrs["start"]);
        Assert.Equal("B", Assert.Single(original.Children).Text.String);
    }

    [Fact]
    public void WhatIsPastedIsNotWhatWasHandedIn()
    {
        var pasted = new[] { Bullet("x", "X", Bullet("y", "Y")), P("w", "W") };
        var document = Doc(P("a", "A"));
        document.Paste(pasted, EditorSelection.Caret("a", 1), BlockIds.Sequential());
        Assert.Equal("p#a A\nbullet#n1 X\n  bullet#n3 Y\np#n5 W", Outline(document));

        document.Update("n1", block => block.Text = new RichText("changed"));
        document.Update("n3", block => block.Text = new RichText("changed"));
        document.Remove(["n5"], BlockIds.Sequential("m"));

        // The blocks handed in can be pasted again, as they were.
        Assert.Equal("bullet#x X\n  bullet#y Y\np#w W", Outline(Doc(pasted)));
    }

    [Fact]
    public void TheCaretGoesToTheEndOfWhatWasPastedHoweverItIsTidied()
    {
        // A paragraph cannot hold a block, so tidying moves the inner one
        // out. The caret still goes to the end of the last block pasted.
        var holder = new Block("x", BlockKind.Paragraph, new RichText("X"), [P("y", "Yy")]);
        var result = Edit(
            Doc(P("a", "A")), (document, ids) => document.Paste([Divider("d"), holder], EditorSelection.Caret("a", 1), ids));
        Assert.Equal("p#a A\ndivider#n1\np#n2 X\np#n3 Yy", result.Outline);
        Assert.Equal("n3|2", result.Selection);
        Assert.Equal("Yy", Assert.Single(holder.Children).Text.String);
    }

    [Fact]
    public void ASnapshotIsNotTouchedByLaterEdits()
    {
        var document = Doc(
            P("a", "Hello world"), Bullet("b", "B", Bullet("c", "C")), Quote("q", P("x", "X")),
            Columns("r", Column("c1", P("l", "L")), Column("c2", P("m", "M"))));
        var snapshot = document.Clone();
        var outline = Outline(snapshot);
        var json = snapshot.Json;
        var ids = BlockIds.Sequential();

        document.Split("a", 5, 5, ids);
        document.Indent("c");
        document.Outdent("c");
        document.Turn("b", BlockTarget.Heading(2), ids);
        document.Turn("q", BlockTarget.Callout, ids);
        document.DeleteBackward("x", ids);
        document.DeleteForward("a");
        document.Move(["l"], DropTarget.After("a"), ids);
        document.Duplicate("c", ids);
        document.Paste([P("p", "P"), Divider("d")], EditorSelection.Caret("c", 1), ids);
        document.Remove(["m"], ids);
        document.ApplyBlockShortcut("x", 0, ids);

        Assert.NotEqual(outline, Outline(document));
        Assert.Equal(outline, Outline(snapshot));
        Assert.Equal(json, snapshot.Json);
    }
}
