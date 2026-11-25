namespace Heracles.Model;

/// <summary>The original app's library, brought across the first time the app starts.</summary>
internal sealed partial class AppModel
{
    /// <summary>What the original app called its database. It kept it in the library folder.</summary>
    public const string LegacyDatabaseName = "heracles.sqlite";

    /// <summary>The original app's database, if it is where that app kept it.</summary>
    public static string? LegacyDatabase(string dataDirectory)
    {
        var file = Path.Combine(dataDirectory, LegacyDatabaseName);
        return File.Exists(file) ? file : null;
    }

    /// <summary>
    /// On the first start, bring the original app's library across. Does
    /// nothing if this library already has content or the import has been
    /// done: the core refuses both. The core copies the old database before
    /// it reads it, and never changes it.
    /// </summary>
    /// <returns>What was imported, or null if there was nothing to do.</returns>
    public ImportReport? ImportLegacyLibraryIfNeeded(string? database)
    {
        if (database is null) return null;
        // An empty library is one placeholder notebook and nothing else.
        try
        {
            if (Store.ListNotes().Length != 1) return null;
        }
        catch (UniffiException) { return null; }

        try { return Store.ImportLegacy(database); }
        // Already imported. That is no fault, and nothing to tell the user.
        catch (CoreException.Invalid) { return null; }
        catch (UniffiException error)
        {
            LastError = "The library from the original Heracles could not be imported. It has not been changed. "
                + error.Message;
            return null;
        }
    }
}

/// <summary>What the summary says once the old library is across. The Mac's words.</summary>
internal static class ImportSummary
{
    public const string Headline = "Your library has been imported";

    public const string Kept = "The previous library was not changed. A copy of it was saved at:";

    public const string Adjusted = "Some items needed adjusting to fit:";

    public static string Counts(ImportReport report) =>
        $"{Count(report.Notes, "note")}, {Count(report.Tasks, "task")} and {Count(report.Images, "image")} "
        + "were brought across from the previous version of Heracles.";

    private static string Count(uint number, string what) => number == 1 ? $"1 {what}" : $"{number} {what}s";
}
