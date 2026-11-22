import Foundation
import HeraclesCore

/// What to do with a link the user clicked in a note.
public enum NavigationDecision: Equatable, Sendable {
    /// A link to the outside world. The system opens it.
    case openExternally
    /// A link to a note. The app opens it.
    case openInApp
    case deny
}

/// Which links the app follows. A note can hold any address at all, typed
/// or imported, so only the kinds known to be safe to hand on are opened.
public enum NavigationPolicy {
    public static func decide(_ url: URL?) -> NavigationDecision {
        guard let url, let scheme = url.scheme?.lowercased() else { return .deny }
        switch scheme {
        case "http", "https", "mailto":
            return .openExternally
        case AppLink.scheme:
            return AppLink(url) == nil ? .deny : .openInApp
        default:
            return .deny
        }
    }
}

/// A `heracles://note/<id>` link, optionally to one block of the note.
public struct AppLink: Equatable, Sendable {
    public static let scheme = "heracles"

    public var noteID: String
    public var blockID: String?

    public init(noteID: String, blockID: String?) {
        self.noteID = noteID
        self.blockID = blockID
    }

    public init?(_ url: URL) {
        let parts = url.pathComponents
        guard url.scheme?.lowercased() == Self.scheme, url.host == "note",
            parts.count == 2, parts[0] == "/", !parts[1].isEmpty
        else { return nil }
        noteID = parts[1]
        let block = url.fragment.flatMap { $0.hasPrefix("block=") ? String($0.dropFirst(6)) : nil }
        blockID = block.flatMap { $0.isEmpty ? nil : $0 }
    }

    public var url: URL {
        var components = URLComponents()
        components.scheme = Self.scheme
        components.host = "note"
        components.path = "/\(noteID)"
        components.fragment = blockID.map { "block=\($0)" }
        return components.url!
    }
}
