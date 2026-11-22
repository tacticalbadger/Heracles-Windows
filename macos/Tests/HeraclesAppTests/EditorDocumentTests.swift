import Foundation
import Testing

@testable import HeraclesApp

/// Run an edit on a document. Gives back the document as an outline and
/// where the edit left the selection. New blocks get the ids n1, n2, ...
private func edit(
    _ document: EditorDocument, _ change: (inout EditorDocument, inout BlockIDs) -> EditorSelection?
) -> (outline: String, selection: String) {
    var document = document
    var ids = BlockIDs.sequential()
    let selection = change(&document, &ids)
    return (outline(document), selection.map(shape) ?? "nil")
}

struct SplitTests {
    @Test func splitsAParagraphAtTheCaret() {
        let result = edit(doc(p("a", "Hello world"))) { $0.split("a", at: 5..<5, ids: &$1) }
        #expect(result.outline == "p#a Hello\np#n1  world")
        #expect(result.selection == "n1|0")
    }

    @Test func aSelectionIsReplacedByTheSplit() {
        let result = edit(doc(p("a", "Hello world"))) { $0.split("a", at: 5..<11, ids: &$1) }
        #expect(result.outline == "p#a Hello\np#n1")
        #expect(result.selection == "n1|0")
    }

    @Test func atTheStartTheTextKeepsItsBlockAndAnEmptyOneGoesAbove() {
        // A link to the block should still land on its text.
        let result = edit(doc(p("a", "Hello"))) { $0.split("a", at: 0..<0, ids: &$1) }
        #expect(result.outline == "p#n1\np#a Hello")
        #expect(result.selection == "a|0")
    }

    @Test func theSecondHalfKeepsItsMarks() {
        var document = doc(p("a", "Hello world"))
        document.blocks[0].text.set(.bold, true, in: 6..<11)
        var ids = BlockIDs.sequential()
        _ = document.split("a", at: 8..<8, ids: &ids)
        #expect(shape(document.blocks[0].text) == "[Hello ][b:wo]")
        #expect(shape(document.blocks[1].text) == "[b:rld]")
    }

    @Test func afterAHeadingComesAParagraph() {
        let result = edit(doc(h(2, "a", "Title"))) { $0.split("a", at: 5..<5, ids: &$1) }
        #expect(result.outline == "h2#a Title\np#n1")
        #expect(result.selection == "n1|0")
    }

    @Test func aHeadingSplitInTheMiddleIsTwoHeadings() {
        let result = edit(doc(h(2, "a", "Title"))) { $0.split("a", at: 2..<2, ids: &$1) }
        #expect(result.outline == "h2#a Ti\nh2#n1 tle")
    }

    @Test func aListItemSplitsIntoTwoItemsAndTheNewOneTakesTheChildren() {
        var document = doc(bullet("a", "Hello", [bullet("c", "child")]))
        var ids = BlockIDs.sequential()
        let selection = document.split("a", at: 2..<2, ids: &ids)
        #expect(outline(document) == "bullet#a He\nbullet#n1 llo\n  bullet#c child")
        #expect(shape(selection) == "n1|0")
        #expect(document.blocks[1].paragraphID == "n2")
    }

    @Test func aNewTaskStartsUnchecked() {
        let result = edit(doc(todo("a", "Done", checked: true))) { $0.split("a", at: 4..<4, ids: &$1) }
        #expect(result.outline == "done#a Done\ntodo#n1")
    }

    @Test func anEmptyListItemAtTheTopBecomesAParagraph() {
        let result = edit(doc(bullet("a", "A"), bullet("b"))) { $0.split("b", at: 0..<0, ids: &$1) }
        #expect(result.outline == "bullet#a A\np#b")
        #expect(result.selection == "b|0")
    }

    @Test func anEmptyNestedListItemMovesOutALevel() {
        let result = edit(doc(bullet("a", "A", [bullet("b")]))) { $0.split("b", at: 0..<0, ids: &$1) }
        #expect(result.outline == "bullet#a A\nbullet#b")
        #expect(result.selection == "b|0")
    }

    @Test func anEmptyParagraphAtTheEndOfAQuoteLeavesIt() {
        let result = edit(doc(quote("q", [p("x", "Quoted"), p("y")]))) { $0.split("y", at: 0..<0, ids: &$1) }
        #expect(result.outline == "quote#q\n  p#x Quoted\np#y")
        #expect(result.selection == "y|0")
    }

    @Test func leavingAQuoteThatHoldsNothingElseRemovesIt() {
        let result = edit(doc(quote("q", [p("x")]))) { $0.split("x", at: 0..<0, ids: &$1) }
        #expect(result.outline == "p#x")
    }

