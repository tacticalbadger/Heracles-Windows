import Foundation

/// A link on a run of text.
public struct TextLink: Hashable, Sendable {
    public var href: String
    /// The link's other attributes, kept as they were stored.
    public var extra: [String: JSONValue]

    public init(href: String, extra: [String: JSONValue] = [:]) {
        self.href = href
        self.extra = extra
    }
}

/// The marks a character can carry.
public enum TextMark: CaseIterable, Sendable {
    case bold, italic, strike, code
}

/// How a run of text is marked.
public struct TextStyle: Hashable, Sendable {
    public var bold: Bool
    public var italic: Bool
    public var strike: Bool
    public var code: Bool
    public var link: TextLink?
    /// Marks this editor does not know, kept as they were stored.
    public var other: [JSONValue]

    public static let plain = TextStyle()

    public init(
        bold: Bool = false, italic: Bool = false, strike: Bool = false, code: Bool = false,
        link: TextLink? = nil, other: [JSONValue] = []
    ) {
        self.bold = bold
        self.italic = italic
        self.strike = strike
        self.code = code
        self.link = link
        self.other = other
    }

    public subscript(mark: TextMark) -> Bool {
        get {
            switch mark {
            case .bold: bold
            case .italic: italic
            case .strike: strike
            case .code: code
            }
        }
        set {
            switch mark {
            case .bold: bold = newValue
            case .italic: italic = newValue
            case .strike: strike = newValue
            case .code: code = newValue
            }
        }
    }
}

/// The text of one block: characters and the marks on them.
///
/// Offsets and lengths are in UTF-16 units, the way `NSRange` and text views
/// count, so a selection can be handed over without converting it. A line
/// break inside a block is the character `\n`.
public struct RichText: Equatable, Sendable {
    public struct Span: Equatable, Sendable {
        public var text: String
        public var style: TextStyle
        /// An inline node this editor does not know, kept as stored. It
        /// takes one place in the text, shown as an object character.
        public var atom: JSONValue?

        var length: Int { text.utf16.count }
    }

    /// Runs of text. No run is empty, and no two neighbours share a style.
    public private(set) var spans: [Span]

    /// The character that stands in for an inline node this editor does not know.
    static let atomCharacter = "\u{FFFC}"

    public init() {
        spans = []
    }

    public init(_ string: String, style: TextStyle = .plain) {
        spans = string.isEmpty ? [] : [Span(text: string, style: style)]
    }

    init(spans: [Span]) {
        self.spans = []
        for span in spans { append(span) }
    }

    public var isEmpty: Bool { spans.isEmpty }
    public var length: Int { spans.reduce(0) { $0 + $1.length } }
    public var string: String { spans.map(\.text).joined() }

    private mutating func append(_ span: Span) {
        guard !span.text.isEmpty else { return }
        if span.atom == nil, let last = spans.last, last.atom == nil, last.style == span.style {
            spans[spans.count - 1].text += span.text
        } else {
            spans.append(span)
        }
    }

    public static func + (left: RichText, right: RichText) -> RichText {
        var joined = left
        for span in right.spans { joined.append(span) }
        return joined
    }

    public static func += (left: inout RichText, right: RichText) {
        left = left + right
    }

    // MARK: Ranges

    private func clamped(_ range: Range<Int>) -> Range<Int> {
        let length = length
        let lower = min(max(range.lowerBound, 0), length)
        return lower..<min(max(range.upperBound, lower), length)
    }

    /// The text within `range`, with its marks.
    public func subtext(_ range: Range<Int>) -> RichText {
        let range = clamped(range)
        var piece = RichText()
        var offset = 0
        for span in spans {
            let end = offset + span.length
            let lower = max(range.lowerBound, offset)
            let upper = min(range.upperBound, end)
            if lower < upper {
                let text = (span.text as NSString).substring(with: NSRange(location: lower - offset, length: upper - lower))
                piece.append(Span(text: text, style: span.style, atom: span.atom))
            }
            offset = end
        }
        return piece
    }

    public mutating func replace(_ range: Range<Int>, with replacement: RichText) {
        let range = clamped(range)
        self = subtext(0..<range.lowerBound) + replacement + subtext(range.upperBound..<length)
    }

    /// Rewrite the style of every character in `range`.
    private mutating func restyle(_ range: Range<Int>, _ change: (inout TextStyle) -> Void) {
        let range = clamped(range)
        var middle = subtext(range)
        for index in middle.spans.indices where middle.spans[index].atom == nil {
            change(&middle.spans[index].style)
        }
        // The spans were changed in place, so neighbours may now match.
        replace(range, with: RichText(spans: middle.spans))
    }

