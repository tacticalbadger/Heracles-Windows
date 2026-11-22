import Foundation
import Testing

@testable import HeraclesApp

/// Rich text as a string: each span in brackets with its marks, so a test
/// can say what it expects in one line. `[b:Hello][ world]`.
func shape(_ text: RichText) -> String {
    text.spans.map { span in
        var marks = ""
        if span.style.bold { marks += "b" }
        if span.style.italic { marks += "i" }
        if span.style.strike { marks += "s" }
        if span.style.code { marks += "c" }
        if let link = span.style.link { marks += "<\(link.href)>" }
        if span.atom != nil { marks += "@" }
        return marks.isEmpty ? "[\(span.text)]" : "[\(marks):\(span.text)]"
    }.joined()
}

private func json(_ text: String) -> [JSONValue] {
    JSONValue(text: text)!.arrayValue!
}

private let bold = TextStyle(bold: true)

struct JSONValueTests {
    @Test func readsAndWritesEveryKindOfValue() {
        let value = JSONValue(text: #"{"a":[1,2.5,true,null,"x"],"b":{"c":false}}"#)!
        #expect(value["a"]?.arrayValue == [.number(1), .number(2.5), .bool(true), .null, .string("x")])
        #expect(value["b"]?["c"] == .bool(false))
        // Keys are written in order, and whole numbers without a fraction.
        #expect(value.text == #"{"a":[1,2.5,true,null,"x"],"b":{"c":false}}"#)
    }

    @Test func tellsABooleanFromANumber() {
        let value = JSONValue(text: #"{"checked":true,"level":1}"#)!
        #expect(value["checked"] == .bool(true))
        #expect(value["level"] == .number(1))
        #expect(value["level"]?.intValue == 1)
        #expect(value["checked"]?.intValue == nil)
    }

    @Test func leavesSlashesAlone() {
        #expect(JSONValue.string("heracles-image://local/ab").text == #""heracles-image://local/ab""#)
    }

    @Test func refusesTextThatIsNotJSON() {
        #expect(JSONValue(text: "{nope") == nil)
    }
}

struct RichTextTests {
    @Test func plainTextIsOneSpan() {
        let text = RichText("Hello")
        #expect(shape(text) == "[Hello]")
        #expect(text.length == 5)
        #expect(text.string == "Hello")
        #expect(RichText().isEmpty)
        #expect(RichText("").spans.isEmpty)
    }

    @Test func lengthsAreCountedTheWayTextViewsCountThem() {
        // An emoji is two UTF-16 units, which is what NSRange counts.
        #expect(RichText("a😀b").length == 4)
    }

    @Test func neighbouringSpansWithTheSameStyleBecomeOne() {
        let text = RichText("Hel") + RichText("lo") + RichText(" bold", style: bold) + RichText("er", style: bold)
        #expect(shape(text) == "[Hello][b: bolder]")
    }

    @Test func readsInlineContentFromADocument() {
        let text = RichText(inline: json(
            """
            [{"type":"text","text":"Pack the "},
             {"type":"text","text":"lantern","marks":[{"type":"bold"},{"type":"italic"}]},
             {"type":"hardBreak"},
             {"type":"text","text":"site","marks":[{"type":"link","attrs":{"href":"https://example.com","target":"_blank"}}]},
             {"type":"text","text":"x","marks":[{"type":"code"},{"type":"strike"}]}]
            """))
        #expect(shape(text) == "[Pack the ][bi:lantern][\n][<https://example.com>:site][sc:x]")
    }

    @Test func writesInlineContentBackAsItWasStored() {
        let stored = json(
            """
            [{"type":"text","text":"Pack the "},
             {"type":"text","text":"lantern","marks":[{"type":"bold"},{"type":"italic"}]},
             {"type":"hardBreak"},
             {"type":"text","text":"site","marks":[{"type":"link","attrs":{"href":"https://example.com","target":"_blank"}}]}]
            """)
        #expect(RichText(inline: stored).inlineJSON == stored)
    }

    @Test func aLineBreakInsideMarkedTextSplitsTheRun() {
        var text = RichText("one\ntwo", style: bold)
        #expect(text.inlineJSON == json(
            """
            [{"type":"text","text":"one","marks":[{"type":"bold"}]},{"type":"hardBreak"},
             {"type":"text","text":"two","marks":[{"type":"bold"}]}]
            """))
        text = RichText("\n")
        #expect(text.inlineJSON == json(#"[{"type":"hardBreak"}]"#))
    }