    @Test func anEmptyParagraphAtTheEndOfAListItemLeavesTheItem() {
        let result = edit(doc(bullet("a", "A", [p("x")]))) { $0.split("x", at: 0..<0, ids: &$1) }
        #expect(result.outline == "bullet#a A\np#x")
    }

    @Test func aParagraphInsideAQuoteSplitsInsideIt() {
        let result = edit(doc(quote("q", [p("x", "Quoted")]))) { $0.split("x", at: 6..<6, ids: &$1) }
        #expect(result.outline == "quote#q\n  p#x Quoted\n  p#n1")
    }
}

struct DeleteBackwardTests {
    @Test func aHeadingBecomesAParagraph() {
        let result = edit(doc(h(1, "a", "Title"))) { $0.deleteBackward(atStartOf: "a", ids: &$1) }
        #expect(result.outline == "p#a Title")
        #expect(result.selection == "a|0")
    }

    @Test func aListItemBecomesAParagraphAndItsChildrenFollowIt() {
        let result = edit(doc(todo("a", "A", checked: true, [bullet("b", "B")]))) { $0.deleteBackward(atStartOf: "a", ids: &$1) }
        #expect(result.outline == "p#a A\nbullet#b B")
        #expect(result.selection == "a|0")
    }

    @Test func aNestedListItemBecomesAParagraphWhereItIs() {
        let result = edit(doc(bullet("a", "A", [bullet("b", "B")]))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "bullet#a A\n  p#b B")
    }

    @Test func aParagraphJoinsTheOneBefore() {
        var document = doc(p("a", "Hello"), p("b", "World"))
        document.blocks[1].text.set(.bold, true, in: 0..<5)
        var ids = BlockIDs.sequential()
        let selection = document.deleteBackward(atStartOf: "b", ids: &ids)
        #expect(outline(document) == "p#a HelloWorld")
        #expect(shape(document.blocks[0].text) == "[Hello][b:World]")
        #expect(selection.map(shape) == "a|5")
    }

    @Test func itJoinsTheLastThingShownAboveIt() {
        let result = edit(doc(bullet("a", "A", [bullet("c", "C")]), p("b", "tail"))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "bullet#a A\n  bullet#c Ctail")
        #expect(result.selection == "c|1")
    }

    @Test func aParagraphNestedInAnItemJoinsTheItemsText() {
        let result = edit(doc(bullet("a", "A", [p("b", "more")]))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "bullet#a Amore")
        #expect(result.selection == "a|1")
    }

    @Test func nothingHappensAtTheVeryTop() {
        let result = edit(doc(p("a", "Hello"))) { $0.deleteBackward(atStartOf: "a", ids: &$1) }
        #expect(result.outline == "p#a Hello")
        #expect(result.selection == "nil")
    }

    @Test func theFirstParagraphOfAQuoteLeavesIt() {
        let result = edit(doc(quote("q", [p("x", "One"), p("y", "Two")]))) { $0.deleteBackward(atStartOf: "x", ids: &$1) }
        #expect(result.outline == "p#x One\nquote#q\n  p#y Two")
        #expect(result.selection == "x|0")
    }

    @Test func aQuoteLeftEmptyIsRemoved() {
        let result = edit(doc(quote("q", [p("x", "One")]))) { $0.deleteBackward(atStartOf: "x", ids: &$1) }
        #expect(result.outline == "p#x One")
    }

    @Test func aBlockWithoutTextAboveIsSelectedInsteadOfJoined() {
        let result = edit(doc(divider("d"), p("b", "tail"))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "divider#d\np#b tail")
        #expect(result.selection == "[d]")
    }

    @Test func anEmptyParagraphBelowABlockWithoutTextGoesAway() {
        let result = edit(doc(image("i"), p("b"))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "image#i")
        #expect(result.selection == "[i]")
    }

    @Test func belowCodeTheCaretMovesIntoTheCode() {
        var result = edit(doc(code("c", "let x"), p("b"))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "code#c let x")
        #expect(result.selection == "c|5")
        // Text is never poured into code.
        result = edit(doc(code("c", "let x"), p("b", "tail"))) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "code#c let x\np#b tail")
        #expect(result.selection == "c|5")
    }

    @Test func anEmptyCodeBlockBecomesAParagraph() {
        var result = edit(doc(code("c"))) { $0.deleteBackward(atStartOf: "c", ids: &$1) }
        #expect(result.outline == "p#c")
        result = edit(doc(code("c", "x"))) { $0.deleteBackward(atStartOf: "c", ids: &$1) }
        #expect(result.outline == "code#c x")
        #expect(result.selection == "nil")
    }

    @Test func whatAClosedToggleHidesIsSkipped() {
        let document = doc(toggle("t", open: false, [p("s", "Sum"), p("h", "Hidden")]), p("b", "tail"))
        let result = edit(document) { $0.deleteBackward(atStartOf: "b", ids: &$1) }
        #expect(result.outline == "toggle(closed)#t\n  p#s Sumtail\n  p#h Hidden")
        #expect(result.selection == "s|3")
    }
}

