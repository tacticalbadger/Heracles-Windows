import AppKit
import Foundation
import Testing

@testable import HeraclesApp

/// Stands in for the app: records what the editor asks of it.
@MainActor
final class TestHost: BlockEditorHost {
    var changes = 0
    var opened: [String] = []
    var stored: [(data: Data, mime: String)] = []
    var copiedLinks: [String] = []
    var html: [String] = []
    /// What `documentFromHTML` answers.
    var converted: String?

    func editorDidChange(_ editor: BlockEditorController) { changes += 1 }
    func editorDidFocus(_ editor: BlockEditorController) {}
    func editor(_ editor: BlockEditorController, openLink href: String) { opened.append(href) }

    func editor(_ editor: BlockEditorController, storeImage data: Data, mime: String) -> String? {
        stored.append((data, mime))
        return "heracles-image://local/image\(stored.count)"
    }

    func editor(_ editor: BlockEditorController, loadImage source: String, completion: @escaping @MainActor (NSImage?) -> Void) {
        completion(nil)
    }

    func editor(_ editor: BlockEditorController, documentFromHTML html: String) -> String? {
        self.html.append(html)
        return converted
    }

    func editor(_ editor: BlockEditorController, copyLinkToBlock id: String) { copiedLinks.append(id) }
}

/// An editor in a window nobody sees, with the real text views in it.
@MainActor
final class EditorHarness {
    let editor = BlockEditorController(ids: .sequential())
    let host = TestHost()
    let window: NSWindow

    init(_ blocks: Block..., width: CGFloat = 800) {
        _ = NSApplication.shared
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: width, height: 600), styleMask: [.titled], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentView = editor.scrollView
        editor.host = host
        editor.load(json: EditorDocument(blocks: blocks).json)
        window.layoutIfNeeded()
        editor.page.layoutBlocks()
    }

    var outline: String { HeraclesAppTests.outline(editor.document) }
    var selection: String { shape(editor.selection) }

    func text(_ id: String) -> BlockTextView { editor.page.textView(for: id)! }

    /// The text view the keyboard is in.
    var focused: BlockTextView? { window.firstResponder as? BlockTextView }

    func caret(_ id: String, _ offset: Int) { editor.select(.caret(id, offset)) }

    /// Type, one key at a time, the way the keyboard does.
    func type(_ string: String) {
        for character in string {
            guard let view = focused else { return }
            view.insertText(String(character), replacementRange: view.selectedRange())
        }
    }

    /// Press a key that is a command: Return, Backspace, an arrow.
    func press(_ selector: Selector) {
        focused?.doCommand(by: selector)
    }

    func key(_ code: UInt16, shift: Bool = false) -> NSEvent {
        NSEvent.keyEvent(
            with: .keyDown, location: .zero, modifierFlags: shift ? [.shift] : [], timestamp: 0, windowNumber: window.windowNumber,
            context: nil, characters: "", charactersIgnoringModifiers: "", isARepeat: false, keyCode: code)!
    }
}

private let returnKey = #selector(NSResponder.insertNewline(_:))
private let backspace = #selector(NSResponder.deleteBackward(_:))

@MainActor
struct EditorTypingTests {
    @Test func theTextViewsAreTheSystemsNewTextEngine() {
        let harness = EditorHarness(p("a", "Hello"))
        #expect(harness.text("a").textLayoutManager != nil)
        #expect(harness.text("a").string == "Hello")
    }