    @Test func marksAreWrittenInTheCoresOrder() {
        let style = TextStyle(bold: true, italic: true, strike: true, link: TextLink(href: "https://a.example"))
        #expect(RichText("x", style: style).inlineJSON == json(
            """
            [{"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.example"}},
              {"type":"bold"},{"type":"italic"},{"type":"strike"}]}]
            """))
    }

    @Test func keepsMarksAndInlineNodesItDoesNotKnow() {
        let stored = json(
            """
            [{"type":"text","text":"hi","marks":[{"type":"highlight","attrs":{"color":"red"}}]},
             {"type":"mention","attrs":{"id":"u1"}},
             {"type":"text","text":" there"}]
            """)
        let text = RichText(inline: stored)
        // An unknown inline node takes one place in the text.
        #expect(text.length == 9)
        #expect(shape(text) == "[hi][@:\u{FFFC}][ there]")
        #expect(text.inlineJSON == stored)
    }

    @Test func dropsEmptyTextRuns() {
        #expect(RichText(inline: json(#"[{"type":"text","text":""},{"type":"text","text":"a"}]"#)).spans.count == 1)
    }

    @Test func takesAPieceOfTheText() {
        let text = RichText("Hello ") + RichText("bold", style: bold) + RichText(" end")
        #expect(shape(text.subtext(3..<8)) == "[lo ][b:bo]")
        #expect(shape(text.subtext(0..<0)) == "")
        #expect(shape(text.subtext(6..<10)) == "[b:bold]")
        // A range past the end is cut to fit.
        #expect(shape(text.subtext(10..<99)) == "[ end]")
    }

    @Test func replacesARange() {
        var text = RichText("Hello world")
        text.replace(5..<11, with: RichText("!", style: bold))
        #expect(shape(text) == "[Hello][b:!]")
        text.replace(0..<0, with: RichText(">> "))
        #expect(shape(text) == "[>> Hello][b:!]")
        text.replace(3..<8, with: RichText())
        #expect(shape(text) == "[>> ][b:!]")
    }

    @Test func turnsAMarkOnAndOffOverARange() {
        var text = RichText("Hello world")
        text.set(.bold, true, in: 0..<5)
        #expect(shape(text) == "[b:Hello][ world]")
        #expect(text.has(.bold, in: 0..<5))
        #expect(!text.has(.bold, in: 0..<6))
        #expect(!text.has(.bold, in: 3..<3))
        text.set(.italic, true, in: 3..<8)
        #expect(shape(text) == "[b:Hel][bi:lo][i: wo][rld]")
        text.set(.bold, false, in: 0..<11)
        #expect(shape(text) == "[Hel][i:lo wo][rld]")
    }

    @Test func codeTakesTheOtherMarksAway() {
        var text = RichText("abc", style: TextStyle(bold: true, link: TextLink(href: "https://a.example")))
        text.set(.code, true, in: 0..<3)
        #expect(shape(text) == "[c:abc]")
        // And nothing else can be added to code.
        text.set(.bold, true, in: 0..<3)
        #expect(shape(text) == "[c:abc]")
    }

    @Test func setsAndRemovesALink() {
        var text = RichText("see the site now")
        text.setLink(TextLink(href: "https://example.com"), in: 8..<12)
        #expect(shape(text) == "[see the ][<https://example.com>:site][ now]")
        #expect(text.link(at: 10)?.range == 8..<12)
        #expect(text.link(at: 10)?.link.href == "https://example.com")
        // The caret just after a link still counts as on it, just before does not.
        #expect(text.link(at: 12)?.range == 8..<12)
        #expect(text.link(at: 8) == nil)
        #expect(text.link(at: 3) == nil)
        text.setLink(nil, in: 0..<16)
        #expect(shape(text) == "[see the site now]")
    }

    @Test func givesTheStyleNewTypingWouldTake() {
        let text = RichText("plain") + RichText("bold", style: bold)
        #expect(text.style(before: 5) == .plain)
        #expect(text.style(before: 6) == bold)
        #expect(text.style(before: 9) == bold)
        // At the very start there is nothing before, so the first character decides.
        #expect(RichText("bold", style: bold).style(before: 0) == bold)
        #expect(RichText().style(before: 0) == .plain)
    }

    @Test func typingAfterALinkDoesNotExtendIt() {
        let text = RichText("site", style: TextStyle(bold: true, link: TextLink(href: "https://example.com")))
        #expect(text.style(before: 4) == bold)
        #expect(text.style(before: 2).link != nil)
    }
}