struct DeleteForwardTests {
    @Test func theNextParagraphJoinsThisOne() {
        let result = edit(doc(p("a", "Hello"), p("b", "World"))) { document, _ in document.deleteForward(atEndOf: "a") }
        #expect(result.outline == "p#a HelloWorld")
        #expect(result.selection == "a|5")
    }

    @Test func theChildrenOfAJoinedItemStayWhereItWas() {
        let result = edit(doc(p("a", "A"), bullet("b", "B", [bullet("c", "C")]))) { document, _ in document.deleteForward(atEndOf: "a") }
        #expect(result.outline == "p#a AB\nbullet#c C")
    }

    @Test func anItemCanTakeInItsOwnFirstChild() {
        let result = edit(doc(bullet("a", "A", [bullet("b", "B", [bullet("c", "C")]), bullet("d", "D")]))) { document, _ in
            document.deleteForward(atEndOf: "a")
        }
        #expect(result.outline == "bullet#a AB\n  bullet#c C\n  bullet#d D")
    }

    @Test func aBlockWithoutTextBelowIsSelected() {
        let result = edit(doc(p("a", "A"), divider("d"))) { document, _ in document.deleteForward(atEndOf: "a") }
        #expect(result.outline == "p#a A\ndivider#d")
        #expect(result.selection == "[d]")
    }

    @Test func nothingHappensAtTheVeryEndOrBeforeCode() {
        var result = edit(doc(p("a", "A"))) { document, _ in document.deleteForward(atEndOf: "a") }
        #expect(result.selection == "nil")
        result = edit(doc(p("a", "A"), code("c", "x"))) { document, _ in document.deleteForward(atEndOf: "a") }
        #expect(result.outline == "p#a A\ncode#c x")
        #expect(result.selection == "nil")
    }
}

