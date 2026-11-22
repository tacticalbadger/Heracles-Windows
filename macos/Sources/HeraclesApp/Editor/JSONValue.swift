import Foundation

/// A JSON value. Documents come from the core as JSON text, and the editor
/// keeps every attribute it does not understand in this form, so that a
/// document written by a newer editor survives being edited by this one.
public enum JSONValue: Hashable, Sendable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])

    /// Parse JSON text. `nil` when the text is not JSON.
    public init?(text: String) {
        guard let parsed = try? JSONSerialization.jsonObject(with: Data(text.utf8), options: [.fragmentsAllowed])
        else { return nil }
        self.init(parsed)
    }

    private init(_ parsed: Any) {
        switch parsed {
        case let number as NSNumber:
            // A JSON `true` and a JSON `1` both arrive as numbers. Only the
            // first is the boolean object.
            if CFGetTypeID(number) == CFBooleanGetTypeID() {
                self = .bool(number.boolValue)
            } else {
                self = .number(number.doubleValue)
            }
        case let string as String:
            self = .string(string)
        case let array as [Any]:
            self = .array(array.map(JSONValue.init))
        case let object as [String: Any]:
            self = .object(object.mapValues(JSONValue.init))
        default:
            self = .null
        }
    }

    /// The value as JSON text, with keys in alphabetical order so that the
    /// same value always gives the same text.
    public var text: String {
        let options: JSONSerialization.WritingOptions = [.sortedKeys, .withoutEscapingSlashes, .fragmentsAllowed]
        guard let data = try? JSONSerialization.data(withJSONObject: foundation, options: options) else { return "null" }
        return String(decoding: data, as: UTF8.self)
    }

    private var foundation: Any {
        switch self {
        case .null: return NSNull()
        case .bool(let value): return value
        case .number(let value):
            // Whole numbers are written without a fraction: `1`, not `1.0`.
            if value.rounded() == value, abs(value) < 9_007_199_254_740_992 { return Int64(value) }
            return value
        case .string(let value): return value
        case .array(let values): return values.map(\.foundation)
        case .object(let members): return members.mapValues(\.foundation)
        }
    }

    public subscript(key: String) -> JSONValue? {
        if case .object(let members) = self { return members[key] }
        return nil
    }

    public var stringValue: String? {
        if case .string(let value) = self { return value }
        return nil
    }

    public var boolValue: Bool? {
        if case .bool(let value) = self { return value }
        return nil
    }

    public var doubleValue: Double? {
        if case .number(let value) = self { return value }
        return nil
    }

    public var intValue: Int? {
        if case .number(let value) = self, value.rounded() == value, abs(value) < 1e15 { return Int(value) }
        return nil
    }

    public var arrayValue: [JSONValue]? {
        if case .array(let values) = self { return values }
        return nil
    }

    public var objectValue: [String: JSONValue]? {
        if case .object(let members) = self { return members }
        return nil
    }
}
