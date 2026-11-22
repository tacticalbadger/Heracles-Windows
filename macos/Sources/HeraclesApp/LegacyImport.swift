import Foundation
import HeraclesCore

extension AppModel {
    /// The Electron app's database, if it is where that app keeps it.
    public static func legacyDatabase(in dataDirectory: URL) -> URL? {
        let file = dataDirectory.appendingPathComponent("heracles.sqlite")
        return FileManager.default.fileExists(atPath: file.path) ? file : nil
    }

    /// On the first launch of the native app, bring the Electron app's
    /// library across. Does nothing if this library already has content or
    /// the import has been done: the core refuses both.
    ///
    /// Returns what was imported, or `nil` if there was nothing to do.
    @discardableResult
    public func importLegacyLibraryIfNeeded(from database: URL?) -> ImportReport? {
        guard let database else { return nil }
        // An empty library is one placeholder notebook and nothing else.
        guard (try? store.listNotes().count) == 1 else { return nil }
        do {
            return try store.importLegacy(legacyDatabase: database.path)
        } catch CoreError.Invalid {
            // Already imported.
            return nil
        } catch {
            lastError = "The Electron library could not be imported. It has not been changed. "
                + ((error as? LocalizedError)?.errorDescription ?? String(describing: error))
            return nil
        }
    }
}