    @Test func typingReachesTheDocumentAndTheHost() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.caret("a", 5)
        #expect(harness.focused === harness.text("a"))
        harness.type(" world")
        #expect(harness.outline == "p#a Hello world")
        #expect(harness.selection == "a|11")
        #expect(harness.host.changes == 6)
    }

    @Test func openingADocumentChangesNothing() {
        let harness = EditorHarness(p("a", "Hello"), bullet("b", "B"))
        harness.caret("a", 2)
        harness.caret("b", 1)
        #expect(harness.host.changes == 0)
        #expect(!harness.editor.canUndo)
    }

    @Test func returnSplitsTheBlockAndTheCaretFollows() {
        let harness = EditorHarness(p("a", "Hello world"))
        harness.caret("a", 5)
        harness.press(returnKey)
        #expect(harness.outline == "p#a Hello\np#n1  world")
        #expect(harness.selection == "n1|0")
        #expect(harness.focused === harness.text("n1"))
        #expect(harness.text("a").string == "Hello")
        #expect(harness.text("n1").string == " world")
        harness.type("!")
        #expect(harness.outline == "p#a Hello\np#n1 ! world")
    }

    @Test func backspaceAtTheStartJoinsTheBlockAbove() {
        let harness = EditorHarness(p("a", "Hello"), p("b", "World"))
        harness.caret("b", 0)
        harness.press(backspace)
        #expect(harness.outline == "p#a HelloWorld")
        #expect(harness.selection == "a|5")
        #expect(harness.text("a").string == "HelloWorld")
        #expect(harness.focused === harness.text("a"))
    }

    @Test func backspaceInsideTheTextIsTheTextViewsOwn() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.caret("a", 3)
        harness.press(backspace)
        #expect(harness.outline == "p#a Helo")
        #expect(harness.selection == "a|2")
    }

    @Test func forwardDeleteAtTheEndPullsTheNextBlockIn() {
        let harness = EditorHarness(p("a", "Hello"), p("b", "World"))
        harness.caret("a", 5)
        harness.press(#selector(NSResponder.deleteForward(_:)))
        #expect(harness.outline == "p#a HelloWorld")
        #expect(harness.selection == "a|5")
    }

    @Test func tabIndentsAListItemAndShiftTabBringsItBack() {
        let harness = EditorHarness(bullet("a", "A"), bullet("b", "B"))
        harness.caret("b", 1)
        harness.press(#selector(NSResponder.insertTab(_:)))
        #expect(harness.outline == "bullet#a A\n  bullet#b B")
        #expect(harness.selection == "b|1")
        #expect(harness.focused === harness.text("b"))
        harness.press(#selector(NSResponder.insertBacktab(_:)))
        #expect(harness.outline == "bullet#a A\nbullet#b B")
        // A tab is never typed into prose.
        harness.press(#selector(NSResponder.insertTab(_:)))
        harness.press(#selector(NSResponder.insertTab(_:)))
        #expect(harness.text("b").string == "B")
    }

    @Test func aLineBreakStaysInsideTheBlock() {
        let harness = EditorHarness(p("a", "one"))
        harness.caret("a", 3)
        harness.press(#selector(NSResponder.insertLineBreak(_:)))
        harness.type("two")
        #expect(harness.outline == "p#a one⏎two")
        #expect(sameJSON(harness.editor.json, """
            {"type":"doc","content":[{"type":"paragraph","attrs":{"blockId":"a"},"content":[
              {"type":"text","text":"one"},{"type":"hardBreak"},{"type":"text","text":"two"}]}]}
            """))
    }

    @Test func typingKeepsTheMarksOfTheTextItJoins() {
        var block = p("a", "bold")
        block.text.set(.bold, true, in: 0..<4)
        let harness = EditorHarness(block)
        harness.caret("a", 4)
        harness.type("er")
        #expect(shape(harness.editor.document.blocks[0].text) == "[b:bolder]")
    }

    @Test func typingAfterALinkIsNotPartOfIt() {
        var block = p("a", "site")
        block.text.setLink(TextLink(href: "https://example.com"), in: 0..<4)
        let harness = EditorHarness(block)
        harness.caret("a", 4)
        harness.type(" x")
        #expect(shape(harness.editor.document.blocks[0].text) == "[<https://example.com>:site][ x]")
        // Inside it, it is.
        harness.caret("a", 2)
        harness.type("-")
        #expect(shape(harness.editor.document.blocks[0].text) == "[<https://example.com>:si-te][ x]")
    }

    @Test func codeIsTypedAsWrittenAndKeepsItsIndent() {
        let harness = EditorHarness(code("c", "if x {"))
        harness.caret("c", 6)
        harness.press(returnKey)
        harness.press(#selector(NSResponder.insertTab(_:)))
        harness.type("y()")
        harness.press(returnKey)
        harness.type("z")
        #expect(harness.editor.document.blocks[0].text.string == "if x {\n  y()\n  z")
        #expect(harness.outline.hasPrefix("code#c"))
        // No shortcuts inside code.
        harness.type(" **b**")
        #expect(harness.editor.document.blocks[0].text.string.hasSuffix("**b**"))
    }

    @Test func aTrashedNoteCannotBeTypedIn() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.editor.isEditable = false
        harness.caret("a", 5)
        harness.type("!")
        harness.press(returnKey)
        harness.editor.toggle(.bold)
        #expect(harness.outline == "p#a Hello")
        #expect(harness.host.changes == 0)
    }
}

@MainActor
struct EditorShortcutTests {
    @Test func markdownAtTheStartOfALineMakesTheBlock() {
        let harness = EditorHarness(p("a"))
        harness.caret("a", 0)
        harness.type("# ")
        #expect(harness.outline == "h1#a")
        #expect(harness.text("a").string == "")
        harness.type("Title")
        harness.press(returnKey)
        harness.type("- one")
        harness.press(returnKey)
        harness.type("two")
        harness.press(returnKey)
        harness.press(returnKey)
        harness.type("[] task")
        #expect(harness.outline == "h1#a Title\nbullet#n1 one\nbullet#n3 two\ntodo#n5 task")
    }

    @Test func aPairOfStarsMakesBoldAsItIsClosed() {
        let harness = EditorHarness(p("a"))
        harness.caret("a", 0)
        harness.type("so **very** good")
        #expect(shape(harness.editor.document.blocks[0].text) == "[so ][b:very][ good]")
        #expect(harness.text("a").string == "so very good")
    }

    @Test func undoGivesBackWhatWasTyped() {
        let harness = EditorHarness(p("a"))
        harness.caret("a", 0)
        harness.type("# ")
        #expect(harness.outline == "h1#a")
        harness.editor.undo()
        #expect(harness.outline == "p#a # ")
        #expect(harness.selection == "a|2")
        harness.editor.undo()
        #expect(harness.outline == "p#a")
        harness.editor.redo()
        harness.editor.redo()
        #expect(harness.outline == "h1#a")
    }

    @Test func typingIsUndoneARunAtATime() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.caret("a", 5)
        harness.type(" world")
        harness.press(returnKey)
        harness.type("more")
        #expect(harness.outline == "p#a Hello world\np#n1 more")
        harness.editor.undo()
        #expect(harness.outline == "p#a Hello world\np#n1")
        harness.editor.undo()
        #expect(harness.outline == "p#a Hello world")
        #expect(harness.selection == "a|11")
        #expect(harness.text("a").string == "Hello world")
        harness.editor.undo()
        #expect(harness.outline == "p#a Hello")
        #expect(!harness.editor.canUndo)
        harness.editor.redo()
        #expect(harness.outline == "p#a Hello world")
        #expect(harness.text("a").string == "Hello world")
    }

    @Test func theSlashMenuTurnsTheBlockIntoWhatIsChosen() {
        let harness = EditorHarness(p("a"))
        harness.caret("a", 0)
        harness.type("/")
        #expect(harness.editor.slashCommands.count == SlashCommand.all.count)
        #expect(!harness.editor.page.slashMenu.isHidden)
        harness.type("hea")
        #expect(harness.editor.slashCommands.map(\.title) == ["Heading 1", "Heading 2", "Heading 3", "Heading 4"])
        harness.press(#selector(NSResponder.moveDown(_:)))
        harness.press(returnKey)
        #expect(harness.outline == "h2#a")
        #expect(harness.editor.slashCommands.isEmpty)
        #expect(harness.editor.page.slashMenu.isHidden)
        #expect(harness.selection == "a|0")
    }

    @Test func theSlashMenuOnlyOpensWhereAWordCouldStart() {
        let harness = EditorHarness(p("a", "1"))
        harness.caret("a", 1)
        harness.type("/2")
        #expect(harness.editor.slashCommands.isEmpty)
        harness.type(" /")
        #expect(!harness.editor.slashCommands.isEmpty)
        // Escape closes it and leaves the text alone.
        harness.press(#selector(NSResponder.cancelOperation(_:)))
        #expect(harness.editor.slashCommands.isEmpty)
        #expect(harness.outline == "p#a 1/2 /")
    }

    @Test func theSlashMenuClosesWhenNothingMatchesAndASpaceIsTyped() {
        let harness = EditorHarness(p("a"))
        harness.caret("a", 0)
        harness.type("/zzz")
        // It stays open to say that nothing matches.
        #expect(harness.editor.page.slashMenu.items.isEmpty)
        #expect(!harness.editor.page.slashMenu.isHidden)
        harness.type(" x")
        #expect(harness.editor.page.slashMenu.isHidden)
        #expect(harness.outline == "p#a /zzz x")
    }

    @Test func commandKeysMarkTheSelection() {
        let harness = EditorHarness(p("a", "Hello world"))
        harness.editor.select(.text(block: "a", range: 0..<5))
        harness.editor.toggle(.bold)
        #expect(shape(harness.editor.document.blocks[0].text) == "[b:Hello][ world]")
        #expect(harness.selection == "a|0-5")
        harness.editor.toggle(.bold)
        #expect(shape(harness.editor.document.blocks[0].text) == "[Hello world]")
        // With nothing selected, the mark is for what is typed next.
        harness.caret("a", 11)
        harness.editor.toggle(.italic)
        harness.type("!!")
        #expect(shape(harness.editor.document.blocks[0].text) == "[Hello world][i:!!]")
    }

    @Test func aLinkIsSetOnTheSelectionAndRemovedFromTheCaret() {
        let harness = EditorHarness(p("a", "see the site"))
        harness.editor.select(.text(block: "a", range: 8..<12))
        harness.editor.setLink("example.com")
        #expect(shape(harness.editor.document.blocks[0].text) == "[see the ][<https://example.com>:site]")
        harness.caret("a", 10)
        harness.editor.setLink(nil)
        #expect(shape(harness.editor.document.blocks[0].text) == "[see the site]")
    }

    @Test func aTypedAddressIsLinkedAndTheNextWordIsNot() {
        let harness = EditorHarness(p("a"))
        harness.caret("a", 0)
        harness.type("see https://example.com now")
        #expect(shape(harness.editor.document.blocks[0].text) == "[see ][<https://example.com>:https://example.com][ now]")
    }

    @Test func onlyLinksTheAppCanOpenAreAccepted() {
        #expect(BlockEditorController.cleanAddress("example.com") == "https://example.com")
        #expect(BlockEditorController.cleanAddress("  https://a.example/x  ") == "https://a.example/x")
        #expect(BlockEditorController.cleanAddress("mailto:a@b.example") == "mailto:a@b.example")
        #expect(BlockEditorController.cleanAddress("heracles://note/abc") == "heracles://note/abc")
        #expect(BlockEditorController.cleanAddress("example.com:8080/x") == "https://example.com:8080/x")
        #expect(BlockEditorController.cleanAddress("localhost:3000") == "https://localhost:3000")
        #expect(BlockEditorController.cleanAddress("javascript:alert(1)") == nil)
        #expect(BlockEditorController.cleanAddress("file:///etc/passwd") == nil)
        #expect(BlockEditorController.cleanAddress("data:text/html,x") == nil)
        #expect(BlockEditorController.cleanAddress("two words") == nil)
        #expect(BlockEditorController.cleanAddress("   ") == nil)
    }

    @Test func clickingALinkAsksTheAppToOpenIt() {
        let harness = EditorHarness(p("a", "site"))
        _ = harness.editor.textView(harness.text("a"), clickedOnLink: "https://example.com", at: 0)
        #expect(harness.host.opened == ["https://example.com"])
    }
}

@MainActor
struct EditorNavigationTests {
    @Test func theArrowKeysCrossFromBlockToBlockKeepingTheColumn() {
        let harness = EditorHarness(p("a", "Hello world"), p("b", "Hello world!"), p("c", "Hi"))
        harness.caret("a", 7)
        harness.press(#selector(NSResponder.moveDown(_:)))
        #expect(harness.selection == "b|7")
        #expect(harness.focused === harness.text("b"))
        // A short line does not lose the column for the lines after it.
        harness.press(#selector(NSResponder.moveDown(_:)))
        #expect(harness.selection == "c|2")
        harness.press(#selector(NSResponder.moveUp(_:)))
        #expect(harness.selection == "b|7")
        harness.press(#selector(NSResponder.moveUp(_:)))
        #expect(harness.selection == "a|7")
    }

    @Test func insideAParagraphOfSeveralLinesTheArrowsAreTheTextViewsOwn() {
        let long = String(repeating: "word ", count: 80)
        let harness = EditorHarness(p("a", long), p("b", "next"))
        #expect(harness.text("a").lines.count > 2)
        harness.caret("a", 3)
        harness.press(#selector(NSResponder.moveDown(_:)))
        #expect(harness.selection.hasPrefix("a|"))
        #expect(harness.selection != "a|3")
        // From the last line it leaves the block.
        harness.caret("a", (long as NSString).length)
        harness.press(#selector(NSResponder.moveDown(_:)))
        #expect(harness.selection.hasPrefix("b|"))
    }

    @Test func leftAndRightCrossAtTheEnds() {
        let harness = EditorHarness(p("a", "Hello"), divider("d"), p("b", "World"))
        harness.caret("a", 5)
        harness.press(#selector(NSResponder.moveRight(_:)))
        #expect(harness.selection == "[d]")
        #expect(harness.window.firstResponder === harness.editor.page)
        _ = harness.editor.handleBlockKey(harness.key(125))
        #expect(harness.selection == "b|0")
        harness.press(#selector(NSResponder.moveLeft(_:)))
        #expect(harness.selection == "[d]")
        _ = harness.editor.handleBlockKey(harness.key(126))
        #expect(harness.selection == "a|5")
    }

    @Test func shiftAndAnArrowAtAnEdgeSelectsWholeBlocks() {
        let harness = EditorHarness(p("a", "A"), p("b", "B"), p("c", "C"))
        harness.caret("b", 1)
        harness.press(#selector(NSResponder.moveDownAndModifySelection(_:)))
        #expect(harness.selection == "[b c]")
        #expect(harness.editor.page.selectedBlocks == ["b", "c"])
        _ = harness.editor.handleBlockKey(harness.key(126, shift: true))
        #expect(harness.selection == "[b]")
        _ = harness.editor.handleBlockKey(harness.key(126, shift: true))
        #expect(harness.selection == "[a b]")
        _ = harness.editor.handleBlockKey(harness.key(51))
        #expect(harness.outline == "p#c C")
        #expect(harness.selection == "c|0")
    }

    @Test func aShiftClickInAnotherBlockSelectsTheBlocksBetween() {
        let harness = EditorHarness(p("a", "A"), p("b", "B"), p("c", "C"))
        harness.caret("a", 1)
        // In the same block, the text view extends its own selection.
        #expect(!harness.editor.extendSelection(toBlock: "a"))
        #expect(harness.editor.extendSelection(toBlock: "c"))
        #expect(harness.selection == "[a b c]")
        #expect(harness.editor.extendSelection(toBlock: "b"))
        #expect(harness.selection == "[a b]")
    }

    @Test func selectAllTakesTheBlockThenTheNote() {
        let harness = EditorHarness(p("a", "Hello"), p("b", "World"))
        harness.caret("a", 2)
        harness.press(#selector(NSResponder.selectAll(_:)))
        #expect(harness.selection == "a|0-5")
        harness.press(#selector(NSResponder.selectAll(_:)))
        #expect(harness.selection == "[a b]")
    }

    @Test func escapeSelectsTheBlockAndReturnGoesBackToTyping() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.caret("a", 2)
        harness.press(#selector(NSResponder.cancelOperation(_:)))
        #expect(harness.selection == "[a]")
        _ = harness.editor.handleBlockKey(harness.key(36))
        #expect(harness.selection == "a|5")
        #expect(harness.focused === harness.text("a"))
    }

    @Test func onlyOneBlocksTextIsEverSelected() {
        let harness = EditorHarness(p("a", "Hello"), p("b", "World"))
        harness.editor.select(.text(block: "a", range: 0..<5))
        harness.caret("b", 2)
        #expect(harness.text("a").selectedRange().length == 0)
        #expect(harness.selection == "b|2")
    }

    @Test func focusingTheEndAddsALineOnlyWhenThereIsNone() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.editor.focusEnd()
        #expect(harness.outline == "p#a Hello\np#n1")
        #expect(harness.selection == "n1|0")
        harness.editor.focusEnd()
        #expect(harness.outline == "p#a Hello\np#n1")
    }
}

@MainActor
struct EditorBlockActionTests {
    @Test func checkingATaskChangesTheDocument() {
        let harness = EditorHarness(todo("t", "Book ferry"))
        harness.editor.setChecked("t", true)
        #expect(harness.outline == "done#t Book ferry")
        #expect(harness.host.changes == 1)
        harness.editor.undo()
        #expect(harness.outline == "todo#t Book ferry")
    }

    @Test func foldingAToggleMovesTheCaretOutOfWhatItHides() {
        let harness = EditorHarness(toggle("g", [p("s", "Summary"), p("h", "Hidden")]))
        harness.caret("h", 2)
        harness.editor.setOpen("g", false)
        #expect(harness.outline == "toggle(closed)#g\n  p#s Summary\n  p#h Hidden")
        #expect(harness.selection == "s|7")
        #expect(harness.editor.page.blockView(for: "h")?.isHidden == true)
    }

    @Test func duplicateMoveAndRemove() {
        let harness = EditorHarness(p("a", "A"), p("b", "B"))
        harness.editor.duplicate("a")
        #expect(harness.outline == "p#a A\np#n1 A\np#b B")
        #expect(harness.selection == "[n1]")
        #expect(harness.editor.move(["b"], to: .before("a")))
        #expect(harness.outline == "p#b B\np#a A\np#n1 A")
        harness.editor.remove(["a", "n1"])
        #expect(harness.outline == "p#b B")
        harness.editor.undo()
        harness.editor.undo()
        #expect(harness.outline == "p#a A\np#n1 A\np#b B")
    }

    @Test func turningTheBlockBeingTypedInKeepsTheCaret() {
        let harness = EditorHarness(p("a", "Hello"))
        harness.caret("a", 3)
        harness.editor.turnCurrentBlock(into: .heading(2))
        #expect(harness.outline == "h2#a Hello")
        #expect(harness.selection == "a|3")
        #expect(harness.focused === harness.text("a"))
        harness.editor.turnCurrentBlock(into: .code)
        #expect(harness.outline == "code#a Hello")
        #expect(harness.focused === harness.text("a"))
        #expect(harness.text("a").isCode)
    }

    @Test func aRevealedBlockIsFoundByEitherOfAListItemsIds() {
        let harness = EditorHarness(p("a", "A"), toggle("g", open: false, [p("s", "S"), bullet("b", "B")]))
        #expect(harness.editor.reveal("a"))
        #expect(harness.editor.reveal("b-p"))
        // It was folded away, so its toggle is now open.
        #expect(harness.editor.document.block("g")?.isOpen == true)
        #expect(!harness.editor.reveal("missing"))
    }

    @Test func droppingImageFilesStoresThemAndMakesBlocks() throws {
        let harness = EditorHarness(p("a", "A"), p("b", "B"))
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let file = folder.appendingPathComponent("Map of the coast.png")
        try Data([0x89, 0x50, 0x4E, 0x47]).write(to: file)
        let other = folder.appendingPathComponent("notes.txt")
        try Data("x".utf8).write(to: other)

        #expect(harness.editor.insertImages(from: [file], at: .before("b")))
        #expect(harness.outline == "p#a A\nimage#n1\np#b B")
        #expect(harness.host.stored.map(\.mime) == ["image/png"])
        let image = harness.editor.document.block("n1")
        #expect(image?.imageSource == "heracles-image://local/image1")
        #expect(image?.attrs["alt"] == .string("Map of the coast"))
        #expect(BlockEditorController.imageType(of: other) == nil)
    }
}

@MainActor
struct EditorPasteboardTests {
    private func board() -> NSPasteboard { NSPasteboard(name: NSPasteboard.Name("heracles-test-\(UUID().uuidString)")) }

    @Test func copiedBlocksPasteBackAsBlocksAndElsewhereAsText() {
        let harness = EditorHarness(h(2, "h", "Title"), bullet("b", "Item", [bullet("c", "Child")]), p("z"))
        let board = board()
        defer { board.releaseGlobally() }
        harness.editor.select(.blocks(["h", "b"]))
        #expect(harness.editor.copySelection(to: board))
        #expect(board.string(forType: .string) == "## Title\n- Item\n  - Child")

        harness.caret("z", 0)
        #expect(harness.editor.paste(from: board))
        #expect(harness.outline == "h2#h Title\nbullet#b Item\n  bullet#c Child\nh2#n1 Title\nbullet#n2 Item\n  bullet#n4 Child")
    }

    @Test func copiedTextKeepsItsMarksWhenPastedBack() {
        var block = p("a", "Hello world")
        block.text.set(.bold, true, in: 6..<11)
        let harness = EditorHarness(block, p("b", "to: "))
        let board = board()
        defer { board.releaseGlobally() }
        board.clearContents()
        board.setString("world", forType: .string)
        harness.editor.select(.text(block: "a", range: 6..<11))
        harness.editor.addCopiedText(to: board)

        harness.caret("b", 4)
        #expect(harness.editor.paste(from: board))
        #expect(shape(harness.editor.document.blocks[1].text) == "[to: ][b:world]")
        #expect(harness.selection == "b|9")
        // Pasting as plain text leaves the marks behind.
        #expect(harness.editor.paste(from: board, plainOnly: true))
        #expect(shape(harness.editor.document.blocks[1].text) == "[to: ][b:world][world]")
    }

    @Test func cuttingBlocksRemovesThem() {
        let harness = EditorHarness(p("a", "A"), p("b", "B"))
        let board = board()
        defer { board.releaseGlobally() }
        harness.editor.select(.blocks(["a"]))
        #expect(harness.editor.cutSelection(to: board))
        #expect(harness.outline == "p#b B")
        #expect(board.string(forType: .string) == "A")
    }

    @Test func richTextFromElsewhereIsConvertedByTheApp() {
        let harness = EditorHarness(p("a"))
        harness.host.converted = """
            {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"From the web"}]},
              {"type":"paragraph","content":[{"type":"text","text":"Body"}]}]}
            """
        let board = board()
        defer { board.releaseGlobally() }
        board.clearContents()
        board.setString("<h2 onclick='x()'>From the web</h2><p>Body</p>", forType: .html)
        board.setString("From the web\nBody", forType: .string)
        harness.caret("a", 0)
        #expect(harness.editor.paste(from: board))
        #expect(harness.host.html == ["<h2 onclick='x()'>From the web</h2><p>Body</p>"])
        #expect(harness.outline == "h2#n1 From the web\np#n2 Body")
    }

    @Test func anAddressPastedOverSelectedTextLinksIt() {
        let harness = EditorHarness(p("a", "see the site"), p("b"))
        let board = board()
        defer { board.releaseGlobally() }
        board.clearContents()
        board.setString("https://example.com/x", forType: .string)
        harness.editor.select(.text(block: "a", range: 8..<12))
        #expect(harness.editor.paste(from: board))
        #expect(shape(harness.editor.document.blocks[0].text) == "[see the ][<https://example.com/x>:site]")
        // Pasted where nothing is selected, it is the link's own text.
        harness.caret("b", 0)
        #expect(harness.editor.paste(from: board))
        #expect(shape(harness.editor.document.blocks[1].text) == "[<https://example.com/x>:https://example.com/x]")
    }

    @Test func plainTextOfSeveralLinesBecomesParagraphs() {
        let harness = EditorHarness(p("a", "Start"))
        let board = board()
        defer { board.releaseGlobally() }
        board.clearContents()
        board.setString("one\n\ntwo", forType: .string)
        harness.caret("a", 5)
        #expect(harness.editor.paste(from: board))
        #expect(harness.outline == "p#a Start\np#n1 one\np#n2 two")
    }

    @Test func aPictureOnTheClipboardBecomesAnImageBlock() {
        let harness = EditorHarness(p("a", "A"))
        let board = board()
        defer { board.releaseGlobally() }
        board.clearContents()
        board.setData(Data([0x89, 0x50, 0x4E, 0x47]), forType: .png)
        harness.caret("a", 1)
        #expect(harness.editor.paste(from: board))
        #expect(harness.outline == "p#a A\nimage#n1")
        #expect(harness.host.stored.count == 1)
    }

    @Test func intoCodeTextGoesExactlyAsItIs() {
        let harness = EditorHarness(code("c", "a"))
        let board = board()
        defer { board.releaseGlobally() }
        board.clearContents()
        board.setString("\n\n  b\n", forType: .string)
        board.setString("<p>b</p>", forType: .html)
        harness.caret("c", 1)
        #expect(harness.editor.paste(from: board))
        #expect(harness.editor.document.blocks[0].text.string == "a\n\n  b\n")
    }
}

@MainActor
struct EditorLayoutTests {
    @Test func blocksAreStackedDownThePageInOrder() {
        let harness = EditorHarness(
            h(1, "h", "Title"), p("a", String(repeating: "long ", count: 120)), bullet("b", "B", [bullet("c", "C")]),
            quote("q", [p("x", "Quoted")]), code("k", "let x = 1"), divider("d"), p("z", "End"))
        let page = harness.editor.page!
        var bottom: CGFloat = 0
        for id in ["h", "a", "b", "q", "k", "d", "z"] {
            let frame = page.frame(of: id)!
            #expect(frame.minY >= bottom, "\(id) starts below the block before it")
            #expect(frame.height > 0)
            bottom = frame.maxY
        }
        // A paragraph that wraps is as tall as its lines.
        #expect(page.frame(of: "a")!.height > page.frame(of: "z")!.height * 3)
        // What is inside a block is drawn inside it.
        #expect(page.frame(of: "b")!.contains(page.frame(of: "c")!))
        #expect(page.frame(of: "c")!.minX > page.frame(of: "b")!.minX)
        #expect(page.frame.height >= bottom)
    }

    @Test func columnsSitSideBySideByTheirWidths() {
        let wide = { () -> Block in var block = column("c1", [p("a", "A")]); block.width = 2; return block }()
        let harness = EditorHarness(columns("r", [wide, column("c2", [p("b", "B")])]))
        let page = harness.editor.page!
        // Columns have no stored ids, so they were given new ones on loading.
        let row = harness.editor.document.blocks[0]
        let left = page.frame(of: row.children[0].id)!
        let right = page.frame(of: row.children[1].id)!
        #expect(left.maxX < right.minX)
        #expect(abs(left.width - right.width * 2) < 2)
        #expect(left.minY == right.minY)
    }

    @Test func theBlockUnderAPointIsFoundByItsRow() {
        let harness = EditorHarness(p("a", "A"), bullet("b", "B", [bullet("c", "C")]), p("z", "Z"))
        let page = harness.editor.page!
        let child = page.frame(of: "c")!
        // In the margin beside a block counts as on it.
        #expect(page.blockView(at: NSPoint(x: 5, y: child.midY))?.block.id == "c")
        #expect(page.blockView(at: NSPoint(x: child.midX, y: page.frame(of: "b")!.minY + 4))?.block.id == "b")
        #expect(page.blockView(at: NSPoint(x: child.midX, y: page.frame(of: "z")!.maxY + 300)) == nil)
    }

    @Test func aCalloutIsFoundAsAWholeFromTheMarginAndItsBlocksFromInside() {
        let harness = EditorHarness(p("a", "A"), callout("k", [p("x", "X"), p("y", "Y")]), quote("q", [p("z", "Z")]))
        let page = harness.editor.page!
        let inner = page.frame(of: "y")!
        #expect(page.blockView(at: NSPoint(x: 5, y: inner.midY))?.block.id == "k")
        #expect(page.blockView(at: NSPoint(x: inner.midX, y: inner.midY))?.block.id == "y")
        #expect(page.blockView(at: NSPoint(x: 5, y: page.frame(of: "z")!.midY))?.block.id == "q")
        // So a block dragged to the margin lands beside the callout, not in it.
        #expect(page.dropTarget(at: NSPoint(x: 5, y: page.frame(of: "k")!.maxY - 2), moving: ["a"]) == .after("k"))
    }

    @Test func aDropLandsBeforeAfterOrBesideTheBlockUnderIt() {
        let harness = EditorHarness(p("a", "A"), p("b", "B"), bullet("c", "C", [bullet("d", "D")]), p("z", "Z"))
        let page = harness.editor.page!
        let b = page.frame(of: "b")!
        #expect(page.dropTarget(at: NSPoint(x: b.minX + 10, y: b.minY + 2), moving: ["z"]) == .before("b"))
        #expect(page.dropTarget(at: NSPoint(x: b.minX + 10, y: b.maxY - 2), moving: ["z"]) == .after("b"))
        #expect(page.dropTarget(at: NSPoint(x: b.maxX - 10, y: b.midY), moving: ["z"]) == .rightOf("b"))
        // Just under an item that holds others is at the head of them.
        let c = page.frame(of: "c")!
        let own = harness.text("c").frame.height
        #expect(page.dropTarget(at: NSPoint(x: c.minX + 10, y: c.minY + own - 2), moving: ["z"]) == .before("d"))
        // A block is never dropped on itself or inside itself.
        #expect(page.dropTarget(at: NSPoint(x: c.minX + 10, y: c.minY + 2), moving: ["c"]) != .before("c"))
        // Past the end of the note is after the last block.
        #expect(page.dropTarget(at: NSPoint(x: b.minX + 10, y: page.frame(of: "z")!.maxY + 200), moving: ["a"]) == .after("z"))
    }

    @Test func typingInALongNoteStaysQuick() {
        let blocks = (0..<600).map { p("p\($0)", "Paragraph \($0) with a little text in it to lay out.") }
        let harness = EditorHarness(p("a", "Start"))
        harness.editor.load(json: EditorDocument(blocks: blocks).json)
        harness.caret("p300", 5)
        let took = ContinuousClock().measure {
            for _ in 0..<50 {
                harness.type("x")
                // What the window does before each redraw.
                harness.editor.page.layoutBlocks()
            }
        }
        print("50 keystrokes in a 600-block note took \(took)")
        #expect(took < .seconds(2))
    }

    @Test func aLongNoteOpensQuickly() {
        let blocks = (0..<600).map { p("p\($0)", "Paragraph \($0) with a little text in it to lay out.") }
        _ = NSApplication.shared
        let json = EditorDocument(blocks: blocks).json
        var height: CGFloat = 0
        let took = ContinuousClock().measure {
            let editor = BlockEditorController(ids: .sequential())
            editor.scrollView.frame = NSRect(x: 0, y: 0, width: 800, height: 600)
            editor.load(json: json)
            height = editor.page.frame.height
        }
        #expect(height > 600 * 20, "every block was laid out")
        print("600 blocks loaded and laid out in \(took)")
        #expect(took < .seconds(3))
    }
}

@MainActor
struct EditorThemeTests {
    @Test func aBlocksTextSurvivesTheTripThroughATextView() {
        var block = p("a", "plain bold italic code link struck")
        block.text.set(.bold, true, in: 6..<10)
        block.text.set(.italic, true, in: 11..<17)
        block.text.set(.code, true, in: 18..<22)
        block.text.setLink(TextLink(href: "https://example.com", extra: ["target": .string("_blank")]), in: 23..<27)
        block.text.set(.strike, true, in: 28..<34)
        block.text += RichText(inline: [JSONValue(text: #"{"type":"mention","attrs":{"id":"u1"}}"#)!])
        let shown = EditorTheme.attributedString(for: block)
        #expect(EditorTheme.richText(from: shown) == block.text)
    }

    @Test func marksShowAsFontsAndLines() {
        var block = p("a", "bold italic code struck")
        block.text.set(.bold, true, in: 0..<4)
        block.text.set(.italic, true, in: 5..<11)
        block.text.set(.code, true, in: 12..<16)
        block.text.set(.strike, true, in: 17..<23)
        let shown = EditorTheme.attributedString(for: block)
        func font(_ index: Int) -> NSFont { shown.attribute(.font, at: index, effectiveRange: nil) as! NSFont }
        #expect(font(0).fontDescriptor.symbolicTraits.contains(.bold))
        #expect(font(5).fontDescriptor.symbolicTraits.contains(.italic))
        #expect(font(12).isFixedPitch)
        #expect(shown.attribute(.strikethroughStyle, at: 17, effectiveRange: nil) != nil)
        #expect(shown.attribute(.strikethroughStyle, at: 0, effectiveRange: nil) == nil)
    }

    @Test func fontsAndColorsFromElsewhereAreNotMarks() {
        let foreign = NSAttributedString(
            string: "pasted", attributes: [.font: NSFont.boldSystemFont(ofSize: 30), .foregroundColor: NSColor.red])
        #expect(shape(EditorTheme.richText(from: foreign)) == "[pasted]")
    }

    @Test func aFinishedTaskIsStruckThrough() {
        let shown = EditorTheme.attributedString(for: todo("t", "Done", checked: true))
        #expect(shown.attribute(.strikethroughStyle, at: 0, effectiveRange: nil) != nil)
        // That is how it looks, not a mark on the text.
        #expect(shape(EditorTheme.richText(from: shown)) == "[Done]")
    }
}
