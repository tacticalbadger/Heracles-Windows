import Foundation

/// What is selected in an editor: nothing, text inside one block, or whole
/// blocks. A caret is a text selection with an empty range.
public enum EditorSelection: Equatable, Sendable {
    case none
    case text(block: String, range: Range<Int>)
    /// Whole blocks, in document order. None is inside another.
    case blocks([String])

    public static func caret(_ block: String, _ offset: Int) -> EditorSelection {
        .text(block: block, range: offset..<offset)
    }
}
