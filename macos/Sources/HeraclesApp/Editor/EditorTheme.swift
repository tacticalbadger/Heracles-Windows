import AppKit

extension NSAttributedString.Key {
    /// The marks on a run of text, as a `StyleBox`. This is what the editor
    /// reads back from a text view. Fonts and colors are only how it looks.
    static let editorStyle = NSAttributedString.Key("heracles.style")
    /// An inline node this editor does not know, as an `AtomBox`.
    static let editorAtom = NSAttributedString.Key("heracles.atom")
}

/// A `TextStyle` as an attribute value. Attribute values must be objects,
/// and equal ones must compare equal for runs to join up.
final class StyleBox: NSObject {
    let style: TextStyle

    init(_ style: TextStyle) {
        self.style = style
    }

    override func isEqual(_ object: Any?) -> Bool {
        (object as? StyleBox)?.style == style
    }

    override var hash: Int { style.hashValue }
}

final class AtomBox: NSObject {
    let value: JSONValue

    init(_ value: JSONValue) {
        self.value = value
    }

    override func isEqual(_ object: Any?) -> Bool {
        (object as? AtomBox)?.value == value
    }

    override var hash: Int { value.hashValue }
}

/// How the editor looks: fonts, colors and spacing, and the conversion
/// between a block's text and what its text view shows.
@MainActor
enum EditorTheme {
    static let bodySize: CGFloat = 15
    static let codeSize: CGFloat = 13
    /// The widest a note's text column gets.
    static let contentWidth: CGFloat = 720
    /// Room beside the text column, where the block handle appears.
    static let gutter: CGFloat = 60
    /// The width of a list marker, and so how far nested blocks are indented.
    static let indent: CGFloat = 26
    /// Space between one block and the next.
    static let blockSpacing: CGFloat = 5
    static let columnGap: CGFloat = 40
    static let topPadding: CGFloat = 8
    /// Empty space below the last block, to click in and to scroll into.
    static let bottomPadding: CGFloat = 140

    // MARK: Fonts

    private static var lineHeights: [String: CGFloat] = [:]

    /// The height of one line of a font.
    static func lineHeight(of font: NSFont) -> CGFloat {
        let key = "\(font.fontName) \(font.pointSize)"
        if let known = lineHeights[key] { return known }
        let height = NSLayoutManager().defaultLineHeight(for: font)
        lineHeights[key] = height
        return height
    }

    static func baseFont(for block: Block) -> NSFont {
        switch block.kind {
        case .heading:
            switch block.level {
            case 1: return .systemFont(ofSize: 28, weight: .bold)
            case 2: return .systemFont(ofSize: 22, weight: .bold)
            case 3: return .systemFont(ofSize: 18, weight: .semibold)
            default: return .systemFont(ofSize: bodySize, weight: .semibold)
            }
        case .code:
            return .monospacedSystemFont(ofSize: codeSize, weight: .regular)
        default:
            return .systemFont(ofSize: bodySize)
        }
    }

    /// Extra space above a block, on top of the usual spacing.
    static func topMargin(for block: Block) -> CGFloat {
        guard block.kind == .heading else { return 0 }
        switch block.level {
        case 1: return 16
        case 2: return 12
        case 3: return 8
        default: return 4
        }
    }

    static func paragraphStyle(for block: Block) -> NSParagraphStyle {
        let style = NSMutableParagraphStyle()
        style.lineSpacing = block.kind == .code ? 3 : block.kind == .heading ? 2 : 4
        return style
    }

    private static func font(_ base: NSFont, for style: TextStyle) -> NSFont {
        if style.code {
            return .monospacedSystemFont(ofSize: (base.pointSize * 0.9).rounded(), weight: .regular)
        }
        var traits: NSFontDescriptor.SymbolicTraits = base.fontDescriptor.symbolicTraits
        if style.bold { traits.insert(.bold) }
        if style.italic { traits.insert(.italic) }
        guard traits != base.fontDescriptor.symbolicTraits else { return base }
        return NSFont(descriptor: base.fontDescriptor.withSymbolicTraits(traits), size: base.pointSize) ?? base
    }

    // MARK: Text

    /// How a run with these marks looks in this block, and the marks
    /// themselves, to be read back later.
    static func attributes(for style: TextStyle, in block: Block) -> [NSAttributedString.Key: Any] {
        let base = baseFont(for: block)
        let isDone = block.kind == .todo && block.isChecked
        var attributes: [NSAttributedString.Key: Any] = [
            .font: block.kind == .code ? base : font(base, for: style),
            .foregroundColor: isDone ? NSColor.secondaryLabelColor : NSColor.labelColor,
            .paragraphStyle: paragraphStyle(for: block),
        ]
        guard block.kind != .code else { return attributes }
        attributes[.editorStyle] = StyleBox(style)
        if style.strike || isDone {
            attributes[.strikethroughStyle] = NSUnderlineStyle.single.rawValue
        }
        if style.code {
            attributes[.foregroundColor] = NSColor.systemRed
            attributes[.backgroundColor] = NSColor.quaternaryLabelColor.withAlphaComponent(0.12)
        }
        if let link = style.link {
            // A text view draws and opens links by this attribute.
            attributes[.link] = link.href
        }
        return attributes
    }

    /// A block's text as its text view shows it.
    static func attributedString(for block: Block) -> NSAttributedString {
        let result = NSMutableAttributedString()
        for span in block.text.spans {
            var attributes = attributes(for: span.style, in: block)
            if let atom = span.atom {
                attributes[.editorAtom] = AtomBox(atom)
                attributes[.foregroundColor] = NSColor.tertiaryLabelColor
            }
            // An unknown inline node is shown as a small mark, one character wide.
            result.append(NSAttributedString(string: span.atom == nil ? span.text : "\u{FFFC}", attributes: attributes))
        }
        return result
    }

    /// Read a block's text back from what its text view holds. Only the
    /// editor's own attributes count: whatever fonts or colors arrived by
    /// some other road are not marks.
    static func richText(from attributed: NSAttributedString, isCode: Bool = false) -> RichText {
        if isCode { return RichText(clean(attributed.string)) }
        var spans: [RichText.Span] = []
        attributed.enumerateAttributes(in: NSRange(location: 0, length: attributed.length)) { attributes, range, _ in
            let text = attributed.attributedSubstring(from: range).string
            let style = (attributes[.editorStyle] as? StyleBox)?.style ?? .plain
            if let atom = (attributes[.editorAtom] as? AtomBox)?.value {
                // One stored node per object character.
                for _ in text where text.contains("\u{FFFC}") {
                    spans.append(RichText.Span(text: RichText.atomCharacter, style: .plain, atom: atom))
                }
            } else {
                // An object character without a node behind it is an
                // attachment from elsewhere. There is nothing to store for it.
                spans.append(RichText.Span(text: clean(text).replacingOccurrences(of: "\u{FFFC}", with: ""), style: style))
            }
        }
        return RichText(spans: spans)
    }

    /// Every kind of line break becomes the one the editor stores.
    private static func clean(_ text: String) -> String {
        text.replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
            .replacingOccurrences(of: "\u{2028}", with: "\n")
            .replacingOccurrences(of: "\u{2029}", with: "\n")
    }

    // MARK: Colors

    static var selectionColor: NSColor { NSColor.controlAccentColor.withAlphaComponent(0.18) }
    static var panelColor: NSColor { NSColor.labelColor.withAlphaComponent(0.05) }
    static var ruleColor: NSColor { NSColor.separatorColor }
}