struct IndentTests {
    @Test func aListItemMovesUnderTheItemBeforeIt() {
        var document = doc(bullet("a", "A"), bullet("b", "B", [bullet("c", "C")]))
        do { let done = document.indent("b"); #expect(done) }
        #expect(outline(document) == "bullet#a A\n  bullet#b B\n    bullet#c C")
    }

    @Test func itGoesAfterTheChildrenAlreadyThere() {
        var document = doc(bullet("a", "A", [bullet("x", "X")]), todo("b", "B"))
        do { let done = document.indent("b"); #expect(done) }
        #expect(outline(document) == "bullet#a A\n  bullet#x X\n  todo#b B")
    }

    @Test func nothingToIndentUnder() {
        var document = doc(bullet("a", "A"), p("b", "B"), bullet("c", "C"))
        do { let done = document.indent("a"); #expect(!done) }
        do { let done = document.indent("b"); #expect(!done) }
        do { let done = document.indent("c"); #expect(!done) }
        #expect(outline(document) == "bullet#a A\np#b B\nbullet#c C")
    }

    @Test func outdentingTakesTheItemsBelowItAlong() {
        var document = doc(bullet("a", "A", [bullet("b", "B"), bullet("c", "C")]))
        do { let done = document.outdent("b"); #expect(done) }
        #expect(outline(document) == "bullet#a A\nbullet#b B\n  bullet#c C")
    }

    @Test func outdentingKeepsTheItemsOwnChildrenFirst() {
        var document = doc(bullet("a", "A", [bullet("b", "B", [bullet("x", "X")]), bullet("c", "C")]))
        do { let done = document.outdent("b"); #expect(done) }
        #expect(outline(document) == "bullet#a A\nbullet#b B\n  bullet#x X\n  bullet#c C")
    }

    @Test func nothingToOutdentFrom() {
        var document = doc(bullet("a", "A", [p("x", "more"), bullet("c", "C")]), quote("q", [bullet("d", "D")]))
        do { let done = document.outdent("a"); #expect(!done) }
        do { let done = document.outdent("d"); #expect(!done) }
        // A paragraph cannot take the blocks below it along, so it only
        // moves out from the end.
        do { let done = document.outdent("x"); #expect(!done) }
        #expect(outline(document) == "bullet#a A\n  p#x more\n  bullet#c C\nquote#q\n  bullet#d D")
    }

    @Test func aParagraphAtTheEndOfAnItemMovesOut() {
        var document = doc(bullet("a", "A", [p("x", "more")]))
        do { let done = document.outdent("x"); #expect(done) }
        #expect(outline(document) == "bullet#a A\np#x more")
    }
}

struct TurnIntoTests {
    @Test func textBlocksChangeKindAndKeepTheirIdAndText() {
        var result = edit(doc(p("a", "Text"))) { $0.turn("a", into: .heading(2), ids: &$1) }
        #expect(result.outline == "h2#a Text")
        #expect(result.selection == "a|0")
        result = edit(doc(h(3, "a", "Text"))) { $0.turn("a", into: .todo, ids: &$1) }
        #expect(result.outline == "todo#a Text")
        result = edit(doc(todo("a", "Text", checked: true))) { $0.turn("a", into: .bullet, ids: &$1) }
        #expect(result.outline == "bullet#a Text")
    }

    @Test func aNewListItemGetsAnIdForItsParagraph() {
        var document = doc(p("a", "Text"))
        var ids = BlockIDs.sequential()
        _ = document.turn("a", into: .numbered, ids: &ids)
        #expect(document.blocks[0].paragraphID == "n1")
        #expect(document.blocks[0].attrs.isEmpty)
        _ = document.turn("a", into: .heading(1), ids: &ids)
        #expect(document.blocks[0].paragraphID == nil)
        #expect(document.blocks[0].attrs == ["level": .number(1)])
    }

    @Test func childrenOfAnItemThatStopsBeingOneFollowIt() {
        let result = edit(doc(bullet("a", "A", [bullet("b", "B")]), p("z", "Z"))) { $0.turn("a", into: .paragraph, ids: &$1) }
        #expect(result.outline == "p#a A\nbullet#b B\np#z Z")
    }

    @Test func wrapsABlockInAQuoteCalloutOrToggle() {
        var result = edit(doc(p("a", "Text"))) { $0.turn("a", into: .quote, ids: &$1) }
        #expect(result.outline == "quote#n1\n  p#a Text")
        #expect(result.selection == "a|0")
        result = edit(doc(bullet("a", "Text"))) { $0.turn("a", into: .callout, ids: &$1) }
        #expect(result.outline == "callout#n1\n  bullet#a Text")
        result = edit(doc(p("a", "Text"))) { $0.turn("a", into: .toggle, ids: &$1) }
        #expect(result.outline == "toggle#n1\n  p#a Text")
    }

    @Test func aContainerChangesKindOrLetsItsBlocksOut() {
        var result = edit(doc(quote("q", [p("a", "A"), p("b", "B")]))) { $0.turn("q", into: .callout, ids: &$1) }
        #expect(result.outline == "callout#q\n  p#a A\n  p#b B")
        result = edit(doc(quote("q", [p("a", "A"), p("b", "B")]), p("z"))) { $0.turn("q", into: .paragraph, ids: &$1) }
        #expect(result.outline == "p#a A\np#b B\np#z")
        #expect(result.selection == "a|0")
    }

    @Test func turnsTextIntoCodeAndBack() {
        var document = doc(p("a", "let x"))
        document.blocks[0].text.set(.bold, true, in: 0..<3)
        var ids = BlockIDs.sequential()
        _ = document.turn("a", into: .code, ids: &ids)
        #expect(outline(document) == "code#a let x")
        #expect(shape(document.blocks[0].text) == "[let x]")
        #expect(document.blocks[0].language == "plaintext")
        _ = document.turn("a", into: .paragraph, ids: &ids)
        #expect(outline(document) == "p#a let x")
        #expect(document.blocks[0].attrs.isEmpty)
    }

    @Test func aDividerTakesTheBlocksPlaceAndAParagraphFollows() {
        var result = edit(doc(p("a"))) { $0.turn("a", into: .divider, ids: &$1) }
        #expect(result.outline == "divider#a\np#n1")
        #expect(result.selection == "n1|0")
        // Text is not thrown away: the divider goes below it.
        result = edit(doc(p("a", "Text"))) { $0.turn("a", into: .divider, ids: &$1) }
        #expect(result.outline == "p#a Text\ndivider#n1\np#n2")
        #expect(result.selection == "n2|0")
    }
}

struct RemoveTests {
    @Test func theCaretGoesToTheEndOfTheBlockAbove() {
        let result = edit(doc(p("a", "Hello"), p("b", "B"), p("c", "C"))) { $0.remove(["b"], ids: &$1) }
        #expect(result.outline == "p#a Hello\np#c C")
        #expect(result.selection == "a|5")
    }

    @Test func orToTheStartOfTheBlockBelowWhenNothingIsAbove() {
        let result = edit(doc(p("a", "A"), p("b", "B"))) { $0.remove(["a"], ids: &$1) }
        #expect(result.outline == "p#b B")
        #expect(result.selection == "b|0")
    }

    @Test func aNoteIsNeverLeftWithNoBlocks() {
        let result = edit(doc(p("a", "A"), divider("d"))) { $0.remove(["a", "d"], ids: &$1) }
        #expect(result.outline == "p#n1")
        #expect(result.selection == "n1|0")
    }

    @Test func removingABlockRemovesWhatIsInsideIt() {
        let result = edit(doc(p("z", "Z"), bullet("a", "A", [bullet("b", "B")]), quote("q", [p("x", "X")]))) {
            $0.remove(["a", "x"], ids: &$1)
        }
        #expect(result.outline == "p#z Z")
        #expect(result.selection == "z|1")
    }

    @Test func aBlockWithoutTextAboveIsSelected() {
        let result = edit(doc(divider("d"), p("b", "B"))) { $0.remove(["b"], ids: &$1) }
        #expect(result.outline == "divider#d")
        #expect(result.selection == "[d]")
    }

    @Test func aRowLeftWithOneColumnStopsBeingARow() {
        let document = doc(columns("r", [column("c1", [p("a", "A")]), column("c2", [p("b", "B")])]), p("z"))
        let result = edit(document) { $0.remove(["b"], ids: &$1) }
        #expect(result.outline == "p#a A\np#z")
        #expect(result.selection == "a|1")
    }
}

struct DuplicateTests {
    @Test func theCopyFollowsTheOriginalWithIdsOfItsOwn() {
        var document = doc(bullet("a", "A", [bullet("b", "B")]), p("z"))
        document.blocks[0].startsList = true
        var ids = BlockIDs.sequential()
        let copy = document.duplicate("a", ids: &ids)
        #expect(copy == "n1")
        #expect(outline(document) == "^bullet#a A\n  bullet#b B\nbullet#n1 A\n  bullet#n3 B\np#z")
        #expect(document.blocks[1].paragraphID == "n2")
        #expect(document.blocks[1].children[0].paragraphID == "n4")
        // The original is untouched, ids and all.
        #expect(document.blocks[0].paragraphID == "a-p")
    }

    @Test func thereIsNothingToCopyWithoutTheBlock() {
        var document = doc(p("a"))
        var ids = BlockIDs.sequential()
        let copy = document.duplicate("missing", ids: &ids)
        #expect(copy == nil)
    }
}

struct MoveTests {
    @Test func movesABlockBeforeOrAfterAnother() {
        var result = edit(doc(p("a", "A"), p("b", "B"), p("c", "C"))) { document, ids in
            _ = document.move(["c"], to: .before("a"), ids: &ids)
            return nil
        }
        #expect(result.outline == "p#c C\np#a A\np#b B")
        result = edit(doc(p("a", "A"), p("b", "B"), p("c", "C"))) { document, ids in
            _ = document.move(["a"], to: .after("b"), ids: &ids)
            return nil
        }
        #expect(result.outline == "p#b B\np#a A\np#c C")
    }

    @Test func aBlockMovesWithEverythingInsideIt() {
        var document = doc(bullet("a", "A", [bullet("b", "B")]), bullet("c", "C", [bullet("d", "D")]))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["a"], to: .after("d"), ids: &ids); #expect(done) }
        #expect(outline(document) == "bullet#c C\n  bullet#d D\n  bullet#a A\n    bullet#b B")
    }

    @Test func severalBlocksKeepTheirOrder() {
        var document = doc(p("a", "A"), p("b", "B"), p("c", "C"), p("d", "D"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["c", "d"], to: .before("a"), ids: &ids); #expect(done) }
        #expect(outline(document) == "p#c C\np#d D\np#a A\np#b B")
    }

    @Test func aBlockCannotMoveIntoItselfOrNextToItself() {
        var document = doc(bullet("a", "A", [bullet("b", "B")]), p("z"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["a"], to: .after("b"), ids: &ids); #expect(!done) }
        do { let done = document.move(["a"], to: .before("a"), ids: &ids); #expect(!done) }
        do { let done = document.move(["missing"], to: .before("a"), ids: &ids); #expect(!done) }
        do { let done = document.move(["a"], to: .before("missing"), ids: &ids); #expect(!done) }
        #expect(outline(document) == "bullet#a A\n  bullet#b B\np#z")
    }

    @Test func aMovedItemJoinsTheListItLandsIn() {
        var document = doc(bullet("a", "A"), p("x"), bullet("b", "B"), bullet("c", "C"))
        document.blocks[3].startsList = true
        var ids = BlockIDs.sequential()
        do { let done = document.move(["c"], to: .after("a"), ids: &ids); #expect(done) }
        #expect(outline(document) == "bullet#a A\nbullet#c C\np#x\nbullet#b B")
    }

    @Test func droppingBesideABlockMakesTwoColumns() {
        var document = doc(p("a", "A"), p("b", "B"), p("z"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["b"], to: .rightOf("a"), ids: &ids); #expect(done) }
        #expect(outline(document) == "columns#n1\n  column#n2\n    p#a A\n  column#n3\n    p#b B\np#z")

        document = doc(p("a", "A"), p("b", "B"))
        ids = BlockIDs.sequential()
        do { let done = document.move(["b"], to: .leftOf("a"), ids: &ids); #expect(done) }
        #expect(outline(document) == "columns#n1\n  column#n2\n    p#b B\n  column#n3\n    p#a A")
    }

    @Test func droppingBesideAColumnAddsAColumnToTheRow() {
        var document = doc(columns("r", [column("c1", [p("a", "A")]), column("c2", [p("b", "B")])]), p("c", "C"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["c"], to: .rightOf("a"), ids: &ids); #expect(done) }
        #expect(outline(document) == "columns#r\n  column#c1\n    p#a A\n  column#n1\n    p#c C\n  column#c2\n    p#b B")
    }

    @Test func takingTheLastBlockOutOfAColumnRemovesTheColumn() {
        var document = doc(
            columns("r", [column("c1", [p("a", "A")]), column("c2", [p("b", "B")]), column("c3", [p("c", "C")])]), p("z"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["b"], to: .after("z"), ids: &ids); #expect(done) }
        #expect(outline(document) == "columns#r\n  column#c1\n    p#a A\n  column#c3\n    p#c C\np#z\np#b B")
        // And a row with one column left is no longer a row.
        do { let done = document.move(["c"], to: .after("z"), ids: &ids); #expect(done) }
        #expect(outline(document) == "p#a A\np#z\np#c C\np#b B")
    }

    @Test func onlyBlocksAtTheTopOrInAColumnCanHaveColumnsBesideThem() {
        var document = doc(quote("q", [p("a", "A")]), p("b", "B"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["b"], to: .rightOf("a"), ids: &ids); #expect(done) }
        #expect(outline(document) == "quote#q\n  p#a A\n  p#b B")
    }

    @Test func aRowCannotGoInsideAColumn() {
        var document = doc(columns("r", [column("c1", [p("a", "A")]), column("c2", [p("b", "B")])]), p("z", "Z"))
        var ids = BlockIDs.sequential()
        do { let done = document.move(["r"], to: .after("z"), ids: &ids); #expect(done) }
        #expect(outline(document) == "p#z Z\ncolumns#r\n  column#c1\n    p#a A\n  column#c2\n    p#b B")
        // Its own columns are inside it, and another row's would nest rows.
        document = doc(
            columns("r", [column("c1", [p("a", "A")]), column("c2", [p("b", "B")])]),
            columns("s", [column("d1", [p("c", "C")]), column("d2", [p("d", "D")])]))
        do { let done = document.move(["r"], to: .after("c"), ids: &ids); #expect(!done) }
        do { let done = document.move(["r"], to: .rightOf("c"), ids: &ids); #expect(!done) }
    }
}

struct NormalizeTests {
    @Test func tidiesWhatAnEditCanLeaveBehind() {
        var document = doc(
            quote("q", []), columns("r", [column("c1", []), column("c2", [p("b", "B")])]), callout("k", [toggle("t", [])]))
        var ids = BlockIDs.sequential()
        document.normalize(ids: &ids)
        #expect(outline(document) == "p#b B")
    }

    @Test func aBlockThatCannotHoldBlocksLetsThemOut() {
        var document = doc(Block(id: "a", kind: .paragraph, text: RichText("A"), children: [p("b", "B")]))
        var ids = BlockIDs.sequential()
        document.normalize(ids: &ids)
        #expect(outline(document) == "p#a A\np#b B")
    }

    @Test func anEmptyDocumentGetsAParagraph() {
        var document = doc()
        var ids = BlockIDs.sequential()
        document.normalize(ids: &ids)
        #expect(outline(document) == "p#n1")
    }
}

struct BlockShortcutTests {
    private func typed(_ text: String, caret: Int? = nil, in block: Block? = nil) -> (outline: String, selection: String) {
        var block = block ?? p("a")
        block.text = RichText(text)
        return edit(doc(block)) { $0.applyBlockShortcut(in: "a", caret: caret ?? (text as NSString).length, ids: &$1) }
    }

    @Test func hashesMakeAHeading() {
        #expect(typed("# ").outline == "h1#a")
        #expect(typed("### ").outline == "h3#a")
        #expect(typed("###### ").outline == "h6#a")
        #expect(typed("####### ").selection == "nil")
        #expect(typed("# ").selection == "a|0")
    }

    @Test func theRestOfTheLineIsKept() {
        let result = typed("## Existing text", caret: 3)
        #expect(result.outline == "h2#a Existing text")
        #expect(result.selection == "a|0")
    }

    @Test func aDashStarOrPlusMakesABullet() {
        #expect(typed("- ").outline == "bullet#a")
        #expect(typed("* ").outline == "bullet#a")
        #expect(typed("+ ").outline == "bullet#a")
    }

    @Test func aNumberMakesANumberedItemThatStartsThere() {
        var document = doc(p("a", "1. "))
        var ids = BlockIDs.sequential()
        _ = document.applyBlockShortcut(in: "a", caret: 3, ids: &ids)
        #expect(outline(document) == "numbered#a")
        #expect(document.blocks[0].listAttrs.isEmpty)

        document = doc(p("a", "7) "))
        _ = document.applyBlockShortcut(in: "a", caret: 3, ids: &ids)
        #expect(outline(document) == "numbered#a")
        #expect(document.blocks[0].listAttrs == ["start": .number(7)])
    }

    @Test func bracketsMakeATask() {
        #expect(typed("[] ").outline == "todo#a")
        #expect(typed("[ ] ").outline == "todo#a")
        #expect(typed("[x] ").outline == "done#a")
    }

    @Test func anAngleBracketMakesAQuote() {
        let result = typed("> ")
        #expect(result.outline == "quote#n1\n  p#a")
        #expect(result.selection == "a|0")
    }

    @Test func backticksMakeACodeBlock() {
        #expect(typed("``` ").outline == "code#a")
        var document = doc(p("a", "```swift "))
        var ids = BlockIDs.sequential()
        _ = document.applyBlockShortcut(in: "a", caret: 9, ids: &ids)
        #expect(outline(document) == "code#a")
        #expect(document.blocks[0].language == "swift")
    }

    @Test func threeDashesMakeADivider() {
        var result = typed("---")
        #expect(result.outline == "divider#a\np#n1")
        #expect(result.selection == "n1|0")
        // macOS turns two dashes into a long one as they are typed.
        result = typed("—-")
        #expect(result.outline == "divider#a\np#n1")
    }

    @Test func nothingHappensAwayFromTheStartOrInOtherBlocks() {
        #expect(typed("# title", caret: 7).selection == "nil")
        #expect(typed("a # ").selection == "nil")
        #expect(typed("#").selection == "nil")
        #expect(typed("# ", in: h(2, "a")).selection == "nil")
        #expect(typed("- ", in: code("a")).selection == "nil")
    }
}

struct InlineShortcutTests {
    private func typed(_ text: String) -> String {
        guard let result = RichText(text).applyingInlineShortcut(caret: (text as NSString).length) else { return "nil" }
        return "\(shape(result.text)) \(result.caret)"
    }

    @Test func starsAndUnderscoresMakeBoldAndItalic() {
        #expect(typed("a **bold**") == "[a ][b:bold] 6")
        #expect(typed("__bold__") == "[b:bold] 4")
        #expect(typed("an *italic*") == "[an ][i:italic] 9")
        #expect(typed("_it_") == "[i:it] 2")
    }

    @Test func backticksMakeCodeAndTildesStrike() {
        #expect(typed("run `ls -la`") == "[run ][c:ls -la] 10")
        #expect(typed("~~gone~~") == "[s:gone] 4")
    }

    @Test func anUnfinishedOrUnlikelyPairIsLeftAlone() {
        #expect(typed("**bold*") == "nil")
        #expect(typed("2 * 3 *") == "nil")
        #expect(typed("snake_case_") == "nil")
        #expect(typed("**") == "nil")
        #expect(typed("* *") == "nil")
        #expect(typed("plain") == "nil")
    }

    @Test func onlyThePairThatEndsAtTheCaretCounts() {
        let text = RichText("*a* b")
        #expect(text.applyingInlineShortcut(caret: 5) == nil)
        let result = text.applyingInlineShortcut(caret: 3)
        #expect(result.map { shape($0.text) } == "[i:a][ b]")
        #expect(result?.caret == 1)
    }

    @Test func marksAlreadyOnTheTextAreKept() {
        var text = RichText("**go there**")
        text.setLink(TextLink(href: "https://example.com"), in: 5..<10)
        let result = text.applyingInlineShortcut(caret: 12)
        #expect(result.map { shape($0.text) } == "[b:go ][b<https://example.com>:there]")
    }

    @Test func nothingIsReadInsideCode() {
        var text = RichText("x *a*")
        text.set(.code, true, in: 0..<5)
        #expect(text.applyingInlineShortcut(caret: 5) == nil)
    }
}

struct AutolinkTests {
    private func typed(_ text: String) -> String {
        RichText(text).autolinked(caret: (text as NSString).length).map(shape) ?? "nil"
    }

    @Test func anAddressBecomesALinkWhenTheSpaceAfterItIsTyped() {
        #expect(typed("see https://example.com/a?b=1 ") == "[see ][<https://example.com/a?b=1>:https://example.com/a?b=1][ ]")
        #expect(typed("http://example.com ") == "[<http://example.com>:http://example.com][ ]")
    }

    @Test func theFullStopAfterAnAddressIsNotPartOfIt() {
        #expect(typed("at https://example.com. ") == "[at ][<https://example.com>:https://example.com][. ]")
    }

    @Test func otherWordsAreLeftAlone() {
        #expect(typed("example.com ") == "nil")
        #expect(typed("https:// ") == "nil")
        #expect(typed("ftp://example.com ") == "nil")
        #expect(typed("javascript:alert(1) ") == "nil")
        #expect(typed("https://example.com") == "nil")
        #expect(typed(" ") == "nil")
    }

    @Test func aLinkOrCodeIsNotLinkedAgain() {
        var text = RichText("https://example.com ")
        text.setLink(TextLink(href: "https://other.example"), in: 0..<19)
        #expect(text.autolinked(caret: 20) == nil)
        text = RichText("https://example.com ")
        text.set(.code, true, in: 0..<19)
        #expect(text.autolinked(caret: 20) == nil)
    }

    @Test func tellsAWebAddressFromOtherText() {
        #expect(RichText.isWebAddress("https://example.com/path"))
        #expect(!RichText.isWebAddress("https://example.com and more"))
        #expect(!RichText.isWebAddress("mailto:a@b.example"))
        #expect(!RichText.isWebAddress("not an address"))
    }
}

struct PasteTests {
    @Test func oneParagraphGoesIntoTheLineBeingTyped() {
        var pasted = p("x", "new")
        pasted.text.set(.bold, true, in: 0..<3)
        var document = doc(p("a", "Hello world"))
        var ids = BlockIDs.sequential()
        let selection = document.paste([pasted], at: .text(block: "a", range: 6..<11), ids: &ids)
        #expect(shape(document.blocks[0].text) == "[Hello ][b:new]")
        #expect(shape(selection) == "a|9")
    }

    @Test func severalBlocksGoBelowTheBlockWithIdsOfTheirOwn() {
        let result = edit(doc(p("a", "Hello"), p("z"))) {
            $0.paste([h(1, "a", "Title"), bullet("b", "B", [bullet("c", "C")])], at: .caret("a", 2), ids: &$1)
        }
        #expect(result.outline == "p#a Hello\nh1#n1 Title\nbullet#n2 B\n  bullet#n4 C\np#z")
        #expect(result.selection == "n4|1")
    }

    @Test func anEmptyParagraphIsReplacedByWhatIsPasted() {
        let result = edit(doc(p("a"))) { $0.paste([h(2, "x", "Title")], at: .caret("a", 0), ids: &$1) }
        #expect(result.outline == "h2#n1 Title")
        #expect(result.selection == "n1|5")
    }

    @Test func selectedBlocksAreReplaced() {
        let result = edit(doc(p("a", "A"), p("b", "B"), p("c", "C"))) {
            $0.paste([p("x", "X"), p("y", "Y")], at: .blocks(["a", "b"]), ids: &$1)
        }
        #expect(result.outline == "p#n1 X\np#n2 Y\np#c C")
        #expect(result.selection == "n2|1")
    }

    @Test func intoCodeOnlyTheWordsGo() {
        var document = doc(code("c", "let x"))
        var ids = BlockIDs.sequential()
        let selection = document.paste([p("x", " = 1"), bullet("y", "two")], at: .caret("c", 5), ids: &ids)
        #expect(document.blocks[0].text.string == "let x = 1\ntwo")
        #expect(shape(selection) == "c|13")
    }

    @Test func aBlockWithoutTextCanBePastedToo() {
        let result = edit(doc(p("a", "A"))) { $0.paste([divider("d")], at: .caret("a", 1), ids: &$1) }
        #expect(result.outline == "p#a A\ndivider#n1")
        #expect(result.selection == "[n1]")
    }
}

struct BlockSelectionTests {
    private let document = doc(
        p("a", "A"), bullet("b", "B", [bullet("c", "C"), bullet("d", "D")]), quote("q", [p("e", "E")]), p("f", "F"))

    @Test func selectsEveryBlockBetweenTwo() {
        #expect(document.blocks(from: "a", to: "f") == ["a", "b", "q", "f"])
        #expect(document.blocks(from: "f", to: "a") == ["a", "b", "q", "f"])
        #expect(document.blocks(from: "a", to: "a") == ["a"])
    }

    @Test func aBlockStandsForEverythingInsideIt() {
        #expect(document.blocks(from: "b", to: "d") == ["b"])
        #expect(document.blocks(from: "c", to: "e") == ["c", "d", "q"])
        #expect(document.blocks(from: "d", to: "f") == ["d", "q", "f"])
    }

    @Test func copiesBlocksAsText() {
        let document = doc(
            h(2, "h", "Title"), p("p", "Para"), bullet("b", "B", [todo("t", "T", checked: true)]), numbered("n", "One"),
            numbered("m", "Two"), divider("d"), code("c", "x = 1", language: "python"), quote("q", [p("e", "E")]))
        #expect(document.plainText(of: ["h", "p", "b", "n", "m", "d", "c", "q"]) == """
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
            """)
    }
}