    // MARK: Marks

    /// True when the range is not empty and every character in it has the mark.
    public func has(_ mark: TextMark, in range: Range<Int>) -> Bool {
        let piece = subtext(range)
        return !piece.isEmpty && piece.spans.allSatisfy { $0.style[mark] }
    }

    /// Turn a mark on or off over a range. Code stands alone: turning it on
    /// takes the other marks away, and nothing else can be added to code.
    public mutating func set(_ mark: TextMark, _ on: Bool, in range: Range<Int>) {
        restyle(range) { style in
            if mark == .code, on {
                style = TextStyle(code: true, other: style.other)
            } else if mark == .code || !style.code {
                style[mark] = on
            }
        }
    }

    public mutating func setLink(_ link: TextLink?, in range: Range<Int>) {
        restyle(range) { style in
            if !style.code { style.link = link }
        }
    }

    /// The link the caret is in or at the end of, and how far it reaches.
    public func link(at offset: Int) -> (range: Range<Int>, link: TextLink)? {
        var start = 0
        var index = 0
        while index < spans.count {
            guard let link = spans[index].style.link else {
                start += spans[index].length
                index += 1
                continue
            }
            // A link can run over several spans when part of it is bold.
            var end = start
            while index < spans.count, spans[index].style.link == link {
                end += spans[index].length
                index += 1
            }
            if offset > start, offset <= end { return (start..<end, link) }
            start = end
        }
        return nil
    }

    /// The style that text typed at `offset` takes: that of the character
    /// before it, or of the first character at the very start. A link is
    /// only continued from inside it, so typing after a link is not linked.
    public func style(before offset: Int) -> TextStyle {
        guard let first = spans.first else { return .plain }
        var style = first.style
        var end = 0
        for span in spans {
            end += span.length
            style = span.atom == nil ? span.style : .plain
            if offset <= end { break }
        }
        if let link = style.link, self.link(at: offset)?.link != link || self.link(at: offset)?.range.upperBound == offset {
            style.link = nil
        }
        return style
    }

    // MARK: The document's inline content

    /// Read the inline content of a paragraph or heading.
    public init(inline nodes: [JSONValue]) {
        spans = []
        for node in nodes {
            let type = node["type"]?.stringValue
            if type == "text" {
                append(Span(text: node["text"]?.stringValue ?? "", style: TextStyle(marks: node["marks"]?.arrayValue ?? [])))
            } else if type == "hardBreak" {
                append(Span(text: "\n", style: .plain))
            } else {
                spans.append(Span(text: Self.atomCharacter, style: .plain, atom: node))
            }
        }
    }

    /// The inline content to store: runs of text, with a node of its own
    /// for each line break.
    public var inlineJSON: [JSONValue] {
        var nodes: [JSONValue] = []
        for span in spans {
            if let atom = span.atom {
                nodes.append(atom)
                continue
            }
            let marks = span.style.marks
            let lines = span.text.components(separatedBy: "\n")
            for (index, line) in lines.enumerated() {
                if index > 0 { nodes.append(.object(["type": .string("hardBreak")])) }
                guard !line.isEmpty else { continue }
                var node: [String: JSONValue] = ["type": .string("text"), "text": .string(line)]
                if !marks.isEmpty { node["marks"] = .array(marks) }
                nodes.append(.object(node))
            }
        }
        return nodes
    }
}

extension TextStyle {
    /// Read the marks of a text node.
    init(marks: [JSONValue]) {
        self.init()
        for mark in marks {
            switch mark["type"]?.stringValue {
            case "bold": bold = true
            case "italic": italic = true
            case "strike": strike = true
            case "code": code = true
            case "link":
                var attrs = mark["attrs"]?.objectValue ?? [:]
                let href = attrs.removeValue(forKey: "href")?.stringValue ?? ""
                link = TextLink(href: href, extra: attrs)
            default: other.append(mark)
            }
        }
    }

    /// The marks to store, in the order the core writes them.
    var marks: [JSONValue] {
        var marks: [JSONValue] = []
        if let link {
            var attrs = link.extra
            attrs["href"] = .string(link.href)
            marks.append(.object(["type": .string("link"), "attrs": .object(attrs)]))
        }
        if bold { marks.append(.object(["type": .string("bold")])) }
        if code { marks.append(.object(["type": .string("code")])) }
        if italic { marks.append(.object(["type": .string("italic")])) }
        if strike { marks.append(.object(["type": .string("strike")])) }
        return marks + other
    }
}
