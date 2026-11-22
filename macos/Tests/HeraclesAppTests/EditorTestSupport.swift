import Foundation

@testable import HeraclesApp

// Short ways to build blocks in tests, and a way to print a document so an
// expectation can be written as a few lines of text.

func p(_ id: String, _ text: String = "") -> Block {
    Block(id: id, kind: .paragraph, text: RichText(text))
}

func h(_ level: Int, _ id: String, _ text: String = "") -> Block {
    var block = Block(id: id, kind: .heading, text: RichText(text))
    block.level = level
    return block
}

func bullet(_ id: String, _ text: String = "", _ children: [Block] = []) -> Block {
    Block(id: id, kind: .bullet, text: RichText(text), children: children, paragraphID: "\(id)-p")
}

func numbered(_ id: String, _ text: String = "", _ children: [Block] = []) -> Block {
    Block(id: id, kind: .numbered, text: RichText(text), children: children, paragraphID: "\(id)-p")
}

func todo(_ id: String, _ text: String = "", checked: Bool = false, _ children: [Block] = []) -> Block {
    var block = Block(id: id, kind: .todo, text: RichText(text), children: children, paragraphID: "\(id)-p")
    block.isChecked = checked
    return block
}

func quote(_ id: String, _ children: [Block]) -> Block {
    Block(id: id, kind: .quote, children: children)
}

func callout(_ id: String, _ children: [Block]) -> Block {
    Block(id: id, kind: .callout, children: children)
}

func toggle(_ id: String, open: Bool = true, _ children: [Block]) -> Block {
    var block = Block(id: id, kind: .toggle, children: children)
    block.isOpen = open
    return block
}

func columns(_ id: String, _ columns: [Block]) -> Block {
    Block(id: id, kind: .columns, children: columns)
}

func column(_ id: String, _ children: [Block]) -> Block {
    Block(id: id, kind: .column, children: children)
}

func divider(_ id: String) -> Block {
    Block(id: id, kind: .divider)
}

func code(_ id: String, _ text: String = "", language: String = "plaintext") -> Block {
    var block = Block(id: id, kind: .code, text: RichText(text))
    block.language = language
    return block
}

func image(_ id: String, src: String = "heracles-image://local/abc") -> Block {
    var block = Block(id: id, kind: .image)
    block.attrs["src"] = .string(src)
    return block
}

func doc(_ blocks: Block...) -> EditorDocument {
    EditorDocument(blocks: blocks)
}

/// A document as an outline: one line per block, children indented.
/// `bullet#b One` is a bullet with the id `b` and the text "One".
func outline(_ document: EditorDocument) -> String {
    var lines: [String] = []
    func walk(_ blocks: [Block], _ depth: Int) {
        for block in blocks {
            var name: String
            switch block.kind {
            case .paragraph: name = "p"
            case .heading: name = "h\(block.level)"
            case .bullet: name = "bullet"
            case .numbered: name = "numbered"
            case .todo: name = block.isChecked ? "done" : "todo"
            case .code: name = "code"
            case .quote: name = "quote"
            case .callout: name = "callout"
            case .toggle: name = block.isOpen ? "toggle" : "toggle(closed)"
            case .columns: name = "columns"
            case .column: name = "column"
            case .divider: name = "divider"
            case .image: name = "image"
            case .unknown: name = "unknown"
            }
            if block.startsList { name = "^" + name }
            let text = block.text.string.replacingOccurrences(of: "\n", with: "⏎")
            lines.append(String(repeating: "  ", count: depth) + "\(name)#\(block.id)" + (text.isEmpty ? "" : " \(text)"))
            walk(block.children, depth + 1)
        }
    }
    walk(document.blocks, 0)
    return lines.joined(separator: "\n")
}

/// A selection as text: `a|3` is a caret, `a|1-4` a range, `[a b]` blocks.
func shape(_ selection: EditorSelection) -> String {
    switch selection {
    case .none: return "none"
    case .text(let block, let range):
        return range.isEmpty ? "\(block)|\(range.lowerBound)" : "\(block)|\(range.lowerBound)-\(range.upperBound)"
    case .blocks(let ids): return "[\(ids.joined(separator: " "))]"
    }
}

func sameJSON(_ a: String, _ b: String) -> Bool {
    guard let first = JSONValue(text: a), let second = JSONValue(text: b) else { return false }
    return first == second
}
